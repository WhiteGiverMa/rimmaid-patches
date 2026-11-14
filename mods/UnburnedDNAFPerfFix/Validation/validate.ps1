param([string]$Logs = 'C:\Users\A1337\AppData\Local\Temp\opencode\unburned-dnaf-perf-20261114')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force $Logs | Out-Null
dotnet build "$root/Source/UnburnedDNAFPerfFix.csproj" -c Release 2>&1 | Tee-Object "$Logs/build.log"
if ($LASTEXITCODE) { throw 'Runtime build failed' }
dotnet build "$PSScriptRoot/Validation.csproj" -c Release 2>&1 | Tee-Object "$Logs/validation-build.log"
if ($LASTEXITCODE) { throw 'Validation build failed' }
dotnet run --project "$PSScriptRoot/Validation.csproj" -c Release --no-build -- bind 2>&1 | Tee-Object "$Logs/bind.log"
if ($LASTEXITCODE) { throw 'QA assembly binding failed' }
foreach ($case in @(
    @{Name='red'; Args=@('red')},
    @{Name='green'; Args=@('green')},
    @{Name='green-no-dnaf'; Args=@('green','--no-dnaf')},
    @{Name='green-sidecar-first'; Args=@('green','--sidecar-first')}
)) {
    $caseArgs = $case.Args
    dotnet run --project "$PSScriptRoot/Validation.csproj" -c Release --no-build -- @caseArgs 2>&1 | Tee-Object "$Logs/$($case.Name).log"
    if ($LASTEXITCODE) { throw "Validation failed: $($case.Name)" }
}
Write-Output 'All RED reproduction and GREEN regression modes passed.'
