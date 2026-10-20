param([Parameter(Mandatory = $true)][string]$BackupFolder)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath($BackupFolder)
$manifestPath = Join-Path $root "manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "No se encontró manifest.json." }
$items = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($item in $items) {
    $file = Join-Path $root $item.Path
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Falta un archivo del respaldo." }
    $info = Get-Item -LiteralPath $file
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ($info.Length -ne $item.Length -or $hash -ne $item.Sha256) { throw "El respaldo no coincide con su manifiesto." }
}
Write-Output "Integridad de los archivos verificada: $($items.Count) archivos. Esto no demuestra que SQL Server ni los enlaces se restauren correctamente."
