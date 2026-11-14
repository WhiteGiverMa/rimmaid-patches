param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$DmssrPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\3835512735\1.7\Assemblies\DMSSR.dll',
    [string]$PatchPath = (Join-Path $PSScriptRoot '..\1.7\Assemblies\DMSSRStatPathPerfFix.dll'),
    [string]$FortifiedPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\3498575851\1.7\Assemblies\Fortified.dll',
    [string]$BaselinePatchPath = ''
)

$ErrorActionPreference = 'Stop'
$env:DMSSR_VALIDATION_FORTIFIED_DIR = Split-Path -Parent $FortifiedPath
$env:DOTNET_TieredCompilation = '0'

dotnet build (Join-Path $PSScriptRoot 'Validation.csproj') -c Release --nologo `
    "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$HarmonyPath" "-p:DmssrPath=$DmssrPath" "-p:FortifiedPath=$FortifiedPath"
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }

$driver = Join-Path $PSScriptRoot 'bin\Release\net10.0\Validation.dll'
if ($BaselinePatchPath) {
    Copy-Item -LiteralPath $BaselinePatchPath -Destination (Join-Path (Split-Path $driver) 'DMSSRStatPathPerfFix.dll') -Force
    dotnet $driver bind $GameRoot $HarmonyPath $DmssrPath $BaselinePatchPath
    if ($LASTEXITCODE -ne 0) { throw 'Baseline bind failed.' }
    dotnet $driver late-red $GameRoot $HarmonyPath $DmssrPath $BaselinePatchPath
    if ($LASTEXITCODE -ne 0) { throw '1.0.0 late-startup RED failed.' }
}
Copy-Item -LiteralPath $PatchPath -Destination (Join-Path (Split-Path $driver) 'DMSSRStatPathPerfFix.dll') -Force
foreach ($mode in @('bind', 'red', 'green', 'early-green')) {
    dotnet $driver $mode $GameRoot $HarmonyPath $DmssrPath $PatchPath
    if ($LASTEXITCODE -ne 0) { throw "Validation mode failed: $mode" }
}
