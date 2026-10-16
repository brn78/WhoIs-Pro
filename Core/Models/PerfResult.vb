Namespace Models

    ''' <summary>Latenza verso un indirizzo: una serie di ping, con variazione (jitter) e pacchetti persi.</summary>
    Public Class LatencyStats
        Public Property Target As String = ""
        Public Property Sent As Integer
        Public Property Received As Integer
        Public Property MinMs As Double
        Public Property AvgMs As Double
        Public Property MaxMs As Double
        ''' <summary>Variazione media tra un ping e il successivo: quanto è stabile la latenza.</summary>
        Public Property JitterMs As Double
        Public Property LossPct As Double
    End Class

    ''' <summary>Un test delle prestazioni di rete: rete locale (gateway), Internet, DNS, download e upload.</summary>
    Public Class PerfResult
        Public Property At As DateTime = DateTime.Now
        Public Property Adapter As String = ""
        ''' <summary>Velocità del collegamento della scheda (Mbit/s), come la dice Windows.</summary>
        Public Property LinkMbps As Long
        Public Property Local As LatencyStats
        Public Property Internet As LatencyStats
        ''' <summary>Tempo medio di risposta del server DNS (nomi mai chiesti prima, quindi senza cache).</summary>
        Public Property DnsMs As Double
        Public Property DownloadMbps As Double
        Public Property UploadMbps As Double
        ''' <summary>Server di prova usato, es. «Cloudflare · MXP» (codice dell'aeroporto del centro dati).</summary>
        Public Property Server As String = ""
        Public Property Seconds As Double
        Public Property Cancelled As Boolean
        Public Property [Error] As String = ""
    End Class

End Namespace
