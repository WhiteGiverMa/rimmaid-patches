param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$DmssrPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\3835512735\1.7\Assemblies\DMSSR.dll',
    [string]$PatchPath = (Join-Path $PSScriptRoot '..\1.7\Assemblies\DMSSRStatPathPerfFix.dll')
)

$ErrorActionPreference = 'Stop'

dotnet build (Join-Path $PSScriptRoot 'Validation.csproj') -c Release --nologo `
    "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$HarmonyPath" "-p:DmssrPath=$DmssrPath"
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }

$driver = Join-Path $PSScriptRoot 'bin\Release\net10.0\Validation.dll'
foreach ($mode in @('bind', 'red', 'green')) {
    dotnet $driver $mode $GameRoot $HarmonyPath $DmssrPath $PatchPath
    if ($LASTEXITCODE -ne 0) { throw "Validation mode failed: $mode" }
}
