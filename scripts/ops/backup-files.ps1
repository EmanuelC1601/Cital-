param(
    [Parameter(Mandatory = $true)][string]$UploadsPath,
    [Parameter(Mandatory = $true)][string]$DataProtectionKeysPath,
    [Parameter(Mandatory = $true)][string]$EncryptedDestination
)

$ErrorActionPreference = "Stop"
$uploads = [System.IO.Path]::GetFullPath($UploadsPath)
$keys = [System.IO.Path]::GetFullPath($DataProtectionKeysPath)
$destination = [System.IO.Path]::GetFullPath($EncryptedDestination)
if (-not (Test-Path -LiteralPath $uploads -PathType Container)) { throw "No existe la ruta de imágenes configurada." }
if (-not (Test-Path -LiteralPath $keys -PathType Container)) { throw "No existe el anillo de claves configurado." }
if ($destination.StartsWith($uploads, [StringComparison]::OrdinalIgnoreCase) -or $destination.StartsWith($keys, [StringComparison]::OrdinalIgnoreCase)) {
    throw "El respaldo debe estar fuera de las rutas de origen."
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
$backupId = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$target = Join-Path $destination $backupId
New-Item -ItemType Directory -Path $target | Out-Null
Copy-Item -LiteralPath $uploads -Destination (Join-Path $target "uploads") -Recurse
Copy-Item -LiteralPath $keys -Destination (Join-Path $target "dataprotection-keys") -Recurse
$manifest = Get-ChildItem -LiteralPath $target -File -Recurse | ForEach-Object {
    [pscustomobject]@{ Path = $_.FullName.Substring($target.Length + 1); Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $target "manifest.json")
Write-Output "Copia de imágenes y claves creada en $target. Verifica que el volumen/destino tenga cifrado en reposo y acceso restringido."
