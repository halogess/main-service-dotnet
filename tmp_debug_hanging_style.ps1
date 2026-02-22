[xml]$doc = Get-Content 'tmp_elab_check_20260220/word/document.xml'
$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$ns.AddNamespace('w','http://schemas.openxmlformats.org/wordprocessingml/2006/main')
$xpath = '//w:p[w:pPr/w:pStyle[@w:val="STTSAlgoritmaContent" or @w:val="STTSSegmenProgramContent" or @w:val="Style1" or @w:val="Style2"]]'
$paras = $doc.SelectNodes($xpath,$ns)
$stats = @{}
foreach($p in $paras){
  $styleNode = $p.SelectSingleNode('w:pPr/w:pStyle',$ns)
  $style = if($styleNode){$styleNode.GetAttribute('val','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{'(none)'}
  $ind = $p.SelectSingleNode('w:pPr/w:ind',$ns)
  $h = if($ind){$ind.GetAttribute('hanging','http://schemas.openxmlformats.org/wordprocessingml/2006/main')}else{'(none)'}
  $key = "$style|$h"
  if(-not $stats.ContainsKey($key)){ $stats[$key]=0 }
  $stats[$key]++
}
$stats.Keys | Sort-Object | ForEach-Object { "$_ => $($stats[$_])" }
