Option Strict On

Imports System.Globalization
Imports System.IO
Imports Microsoft.Data.Sqlite
Imports WhoIsPro.Network.Scan

Namespace Inventory

    ''' <summary>
    ''' Database SQLite dell'archivio della rete (archivio-rete.db nella cartella dei dati): apertura, schema, migrazioni e
    ''' comandi di supporto. Le tabelle: net_networks (le reti: una per router, o per sottorete), net_hosts (dispositivi,
    ''' unici per rete), net_scans (scansioni), net_events (cambiamenti).
    ''' </summary>
    Public NotInheritable Class InventoryStore
        Implements IDisposable

#Region "Campi"

        Private ReadOnly _connection As SqliteConnection

        ''' <summary>Colonne di net_hosts dopo l'id: le stesse alla creazione e nella ricostruzione della migrazione.</summary>
        Private Const HostsBody As String = "
                    network_key  TEXT NOT NULL DEFAULT '',
                    host_key     TEXT NOT NULL,
                    mac          TEXT,
                    ip           TEXT NOT NULL,
                    hostname     TEXT,
                    netbios      TEXT,
                    workgroup    TEXT,
                    vendor       TEXT,
                    device_type  TEXT,
                    category     TEXT,
                    glyph        TEXT,
                    os_guess     TEXT,
                    is_gateway   INTEGER NOT NULL DEFAULT 0,
                    is_local     INTEGER NOT NULL DEFAULT 0,
                    first_seen   TEXT NOT NULL,
                    last_seen    TEXT NOT NULL,
                    seen_count   INTEGER NOT NULL DEFAULT 1,
                    miss_count   INTEGER NOT NULL DEFAULT 0,
                    last_scan_id INTEGER,
                    ports_json   TEXT,
                    latency_ms   REAL,
                    alias        TEXT,
                    trust        INTEGER NOT NULL DEFAULT 0,
                    notes        TEXT,
                    icon         TEXT,
                    icon_color   TEXT,
                    extra_ips    TEXT,
                    uplink       TEXT,
                    topo_x       REAL,
                    topo_y       REAL,
                    UNIQUE(network_key, host_key)"
        Private Const HostColumnList As String = "network_key, host_key, mac, ip, hostname, netbios, workgroup, vendor, device_type, category, glyph, os_guess, is_gateway, is_local, first_seen, last_seen, seen_count, miss_count, last_scan_id, ports_json, latency_ms, alias, trust, notes, icon, icon_color, extra_ips, uplink, topo_x, topo_y"

#End Region

#Region "Costruzione"

        Public Sub New(databasePath As String)
            Dim folder = Path.GetDirectoryName(databasePath)
            If Not String.IsNullOrEmpty(folder) Then Directory.CreateDirectory(folder)
            Me.DatabasePath = databasePath
            _connection = New SqliteConnection(New SqliteConnectionStringBuilder With {
                .DataSource = databasePath, .Mode = SqliteOpenMode.ReadWriteCreate, .Cache = SqliteCacheMode.Shared
            }.ToString())
            _connection.Open()
            CreateSchema()
            Migrate()
            CreateIndexes()
            Network = New NetworkInventory(Me)
        End Sub

        ''' <summary>Nome del database: «archivio-rete.db» nella cartella dei dati del programma.</summary>
        Public Const FileName As String = "archivio-rete.db"

        Public ReadOnly Property DatabasePath As String

        ''' <summary>Archivio della rete locale: dispositivi visti nel tempo, scansioni e cambiamenti, rete per rete.</summary>
        Public ReadOnly Property Network As NetworkInventory

        Private Sub CreateSchema()
            Execute($"PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS net_networks (
                    key          TEXT PRIMARY KEY,
                    label        TEXT NOT NULL,
                    cidr         TEXT,
                    gateway_ip   TEXT,
                    gateway_mac  TEXT,
                    alias        TEXT,
                    dhcp_range   TEXT,
                    topo_manual  INTEGER NOT NULL DEFAULT 0,
                    first_seen   TEXT NOT NULL,
                    last_seen    TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS net_hosts (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,{HostsBody});
                CREATE TABLE IF NOT EXISTS net_scans (
                    id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at    TEXT NOT NULL,
                    duration_ms   INTEGER NOT NULL,
                    target        TEXT NOT NULL,
                    hosts_count   INTEGER NOT NULL,
                    new_count     INTEGER NOT NULL,
                    gone_count    INTEGER NOT NULL,
                    changed_count INTEGER NOT NULL,
                    summary       TEXT NOT NULL,
                    network_key   TEXT);
                CREATE TABLE IF NOT EXISTS net_events (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    scan_id      INTEGER,
                    host_id      INTEGER,
                    at           TEXT NOT NULL,
                    kind         TEXT NOT NULL,
                    title        TEXT NOT NULL,
                    details      TEXT NOT NULL,
                    acknowledged INTEGER NOT NULL DEFAULT 0,
                    network_key  TEXT);
                CREATE TABLE IF NOT EXISTS net_topo_nodes (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    network_key  TEXT NOT NULL,
                    kind         TEXT NOT NULL,
                    name         TEXT NOT NULL,
                    uplink       TEXT,
                    topo_x       REAL,
                    topo_y       REAL);")
        End Sub

        Private Sub CreateIndexes()
            Execute("CREATE INDEX IF NOT EXISTS ix_net_events_at ON net_events(at);
                CREATE INDEX IF NOT EXISTS ix_net_events_host ON net_events(host_id);
                CREATE INDEX IF NOT EXISTS ix_net_events_network ON net_events(network_key);
                CREATE INDEX IF NOT EXISTS ix_net_hosts_last_seen ON net_hosts(last_seen);
                CREATE INDEX IF NOT EXISTS ix_net_hosts_network ON net_hosts(network_key);
                CREATE INDEX IF NOT EXISTS ix_net_scans_network ON net_scans(network_key);")
        End Sub

#End Region

#Region "Migrazioni"

        ''' <summary>Database delle versioni precedenti: colonne dell'icona (prime prove) e archivio per rete (2.6.0 → 2.7).</summary>
        Private Sub Migrate()
            Dim hostColumns = Columns("net_hosts")
            If Not hostColumns.Contains("icon") Then Execute("ALTER TABLE net_hosts ADD COLUMN icon TEXT")
            If Not hostColumns.Contains("icon_color") Then Execute("ALTER TABLE net_hosts ADD COLUMN icon_color TEXT")
            ' 2.8: altri IPv4 dello stesso dispositivo (stesso MAC), prima della ricostruzione per rete che copia tutte le colonne
            If Not hostColumns.Contains("extra_ips") Then Execute("ALTER TABLE net_hosts ADD COLUMN extra_ips TEXT")
            ' 2.9: topologia (a chi è collegato il dispositivo e dove sta nel disegno)
            If Not hostColumns.Contains("uplink") Then Execute("ALTER TABLE net_hosts ADD COLUMN uplink TEXT")
            If Not hostColumns.Contains("topo_x") Then Execute("ALTER TABLE net_hosts ADD COLUMN topo_x REAL")
            If Not hostColumns.Contains("topo_y") Then Execute("ALTER TABLE net_hosts ADD COLUMN topo_y REAL")
            If Not hostColumns.Contains("network_key") Then MigrateToNetworks()
            ' 2.8.2: intervallo DHCP dichiarato dall'utente per ogni rete
            Dim networkColumns = Columns("net_networks")
            If Not networkColumns.Contains("dhcp_range") Then Execute("ALTER TABLE net_networks ADD COLUMN dhcp_range TEXT")
            ' 2.11: topologia composta a mano (tela vuota, oggetti trascinati dall'elenco) invece dell'albero automatico
            If Not networkColumns.Contains("topo_manual") Then Execute("ALTER TABLE net_networks ADD COLUMN topo_manual INTEGER NOT NULL DEFAULT 0")
        End Sub

        ''' <summary>
        ''' Dalla 2.6.0 (un solo archivio) alla 2.7 (un archivio per rete): ogni dispositivo va nella rete della sua sottorete /24
        ''' («net:…»); alla prima scansione con il router riconosciuto passa alla rete vera. La chiave unica diventa (rete,
        ''' dispositivo), quindi la tabella si ricostruisce con gli stessi id; scansioni ed eventi prendono la rete dal bersaglio
        ''' e dal dispositivo.
        ''' </summary>
        Private Sub MigrateToNetworks()
            Using tx = BeginTransaction()
                Execute("ALTER TABLE net_hosts ADD COLUMN network_key TEXT NOT NULL DEFAULT ''")
                For Each pair In ReadPairs("SELECT id, ip FROM net_hosts")
                    Execute("UPDATE net_hosts SET network_key = $k WHERE id = $id", ("$k", NetworkIdentity.ForIp(pair.Value).Key), ("$id", pair.Key))
                Next
                Execute($"CREATE TABLE net_hosts_v2 (id INTEGER PRIMARY KEY AUTOINCREMENT,{HostsBody});
                          INSERT INTO net_hosts_v2 (id, {HostColumnList}) SELECT id, {HostColumnList} FROM net_hosts;
                          DROP TABLE net_hosts;
                          ALTER TABLE net_hosts_v2 RENAME TO net_hosts;")
                If Not Columns("net_scans").Contains("network_key") Then Execute("ALTER TABLE net_scans ADD COLUMN network_key TEXT")
                If Not Columns("net_events").Contains("network_key") Then Execute("ALTER TABLE net_events ADD COLUMN network_key TEXT")
                For Each pair In ReadPairs("SELECT id, target FROM net_scans WHERE network_key IS NULL")
                    Dim seed = pair.Value.Split({"/"c, "-"c})(0).Trim()
                    Dim value As UInteger
                    If Ipv4.TryParse(seed, value) Then Execute("UPDATE net_scans SET network_key = $k WHERE id = $id", ("$k", NetworkIdentity.ForIp(seed).Key), ("$id", pair.Key))
                Next
                Execute("UPDATE net_events SET network_key = (SELECT h.network_key FROM net_hosts h WHERE h.id = net_events.host_id) WHERE network_key IS NULL")
                Execute("INSERT OR IGNORE INTO net_networks(key, label, cidr, first_seen, last_seen)
                         SELECT network_key, substr(network_key, 5), substr(network_key, 5), MIN(first_seen), MAX(last_seen) FROM net_hosts GROUP BY network_key")
                tx.Commit()
            End Using
        End Sub

        Private Function Columns(table As String) As HashSet(Of String)
            Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Using cmd = Command($"PRAGMA table_info({table})")
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        names.Add(r.GetString(1))
                    End While
                End Using
            End Using
            Return names
        End Function

        Private Function ReadPairs(sql As String) As List(Of KeyValuePair(Of Long, String))
            Dim list As New List(Of KeyValuePair(Of Long, String))()
            Using cmd = Command(sql)
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        list.Add(New KeyValuePair(Of Long, String)(r.GetInt64(0), If(r.IsDBNull(1), "", r.GetString(1))))
                    End While
                End Using
            End Using
            Return list
        End Function

#End Region

#Region "Supporto SQL"

        Friend Function Command(sql As String, ParamArray parameters As (Name As String, Value As Object)()) As SqliteCommand
            Dim cmd = _connection.CreateCommand()
            cmd.CommandText = sql
            For Each p In parameters
                cmd.Parameters.AddWithValue(p.Name, If(p.Value, DBNull.Value))
            Next
            Return cmd
        End Function

        Friend Sub Execute(sql As String, ParamArray parameters As (Name As String, Value As Object)())
            Using cmd = Command(sql, parameters)
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        Friend Function LastInsertId() As Long
            Using cmd = Command("SELECT last_insert_rowid()")
                Return CLng(If(cmd.ExecuteScalar(), 0L))
            End Using
        End Function

        Friend Function Scalar(sql As String, ParamArray parameters As (Name As String, Value As Object)()) As Object
            Using cmd = Command(sql, parameters)
                Dim value = cmd.ExecuteScalar()
                Return If(TypeOf value Is DBNull, Nothing, value)
            End Using
        End Function

        ''' <summary>Transazione per le scritture multiple (una scansione tocca decine di righe).</summary>
        Friend Function BeginTransaction() As SqliteTransaction
            Return _connection.BeginTransaction()
        End Function

        Friend Shared Function ToIso(value As DateTime) As String
            Return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
        End Function

        Friend Shared Function Parse(iso As String) As DateTime
            Return DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime()
        End Function

#End Region

        Public Sub Dispose() Implements IDisposable.Dispose
            _connection.Dispose()
        End Sub

    End Class

End Namespace
