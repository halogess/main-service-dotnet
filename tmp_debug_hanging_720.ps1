[xml]$doc = Get-Content 'tmp_elab_check_20260220/word/document.xml'
$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$ns.AddNamespace('w','http://schemas.openxmlformats.org/wordprocessingml/2006/main')
$xpath = '//w:p[w:pPr/w:pStyle[@w:val="STTSAlgoritmaContent" or @w:val="STTSSegmenProgramContent" or @w:val="Style1" or @w:val="Style2"]]'
$paras = $doc.SelectNodes($xpath,$ns)
$idx=0
$printed=0
foreach($p in $paras){
  $idx++
  $ind = $p.SelectSingleNode('w:pPr/w:ind',$ns)
  if(-not $ind){ continue }
  $h = $ind.GetAttribute('hanging','http://schemas.openxmlformats.org/wordprocessingml/2006/main')
  if($h -ne '720'){ continue }
  $styleNode = $p.SelectSingleNode('w:pPr/w:pStyle',$ns)
  $style = if($styleNode){$styleNode.GetAttribute('val','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{''}
  $numNode = $p.SelectSingleNode('w:pPr/w:numPr/w:numId',$ns)
  $numId = if($numNode){$numNode.GetAttribute('val','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{''}
  $txt = (($p.SelectNodes('.//w:t',$ns) | ForEach-Object { $_.InnerText }) -join '').Trim()
  if($txt.Length -gt 90){$txt=$txt.Substring(0,90)}
  Write-Output "idx=$idx style=$style numId=$numId text=$txt"
  $printed++
  if($printed -ge 15){ break }
}
"totalShown=$printed"
