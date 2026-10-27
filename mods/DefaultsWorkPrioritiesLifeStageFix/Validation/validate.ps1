param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$DefaultsPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\3285178686\1.6\Assemblies\Defaults.dll',
    [string]$PatchPath = (Join-Path $PSScriptRoot '..\Assemblies\DefaultsWorkPrioritiesLifeStageFix.dll')
)
$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'Validation.csproj') -c Release --nologo "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$HarmonyPath"
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }
$driver = Join-Path $PSScriptRoot 'bin\Release\net10.0\Validation.dll'
foreach ($mode in @('bind', 'red', 'green', 'rollback', 'absent')) {
    dotnet $driver $mode $GameRoot $HarmonyPath $DefaultsPath $PatchPath
    if ($LASTEXITCODE -ne 0) { throw "Validation mode failed: $mode" }
}
