param(
    [string]$ConfigFixture = "$env:USERPROFILE/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Config/ModsConfig.xml",
    [string]$WorkDirectory = "$env:LOCALAPPDATA/Temp/opencode/dmssr-soliton-20261112/deployment-validation"
)
$ErrorActionPreference = 'Stop'
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if (!$WorkDirectory.StartsWith([IO.Path]::GetFullPath("$env:LOCALAPPDATA/Temp/opencode/").TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use an opencode Temp subdirectory for this deployment fixture.'
}
$sandbox = Join-Path $WorkDirectory (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
New-Item -ItemType Directory -Force -Path $sandbox | Out-Null
$config = Join-Path $sandbox 'ModsConfig.xml'
Copy-Item -LiteralPath $ConfigFixture -Destination $config
$original = [IO.File]::ReadAllBytes($config)
[xml]$before = [IO.File]::ReadAllText($config)
$beforeIds = @($before.ModsConfigData.activeMods.li)
$gameRoot = Join-Path $sandbox 'Game'
$receiptRoot = Join-Path $sandbox 'receipts'
$deploy = Join-Path $PSScriptRoot 'deploy.ps1'

& {
    function Get-Process { param([string]$ErrorAction) @() }
    & $deploy -GameRoot $gameRoot -ConfigPath $config -ReceiptRoot $receiptRoot -Enable
    & $deploy -GameRoot $gameRoot -ConfigPath $config -ReceiptRoot $receiptRoot -Enable
}
[xml]$after = [IO.File]::ReadAllText($config)
$afterIds = @($after.ModsConfigData.activeMods.li)
$package = 'whitegiver.dmssr.solitonchargedecay'
$index = [Array]::IndexOf($afterIds, 'cephalopod.deadmanswitch.synthriada')
if ($afterIds.Count -ne $beforeIds.Count + 1 -or $afterIds[$index + 1] -cne $package) { throw 'Insert position/count mismatch.' }
if ((($afterIds | Where-Object { $_ -ne $package }) -join "`n") -cne ($beforeIds -join "`n")) { throw 'Existing sequence changed.' }
$afterText = [IO.File]::ReadAllText($config)
$withoutAddition = [regex]::Replace($afterText, '(?im)^[ \t]*<li>whitegiver\.dmssr\.solitonchargedecay</li>\r?\n', '')
if (![Linq.Enumerable]::SequenceEqual[byte]([Text.Encoding]::UTF8.GetBytes($withoutAddition), $original)) {
    throw 'Existing configuration bytes changed.'
}
$backups = @(Get-ChildItem -Recurse -Filter ModsConfig-before.xml -LiteralPath $receiptRoot)
if ($backups.Count -ne 2) { throw 'Expected a backup from each run.' }
$modRoot = Split-Path -Parent $PSScriptRoot
foreach ($relative in @('About/About.xml', 'LoadFolders.xml', 'README.md', 'INVESTIGATION.md', 'AUTHOR-FEEDBACK.md', '1.7/Assemblies/DMSSRSolitonChargeDecay.dll')) {
    $target = Join-Path $gameRoot "Mods/DMSSRSolitonChargeDecay/$relative"
    if ((Get-FileHash -LiteralPath $target).Hash -cne (Get-FileHash -LiteralPath (Join-Path $modRoot $relative)).Hash) {
        throw "Fixture deployment hash mismatch: $relative"
    }
}
Write-Output "PASS: sandbox deployment inserts one entry, preserves original bytes/order, backs up configuration, matches six files and is idempotent. Output: $sandbox"
