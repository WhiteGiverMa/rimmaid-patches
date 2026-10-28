param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$WorkshopRoot = 'A:\SteamLibrary\steamapps\workshop\content\294100',
    [string]$QaRoot = (Join-Path ([IO.Path]::GetTempPath()) 'opencode/dms-mech-storage/qa'),
    [string]$ModPath = (Join-Path $PSScriptRoot '../Source/bin/Release/net10.0/DMSMechStorage.dll')
)
$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'Validation.csproj') -c Release --nologo "-p:GameRoot=$GameRoot" "-p:WorkshopRoot=$WorkshopRoot"
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }
$driver = Join-Path $PSScriptRoot 'bin/Release/net10.0/Validation.dll'
dotnet $driver bind $GameRoot $WorkshopRoot $QaRoot
if ($LASTEXITCODE -ne 0) { throw 'QA-only binding failed.' }
dotnet $driver run $GameRoot $WorkshopRoot $ModPath $QaRoot
if ($LASTEXITCODE -ne 0) { throw 'Offline validation failed.' }
