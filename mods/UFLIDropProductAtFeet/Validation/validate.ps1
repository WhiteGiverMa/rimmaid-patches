param(
    [string]$Qa = "C:\Users\A1337\AppData\Local\Temp\opencode\ufli-drop-at-feet-20261109"
)

$ErrorActionPreference = "Stop"
$project = $PSScriptRoot
$qaFull = [System.IO.Path]::GetFullPath($Qa)

function Invoke-Validation([string]$Mode, [string]$LogName) {
    dotnet run -c Release --project $project -- $Mode $qaFull | Tee-Object -FilePath (Join-Path $qaFull $LogName)
    if ($LASTEXITCODE -ne 0) {
        throw "Validation '$Mode' failed with exit code $LASTEXITCODE"
    }
}

Write-Host "QA: $qaFull"
Invoke-Validation "bind" "validation-bind.log"
Invoke-Validation "red" "validation-red.log"
Invoke-Validation "green" "validation-green.log"
Write-Host "Validation complete. Logs are in $qaFull"
