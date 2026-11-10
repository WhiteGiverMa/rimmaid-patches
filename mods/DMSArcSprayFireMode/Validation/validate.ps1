param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$WorkshopRoot = 'A:\SteamLibrary\steamapps\workshop\content\294100',
    [string]$QaRoot = 'C:\Users\A1337\AppData\Local\Temp\opencode\dms-arcspray-firemode-20261107',
    [switch]$WithSoundCompatibility
)
$ErrorActionPreference = 'Stop'
$mod = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $PSScriptRoot 'Validation.csproj') -c Release --nologo "-p:GameRoot=$GameRoot" "-p:WorkshopRoot=$WorkshopRoot"
if ($LASTEXITCODE) { throw 'Build failed.' }
New-Item -ItemType Directory -Path $QaRoot -Force | Out-Null
$driver = Join-Path $PSScriptRoot 'bin/Release/net10.0/Validation.dll'
$modes = @('bind', 'red', 'green', 'ce')
if ($WithSoundCompatibility) { $modes += @('sound-red', 'sound', 'sound-ce') }
$modes += @('drift-bind', 'drift')
if ($WithSoundCompatibility) { $modes += 'sound-drift' }
$modes += 'restore'
foreach ($mode in $modes) {
    if ($mode -eq 'bind') { & dotnet $driver $mode $GameRoot $WorkshopRoot $mod $QaRoot *> (Join-Path $QaRoot "$mode.txt") }
    else { & dotnet $driver $mode $GameRoot $WorkshopRoot $mod $QaRoot 2>&1 | Tee-Object -FilePath (Join-Path $QaRoot "$mode.txt") }
    if ($LASTEXITCODE) { throw "Failed: $mode" }
}
