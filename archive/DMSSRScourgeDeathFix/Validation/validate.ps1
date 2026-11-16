param(
    [ValidateSet('red', 'green', 'green-first', 'both')][string]$Mode = 'both',
    [string]$BoundGamePath = 'G:\dev\rimmaid-patches\mods\DMSEWorldPerfFix\Validation\bin\Release\net10.0\bound\Assembly-CSharp.dll',
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$WorkshopRoot = 'A:\SteamLibrary\steamapps\workshop\content\294100',
    [string]$LogDirectory = ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'dmssr-scourge-death-fix-validation'))
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $LogDirectory | Out-Null
$mod = Split-Path $PSScriptRoot -Parent
dotnet build "$mod\Source\DMSSRScourgeDeathFix.csproj" -c Release -v minimal "-p:GameRoot=$GameRoot" "-p:WorkshopRoot=$WorkshopRoot" 2>&1 | Tee-Object "$LogDirectory\release-build.log"
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
dotnet build "$PSScriptRoot\Validation.csproj" -c Release -v minimal "-p:BoundGamePath=$BoundGamePath" "-p:GameRoot=$GameRoot" "-p:WorkshopRoot=$WorkshopRoot" 2>&1 | Tee-Object "$LogDirectory\validation-build.log"
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }
foreach ($phase in @('red', 'green', 'green-first')) {
    if ($Mode -ne 'both' -and $Mode -ne $phase) { continue }
    dotnet "$PSScriptRoot\bin\Release\net10.0\Validation.dll" $phase $BoundGamePath $GameRoot $WorkshopRoot "$mod\1.7\Assemblies\DMSSRScourgeDeathFix.dll" 2>&1 | Tee-Object "$LogDirectory\$phase.log"
    if ($LASTEXITCODE -ne 0) { throw "$phase failed." }
}
