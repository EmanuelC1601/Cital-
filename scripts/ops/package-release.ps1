param(
    [string]$OutputPath = "artifacts/release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

if (-not $SkipTests -and [string]::IsNullOrWhiteSpace($env:CITALU_SQLSERVER_TEST_CONNECTION)) {
    throw "Define CITALU_SQLSERVER_TEST_CONNECTION para que las pruebas relacionales se ejecuten contra SQL Server real."
}

dotnet restore Citalu.sln
if ($LASTEXITCODE -ne 0) { throw "dotnet restore falló ($LASTEXITCODE)." }

dotnet build Citalu.sln --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build Release falló ($LASTEXITCODE)." }

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore falló ($LASTEXITCODE)." }

dotnet ef migrations has-pending-model-changes --project src/Citalu.Infrastructure --startup-project src/Citalu.Web
if ($LASTEXITCODE -ne 0) { throw "El modelo EF tiene cambios sin migración ($LASTEXITCODE)." }

if (-not $SkipTests) {
    dotnet test Citalu.sln --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test Release falló ($LASTEXITCODE)." }
}

$fullOutput = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path $fullOutput | Out-Null
$applicationOutput = Join-Path $fullOutput "app"
dotnet publish src/Citalu.Web/Citalu.Web.csproj --configuration Release --no-restore --output $applicationOutput
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló ($LASTEXITCODE)." }

dotnet ef migrations script --idempotent --project src/Citalu.Infrastructure --startup-project src/Citalu.Web --output (Join-Path $fullOutput "migrations.sql")
if ($LASTEXITCODE -ne 0) { throw "No se pudo generar el script SQL idempotente ($LASTEXITCODE)." }

$commit = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw "No se pudo leer el commit actual." }
Set-Content -Path (Join-Path $fullOutput "commit.txt") -Value $commit -NoNewline
$zip = Join-Path (Split-Path $fullOutput -Parent) "Citalu-release-$commit.zip"
if (Test-Path -LiteralPath $zip) { throw "El paquete ya existe; elige otro OutputPath para conservar ambos artefactos." }
Compress-Archive -Path (Join-Path $fullOutput "*") -DestinationPath $zip -CompressionLevel Optimal
Write-Output "Artefacto preparado en $fullOutput y $zip (commit $commit). No se desplegó ni se aplicó SQL."
