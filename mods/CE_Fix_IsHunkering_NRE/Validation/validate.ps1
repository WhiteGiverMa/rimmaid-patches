param(
    [string]$PatchDll = "$PSScriptRoot\..\1.6\Assemblies\CE_Fix_IsHunkering_NRE.dll",
    [string]$CombatExtendedDll = 'A:\SteamLibrary\steamapps\common\RimWorld\Mods\CombatExtendedLocal\Assemblies\CombatExtended.dll',
    [string]$HarmonyDll = 'A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\Current\Assemblies\0Harmony.dll',
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld'
)

# Seam-level validation in-process: load the real game/CE/Harmony assemblies, run the
# sidecar's [StaticConstructorOnStartup] constructor like RimWorld does, inspect Harmony
# topology and exercise the patch entry points. Does not modify the game or the repository.
# Exit code 0 = green topology, 1 = failures (point -PatchDll at the pre-fix DLL for red).
#
# RimWorld's MonoMod-based Harmony cannot initialize its shared state on CoreCLR, so when
# launched from pwsh this script re-runs itself under Windows PowerShell 5.1 (.NET Framework).
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -eq 'Core') {
    $winPs = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $winPs -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath `
        -PatchDll $PatchDll -CombatExtendedDll $CombatExtendedDll -HarmonyDll $HarmonyDll -GameRoot $GameRoot
    exit $LASTEXITCODE
}

$managed = "$GameRoot\RimWorldWin64_Data\Managed"
$loaded = @(
    "$managed\UnityEngine.CoreModule.dll",
    "$managed\Assembly-CSharp.dll",
    (Resolve-Path -LiteralPath $CombatExtendedDll).Path,
    (Resolve-Path -LiteralPath $HarmonyDll).Path,
    (Resolve-Path -LiteralPath $PatchDll).Path
)
foreach ($path in $loaded) {
    if (!(Test-Path -LiteralPath $path)) { throw "Required assembly not found: $path" }
    [void][Reflection.Assembly]::LoadFrom($path)
}
# Compile-time references exclude 0Harmony: its netstandard-era Span metadata is rejected
# by the PowerShell 5.1 in-box compiler. Driver.cs reaches Harmony APIs by reflection.
# -IgnoreWarnings: the in-box compiler reports metadata-only Span resolution warnings
# (no Span is used by Driver.cs); every code path is exercised at runtime below.
$compileReferences = @($loaded[0], $loaded[1], $loaded[2], $loaded[4])
Add-Type -Path "$PSScriptRoot\Driver.cs" -ReferencedAssemblies $compileReferences -IgnoreWarnings
$failures = [IsHunkeringPatchValidation]::Run()
if ($failures -gt 0) { exit 1 }
