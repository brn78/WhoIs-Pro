# Server statico per l'anteprima dell'interfaccia (dev\preview.html) senza compilare il programma.
# Uso: powershell -ExecutionPolicy Bypass -File dev\serve.ps1 [-Port 5517]
param([int]$Port = 5517)

$root = Split-Path -Parent $PSScriptRoot
$types = @{
  '.html' = 'text/html; charset=utf-8'; '.js' = 'text/javascript; charset=utf-8'; '.css' = 'text/css; charset=utf-8'
  '.json' = 'application/json'; '.png' = 'image/png'; '.ico' = 'image/x-icon'; '.svg' = 'image/svg+xml'; '.txt' = 'text/plain; charset=utf-8'
}
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "Anteprima su http://localhost:$Port/dev/preview.html (cartella $root)"
while ($listener.IsListening) {
  $ctx = $listener.GetContext()
  $path = [Uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath.TrimStart('/'))
  $res = $ctx.Response
  if ($path -eq '') { $res.Redirect('/dev/preview.html'); $res.Close(); continue }
  $file = [IO.Path]::GetFullPath((Join-Path $root $path))
  $res.Headers.Add('Cache-Control', 'no-store')
  if ($file.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $file -PathType Leaf)) {
    $ext = [IO.Path]::GetExtension($file).ToLowerInvariant()
    $res.ContentType = if ($types.ContainsKey($ext)) { $types[$ext] } else { 'application/octet-stream' }
    $bytes = [IO.File]::ReadAllBytes($file)
    $res.ContentLength64 = $bytes.Length
    $res.OutputStream.Write($bytes, 0, $bytes.Length)
  } else {
    $res.StatusCode = 404
  }
  $res.Close()
}
