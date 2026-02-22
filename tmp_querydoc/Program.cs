using MySqlConnector;
using System.Globalization;
var cs="Server=localhost;Port=3307;Database=db_korektor_buku;User=jessica;Password=pass123;TreatTinyAsBoolean=false";
await using var c=new MySqlConnection(cs);
await c.OpenAsync();
var sql=@"
SELECT e.delemen_sequence,e.delemen_id,e.delemen_type,
CAST(JSON_UNQUOTE(JSON_EXTRACT(e.delemen_json_tree,'$.dfp_id')) AS UNSIGNED) AS dfp_id,
pf.dfp_p_style_id,pf.dfp_is_list,pf.dfp_list_numId,pf.dfp_list_ilvl,
LEFT(v.dev_text,80) txt
FROM dokumen_elemen e
JOIN dokumen_part p ON p.dpart_id=e.dpart_id
JOIN dokumen_section s ON s.dsec_id=p.dpart_id
LEFT JOIN dokumen_format_paragraf pf ON pf.dfp_id=CAST(JSON_UNQUOTE(JSON_EXTRACT(e.delemen_json_tree,'$.dfp_id')) AS UNSIGNED)
LEFT JOIN dokumen_elemen_visual v ON v.dokumen_id=650 AND v.dokumen_elemen_id=e.delemen_id
WHERE s.dokumen_id=650 AND p.dpart_type='body' AND e.delemen_sequence BETWEEN 68 AND 90
ORDER BY e.delemen_sequence, v.dev_page, v.dev_id";
await using var cmd=new MySqlCommand(sql,c);
await using var r=await cmd.ExecuteReaderAsync();
while(await r.ReadAsync()){
 string V(string n)=>r[n]==DBNull.Value?"-":Convert.ToString(r[n],CultureInfo.InvariantCulture)??"-";
 Console.WriteLine($"seq={V("delemen_sequence"),4} id={V("delemen_id"),7} type={V("delemen_type"),15} style={V("dfp_p_style_id"),12} isList={V("dfp_is_list"),5} num={V("dfp_list_numId"),3} ilvl={V("dfp_list_ilvl"),2} txt={V("txt")}");
}
