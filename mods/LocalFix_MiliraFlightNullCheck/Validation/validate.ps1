param(
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$WorkshopRoot = 'A:\SteamLibrary\steamapps\workshop\content\294100',
    [string]$PatchDll = "$PSScriptRoot\..\1.6\Assemblies\LocalFix_MiliraFlightNullCheck.dll",
    [switch]$NoBuild,
    [switch]$Help
)

if ($Help) {
    Write-Output 'Usage: pwsh -File validate.ps1 [-GameRoot <path>] [-WorkshopRoot <path>] [-PatchDll <dll>] [-NoBuild]'
    Write-Output 'Runs the patch against the real game and Milira assemblies in isolation. Does not launch or modify the game.'
    exit 0
}

$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameRoot 'RimWorldWin64_Data\Managed'
$harmony = Join-Path $WorkshopRoot '2009463077\Current\Assemblies\0Harmony.dll'
$milira  = Join-Path $WorkshopRoot '3256974620\1.6\Assemblies\Milira.dll'

foreach ($path in @($managed, $harmony, $milira, $PatchDll)) {
    if (!(Test-Path -LiteralPath $path)) { throw "Required path not found: $path" }
}

$driverProject = Join-Path $PSScriptRoot 'Driver\LocalFix_MiliraFlightNullCheck.Validation.csproj'
$driverExe = Join-Path $PSScriptRoot 'Driver\bin\Release\net48\LocalFix_MiliraFlightNullCheck.Validation.exe'

if (!$NoBuild) {
    dotnet build $driverProject -c Release | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Validation driver build failed.' }
}
if (!(Test-Path -LiteralPath $driverExe)) { throw "Validation driver not found: $driverExe" }

Write-Host ('PatchDll SHA256: ' + (Get-FileHash -Algorithm SHA256 -LiteralPath $PatchDll).Hash)
Write-Host ('Milira   SHA256: ' + (Get-FileHash -Algorithm SHA256 -LiteralPath $milira).Hash)
Write-Host ('Harmony  SHA256: ' + (Get-FileHash -Algorithm SHA256 -LiteralPath $harmony).Hash)

$script:anyFailed = $false
function Invoke-Mode([string]$title, [string[]]$modeArguments) {
    Write-Host ''
    Write-Host ('### ' + $title)
    & $driverExe @modeArguments
    if ($LASTEXITCODE -ne 0) {
        $script:anyFailed = $true
        Write-Host ('MODE FAILED: ' + $title + ' (exit ' + $LASTEXITCODE + ')')
    }
}

Invoke-Mode 'inspect - self-registration present in the packaged DLL' @('inspect', $PatchDll, $managed, $harmony, $milira)
Invoke-Mode 'red - upstream crash without the guard' @('red', $managed, $harmony, $milira)
Invoke-Mode 'green - registered guard prevents the crash' @('green', $PatchDll, $managed, $harmony, $milira)

if ($script:anyFailed) {
    Write-Host ''
    Write-Host 'VALIDATION FAILED'
    exit 1
}
Write-Host ''
Write-Host 'VALIDATION PASSED'
exit 0
