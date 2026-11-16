param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$DmssrPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\3835512735\1.7\Assemblies\DMSSR.dll',
    [string]$FortifiedPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\3498575851\1.7\Assemblies\Fortified.dll',
    [string]$LegionPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\3666117000\1.7\Assemblies\DMS_Legion.dll',
    [string]$DmsCorePath = 'A:\SteamLibrary\steamapps\workshop\content\294100\3819799478\1.7\Assemblies\DMS.dll',
    [string]$AncientCorpsRoot = 'A:\SteamLibrary\steamapps\workshop\content\294100\3469398006',
    [string]$PatchPath = (Join-Path $PSScriptRoot '..\1.7\Assemblies\DMSSRLegionCustomizationCompat.dll')
)

$ErrorActionPreference = 'Stop'

dotnet build (Join-Path $PSScriptRoot 'Validation.csproj') -c Release --nologo `
    "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$HarmonyPath" "-p:DmssrPath=$DmssrPath" `
    "-p:FortifiedPath=$FortifiedPath" "-p:LegionPath=$LegionPath" "-p:DmsCorePath=$DmsCorePath" `
    "-p:AncientCorpsRoot=$AncientCorpsRoot"
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }

$driver = Join-Path $PSScriptRoot 'bin\Release\net10.0\Validation.dll'
foreach ($mode in @('bind', 'red', 'green')) {
    dotnet $driver $mode $GameRoot $HarmonyPath $DmssrPath $FortifiedPath $LegionPath $DmsCorePath $AncientCorpsRoot $PatchPath
    if ($LASTEXITCODE -ne 0) { throw "Validation mode failed: $mode" }
}
