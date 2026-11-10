param(
    [string]$GameRoot = 'A:/SteamLibrary/steamapps/common/RimWorld',
    [string]$HarmonyPath = 'A:/SteamLibrary/steamapps/workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll',
    [string]$CecilPath = "$HOME/.nuget/packages/mono.cecil/0.11.6/lib/netstandard2.0/Mono.Cecil.dll",
    [string]$WorkDirectory = (Join-Path $env:TEMP 'opencode/equippable-ability-validation'),
    [string]$PatchPath = ''
)
$ErrorActionPreference = 'Stop'
$GameRoot = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\', '/')
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if ($WorkDirectory.Equals($GameRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $WorkDirectory.StartsWith($GameRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Offline validation must not write inside the game directory.'
}
if (!(Test-Path -LiteralPath $CecilPath)) { throw 'Mono.Cecil 0.11.6 is required for offline isolation.' }
$modRoot = Split-Path -Parent $PSScriptRoot
if (!$PatchPath) {
    & dotnet build (Join-Path $modRoot 'Source/EquippableAbilityPsycastLoadFix.csproj') -c Release --nologo "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$HarmonyPath"
    if ($LASTEXITCODE) { throw 'Patch build failed.' }
    $PatchPath = Join-Path $modRoot 'Source/bin/Release/net10.0/EquippableAbilityPsycastLoadFix.dll'
}
& dotnet build (Join-Path $PSScriptRoot 'Driver/Driver.csproj') -c Release --nologo "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$HarmonyPath" "-p:FixPath=$PatchPath"
if ($LASTEXITCODE) { throw 'Driver build failed.' }

Add-Type -Path $CecilPath
$bound = Join-Path $WorkDirectory 'bound'
New-Item -ItemType Directory -Force -Path $bound | Out-Null
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($GameRoot)
$reader = [Mono.Cecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$original = Join-Path $GameRoot 'Assembly-CSharp.dll'
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($original, $reader)
$isolated = [Collections.Generic.List[string]]::new()
foreach ($typeName in @('Verse.ModsConfig', 'Verse.ModLister', 'Verse.BaseContent', 'Verse.Log')) {
    foreach ($method in $assembly.MainModule.GetType($typeName).Methods) {
        if ($method.HasBody -and $method.ReturnType.FullName -eq 'System.Void' -and
            ($method.Name -eq '.cctor' -or ($typeName -eq 'Verse.Log' -and $method.Name -in @('Message','Warning','Error','ErrorOnce','WarningOnce')))) {
            $method.Body = [Mono.Cecil.Cil.MethodBody]::new($method)
            $method.Body.GetILProcessor().Append([Mono.Cecil.Cil.Instruction]::Create([Mono.Cecil.Cil.OpCodes]::Ret))
            $isolated.Add($method.FullName)
        }
    }
}
$assembly.Write((Join-Path $bound 'Assembly-CSharp.dll'))
$assembly.Dispose()
$resolver.Dispose()
[pscustomobject]@{
    originalAssemblySha256 = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
    patchSha256 = (Get-FileHash -LiteralPath $PatchPath -Algorithm SHA256).Hash
    isolatedMethods = @($isolated)
    note = 'Only a temporary game DLL copy is isolated. CompEquippableAbility, Ability/Psycast and Scribe save/load methods are unchanged. XML fixtures are not player saves.'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $WorkDirectory 'isolation-receipt.json') -Encoding utf8
$env:RW_VALIDATION_REFS = $bound
$env:RW_VALIDATION_OUTPUT = Join-Path $WorkDirectory 'roundtrip'
$driver = Join-Path $PSScriptRoot 'Driver/bin/Release/net10.0/Driver.dll'
& dotnet $driver | Tee-Object -FilePath (Join-Path $WorkDirectory 'validation.txt')
if ($LASTEXITCODE) { throw 'Real Scribe regression failed.' }
