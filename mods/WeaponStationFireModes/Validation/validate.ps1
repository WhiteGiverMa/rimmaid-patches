param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$WorkshopRoot = 'A:\SteamLibrary\steamapps\workshop\content\294100',
    [string]$PatchPath = '',
    [string]$QaRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Temp\opencode\weaponstation-firemodes-qa')
)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$harmony = Join-Path $WorkshopRoot '2009463077\1.7\Assemblies\0Harmony.dll'
dotnet build (Join-Path $PSScriptRoot 'Validation.csproj') -c Release --nologo `
    "-p:GameRoot=$GameRoot" "-p:WorkshopRoot=$WorkshopRoot" "-p:HarmonyPath=$harmony"
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }
if (-not $PatchPath) { $PatchPath = Join-Path $modRoot 'Source\bin\Release\net10.0\WeaponStationFireModes.dll' }
$driver = Join-Path $PSScriptRoot 'bin\Release\net10.0\Validation.dll'
New-Item -ItemType Directory -Path $QaRoot -Force | Out-Null
foreach ($mode in @('bind', 'red', 'run', 'ce')) {
    dotnet $driver $mode $GameRoot $WorkshopRoot $modRoot $PatchPath $QaRoot |
        Tee-Object -FilePath (Join-Path $QaRoot "$mode.txt")
    if ($LASTEXITCODE -ne 0) { throw "Validation failed: $mode" }
}
Write-Output 'PASS: isolated real-assembly validation; no game process or user save/config was loaded.'
