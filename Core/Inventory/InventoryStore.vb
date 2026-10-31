Option Strict On

Imports System.Globalization
Imports System.IO
Imports Microsoft.Data.Sqlite

Namespace Inventory

    ''' <summary>
    ''' Database SQLite dell'archivio della rete (archivio-rete.db nella cartella dei dati): apertura, schema e
    ''' comandi di supporto. Le tabelle: net_hosts (dispositivi), net_scans (scansioni), net_events (cambiamenti).
    ''' </summary>
    Public NotInheritable Class InventoryStore
        Implements IDisposable

#Region "Campi"

        Private ReadOnly _connection As SqliteConnection

#End Region

#Region "Costruzione"

        Public Sub New(databasePath As String)
            Dim folder = Path.GetDirectoryName(databasePath)
            If Not String.IsNullOrEmpty(folder) Then Directory.CreateDirectory(folder)
            _connection = New SqliteConnection(New SqliteConnectionStringBuilder With {
                .DataSource = databasePath, .Mode = SqliteOpenMode.ReadWriteCreate, .Cache = SqliteCacheMode.Shared
            }.ToString())
            _connection.Open()
            CreateSchema()
            Network = New NetworkInventory(Me)
        End Sub

        ''' <summary>Archivio della rete locale: dispositivi visti nel tempo, scansioni e cambiamenti.</summary>
        Public ReadOnly Property Network As NetworkInventory

        Private Sub CreateSchema()
            Execute("PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS net_hosts (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    host_key     TEXT NOT NULL UNIQUE,
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
                    notes        TEXT);
                CREATE TABLE IF NOT EXISTS net_scans (
                    id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at    TEXT NOT NULL,
                    duration_ms   INTEGER NOT NULL,
                    target        TEXT NOT NULL,
                    hosts_count   INTEGER NOT NULL,
                    new_count     INTEGER NOT NULL,
                    gone_count    INTEGER NOT NULL,
                    changed_count INTEGER NOT NULL,
                    summary       TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS net_events (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    scan_id      INTEGER,
                    host_id      INTEGER,
                    at           TEXT NOT NULL,
                    kind         TEXT NOT NULL,
                    title        TEXT NOT NULL,
                    details      TEXT NOT NULL,
                    acknowledged INTEGER NOT NULL DEFAULT 0);
                CREATE INDEX IF NOT EXISTS ix_net_events_at ON net_events(at);
                CREATE INDEX IF NOT EXISTS ix_net_events_host ON net_events(host_id);
                CREATE INDEX IF NOT EXISTS ix_net_hosts_last_seen ON net_hosts(last_seen);")
        End Sub

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
