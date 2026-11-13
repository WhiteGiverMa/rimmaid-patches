param(
    [string]$GameRoot = 'A:/SteamLibrary/steamapps/common/RimWorld',
    [string]$WorkshopRoot = 'A:/SteamLibrary/steamapps/workshop/content/294100',
    [string]$CecilPath = "$HOME/.nuget/packages/mono.cecil/0.11.6/lib/netstandard2.0/Mono.Cecil.dll",
    [string]$WorkDirectory = (Join-Path $env:LOCALAPPDATA 'Temp/opencode/dmssr-soliton-20261112/validation'),
    [string]$PatchPath = ''
)
$ErrorActionPreference = 'Stop'
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
foreach ($protected in @($GameRoot, $WorkshopRoot)) {
    $protected = [IO.Path]::GetFullPath($protected).TrimEnd('\', '/')
    if ($WorkDirectory.Equals($protected, [StringComparison]::OrdinalIgnoreCase) -or
        $WorkDirectory.StartsWith($protected + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Choose an offline work directory outside the game and Workshop roots.'
    }
}
$harmonyPath = Join-Path $WorkshopRoot '2009463077/1.7/Assemblies/0Harmony.dll'
$dmssrPath = Join-Path $WorkshopRoot '3835512735/1.7/Assemblies/DMSSR.dll'
$modRoot = Split-Path -Parent $PSScriptRoot
if (!$PatchPath) {
    & dotnet build (Join-Path $modRoot 'Source/DMSSRSolitonChargeDecay.csproj') -c Release --nologo "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$harmonyPath" "-p:DmssrPath=$dmssrPath"
    if ($LASTEXITCODE) { throw 'Patch build failed.' }
    $PatchPath = Join-Path $modRoot 'Source/bin/Release/net10.0/DMSSRSolitonChargeDecay.dll'
}
$driverProject = Join-Path $PSScriptRoot 'Driver/Driver.csproj'
& dotnet build $driverProject -c Release --nologo "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$harmonyPath" "-p:DmssrPath=$dmssrPath"
if ($LASTEXITCODE) { throw 'Driver build failed.' }
Add-Type -Path $CecilPath
New-Item -ItemType Directory -Force -Path $WorkDirectory | Out-Null
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($GameRoot)
$reader = [Mono.Cecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$source = Join-Path $GameRoot 'Assembly-CSharp.dll'
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($source, $reader)
$targetBodies = @{}
foreach ($typeName in @('Verse.Verb', 'RimWorld.Building_TurretGun', 'Verse.VerbTracker', 'Verse.Pawn_StanceTracker')) {
    foreach ($method in $assembly.MainModule.GetType($typeName).Methods) {
        if ($method.HasBody -and $method.Name -ne '.cctor') {
            $targetBodies[$method.FullName] = ($method.Body.Instructions | ForEach-Object { $_.ToString() }) -join "`n"
        }
    }
}
$stripped = [Collections.Generic.List[string]]::new()
foreach ($typeName in @('Verse.ModsConfig', 'Verse.ModLister', 'Verse.Log', 'RimWorld.Building_TurretGun')) {
    $type = $assembly.MainModule.GetType($typeName)
    foreach ($method in $type.Methods) {
        if (($method.Name -eq '.cctor' -or ($typeName -eq 'Verse.Log' -and $method.Name -in @('Message', 'Warning', 'Error', 'ErrorOnce', 'WarningOnce'))) -and
            $method.HasBody -and $method.ReturnType.FullName -eq 'System.Void') {
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
$check = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($isolated)
$verified = 0
foreach ($typeName in @('Verse.Verb', 'RimWorld.Building_TurretGun', 'Verse.VerbTracker', 'Verse.Pawn_StanceTracker')) {
    foreach ($method in $check.MainModule.GetType($typeName).Methods) {
        if ($targetBodies.ContainsKey($method.FullName)) {
            $body = ($method.Body.Instructions | ForEach-Object { $_.ToString() }) -join "`n"
            if ($body -cne $targetBodies[$method.FullName]) { throw "Isolated method changed: $($method.FullName)" }
            $verified++
        }
    }
}
$check.Dispose()
if ($verified -ne $targetBodies.Count) { throw 'Isolated method inventory changed.' }
[pscustomobject]@{
    gameSha256 = (Get-FileHash $source).Hash
    isolatedGameSha256 = (Get-FileHash $isolated).Hash
    dmssrSha256 = (Get-FileHash $dmssrPath).Hash
    harmonySha256 = (Get-FileHash $harmonyPath).Hash
    patchSha256 = $(if ($PatchPath -notin @('red', 'expect-decay')) { (Get-FileHash $PatchPath).Hash } else { $null })
    strippedMethods = @($stripped)
    unchangedMethodBodies = $verified
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $WorkDirectory 'isolation-receipt.json') -Encoding utf8
$env:RW_GAME_ROOT = $GameRoot
$env:RW_WORKSHOP_ROOT = $WorkshopRoot
& dotnet (Join-Path $PSScriptRoot 'Driver/bin/Release/net10.0/Driver.dll') $WorkDirectory $PatchPath
if ($LASTEXITCODE) { throw "Real-DLL validation failed ($LASTEXITCODE)." }
