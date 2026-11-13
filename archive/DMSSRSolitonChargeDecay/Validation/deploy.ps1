param(
    [string]$GameRoot = 'A:/SteamLibrary/steamapps/common/RimWorld',
    [string]$ConfigPath = "$env:USERPROFILE/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Config/ModsConfig.xml",
    [string]$ReceiptRoot = "$env:LOCALAPPDATA/Temp/opencode/dmssr-soliton-20261112",
    [switch]$Enable
)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $GameRoot 'Mods/DMSSRSolitonChargeDecay'
$running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in @('RimWorld', 'RimWorldWin64') })
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$receipt = Join-Path $ReceiptRoot "deploy-$stamp"
New-Item -ItemType Directory -Force -Path $receipt | Out-Null
$beforeBytes = [IO.File]::ReadAllBytes($ConfigPath)
$beforeText = [Text.Encoding]::UTF8.GetString($beforeBytes)
[xml]$beforeXml = $beforeText.TrimStart([char]0xFEFF)
$beforeIds = @($beforeXml.ModsConfigData.activeMods.li | ForEach-Object { [string]$_ })
$package = 'whitegiver.dmssr.solitonchargedecay'
$parentId = 'cephalopod.deadmanswitch.synthriada'
if (@($beforeIds | Where-Object { $_ -eq $parentId }).Count -ne 1) { throw 'Expected exactly one active Synthriada entry.' }
$configAfter = $beforeText
if ($Enable -and $package -notin $beforeIds) {
    $pattern = '(?im)^(?<indent>[ \t]*)<li>cephalopod\.deadmanswitch\.synthriada</li>(?<eol>\r?\n)'
    $matches = [regex]::Matches($beforeText, $pattern)
    if ($matches.Count -ne 1) { throw 'Expected one complete Synthriada config line.' }
    $match = $matches[0]
    $addition = $match.Groups['indent'].Value + '<li>' + $package + '</li>' + $match.Groups['eol'].Value
    $configAfter = $beforeText.Insert($match.Index + $match.Length, $addition)
    [xml]$afterXml = $configAfter.TrimStart([char]0xFEFF)
    $afterIds = @($afterXml.ModsConfigData.activeMods.li | ForEach-Object { [string]$_ })
    if ((($afterIds | Where-Object { $_ -ne $package }) -join "`n") -cne ($beforeIds -join "`n")) {
        throw 'Existing mod entries or ordering changed.'
    }
}
elseif ($Enable) {
    $index = [Array]::IndexOf($beforeIds, $parentId)
    if (@($beforeIds | Where-Object { $_ -eq $package }).Count -ne 1 -or $beforeIds[$index + 1] -ne $package) {
        throw 'Existing patch entry must occur once, immediately after Synthriada.'
    }
}
if (Test-Path -LiteralPath $destination) { Copy-Item -LiteralPath $destination -Destination (Join-Path $receipt 'installed-before') -Recurse }
Copy-Item -LiteralPath $ConfigPath -Destination (Join-Path $receipt 'ModsConfig-before.xml')
$files = @('About/About.xml', 'LoadFolders.xml', 'README.md', 'INVESTIGATION.md', 'AUTHOR-FEEDBACK.md', '1.7/Assemblies/DMSSRSolitonChargeDecay.dll')
$hashes = [Collections.Generic.List[object]]::new()
foreach ($relative in $files) {
    $source = Join-Path $modRoot $relative
    $target = Join-Path $destination $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
    $expected = (Get-FileHash -LiteralPath $source).Hash
    $actual = (Get-FileHash -LiteralPath $target).Hash
    if ($expected -cne $actual) { throw "Installed hash mismatch: $relative" }
    $hashes.Add([pscustomobject]@{ path = $relative; sha256 = $actual })
}
if ($Enable -and $configAfter -cne $beforeText) {
    $currentHash = (Get-FileHash -LiteralPath $ConfigPath).Hash
    $backupHash = (Get-FileHash -LiteralPath (Join-Path $receipt 'ModsConfig-before.xml')).Hash
    if ($currentHash -cne $backupHash) { throw 'ModsConfig changed during deployment; retry with its current contents.' }
    [IO.File]::WriteAllBytes($ConfigPath, [Text.Encoding]::UTF8.GetBytes($configAfter))
}
$finalHash = (Get-FileHash -LiteralPath $ConfigPath).Hash
if (!$Enable -and $finalHash -cne (Get-FileHash -LiteralPath (Join-Path $receipt 'ModsConfig-before.xml')).Hash) {
    throw 'Configuration changed during copy-only deployment.'
}
[xml]$finalXml = [IO.File]::ReadAllText($ConfigPath)
$finalIds = @($finalXml.ModsConfigData.activeMods.li)
[pscustomobject]@{
    timestamp = (Get-Date).ToString('o')
    destination = $destination
    runningProcessIds = @($running.Id)
    enabled = $package -in $finalIds
    activeModsBefore = $beforeIds.Count
    activeModsAfter = $finalIds.Count
    configSha256 = $finalHash
    files = @($hashes)
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $receipt 'deploy-receipt.json') -Encoding utf8
Write-Output "Installed $destination; enabled=$($package -in $finalIds); receipt=$receipt"
