param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$DmsRoot = 'A:\SteamLibrary\steamapps\workshop\content\294100\3819799478',
    [string]$DmssrRoot = 'A:\SteamLibrary\steamapps\workshop\content\294100\3835512735',
    [string]$FortifiedPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\3498575851\1.7\Assemblies\Fortified.dll',
    [string]$PatchPath = (Join-Path $PSScriptRoot '..\1.7\Assemblies\DMSSRNeuroglueFix.dll')
)

$ErrorActionPreference = 'Stop'

$dmsDll = Join-Path $DmsRoot '1.7\Assemblies\DMS.dll'
$dmssrDll = Join-Path $DmssrRoot '1.7\Assemblies\DMSSR.dll'

dotnet build (Join-Path $PSScriptRoot 'Validation.csproj') -c Release --nologo `
    "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$HarmonyPath" `
    "-p:DmsPath=$dmsDll" "-p:DmssrPath=$dmssrDll" "-p:FortifiedPath=$FortifiedPath"
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }

$driver = Join-Path $PSScriptRoot 'bin\Release\net10.0\Validation.dll'
foreach ($mode in @('bind', 'red', 'green')) {
    dotnet $driver $mode $GameRoot $HarmonyPath $DmsRoot $DmssrRoot $FortifiedPath $PatchPath
    if ($LASTEXITCODE -ne 0) { throw "Validation mode failed: $mode" }
}
