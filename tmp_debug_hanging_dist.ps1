[xml]$doc = Get-Content 'tmp_elab_check_20260220/word/document.xml'
$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$ns.AddNamespace('w','http://schemas.openxmlformats.org/wordprocessingml/2006/main')
$xpath = '//w:p[w:pPr/w:pStyle[@w:val="STTSAlgoritmaContent" or @w:val="STTSSegmenProgramContent" or @w:val="Style1" or @w:val="Style2"]]'
$paras = $doc.SelectNodes($xpath,$ns)
$counts = @{}
$examples = @{}
$idx=0
foreach($p in $paras){
  $idx++
  $styleNode = $p.SelectSingleNode('w:pPr/w:pStyle',$ns)
  $style = if($styleNode){$styleNode.GetAttribute('val','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{''}
  $numNode = $p.SelectSingleNode('w:pPr/w:numPr/w:numId',$ns)
  $numId = if($numNode){$numNode.GetAttribute('val','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{''}
  $ind = $p.SelectSingleNode('w:pPr/w:ind',$ns)
  $hanging = if($ind){$ind.GetAttribute('hanging','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{''}
  if([string]::IsNullOrEmpty($hanging)){ $hanging='(none)' }
  if(-not $counts.ContainsKey($hanging)){ $counts[$hanging]=0 }
  $counts[$hanging]++
  if(-not $examples.ContainsKey($hanging)){
    $txt = (($p.SelectNodes('.//w:t',$ns) | ForEach-Object { $_.InnerText }) -join '').Trim()
    if($txt.Length -gt 80){$txt=$txt.Substring(0,80)}
    $examples[$hanging] = "idx=$idx style=$style numId=$numId text=$txt"
  }
}
"total=$($paras.Count)"
foreach($k in ($counts.Keys | Sort-Object {[int]($_ -replace '\D','0')})){
  $cm = if($k -match '^\d+$'){ ([int]$k / 1440.0 * 2.54).ToString('F2') } else { '-' }
  "$k twips (~$cm cm) => count=$($counts[$k]); sample: $($examples[$k])"
}
