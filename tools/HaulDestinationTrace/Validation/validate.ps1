param([Parameter(Mandatory = $true)][string]$QaDirectory)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Validation.csproj'
$driver = Join-Path $PSScriptRoot 'bin/Release/net10.0/Validation.dll'
dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic validation build failed.' }
dotnet $driver bind $QaDirectory
if ($LASTEXITCODE -ne 0) { throw 'QA-only initialization isolation failed.' }
dotnet $driver run $QaDirectory
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic validation failed.' }
