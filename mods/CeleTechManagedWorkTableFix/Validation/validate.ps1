param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$CeleTechPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\3446237098\1.7\Assemblies\CeleTech.Base.dll',
    [string]$PatchPath = (Join-Path $PSScriptRoot '..\Assemblies\CeleTechManagedWorkTableFix.dll')
)
$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'Validation.csproj') -c Release --nologo "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$HarmonyPath"
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }
$driver = Join-Path $PSScriptRoot 'bin\Release\net10.0\Validation.dll'
foreach ($mode in @('bind', 'red', 'green', 'absent')) {
    dotnet $driver $mode $GameRoot $HarmonyPath $CeleTechPath $PatchPath
    if ($LASTEXITCODE -ne 0) { throw "Validation mode failed: $mode" }
}
