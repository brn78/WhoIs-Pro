Imports System.IO
Imports System.Text
Imports System.Web
Imports WhoIsPro.Models

Namespace Storage
    Public Module SessionExporter
        Public Sub ExportToHtml(session As ScanSession, filePath As String)
            Dim sb As New StringBuilder()
            sb.AppendLine("<!DOCTYPE html>")
            sb.AppendLine("<html lang=""it"">")
            sb.AppendLine("<head>")
            sb.AppendLine("  <meta charset=""UTF-8"">")
            sb.AppendLine("  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">")
            sb.AppendLine($"  <title>{HttpUtility.HtmlEncode(session.SessionTitle)} - WhoIs Pro Report</title>")
            sb.AppendLine("  <style>")
            sb.AppendLine("    :root { --bg: #0f172a; --card: #1e293b; --border: #334155; --text: #f8fafc; --muted: #94a3b8; --accent: #38bdf8; --success: #22c55e; --warn: #eab308; }")
            sb.AppendLine("    * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }")
            sb.AppendLine("    body { background: var(--bg); color: var(--text); padding: 2rem; }")
            sb.AppendLine("    .container { max-width: 1300px; margin: 0 auto; }")
            sb.AppendLine("    .header { border-bottom: 2px solid var(--border); padding-bottom: 1.5rem; margin-bottom: 2rem; display: flex; justify-content: space-between; align-items: center; }")
            sb.AppendLine("    .title h1 { font-size: 1.8rem; color: var(--accent); display: flex; align-items: center; gap: 0.5rem; }")
            sb.AppendLine("    .meta { color: var(--muted); font-size: 0.9rem; }")
            sb.AppendLine("    .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 1rem; margin-bottom: 2rem; }")
            sb.AppendLine("    .stat-card { background: var(--card); border: 1px solid var(--border); border-radius: 8px; padding: 1.2rem; }")
            sb.AppendLine("    .stat-val { font-size: 2rem; font-weight: bold; color: var(--text); }")
            sb.AppendLine("    .stat-lbl { color: var(--muted); font-size: 0.85rem; text-transform: uppercase; letter-spacing: 0.05em; }")
            sb.AppendLine("    .section { background: var(--card); border: 1px solid var(--border); border-radius: 8px; padding: 1.5rem; margin-bottom: 2rem; }")
            sb.AppendLine("    .section-title { font-size: 1.3rem; margin-bottom: 1rem; border-bottom: 1px solid var(--border); padding-bottom: 0.5rem; display: flex; align-items: center; gap: 0.5rem; }")
            sb.AppendLine("    table { width: 100%; border-collapse: collapse; text-align: left; font-size: 0.9rem; }")
            sb.AppendLine("    th { background: #0b1120; padding: 0.75rem 1rem; color: var(--muted); border-bottom: 1px solid var(--border); }")
            sb.AppendLine("    td { padding: 0.75rem 1rem; border-bottom: 1px solid var(--border); }")
            sb.AppendLine("    tr:hover { background: rgba(56, 189, 248, 0.05); }")
            sb.AppendLine("    .badge { display: inline-block; padding: 0.25rem 0.6rem; border-radius: 9999px; font-size: 0.75rem; font-weight: 600; }")
            sb.AppendLine("    .badge-online { background: rgba(34, 197, 94, 0.2); color: #4ade80; border: 1px solid rgba(34, 197, 94, 0.4); }")
            sb.AppendLine("    .badge-tv { background: rgba(234, 179, 8, 0.2); color: #fde047; }")
            sb.AppendLine("    .badge-iot { background: rgba(168, 85, 247, 0.2); color: #c084fc; }")
            sb.AppendLine("    .badge-phone { background: rgba(56, 189, 248, 0.2); color: #7dd3fc; }")
            sb.AppendLine("    .badge-port { background: #334155; color: #38bdf8; font-family: monospace; font-weight: bold; }")
            sb.AppendLine("    pre { background: #0b1120; padding: 1rem; border-radius: 6px; overflow-x: auto; font-family: 'Consolas', monospace; font-size: 0.85rem; color: #cbd5e1; }")
            sb.AppendLine("    @media print { body { background: #fff; color: #000; } .section, .stat-card { border: 1px solid #ccc; background: #fff; } th { background: #eee; color: #000; } }")
            sb.AppendLine("  </style>")
            sb.AppendLine("</head>")
            sb.AppendLine("<body>")
            sb.AppendLine("  <div class=""container"">")
            sb.AppendLine("    <div class=""header"">")
            sb.AppendLine("      <div class=""title"">")
            sb.AppendLine("        <h1>🔍 WhoIs Pro - Report di Rete Professionale</h1>")
            sb.AppendLine($"        <p class=""meta"">Sessione: <strong>{HttpUtility.HtmlEncode(session.SessionTitle)}</strong> | Creata il: {session.CreatedAt:yyyy-MM-dd HH:mm:ss} | Checksum SHA-256: <code>{session.ChecksumSHA256}</code></p>")
            sb.AppendLine("      </div>")
            sb.AppendLine("    </div>")

            ' Stats
            sb.AppendLine("    <div class=""stats-grid"">")
            sb.AppendLine($"      <div class=""stat-card""><div class=""stat-val"">{session.Hosts.Count}</div><div class=""stat-lbl"">Host Rilevati</div></div>")
            sb.AppendLine($"      <div class=""stat-card""><div class=""stat-val"">{session.Ports.Where(Function(p) p.Status = "Open").Count()}</div><div class=""stat-lbl"">Porte Aperte</div></div>")
            sb.AppendLine($"      <div class=""stat-card""><div class=""stat-val"">{session.WhoisEntries.Count}</div><div class=""stat-lbl"">Ricerche WHOIS</div></div>")
            sb.AppendLine($"      <div class=""stat-card""><div class=""stat-val"">{session.TracerouteHops.Count}</div><div class=""stat-lbl"">Hop Traceroute</div></div>")
            sb.AppendLine("    </div>")

            ' Hosts Table
            sb.AppendLine("    <div class=""section"">")
            sb.AppendLine("      <h2 class=""section-title"">🖥️ Host Rete Rilevati & Tipologia Dispositivo</h2>")
            If session.Hosts.Count = 0 Then
                sb.AppendLine("      <p style=""color:var(--muted);"">Nessun host rilevato in questa sessione.</p>")
            Else
                sb.AppendLine("      <table>")
                sb.AppendLine("        <thead><tr><th>Tipo</th><th>Indirizzo IP</th><th>Hostname</th><th>MAC Address</th><th>Produttore MAC</th><th>Tipologia Dispositivo</th><th>Latenza</th><th>TTL</th></tr></thead>")
                sb.AppendLine("        <tbody>")
                For Each h In session.Hosts
                    sb.AppendLine($"          <tr><td>{h.DeviceIcon}</td><td><strong>{h.IPAddress}</strong></td><td>{HttpUtility.HtmlEncode(h.Hostname)}</td><td><code>{h.MacAddress}</code></td><td>{HttpUtility.HtmlEncode(h.MacVendor)}</td><td><span class=""badge badge-iot"">{HttpUtility.HtmlEncode(h.DeviceType)}</span></td><td>{h.ResponseTimeMs} ms</td><td>{h.TTL}</td></tr>")
                Next
                sb.AppendLine("        </tbody>")
                sb.AppendLine("      </table>")
            End If
            sb.AppendLine("    </div>")

            ' Ports Table
            If session.Ports.Count > 0 Then
                sb.AppendLine("    <div class=""section"">")
                sb.AppendLine("      <h2 class=""section-title"">🔌 Porte Scansionate & Servizi</h2>")
                sb.AppendLine("      <table>")
                sb.AppendLine("        <thead><tr><th>Host</th><th>Porta</th><th>Protocollo</th><th>Stato</th><th>Servizio</th><th>Banner / Dettagli</th><th>Latenza</th></tr></thead>")
                sb.AppendLine("        <tbody>")
                For Each p In session.Ports
                    sb.AppendLine($"          <tr><td>{p.Host}</td><td><span class=""badge badge-port"">{p.PortNumber}</span></td><td>{p.Protocol}</td><td><span class=""badge badge-online"">{p.Status}</span></td><td><strong>{HttpUtility.HtmlEncode(p.ServiceName)}</strong></td><td><code>{HttpUtility.HtmlEncode(p.Banner)}</code></td><td>{p.ResponseTimeMs} ms</td></tr>")
                Next
                sb.AppendLine("        </tbody>")
                sb.AppendLine("      </table>")
                sb.AppendLine("    </div>")
            End If

            ' WHOIS Entries
            If session.WhoisEntries.Count > 0 Then
                sb.AppendLine("    <div class=""section"">")
                sb.AppendLine("      <h2 class=""section-title"">🌐 Informazioni WHOIS</h2>")
                For Each w In session.WhoisEntries
                    sb.AppendLine($"      <h3 style=""margin-top:1rem;color:var(--accent);"">Target: {HttpUtility.HtmlEncode(w.QueryTarget)} (Server: {HttpUtility.HtmlEncode(w.ServerUsed)})</h3>")
                    sb.AppendLine($"      <p style=""margin:0.5rem 0;font-size:0.9rem;"">Registrar: <strong>{HttpUtility.HtmlEncode(w.Registrar)}</strong> | Creazione: {w.CreationDate} | Scadenza: {w.ExpirationDate}</p>")
                    sb.AppendLine($"      <pre>{HttpUtility.HtmlEncode(w.RawText)}</pre>")
                Next
                sb.AppendLine("    </div>")
            End If

            sb.AppendLine("  </div>")
            sb.AppendLine("</body>")
            sb.AppendLine("</html>")

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8)
        End Sub

        Public Sub ExportToCsv(session As ScanSession, filePath As String)
            Dim sb As New StringBuilder()
            sb.AppendLine("Tipo;Indirizzo IP;Hostname;MAC Address;Produttore MAC;Tipologia Dispositivo;Categoria;Latenza (ms);TTL;OS Stimato;Stato")
            For Each h In session.Hosts
                sb.AppendLine($"""{h.DeviceIcon}"";""{h.IPAddress}"";""{h.Hostname}"";""{h.MacAddress}"";""{h.MacVendor}"";""{h.DeviceType}"";""{h.DeviceCategory}"";{h.ResponseTimeMs};{h.TTL};""{h.OSGuess}"";""{h.Status}""")
            Next
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8)
        End Sub

        Public Sub ExportToTxt(session As ScanSession, filePath As String)
            Dim sb As New StringBuilder()
            sb.AppendLine($"================================================================================")
            sb.AppendLine($" WHOIS PRO - REPORT DI RETE PROFESSIONALE")
            sb.AppendLine($" Sessione: {session.SessionTitle}")
            sb.AppendLine($" Creata il: {session.CreatedAt:yyyy-MM-dd HH:mm:ss}")
            sb.AppendLine($" Checksum SHA-256: {session.ChecksumSHA256}")
            sb.AppendLine($"================================================================================")
            sb.AppendLine()
            sb.AppendLine($"--- HOST RILEVATI ({session.Hosts.Count}) ---")
            For Each h In session.Hosts
                sb.AppendLine($"[{h.DeviceIcon}] {h.IPAddress,-16} {If(String.IsNullOrEmpty(h.Hostname), "-", h.Hostname),-25} MAC: {h.MacAddress,-18} {h.DeviceType} ({h.MacVendor}) | RTT: {h.ResponseTimeMs}ms")
            Next
            sb.AppendLine()
            sb.AppendLine($"--- PORTE APERTE ({session.Ports.Count}) ---")
            For Each p In session.Ports
                sb.AppendLine($"Host: {p.Host,-16} Porta: {p.PortNumber,-6} Proto: {p.Protocol,-4} Stato: {p.Status,-8} Servizio: {p.ServiceName} Banner: {p.Banner}")
            Next
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8)
        End Sub
    End Module
End Namespace