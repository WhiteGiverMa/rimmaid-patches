param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$payload = Join-Path $output 'ReinforcedConcreteHeavyBridge'
$zip = Join-Path $output 'ReinforcedConcreteHeavyBridge-1.1.0-rw1.7.zip'
if (Test-Path -LiteralPath $payload) { throw "Refusing to overwrite existing package: $payload" }
if (Test-Path -LiteralPath $zip) { throw "Refusing to overwrite existing ZIP: $zip" }
$dll = Join-Path $modRoot 'Assemblies/ReinforcedConcreteHeavyBridge.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw 'Build and copy the release DLL to Assemblies first.' }
[void][System.IO.Directory]::CreateDirectory($payload)
foreach ($directory in @('About', 'Assemblies', 'Defs', 'Languages')) {
    Copy-Item -LiteralPath (Join-Path $modRoot $directory) -Destination $payload -Recurse
}
Copy-Item -LiteralPath (Join-Path $modRoot 'README.md') -Destination $payload
Compress-Archive -LiteralPath $payload -DestinationPath $zip -CompressionLevel Optimal
Get-FileHash -LiteralPath $zip -Algorithm SHA256
Write-Output "Ready to upload to Workshop: $payload"
