[xml]$doc = Get-Content 'tmp_elab_check_20260220/word/document.xml'
$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$ns.AddNamespace('w','http://schemas.openxmlformats.org/wordprocessingml/2006/main')
$all = $doc.SelectNodes('//w:body/*[self::w:p or self::w:tbl]',$ns)
$lastTitle = ''
$hits = @()
$idx=0
foreach($node in $all){
  if($node.LocalName -ne 'p') { continue }
  $idx++
  $styleNode = $node.SelectSingleNode('w:pPr/w:pStyle',$ns)
  $style = if($styleNode){$styleNode.GetAttribute('val','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{''}
  $txt = (($node.SelectNodes('.//w:t',$ns) | ForEach-Object { $_.InnerText }) -join '').Trim()
  if($style -eq 'STTSSegmenProgram' -or $style -eq 'STTSAlgoritma'){
    $lastTitle = $txt
  }
  if($style -ne 'STTSSegmenProgramContent' -and $style -ne 'STTSAlgoritmaContent' -and $style -ne 'Style1' -and $style -ne 'Style2'){ continue }
  $ind = $node.SelectSingleNode('w:pPr/w:ind',$ns)
  $h = if($ind){$ind.GetAttribute('hanging','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{''}
  if($h -eq '720'){
    if($txt.Length -gt 70){$txt=$txt.Substring(0,70)}
    $hits += "docP=$idx title='$lastTitle' text='$txt'"
  }
}
"total720=$($hits.Count)"
$hits | Select-Object -First 20
