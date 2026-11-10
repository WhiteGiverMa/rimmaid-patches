param(
    [string]$GameRoot = 'A:/SteamLibrary/steamapps/common/RimWorld',
    [string]$HarmonyPath = 'A:/SteamLibrary/steamapps/workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll',
    [string]$PocketSandPath = 'A:/SteamLibrary/steamapps/workshop/content/294100/2226330302/1.6/Assemblies/PocketSand.dll',
    [string]$CecilPath = "$HOME/.nuget/packages/mono.cecil/0.11.6/lib/netstandard2.0/Mono.Cecil.dll",
    [string]$WorkDirectory = (Join-Path $env:TEMP 'opencode/pocketsand-reservation-validation'),
    [string]$PatchPath = ''
)
$ErrorActionPreference = 'Stop'
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
$GameRoot = [IO.Path]::GetFullPath($GameRoot)
$workshopRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PocketSandPath)))
foreach ($protected in @($GameRoot, $workshopRoot)) {
    $protected = [IO.Path]::GetFullPath($protected).TrimEnd('\', '/')
    if ($WorkDirectory.Equals($protected, [StringComparison]::OrdinalIgnoreCase) -or
        $WorkDirectory.StartsWith($protected + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The offline reference directory must be outside the game and Workshop roots.'
    }
}
$modRoot = Split-Path -Parent $PSScriptRoot
$sourceProject = Join-Path $modRoot 'Source/PocketSandEquipReservationFix.csproj'
$driverProject = Join-Path $PSScriptRoot 'Driver/Driver.csproj'
if (!$PatchPath) {
    & dotnet build $sourceProject -c Release --nologo "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$HarmonyPath"
    if ($LASTEXITCODE) { throw 'Patch build failed.' }
    $PatchPath = Join-Path $modRoot 'Source/bin/Release/net10.0/PocketSandEquipReservationFix.dll'
}
& dotnet build $driverProject -c Release --nologo "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$HarmonyPath"
if ($LASTEXITCODE) { throw 'Driver build failed.' }

if (!(Test-Path -LiteralPath $CecilPath)) { throw 'Mono.Cecil 0.11.6 is required only by this offline isolation script.' }
Add-Type -Path $CecilPath
New-Item -ItemType Directory -Force -Path $WorkDirectory | Out-Null
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($GameRoot)
$reader = [Mono.Cecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$sourceAssembly = Join-Path $GameRoot 'Assembly-CSharp.dll'
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($sourceAssembly, $reader)

# Strip only filesystem/configuration and renderer initialization in an offline copy.
# Reservation/job/PocketSand methods under test are never replaced or mocked.
$isolation = @{
    'Verse.ModsConfig' = @('.cctor')
    'Verse.ModLister' = @('.cctor')
    'Verse.Log' = @('.cctor', 'Message', 'Warning', 'Error', 'ErrorOnce', 'WarningOnce')
    'Verse.AI.ReservationManager' = @('.cctor')
    'Verse.PawnDestinationReservationManager' = @('.cctor')
}
$stripped = [Collections.Generic.List[string]]::new()
foreach ($typeName in $isolation.Keys) {
    $type = $assembly.MainModule.GetType($typeName)
    if (!$type) { throw ('Isolation type missing: ' + $typeName) }
    foreach ($method in $type.Methods) {
        if ($method.Name -in $isolation[$typeName] -and $method.HasBody -and $method.ReturnType.FullName -eq 'System.Void') {
            $method.Body = [Mono.Cecil.Cil.MethodBody]::new($method)
            $method.Body.GetILProcessor().Append([Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Ret))
            $stripped.Add($method.FullName)
        }
    }
}
$isolated = Join-Path $WorkDirectory 'Assembly-CSharp.dll'
$assembly.Write($isolated)
$assembly.Dispose()
$resolver.Dispose()
[pscustomobject]@{
    originalAssemblySha256 = (Get-FileHash -LiteralPath $sourceAssembly -Algorithm SHA256).Hash
    isolatedAssemblySha256 = (Get-FileHash -LiteralPath $isolated -Algorithm SHA256).Hash
    pocketSandSha256 = (Get-FileHash -LiteralPath $PocketSandPath -Algorithm SHA256).Hash
    patchSha256 = (Get-FileHash -LiteralPath $PatchPath -Algorithm SHA256).Hash
    strippedMethods = @($stripped)
    note = 'Offline copy only. Target methods and reservation/job logic are unchanged. No live game process, game config or save is touched.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $WorkDirectory 'isolation-receipt.json') -Encoding utf8

$env:RW_GAME_ROOT = $GameRoot
$env:RW_HARMONY_DLL = $HarmonyPath
$env:RW_POCKETSAND_DLL = $PocketSandPath
$driver = Join-Path $PSScriptRoot 'Driver/bin/Release/net10.0/Driver.dll'
& dotnet $driver $WorkDirectory $PatchPath
if ($LASTEXITCODE) { throw ('Real-DLL validation failed with exit code ' + $LASTEXITCODE) }
