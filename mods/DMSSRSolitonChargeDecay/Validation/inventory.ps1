param(
    [string]$WorkshopRoot = 'A:/SteamLibrary/steamapps/workshop/content/294100',
    [string]$OutputPath = "$env:LOCALAPPDATA/Temp/opencode/dmssr-soliton-20261112/def-inventory.json"
)
$ErrorActionPreference = 'Stop'
$root = Join-Path $WorkshopRoot '3835512735'
$all = foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root '1.7/Defs') -Filter *.xml -Recurse) {
    [xml]$document = [IO.File]::ReadAllText($file.FullName)
    foreach ($def in $document.SelectNodes('/Defs/ThingDef')) {
        [pscustomobject]@{ node = $def; file = [IO.Path]::GetRelativePath($root, $file.FullName) }
    }
}
$languages = @{}
$languageXmlCount = 0
foreach ($language in @('English', 'ChineseSimplified (简体中文)', 'ChineseTraditional (繁體中文)')) {
    $map = @{}
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root "Languages/$language") -Filter *.xml -Recurse) {
        [xml]$document = [IO.File]::ReadAllText($file.FullName)
        $languageXmlCount++
        foreach ($entry in $document.DocumentElement.ChildNodes) {
            if ($entry.NodeType -eq [Xml.XmlNodeType]::Element) { $map[$entry.Name] = $entry.InnerText }
        }
    }
    $languages[$language] = $map
}
$names = @('DMSSR_Soliton_Beam_Light', 'DMSSR_Soliton_Beam', 'DMSSR_Soliton_Beam_Heavy',
    'DMSSR_Soliton_Beam_Shotgun', 'DMS_Gun_BeamLaser', 'DMS_Turret_BeamLaser', 'DMSSR_ResentfulFlame_SGWC')
$records = foreach ($name in $names) {
    $record = @($all | Where-Object { $_.node.defName -eq $name })
    if ($record.Count -ne 1) { throw "Expected one ThingDef: $name" }
    $def = $record[0].node
    $texts = @{}
    foreach ($language in $languages.Keys) {
        $texts[$language] = @{
            label = $(if ($languages[$language].ContainsKey("$name.label")) { $languages[$language]["$name.label"] } else { [string]$def.label })
            description = $(if ($languages[$language].ContainsKey("$name.description")) { $languages[$language]["$name.description"] } else { [string]$def.description })
        }
    }
    [pscustomobject]@{
        defName = $name
        file = $record[0].file
        parentName = $def.ParentName
        texture = [string]$def.graphicData.texPath
        verbClass = [string]$def.verbs.li.verbClass
        verbXml = $(if ($def.verbs) { $def.verbs.OuterXml } else { $null })
        texts = $texts
    }
}
$beamNames = @($all | Where-Object { $_.node.SelectSingleNode('verbs/li/verbClass[text()="DMSSR.Verbs.Verb_MechParticleBeam"]') } | ForEach-Object { [string]$_.node.defName })
if ($beamNames.Count -ne 5) { throw "Expected five beam systems, got $($beamNames.Count)." }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
[pscustomobject]@{
    defsXmlCount = @(Get-ChildItem -LiteralPath (Join-Path $root '1.7/Defs') -Filter *.xml -Recurse).Count
    languagesXmlCount = $languageXmlCount
    beamThingDefs = $beamNames
    records = @($records)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output "Inventory saved: $OutputPath"
