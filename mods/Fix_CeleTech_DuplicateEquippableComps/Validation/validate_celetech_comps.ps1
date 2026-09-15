# Boundary validation for Fix_CeleTech_DuplicateEquippableComps
# Emulates RimWorld XML inheritance for the <comps> node chain and applies the patch
# operations, then asserts the number of CompEquippable-derived comps per weapon def.
# BEFORE: 2 (duplicate verbTracker owners)  AFTER: 1
$ErrorActionPreference = 'Stop'

$vanilla = 'A:\SteamLibrary\steamapps\common\RimWorld\Data\Core\Defs\ThingDefs_Misc\Weapons\BaseWeapons.xml'
$cele    = 'A:\SteamLibrary\steamapps\workshop\content\294100\3446237098\1.6\Defs\Weapons'
$patch   = 'A:\SteamLibrary\steamapps\common\RimWorld\Mods\Fix_CeleTech_DuplicateEquippableComps\Patches\FixDuplicateEquippableComps.xml'

# CompEquippable-derived comp classes (compClass resolution incl. CompProperties -> compClass)
$equippableClasses = @('CompEquippable','CeleTech.Base.CompPawnEquipmentGizmo','CeleTech.Base.CompLegendaryWeapons','CeleTech.Base.CompAppWeaTransferEquippableBridge')
$propsToComp = @{
  'CeleTech.Base.CompProperties_PawnEquipmentGizmo' = 'CeleTech.Base.CompPawnEquipmentGizmo'
  'CeleTech.Base.CompProperties_LegendaryWeapons'  = 'CeleTech.Base.CompLegendaryWeapons'
  'CeleTech.Base.CompProperties_AppWeaTransferEquippableBridge' = 'CeleTech.Base.CompAppWeaTransferEquippableBridge'
}

function Comp-Class($li) {
  $cls = $li.GetAttribute('Class')
  if ($cls) { if ($propsToComp.ContainsKey($cls)) { return $propsToComp[$cls] } else { return $cls } }
  $cc = $li.SelectSingleNode('compClass')
  if ($cc) { return $cc.InnerText }
  return '<classless>'
}

# Build one merged raw-def document (pre-inheritance, pre-patch) like RimWorld's combined XML
$doc = New-Object System.Xml.XmlDocument
$root = $doc.CreateElement('Defs')
[void]$doc.AppendChild($root)
foreach ($f in @($vanilla, "$cele\WeaponBase.xml", "$cele\ChargedPower\OICW.xml", "$cele\ChargedSmart\Smart_Basic.xml", "$cele\Melee\TangDao.xml", "$cele\Melee\EMPSword.xml")) {
  $d = New-Object System.Xml.XmlDocument
  $d.Load($f)
  foreach ($n in $d.DocumentElement.ChildNodes) {
    if ($n.NodeType -eq 'Element') { [void]$root.AppendChild($doc.ImportNode($n, $true)) }
  }
}

# Parent comps chain per abstract def (Name attribute), emulating inheritance merge order
$defByName = @{}
foreach ($n in $root.SelectNodes('*[@Name]')) { $defByName[$n.GetAttribute('Name')] = $n }

function Parent-Chain($def) {
  $chain = New-Object System.Collections.Generic.List[System.Xml.XmlNode]
  $cur = $def
  while ($true) {
    $pn = $cur.GetAttribute('ParentName')
    if (-not $pn) { break }
    $cur = $defByName[$pn]
    if (-not $cur) { throw "parent $pn not found" }
    $chain.Insert(0, $cur)
  }
  return $chain
}

function Merged-Comps($def) {
  # returns list of comp class strings after inheritance resolution
  $result = New-Object System.Collections.Generic.List[string]
  $childCompsNode = $def.SelectSingleNode('comps')
  foreach ($anc in (Parent-Chain $def)) {
    $an = $anc.SelectSingleNode('comps')
    if (-not $an) { continue }
    if ($an.GetAttribute('Inherit') -eq 'False') { $result.Clear() }  # Inherit=False: drop everything inherited so far
    foreach ($li in $an.SelectNodes('li')) { $result.Add((Comp-Class $li)) }
  }
  if ($childCompsNode = $def.SelectSingleNode('comps')) {
    if ($childCompsNode.GetAttribute('Inherit') -eq 'False') { $result.Clear() }
    foreach ($li in $childCompsNode.SelectNodes('li')) { $result.Add((Comp-Class $li)) }
  }
  return $result
}

$targets = @('QBZ71_QS','QTS9_OICW','W85XWJ','QBZS85XWJ','QBZ_eightfiveAR','TangDaoPersona','CMC_EMPsword_sevenstars')

function Def-Node($name) { return $root.SelectSingleNode("*[defName='$name']") }

Write-Host '=== BEFORE (unpatched merged defs) ==='
$beforeOk = $true
foreach ($t in $targets) {
  $comps = Merged-Comps (Def-Node $t)
  $eq = @($comps | Where-Object { $equippableClasses -contains $_ })
  $status = if ($eq.Count -eq 2) { 'DUPLICATE (bug reproduced)' } else { "UNEXPECTED count $($eq.Count)"; $beforeOk = $false }
  Write-Host ("{0}: {1} equippable-derived comps [{2}]  <- {3}" -f $t, $eq.Count, ($eq -join ', '), $status)
  if ($eq.Count -ne 2) { $beforeOk = $false }
}
if (-not $beforeOk) { Write-Host 'BEFORE state does not reproduce the bug - aborting.'; exit 1 }

# Apply the patch operations (same semantics as RimWorld PatchOperationRemove/AttributeSet/Add)
$patchDoc = New-Object System.Xml.XmlDocument
$patchDoc.Load($patch)
foreach ($op in $patchDoc.SelectNodes('/Patch/Operation')) {
  $cls = $op.GetAttribute('Class')
  $xp = $op.SelectSingleNode('xpath').InnerText
  $matched = @($doc.SelectNodes($xp))
  Write-Host ("op {0}: xpath matched {1} node(s)" -f $cls, $matched.Count)
  switch ($cls) {
    'PatchOperationRemove' {
      if ($matched.Count -ne 5) { Write-Host 'FAIL: expected exactly 5 removed gizmo declarations'; exit 1 }
      foreach ($n in $matched) { [void]$n.ParentNode.RemoveChild($n) }
    }
    'PatchOperationAttributeSet' {
      $name = $op.SelectSingleNode('name').InnerText
      $val = $op.SelectSingleNode('value').InnerText
      if ($matched.Count -ne 2) { Write-Host 'FAIL: expected exactly 2 comps nodes'; exit 1 }
      foreach ($n in $matched) { $n.SetAttribute($name, $val) }
    }
    'PatchOperationAdd' {
      if ($matched.Count -ne 2) { Write-Host 'FAIL: expected exactly 2 comps nodes'; exit 1 }
      $valNode = $op.SelectSingleNode('value')
      foreach ($n in $matched) {
        foreach ($c in $valNode.ChildNodes) {
          if ($c.NodeType -eq 'Element') { [void]$n.AppendChild($root.OwnerDocument.ImportNode($c, $true)) }
        }
      }
    }
    'PatchOperationReplace' {
      if ($matched.Count -ne 2) { Write-Host 'FAIL: expected exactly 2 Art li nodes'; exit 1 }
      $valNode = $op.SelectSingleNode('value')
      foreach ($n in $matched) {
        $newNode = $n.OwnerDocument.ImportNode($valNode.FirstChild, $true)
        [void]$n.ParentNode.ReplaceChild($newNode, $n)
      }
    }
    default { Write-Host "FAIL: unknown op $cls"; exit 1 }
  }
}

Write-Host '=== AFTER (patched merged defs) ==='
$afterOk = $true
foreach ($t in $targets) {
  $def = Def-Node $t
  $comps = Merged-Comps $def
  $eq = @($comps | Where-Object { $equippableClasses -contains $_ })
  $ok = $eq.Count -eq 1
  if (-not $ok) { $afterOk = $false }
  Write-Host ("{0}: {1} equippable-derived comp [{2}] {3}" -f $t, $eq.Count, ($eq -join ', '), $(if ($ok) {'OK'} else {'FAIL'}))
  Write-Host ("   full comps: {0}" -f ($comps -join ', '))
}
# Pattern B: inherited non-equippable comps must be present exactly once
foreach ($t in @('TangDaoPersona','CMC_EMPsword_sevenstars')) {
  $comps = Merged-Comps (Def-Node $t)
  foreach ($req in @('CompProperties_Forbiddable','CompProperties_Styleable','CompQuality','CompProperties_Art')) {
    $c = @($comps | Where-Object { $_ -eq $req }).Count
    if ($c -ne 1) { Write-Host "FAIL: $t expected exactly 1 '$req', got $c"; $afterOk = $false }
  }
  # Art grammar: the pre-fix active CompArt was the inherited Gun-grammar one
  # (GetComp<CompArt> returns the first match); the patched single Art comp must
  # keep Gun grammar, not the child's Melee grammar.
  $artNode = $root.SelectNodes("*[defName='$t']/comps/li[@Class='CompProperties_Art']")
  if ($artNode.Count -ne 1) { Write-Host "FAIL: $t expected exactly 1 Art li in raw def, got $($artNode.Count)"; $afterOk = $false }
  else {
    $nm = $artNode[0].SelectSingleNode('nameMaker').InnerText
    $dm = $artNode[0].SelectSingleNode('descriptionMaker').InnerText
    if ($nm -ne 'NamerArtWeaponGun' -or $dm -ne 'ArtDescription_WeaponGun') {
      Write-Host "FAIL: $t art grammar changed (nameMaker=$nm, descriptionMaker=$dm)"; $afterOk = $false
    } else { Write-Host "$t art grammar preserved: Gun (NamerArtWeaponGun / ArtDescription_WeaponGun)" }
  }
}
if ($afterOk) { Write-Host 'ALL CHECKS PASSED: every target def has exactly one CompEquippable-derived comp; special comps preserved.'; exit 0 }
else { Write-Host 'AFTER CHECKS FAILED'; exit 1 }
