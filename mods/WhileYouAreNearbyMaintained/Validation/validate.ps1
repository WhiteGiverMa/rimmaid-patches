param(
    [Parameter(Mandatory)][string]$QaDirectory,
    [string]$GameRoot = 'A:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$LegacyDll,
    [string]$CecilPath
)
$ErrorActionPreference = 'Stop'
$qa = [IO.Path]::GetFullPath($QaDirectory)
$game = [IO.Path]::GetFullPath($GameRoot)
$modRoot = Split-Path -Parent $PSScriptRoot
foreach ($protected in @($game, $modRoot, (Split-Path -Parent $HarmonyPath))) {
    if ($qa.StartsWith([IO.Path]::GetFullPath($protected).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $qa -eq [IO.Path]::GetFullPath($protected)) {
        throw "Use a separate temporary QA directory, not $protected"
    }
}
if (-not $CecilPath) {
    $CecilPath = Get-ChildItem -LiteralPath (Join-Path $HOME '.dotnet/tools/.store/ilspycmd') -Filter Mono.Cecil.dll -Recurse -File |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $CecilPath) { throw 'Pass -CecilPath to an existing Mono.Cecil.dll (for example from ilspycmd).' }
New-Item -ItemType Directory -Force -Path $qa | Out-Null
Add-Type -Path $CecilPath
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($game)
$resolver.AddSearchDirectory((Split-Path -Parent $HarmonyPath))
$resolver.AddSearchDirectory([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory())
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $game 'Assembly-CSharp.dll'), $parameters)
foreach ($name in @('Verse.ModsConfig', 'Verse.ModLister', 'Verse.BaseContent')) {
    $type = $assembly.MainModule.Types | Where-Object FullName -EQ $name
    $initializer = $type.Methods | Where-Object Name -EQ '.cctor'
    $initializer.Body.Instructions.Clear()
    $initializer.Body.ExceptionHandlers.Clear()
    $initializer.Body.Variables.Clear()
    $initializer.Body.GetILProcessor().Append([Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Ret))
}
$assembly.Write((Join-Path $qa 'Assembly-CSharp.dll'))
$assembly.Dispose()
if ($LegacyDll) {
    $legacy = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($LegacyDll, $parameters)
    foreach ($reference in $legacy.MainModule.AssemblyReferences) {
        if ($reference.Name.StartsWith('UnityEngine.')) {
            $reference.Name = 'UnityStubs'
            $reference.Version = [Version]'1.0.0.0'
            $reference.PublicKeyToken = [byte[]]@()
        }
    }
    $legacy.Write((Join-Path $qa 'WhileYouAreNearby.dll'))
    $legacy.Dispose()
}
& dotnet build (Join-Path $modRoot 'Source/WhileYouAreNearbyMaintained.csproj') -c Release --nologo "-p:GameRoot=$game" "-p:HarmonyPath=$HarmonyPath"
if ($LASTEXITCODE -ne 0) { throw 'Production build failed.' }
& dotnet build (Join-Path $PSScriptRoot 'Validation.csproj') -c Release --nologo "-p:GameRoot=$game" "-p:HarmonyPath=$HarmonyPath"
if ($LASTEXITCODE -ne 0) { throw 'Validation build failed.' }
$driver = Join-Path $PSScriptRoot 'bin/Release/net10.0/Validation.dll'
if ($LegacyDll) {
    & dotnet $driver $game $qa $HarmonyPath (Join-Path $qa 'WhileYouAreNearby.dll') legacy 2>&1 | Tee-Object -FilePath (Join-Path $qa 'legacy.log')
    if ($LASTEXITCODE -ne 0) { throw 'Legacy control failed.' }
}
$maintained = Join-Path $modRoot 'Source/bin/Release/net10.0/WhileYouAreNearbyMaintained.dll'
& dotnet $driver $game $qa $HarmonyPath $maintained maintained 2>&1 | Tee-Object -FilePath (Join-Path $qa 'maintained.log')
if ($LASTEXITCODE -ne 0) { throw 'Maintained validation failed.' }
Write-Output 'Validated isolated DLLs only; no game deployment or real config/save modification.'
