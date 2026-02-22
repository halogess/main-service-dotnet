[xml]$doc = Get-Content 'tmp_elab_check_20260220/word/document.xml'
$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$ns.AddNamespace('w','http://schemas.openxmlformats.org/wordprocessingml/2006/main')
$xpath = '//w:p[w:pPr/w:pStyle[@w:val="STTSAlgoritmaContent" or @w:val="STTSSegmenProgramContent" or @w:val="Style1" or @w:val="Style2"]]'
$paras = $doc.SelectNodes($xpath,$ns)
foreach($target in 16,82,120){
  $p = $paras.Item($target-1)
  if(-not $p){ continue }
  $text = (($p.SelectNodes('.//w:t',$ns) | ForEach-Object { $_.InnerText }) -join '').Trim()
  if($text.Length -gt 100){$text = $text.Substring(0,100)}
  "=== idx=$target text=$text ==="
  $p.OuterXml
  ""
}
