param([Parameter(Mandatory = $true)][string]$QaDirectory)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Validation.csproj'
$driver = Join-Path $PSScriptRoot 'bin/Release/net10.0/Validation.dll'
dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
foreach ($mode in @('bind', 'red', 'green')) {
    dotnet $driver $mode $QaDirectory
    if ($LASTEXITCODE -ne 0) { throw "Validation mode failed: $mode" }
}
