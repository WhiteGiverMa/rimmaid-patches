param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$WorkshopRoot = 'A:\SteamLibrary\steamapps\workshop\content\294100',
    [string]$CombatExtendedRoot = "$GameRoot\Mods\CombatExtendedLocal",
    [string]$PatchDll = "$PSScriptRoot\..\1.6\Source\bin\Release\net48\WolfeinWeaponBoxCE.dll",
    [switch]$Help
)

if ($Help) {
    Write-Output 'Usage: pwsh -File validate.ps1 [-GameRoot <path>] [-WorkshopRoot <path>] [-CombatExtendedRoot <path>] [-PatchDll <path>]'
    Write-Output 'Runs real patch/game code in isolation. Does not launch or modify the game.'
    exit 0
}
$ErrorActionPreference = 'Stop'
$managed = "$GameRoot\RimWorldWin64_Data\Managed"
$references = @("$managed\UnityEngine.CoreModule.dll", "$managed\Assembly-CSharp.dll", $PatchDll)
foreach ($path in $references) {
    if (!(Test-Path -LiteralPath $path)) { throw "Required assembly not found: $path" }
    [void][Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $path).Path)
}
# Strongly typed calls avoid PowerShell's case-insensitive CLS collisions on RimWorld fields/properties.
Add-Type -Path "$PSScriptRoot\Driver.cs" -ReferencedAssemblies ($references + @(Get-ChildItem "$PSHOME\ref\*.dll").FullName)
[WeaponBoxValidation]::Run(
    "$WorkshopRoot\3473140562\1.6\Defs\ThingDefs_Apparel\Apparel_Sundry.xml",
    "$CombatExtendedRoot\Defs\Stats\Stats_Pawns_Inventory.xml"
)
