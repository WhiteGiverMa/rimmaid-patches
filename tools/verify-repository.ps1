# Run after staging or cloning the repository. Does not modify the game or repository.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
$files = @(& git -C $root -c core.quotepath=false ls-files)
if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'No tracked files; stage the candidate or use a Git clone.' }
$seen = @{}
foreach ($mod in $manifest.mods) {
    [xml]$about = Get-Content -LiteralPath (Join-Path $root "mods/$($mod.directory)/About/About.xml") -Raw
    $id = [string]$about.ModMetaData.packageId
    if ($id -cne $mod.packageId) { throw "Package ID mismatch: $($mod.directory)" }
    if ($seen.ContainsKey($id.ToLowerInvariant())) { throw "Duplicate package ID: $id" }
    $seen[$id.ToLowerInvariant()] = $true
}
$xmlCount = 0
foreach ($relative in $files) {
    if ($relative -match '(^|/)(\.agents|\.omo|\.codegraph|bin|obj|\.git|handoffs?)/|(^|/)(STATUS\.md|ModsConfig\.xml|Prefs\.xml|Player(?:-prev)?\.log|\.env(?:\..*)?|\.debug-journal\.md)$|\.(rws|pdb|bak|log|old|prev)$') {
        throw "Private or generated file tracked: $relative"
    }
    if ($relative -match '(?i)(^|/)(Assembly-CSharp|UnityEngine[^/]*|0Harmony|CombatExtended|AdaptiveStorageFramework|KillFeed)\.dll$') {
        throw "Unexpected third-party binary: $relative"
    }
    if ($relative -match '\.(xml|csproj)$') {
        [xml]$document = Get-Content -LiteralPath (Join-Path $root $relative) -Raw
        $xmlCount++
    }
    if ($relative -match '\.(cs|csproj|md|txt|json|xml|ps1|patch)$' -and $relative -ne 'tools/verify-repository.ps1') {
        $text = Get-Content -LiteralPath (Join-Path $root $relative) -Raw
        if ($text -match 'gho_[a-zA-Z0-9]{20,}|github_pat_[a-zA-Z0-9_]{20,}|-----BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY-----|sk-[a-zA-Z0-9_-]{20,}') {
            throw "Potential credential in $relative"
        }
    }
}
$binaryFiles = @($files | Where-Object { $_.EndsWith('.dll') })
$checksums = @(Get-Content -LiteralPath (Join-Path $root 'checksums.sha256'))
if ($checksums.Count -ne $binaryFiles.Count) { throw 'Binary checksum inventory mismatch' }
$hashedPaths = @{}
foreach ($line in $checksums) {
    if ($line -notmatch '^([a-f0-9]{64})  (.+)$') { throw "Invalid checksum: $line" }
    $expected, $relative = $Matches[1], $Matches[2]
    if ($relative -notin $binaryFiles -or $hashedPaths.ContainsKey($relative)) { throw "Unexpected or duplicate checksum path: $relative" }
    $hashedPaths[$relative] = $true
    if ((Get-FileHash -LiteralPath (Join-Path $root $relative) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw "Binary changed: $relative" }
}
Write-Output "PASS: $($manifest.mods.Count) mod IDs, $xmlCount XML/project files, $($binaryFiles.Count) retained binary hashes, $($files.Count) tracked files checked."
Write-Output 'No game or Workshop files were modified. This is archive validation, not game QA.'
