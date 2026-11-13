param(
    [string]$Configuration = "Release",
    [string]$GameRoot = "A:\SteamLibrary\steamapps\common\RimWorld",
    [string]$HarmonyDll = "A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll",
    [string]$HarAnimateDll = "A:\SteamLibrary\steamapps\workshop\content\294100\3761190956\Assemblies\HARAnimate.dll",
    [string]$AlienRaceDll = "A:\SteamLibrary\steamapps\workshop\content\294100\839005762\1.7\Assemblies\AlienRace.dll",
    [string]$PatchDll = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($PatchDll)) {
    $PatchDll = Join-Path $root "Source\bin\$Configuration\net10.0\HARAnimatePerfFix.dll"
}

Write-Host "== build =="
dotnet build (Join-Path $root "Source\HARAnimatePerfFix.csproj") -c $Configuration -v m
if ($LASTEXITCODE -ne 0) { throw "Source build failed." }
dotnet build (Join-Path $root "Validation\Validation.csproj") -c $Configuration -v m
if ($LASTEXITCODE -ne 0) { throw "Validation build failed." }

$exe = Join-Path $PSScriptRoot "bin\$Configuration\net10.0\Validation.dll"

Write-Host ""
Write-Host "== bind (QA copy of Assembly-CSharp; ModsConfig cctor emptied) =="
dotnet $exe bind $GameRoot $HarmonyDll $HarAnimateDll $AlienRaceDll $PatchDll
if ($LASTEXITCODE -ne 0) { throw "bind failed." }

Write-Host ""
Write-Host "== RED (unpatched upstream) =="
dotnet $exe red $GameRoot $HarmonyDll $HarAnimateDll $AlienRaceDll $PatchDll
if ($LASTEXITCODE -ne 0) { throw "RED run failed." }

Write-Host ""
Write-Host "== GREEN (patched) =="
dotnet $exe green $GameRoot $HarmonyDll $HarAnimateDll $AlienRaceDll $PatchDll
if ($LASTEXITCODE -ne 0) { throw "GREEN run failed." }

Write-Host ""
Write-Host "ALL VALIDATION STEPS PASSED"
