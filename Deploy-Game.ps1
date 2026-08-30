param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,
    [switch]$StartGame
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectDir 'FischerTimeFlow.csproj'
$sourceDll = Join-Path $projectDir 'bin\Release\net472\FischerTimeFlow.dll'
$destinationDll = Join-Path $gameDir 'BepInEx\plugins\FischerTimeFlow.dll'
$gameExe = Join-Path $gameDir "Fischer'sFishingJourney.exe"

$gameProcess = Get-Process -Name "Fischer'sFishingJourney" -ErrorAction SilentlyContinue
if ($null -ne $gameProcess) {
    throw 'The game is still running. Close it before deployment.'
}

dotnet build $projectFile --configuration Release "-p:GameDir=$GameDir"
Copy-Item -LiteralPath $sourceDll -Destination $destinationDll -Force

$sourceHash = (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash
$destinationHash = (Get-FileHash -LiteralPath $destinationDll -Algorithm SHA256).Hash
if ($sourceHash -ne $destinationHash) {
    throw 'The deployed DLL hash does not match the build output.'
}

Write-Output "[OK] Deployed FischerTimeFlow.dll: $destinationHash"

if ($StartGame) {
    Start-Process -FilePath $gameExe
}
