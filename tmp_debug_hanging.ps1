[xml]$doc = Get-Content 'tmp_elab_check_20260220/word/document.xml'
$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$ns.AddNamespace('w','http://schemas.openxmlformats.org/wordprocessingml/2006/main')
$xpath = '//w:p[w:pPr/w:pStyle[@w:val="STTSAlgoritmaContent" or @w:val="STTSSegmenProgramContent" or @w:val="Style1" or @w:val="Style2"]]'
$paras = $doc.SelectNodes($xpath,$ns)
Write-Host "count=$($paras.Count)"
for($i=0; $i -lt [Math]::Min(30, $paras.Count); $i++){
  $p = $paras.Item($i)
  $styleNode = $p.SelectSingleNode('w:pPr/w:pStyle',$ns)
  $style = if($styleNode){$styleNode.GetAttribute('val','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{''}
  $numNode = $p.SelectSingleNode('w:pPr/w:numPr/w:numId',$ns)
  $numId = if($numNode){$numNode.GetAttribute('val','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{''}
  $ind = $p.SelectSingleNode('w:pPr/w:ind',$ns)
  $left = if($ind){$ind.GetAttribute('left','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{''}
  $hanging = if($ind){$ind.GetAttribute('hanging','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{''}
  $txt = (($p.SelectNodes('.//w:t',$ns) | ForEach-Object { $_.InnerText }) -join '').Trim()
  if($txt.Length -gt 70){$txt = $txt.Substring(0,70)}
  Write-Host "idx=$i style=$style numId=$numId left=$left hanging=$hanging text=$txt"
}
