param(
    [string] $OutputPath = (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads\citalu-dataprotection.pfx')
)

$ErrorActionPreference = 'Stop'

if (Test-Path -LiteralPath $OutputPath) {
    throw "El archivo ya existe. Muévelo o elige otra ruta para evitar sobrescribir un PFX existente: $OutputPath"
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$certificate = New-SelfSignedCertificate `
    -Type Custom `
    -Subject 'CN=Citalu Data Protection' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -KeyExportPolicy Exportable `
    -KeyUsage KeyEncipherment, DataEncipherment `
    -NotAfter (Get-Date).AddYears(3)

try {
    # Read-Host keeps the PFX password out of command history and source control.
    $pfxPassword = Read-Host 'Crea una contraseña fuerte para el PFX y guárdala en un gestor seguro' -AsSecureString
    if ($pfxPassword.Length -lt 16) {
        throw 'Usa una contraseña de al menos 16 caracteres.'
    }

    Export-PfxCertificate `
        -Cert $certificate `
        -FilePath $OutputPath `
        -Password $pfxPassword `
        -ChainOption EndEntityCertOnly | Out-Null

    Write-Host "PFX creado localmente: $OutputPath"
    Write-Host 'Súbelo únicamente a private/citalu/keys en el panel y elimina la copia temporal después de guardarla en tu bóveda segura.'
}
finally {
    # The PFX contains its own private key; remove the temporary certificate-store entry.
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
}
