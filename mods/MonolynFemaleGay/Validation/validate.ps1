[CmdletBinding()]
param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$WorkshopRoot = 'A:\SteamLibrary\steamapps\workshop\content\294100',
    [string]$HarmonyPath,
    [string]$AselPath
)

$ErrorActionPreference = 'Stop'
if (!$HarmonyPath) { $HarmonyPath = Join-Path $WorkshopRoot '2009463077/1.7/Assemblies/0Harmony.dll' }
if (!$AselPath) { $AselPath = Join-Path $WorkshopRoot '3742031864/1.7/Assemblies/ASEL.dll' }
$mod = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('opencode/monolyn-femalegay-qa-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null

try {
    dotnet build (Join-Path $PSScriptRoot 'Validation.csproj') -c Release -warnaserror "-p:GameRoot=$GameRoot" "-p:WorkshopRoot=$WorkshopRoot" "-p:HarmonyPath=$HarmonyPath" "-p:AselPath=$AselPath"
    if ($LASTEXITCODE -ne 0) { throw "Release build failed ($LASTEXITCODE)." }
    dotnet (Join-Path $PSScriptRoot 'bin/Release/net10.0/Validation.dll') $mod $GameRoot $HarmonyPath $AselPath $scratch
    if ($LASTEXITCODE -ne 0) { throw "Offline verification failed ($LASTEXITCODE)." }
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
