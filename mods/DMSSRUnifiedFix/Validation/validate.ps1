param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$WorkshopRoot = 'A:\SteamLibrary\steamapps\workshop\content\294100',
    [string]$LogDirectory = ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'dmssr-unified-validation')),
    [string[]]$Modes = @('bind', 'carry-red', 'carry-green', 'stat-red', 'stat-green', 'stat-early-green', 'legion-red', 'legion-green', 'neuro-red', 'neuro-green', 'death-red', 'death-green', 'death-green-first', 'audited-red', 'audited-green', 'no-legion', 'composition', 'isolation', 'awake-red', 'awake-green', 'awake-composition')
)
$ErrorActionPreference = 'Stop'
$mod = Split-Path -Parent $PSScriptRoot
$env:DOTNET_TieredCompilation = '0'
New-Item -ItemType Directory -Force $LogDirectory | Out-Null
dotnet build "$mod\Source\DMSSRUnifiedFix.csproj" -c Release --nologo "-p:GameRoot=$GameRoot" "-p:WorkshopRoot=$WorkshopRoot" 2>&1 | Tee-Object "$LogDirectory\release-build.log"
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
dotnet build "$PSScriptRoot\Validation.csproj" -c Release --nologo "-p:GameRoot=$GameRoot" "-p:WorkshopRoot=$WorkshopRoot" 2>&1 | Tee-Object "$LogDirectory\validation-build.log"
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }
$patch = "$mod\1.7\Assemblies\DMSSRUnifiedFix.dll"
foreach ($mode in $Modes) {
    dotnet "$PSScriptRoot\bin\Release\net10.0\Validation.dll" $mode $GameRoot $WorkshopRoot $patch $mod 2>&1 | Tee-Object "$LogDirectory\$mode.log"
    if ($LASTEXITCODE -ne 0) { throw "Validation failed: $mode" }
}
$inputs = @("$GameRoot\Assembly-CSharp.dll", "$GameRoot\UnityStubs.dll", $patch)
$inputs += "$GameRoot\Mods\DMSMechStorage\Assemblies\DMSMechStorage.dll"
foreach ($entry in @(@('2009463077','0Harmony'),@('3819799478','DMS'),@('3498575851','Fortified'),@('3666117000','DMS_Legion'),@('3835512735','DMSSR'))) {
    $inputs += "$WorkshopRoot\$($entry[0])\1.7\Assemblies\$($entry[1]).dll"
}
$inputs | ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 } | Select-Object Path,Hash | ConvertTo-Json | Set-Content "$LogDirectory\validation-inputs.sha256.json"
Write-Output "PASS: $($Modes.Count) isolated validation phases. Logs and SHA256 receipts: $LogDirectory"
