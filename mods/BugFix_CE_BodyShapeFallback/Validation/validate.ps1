# Regression driver for BugFix_CE_BodyShapeFallback.
# Builds the net48 harness (needs dotnet SDK + .NET Framework 4.8) and runs it against the
# given patch DLL. The harness loads real RimWorld, Harmony, Combat Extended and patch
# assemblies; it does not boot the game. Run with pwsh; default paths are this machine's.
param(
    [string]$PatchDll = "$PSScriptRoot\..\Assemblies\BugFix_CE_BodyShapeFallback.dll"
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Harness\BodyShapeFallbackHarness.csproj'
dotnet build $project -c Release -v minimal
if ($LASTEXITCODE -ne 0) { throw "Harness build failed with exit code $LASTEXITCODE" }

$exe = Join-Path $PSScriptRoot 'Harness\bin\Release\BodyShapeFallbackHarness.exe'
& $exe (Resolve-Path -LiteralPath $PatchDll).Path
exit $LASTEXITCODE
