param([Parameter(Mandatory = $true)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$mod = Split-Path -Parent $PSScriptRoot
[xml]$about = Get-Content -LiteralPath (Join-Path $mod 'About/About.xml') -Raw
$name = Split-Path -Leaf $mod
$payload = Join-Path $OutputDirectory $name
$zip = Join-Path $OutputDirectory "$name-$($about.ModMetaData.modVersion)-rw1.7.zip"
if ((Test-Path -LiteralPath $payload) -or (Test-Path -LiteralPath $zip)) {
    throw 'Package output already exists; choose a fresh output directory.'
}
$folders = @('About', 'Assemblies', 'Languages')
foreach ($folder in $folders) {
    if (-not (Test-Path -LiteralPath (Join-Path $mod $folder) -PathType Container)) {
        throw "Missing release directory: $folder"
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $mod 'Assemblies/MilianWeaponInteractions.dll'))) {
    throw 'Build and copy the verified release DLL into Assemblies first.'
}
New-Item -ItemType Directory -Path $payload -Force | Out-Null
foreach ($folder in $folders) {
    Copy-Item -LiteralPath (Join-Path $mod $folder) -Destination (Join-Path $payload $folder) -Recurse
}
Copy-Item -LiteralPath (Join-Path $mod 'README.md') -Destination (Join-Path $payload 'README.md')
Compress-Archive -LiteralPath $payload -DestinationPath $zip
Get-FileHash -LiteralPath $zip -Algorithm SHA256
