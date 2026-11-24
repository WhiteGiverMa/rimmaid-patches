param(
    [string]$Qa = "C:\Users\A1337\AppData\Local\Temp\opencode\ufli-virtualworker-fix-20261108"
)

$ErrorActionPreference = "Stop"
$project = $PSScriptRoot
$qaFull = [System.IO.Path]::GetFullPath($Qa)

Write-Host "QA: $qaFull"
dotnet run -c Release --project $project -- bind $qaFull
dotnet run -c Release --project $project -- red $qaFull | Tee-Object -FilePath (Join-Path $qaFull "validation-red.log")
dotnet run -c Release --project $project -- green $qaFull | Tee-Object -FilePath (Join-Path $qaFull "validation-green.log")
Write-Host "Validation complete. Logs are in $qaFull"
