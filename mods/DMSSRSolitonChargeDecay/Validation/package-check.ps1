param([string]$PackageRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$checks = 0
$files = @(Get-ChildItem -Recurse -File -LiteralPath $PackageRoot | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
foreach ($file in $files) {
    if ($file.Extension -in @('.xml', '.csproj')) {
        [xml]$document = [IO.File]::ReadAllText($file.FullName)
        $checks++
    }
    if ($file.Extension -eq '.ps1') {
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
        if ($errors.Count) { throw ($errors | Out-String) }
        $checks++
    }
}
[xml]$about = [IO.File]::ReadAllText((Join-Path $PackageRoot 'About/About.xml'))
if ($about.ModMetaData.packageId -cne 'whitegiver.dmssr.solitonchargedecay') { throw 'Unexpected packageId.' }
$checks++
$dll = Join-Path $PackageRoot '1.7/Assemblies/DMSSRSolitonChargeDecay.dll'
if ((Get-FileHash -LiteralPath $dll).Hash -cne '116E1A68A316640E4670C9847EE52BC84B42D75C3BE2C32F0BF46881E4083A99') {
    throw 'Release DLL differs from the validated build.'
}
$checks++
Write-Output "PASS: $checks package checks, $($files.Count) package files."
