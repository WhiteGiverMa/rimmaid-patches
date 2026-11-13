param(
    [Parameter(Mandatory = $true)][string]$Qa,
    [string]$PatchDll = (Join-Path $PSScriptRoot '../1.7/Assemblies/UFLIIwbClipboardFix.dll')
)
$ErrorActionPreference = 'Stop'
$Qa = [IO.Path]::GetFullPath($Qa)
$PatchDll = [IO.Path]::GetFullPath($PatchDll)
New-Item -ItemType Directory -Path $Qa -Force | Out-Null
& dotnet build (Join-Path $PSScriptRoot 'Validation.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }
$driver = Join-Path $PSScriptRoot 'bin/Release/net10.0/Validation.dll'
foreach ($mode in @('bind', 'red', 'green', 'absent')) {
    & dotnet $driver $mode $Qa $PatchDll 2>&1 | Tee-Object -FilePath (Join-Path $Qa "validation-$mode.log")
    if ($LASTEXITCODE -ne 0) { throw "$mode validation failed." }
}
