# generate-snk.ps1 — Ejecutar UNA SOLA VEZ antes del primer build.
# El .snk generado NO debe commitearse al repo (.gitignore ya lo excluye).

$snkPath = "src\integrations\contacts\AxxonContacts.Plugins\AxxonContacts.Plugins.snk"

if (Test-Path $snkPath) {
    Write-Host "El archivo $snkPath ya existe. No se sobreescribe." -ForegroundColor Yellow
    exit 0
}

$snExe = Get-Command sn.exe -ErrorAction SilentlyContinue

if (-not $snExe) {
    $candidates = @(
        "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\sn.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\SDK\ScopedNetFx\sn.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\SDK\ScopedNetFx\sn.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\SDK\ScopedNetFx\sn.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { $snExe = $c; break } }
}

if (-not $snExe) {
    Write-Host "No se encontro sn.exe. Ejecutar desde Developer Command Prompt." -ForegroundColor Red
    exit 1
}

& $snExe -k $snkPath
Write-Host "Strong Name Key generada: $snkPath" -ForegroundColor Green
Write-Host "IMPORTANTE: no commitear este archivo." -ForegroundColor Yellow
