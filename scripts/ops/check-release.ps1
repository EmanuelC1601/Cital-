param([Parameter(Mandatory = $true)][string]$CanonicalBaseUrl)

$ErrorActionPreference = "Stop"
$parsedUrl = $null
if (-not [Uri]::TryCreate($CanonicalBaseUrl, [UriKind]::Absolute, [ref]$parsedUrl) -or $parsedUrl.Scheme -ne "https") {
    throw "La URL de comprobación debe usar HTTPS."
}
$base = $CanonicalBaseUrl.TrimEnd('/')
foreach ($path in @("/health/live", "/health/ready")) {
    $response = Invoke-WebRequest -Uri "$base$path" -Method Get -MaximumRedirection 0 -ErrorAction Stop
    if ($response.StatusCode -ne 200) { throw "La comprobación $path no devolvió HTTP 200." }
}
Write-Output "Health live/readiness respondieron correctamente por HTTPS. No se consultó información de negocio."
