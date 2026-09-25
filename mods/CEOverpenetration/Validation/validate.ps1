param(
    [string]$PatchDll = "$PSScriptRoot\..\Source\CEOverpenetration\bin\CEOverpenetration.dll",
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld'
)

$ErrorActionPreference = 'Stop'
$managed = "$GameRoot\RimWorldWin64_Data\Managed"
$references = @("$managed\UnityEngine.CoreModule.dll", "$managed\Assembly-CSharp.dll", $PatchDll)
foreach ($path in $references) {
    if (!(Test-Path -LiteralPath $path)) { throw "Required assembly not found: $path" }
    [void][Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $path).Path)
}
Add-Type -Path "$PSScriptRoot\Driver.cs" -ReferencedAssemblies ($references + @(Get-ChildItem "$PSHOME\ref\*.dll").FullName)
[OverpenetrationSettingsValidation]::Run()
