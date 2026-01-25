# BAB IV - Subbab 4.3: Ekstraksi Section Properties (Pengaturan Halaman)

## Ringkasan Subbab
Subbab ini membahas secara teknis proses ekstraksi section properties dari dokumen Word menggunakan OpenXML SDK. Section properties (w:sectPr) mendefinisikan pengaturan halaman seperti ukuran kertas, margin, orientasi, penomoran halaman, dan konfigurasi header/footer untuk setiap bagian dokumen. Dalam implementasi `DocxExtractionService`, ekstraksi section merupakan langkah pertama yang harus dilakukan sebelum memproses elemen-elemen body karena setiap elemen akan di-assign ke section yang sesuai. Class `SectionExtractor` melakukan transformasi dari XML element menjadi model `DokumenSection` yang kemudian disimpan ke database. Pemahaman tentang section sangat krusial karena dokumen tugas akhir umumnya memiliki multiple sections dengan pengaturan berbeda, misalnya halaman pendahuluan dengan penomoran romawi dan halaman isi dengan penomoran desimal.

---

## 4.3.1 Lokasi SectionProperties dalam Dokumen

### 4.3.1.1 w:sectPr di Akhir Body (Default Section)

Dalam struktur dokumen Word, section properties untuk section terakhir selalu ditempatkan sebagai direct child dari elemen w:body di bagian akhir dokumen. Elemen ini mendefinisikan pengaturan halaman yang berlaku untuk semua konten dari section break terakhir hingga akhir dokumen. Dalam `DocxExtractionService` line 99-101, deteksi body-level sectPr dilakukan dengan `body.GetFirstChild<SectionProperties>()` meskipun method-nya bernama `GetFirstChild`, secara praktis hanya ada satu sectPr di level body. Penggunaan special index `int.MaxValue` pada sectionInfos menandakan bahwa section ini mencakup semua elemen hingga akhir dokumen tanpa batas index tertentu. Jika dokumen hanya memiliki satu section (tidak ada section break), maka body-level sectPr ini adalah satu-satunya section properties yang ada. Pattern ini berbeda dengan section break yang ditempatkan di dalam paragraf karena body-level sectPr berdiri sendiri sebagai direct child element.

```csharp
// DocxExtractionService.cs lines 99-101
var bodySectPr = body.GetFirstChild<SectionProperties>();
if (bodySectPr != null)
    sectionInfos.Add((int.MaxValue, bodySectPr));
```

### 4.3.1.2 w:sectPr dalam w:pPr (Section Break)

Section break dalam dokumen Word direpresentasikan sebagai child element dari paragraph properties (w:pPr) pada paragraf terakhir sebelum section tersebut berakhir. Ini berarti paragraf yang mengandung section properties sebenarnya adalah paragraf terakhir dari section yang bersangkutan, bukan paragraf pertama dari section berikutnya. Dalam `DocxExtractionService` line 90-95, setiap paragraf diiterasi dan dicek apakah memiliki section properties dengan pattern `para.ParagraphProperties?.GetFirstChild<SectionProperties>()`. Null-conditional operator `?.` digunakan karena tidak semua paragraf memiliki ParagraphProperties, dan tidak semua ParagraphProperties memiliki SectionProperties. Ketika ditemukan, tuple `(elemIndex, sectPr)` ditambahkan ke list `sectionInfos` dimana elemIndex adalah posisi paragraf tersebut dalam urutan body elements. Element index ini penting untuk menentukan mapping antara content elements dengan section yang sesuai.

```csharp
// DocxExtractionService.cs lines 90-95
if (elem is Paragraph para)
{
    var sectPr = para.ParagraphProperties?.GetFirstChild<SectionProperties>();
    if (sectPr != null)
        sectionInfos.Add((elemIndex, sectPr));
}
```

### 4.3.1.3 Urutan Section dari Awal ke Akhir Dokumen

Pengumpulan section properties dilakukan dalam satu pass iterasi body elements untuk menjaga urutan yang benar. Loop pada line 85-97 di `DocxExtractionService` melakukan traversal semua elements dengan mempertahankan counter index yang akan digunakan untuk mapping. Penting untuk dicatat bahwa counter index harus meng-exclude body-level SectionProperties dari penghitungan (line 88 dengan `if (elem is SectionProperties) continue`) karena elemen tersebut bukan konten yang perlu dimapping ke section. Setelah loop selesai, body-level sectPr ditambahkan sebagai entry terakhir dengan index `int.MaxValue`. Urutan dalam list `sectionInfos` sekarang sesuai dengan urutan section dari awal dokumen ke akhir, dimana entry pertama adalah section pertama yang berakhir pada paragraf dengan section break pertama, dan entry terakhir adalah section terakhir yang mencakup konten hingga akhir dokumen. Urutan ini menjadi dasar untuk assignment section ID ke setiap elemen konten.

### 4.3.1.4 Multiple Sections dan Index Tracking

Dokumen tugas akhir umumnya memiliki multiple sections dengan pengaturan berbeda: halaman judul, halaman pengesahan, abstract (mungkin tanpa header), daftar isi dengan penomoran romawi, dan konten utama dengan penomoran desimal. Tracking section dilakukan dengan mengelola dua struktur data: `sectionInfos` untuk menyimpan pasangan (elementIndex, sectPr) sementara selama parsing, dan `sectionIdMap` untuk menyimpan mapping permanen setelah section disimpan ke database. Field `upToElementIndex` pada `sectionIdMap` menentukan batas element index yang masuk ke section tersebut; sebuah element dengan index N masuk ke section pertama yang memiliki `upToElementIndex >= N`. Function inline `GetSectionId()` pada line 225-233 mengimplementasikan lookup ini dengan iterasi sederhana hingga ditemukan section dengan batas yang mencakup element index yang dicari. Pendekatan ini lebih sederhana dibanding menggunakan binary search karena jumlah section dalam dokumen umumnya kecil (kurang dari 10).

```csharp
// DocxExtractionService.cs lines 225-233
uint GetSectionId(int originalElementIndex)
{
    foreach (var (upToIndex, dsecId, _) in sectionIdMap)
    {
        if (originalElementIndex <= upToIndex)
            return dsecId;
    }
    return sectionIdMap.Count > 0 ? sectionIdMap[^1].dsecId : 0;
}
```

---

## 4.3.2 Ekstraksi Ukuran Halaman

### 4.3.2.1 PageSize Element (w:pgSz)

Ekstraksi ukuran halaman dilakukan melalui elemen w:pgSz yang merupakan child dari w:sectPr. Dalam `SectionExtractor.ExtractSectionProperties()` line 45-53, akses ke PageSize dilakukan dengan `sectPr.GetFirstChild<PageSize>()` yang mengembalikan object strongly-typed dengan properties Width, Height, dan Orient. Null check diperlukan karena dokumen yang dibuat dari template sederhana mungkin tidak memiliki explicit page size dan menggunakan default Word. Jika PageSize tidak ada, properties pada model `DokumenSection` akan tetap null yang menandakan penggunaan default. Implementasi ini mengikuti prinsip "store what's explicit" dimana hanya nilai yang secara eksplisit didefinisikan dalam dokumen yang disimpan, memudahkan diferensiasi antara "nilai default" vs "nilai yang sengaja di-set oleh pengguna". Hal ini penting untuk validasi tugas akhir dimana penggunaan nilai default perlu dibedakan dengan nilai yang sesuai pedoman.

```csharp
// SectionExtractor.cs lines 45-53
var pageSize = sectPr.GetFirstChild<PageSize>();
if (pageSize != null)
{
    if (pageSize.Width != null && pageSize.Width.HasValue)
        section.DsecPageWidthTwips = pageSize.Width.Value;
    if (pageSize.Height != null && pageSize.Height.HasValue)
        section.DsecPageHeightTwips = pageSize.Height.Value;
    section.DsecOrientation = pageSize.Orient?.Value == PageOrientationValues.Landscape 
        ? "landscape" : "portrait";
}
```

### 4.3.2.2 Width dan Height dalam Twips

Nilai lebar dan tinggi halaman dalam OpenXML disimpan dalam satuan twips (twentieth of a point). Satu twip sama dengan 1/20 point atau 1/1440 inch, menjadikannya satuan yang sangat presisi untuk layout dokumen. Ukuran kertas A4 yang merupakan standar untuk tugas akhir di Indonesia memiliki dimensi 11906 x 16838 twips yang setara dengan 210mm x 297mm. Dalam model `DokumenSection`, width dan height disimpan langsung dalam twips menggunakan tipe `uint?` (nullable unsigned integer) pada kolom `dsec_page_width_twips` dan `dsec_page_height_twips`. Penyimpanan dalam bentuk native twips menghindari loss of precision yang bisa terjadi jika dikonversi ke satuan lain saat penyimpanan. Konversi ke satuan yang lebih familiar (cm, inch, mm) dilakukan di layer presentasi atau validasi dengan formula: `cm = twips / 566.929`, `inch = twips / 1440`, `mm = twips / 56.693`.

### 4.3.2.3 Orientation: Portrait vs Landscape

Orientasi halaman ditentukan oleh attribute w:orient pada elemen w:pgSz dengan nilai "portrait" atau "landscape". Dalam implementasi `SectionExtractor` line 52, ekstraksi menggunakan null-coalescing dengan ternary operator: jika `pageSize.Orient?.Value` sama dengan `PageOrientationValues.Landscape` maka string "landscape" disimpan, otherwise "portrait". Ini berarti jika attribute orient tidak ada (null), default diasumsikan portrait yang sesuai dengan behavior Word. Penting untuk dipahami bahwa ketika orientasi landscape, nilai Width dan Height dalam w:pgSz tidak otomatis di-swap oleh Word; dokumen mungkin tetap menyimpan Width=11906 dan Height=16838 dengan orient="landscape". Beberapa dokumen lama mungkin melakukan swap manual dimana Width > Height menandakan landscape tanpa attribute orient. Implementasi saat ini mengikuti attribute orient secara eksplisit untuk konsistensi.

### 4.3.2.4 Konversi Twips ke Satuan Lain

Meskipun penyimpanan dilakukan dalam twips, layer validasi dan presentasi memerlukan konversi ke satuan yang lebih familiar. Konstanta konversi yang akurat adalah: `1 inch = 1440 twips`, `1 cm = 566.92913385826795 twips` (atau dibulatkan 567), `1 mm = 56.692913385826795 twips`, dan `1 point = 20 twips`. Untuk kebutuhan validasi margin tugas akhir yang biasanya dispesifikasikan dalam sentimeter (contoh: margin atas 4 cm, margin kiri 4 cm), konversi dapat dilakukan dengan pembagian sederhana. Perlu diperhatikan bahwa hasil konversi mungkin tidak tepat bulat karena perbedaan presisi antara sistem imperial (yang menjadi basis twips) dan metrik. Sebagai contoh, margin 4 cm akan menjadi sekitar 2268 twips (4 × 567), dan saat dikonversi kembali menjadi 3.9994 cm. Toleransi dalam validasi perlu memperhitungkan perbedaan presisi ini.

---

## 4.3.3 Ekstraksi Margin Halaman

### 4.3.3.1 PageMargin Element (w:pgMar)

Margin halaman didefinisikan dalam elemen w:pgMar yang terletak di dalam w:sectPr. Element ini memiliki attributes untuk semua jenis margin termasuk top, bottom, left, right, serta header dan footer margins. Dalam `SectionExtractor.cs` line 56-73, ekstraksi dimulai dengan mendapatkan PageMargin element menggunakan `sectPr.GetFirstChild<PageMargin>()`. Setiap attribute kemudian diekstrak secara individual dengan null-checking karena tidak semua attribute harus ada. Model `DokumenSection` menyimpan masing-masing margin dalam kolom terpisah (`dsec_margin_top_twips`, `dsec_margin_bottom_twips`, dst.) untuk memudahkan query dan validasi per-margin. Pemisahan ini sangat berguna untuk validasi tugas akhir dimana setiap margin memiliki requirement berbeda sesuai pedoman penulisan.

```csharp
// SectionExtractor.cs lines 56-73
var pageMargin = sectPr.GetFirstChild<PageMargin>();
if (pageMargin != null)
{
    if (pageMargin.Top != null)
        section.DsecMarginTopTwips = (uint)Math.Abs((int)pageMargin.Top);
    if (pageMargin.Bottom != null)
        section.DsecMarginBottomTwips = (uint)Math.Abs((int)pageMargin.Bottom);
    if (pageMargin.Left != null)
        section.DsecMarginLeftTwips = pageMargin.Left.Value;
    if (pageMargin.Right != null)
        section.DsecMarginRightTwips = pageMargin.Right.Value;
    // ... header, footer, gutter
}
```

### 4.3.3.2 Top, Bottom, Left, Right Margins

Margin utama (top, bottom, left, right) mendefinisikan jarak dari tepi kertas ke area konten utama dokumen. Yang perlu diperhatikan adalah tipe data yang berbeda: Top dan Bottom disimpan sebagai signed integer dalam XML (`Int32Value`) karena bisa bernilai negatif untuk kasus overlap, sedangkan Left dan Right adalah unsigned (`UInt32Value`). Dalam `SectionExtractor` line 59-66, penanganan top dan bottom menggunakan `Math.Abs()` untuk mengkonversi nilai negatif menjadi positif sebelum disimpan ke model yang menggunakan `uint`. Nilai negatif pada top/bottom margin adalah fitur advanced Word untuk membuat konten overlap dengan area header/footer yang jarang digunakan dalam dokumen akademis. Untuk validasi tugas akhir, margin negatif akan dikonversi ke nilai absolut yang kemudian dibandingkan dengan requirement pedoman.

### 4.3.3.3 Header dan Footer Margins

Header margin (w:header attribute) dan footer margin (w:footer attribute) pada w:pgMar mendefinisikan jarak dari tepi atas/bawah kertas ke area header/footer, bukan jarak dari body content. Ini berbeda dengan top/bottom margin yang mendefinisikan batas area body content. Relasi spasial yang perlu dipahami adalah: area header berada antara tepi atas kertas dan top margin, sedangkan area footer berada antara bottom margin dan tepi bawah kertas. Dalam `SectionExtractor` line 67-70, kedua nilai ini diekstrak dan disimpan dalam `DsecHeaderMarginTwips` dan `DsecFooterMarginTwips`. Untuk dokumen tugas akhir yang umumnya menggunakan header untuk nomor halaman, nilai header margin yang tepat penting untuk memastikan jarak yang konsisten antara tepi kertas dan nomor halaman. Nilai umum adalah sekitar 720 twips (0.5 inch atau ~1.27 cm).

### 4.3.3.4 Gutter dan GutterPosition

Gutter adalah ruang tambahan yang dialokasikan untuk binding (penjilidan) dokumen, biasanya di sisi kiri untuk orientasi portrait atau di atas untuk landscape. Elemen w:gutter pada w:pgMar mendefinisikan ukuran gutter dalam twips. Posisi gutter ditentukan oleh elemen terpisah: w:gutterAtTop untuk menempatkan gutter di atas (override default), dan w:gutterOnRight untuk menempatkan di kanan (jarang digunakan, untuk binding kanan). Dalam `SectionExtractor` line 75-83, penentuan posisi dilakukan dengan memeriksa keberadaan elemen-elemen tersebut menggunakan helper function `IsOn()` yang memeriksa nilai attribute w:val. Default posisi adalah "left" jika tidak ada modifier. Untuk tugas akhir yang dijilid dengan metode hard cover biasanya memerlukan gutter 0.5-1 cm di sisi kiri untuk mengkompensasi ruang yang hilang karena penjilidan.

```csharp
// SectionExtractor.cs lines 75-83
var gutterAtTop = sectPr.GetFirstChild<GutterAtTop>();
var gutterOnRight = sectPr.GetFirstChild<GutterOnRight>();
if (IsOn(gutterAtTop))
    section.DsecGutterPosition = "top";
else if (IsOn(gutterOnRight))
    section.DsecGutterPosition = "right";
else
    section.DsecGutterPosition = "left";
```

---

## 4.3.4 Ekstraksi Pengaturan Section Lainnya

### 4.3.4.1 SectionType: nextPage, continuous, evenPage, oddPage

Section type menentukan bagaimana section break ditampilkan dan mempengaruhi pagination dokumen. Nilai yang valid adalah: `nextPage` (mulai di halaman baru), `continuous` (lanjut di halaman yang sama), `evenPage` (mulai di halaman genap berikutnya), `oddPage` (mulai di halaman ganjil berikutnya), dan `nextColumn` (untuk layout multi-kolom). Dalam `SectionExtractor` line 23-33, section type diekstrak dari elemen w:type child dari w:sectPr. Jika tidak ada, default "nextPage" digunakan yang merupakan behavior Word untuk section break standar. Untuk dokumen tugas akhir, oddPage sering digunakan untuk memastikan bab baru selalu dimulai di halaman ganjil (sisi kanan buku), sementara continuous digunakan untuk perubahan header tanpa page break seperti pergantian dari halaman penomoran romawi ke desimal.

```csharp
// SectionExtractor.cs lines 23-33
var sectionType = sectPr.GetFirstChild<SectionType>();
if (sectionType?.Val?.Value != null)
{
    section.DsecType = sectionType.Val.Value.ToString().ToLower();
}
else
{
    section.DsecType = "nextPage"; // Default
}
```

### 4.3.4.2 TitlePage untuk First Page Different

Property "Title Page" (w:titlePg) mengaktifkan fitur "Different First Page" yang memungkinkan halaman pertama suatu section memiliki header dan footer berbeda dari halaman berikutnya. Ini sangat berguna untuk halaman judul yang tidak boleh menampilkan nomor halaman, atau bab baru yang menampilkan judul bab di footer tanpa header. Dalam `SectionExtractor` line 36-37, ekstraksi menggunakan pattern toggle element dimana keberadaan elemen tanpa attribute val berarti true, dan attribute val="false" atau val="0" berarti false. Expression `titlePage != null && (titlePage.Val?.Value ?? true)` menangani ketiga kasus: element tidak ada (false), element ada tanpa val (true), dan element ada dengan val (sesuai nilai). Hasil disimpan dalam boolean column `dsec_has_title_page`. Untuk dokumen tugas akhir, fitur ini umumnya aktif di section pertama setiap bab.

### 4.3.4.3 PageNumberType: Format dan Start Number

Penomoran halaman dikonfigurasi melalui elemen w:pgNumType yang mendefinisikan format penomoran dan optional starting number. Format yang tersedia termasuk: `decimal` (1, 2, 3), `lowerRoman` (i, ii, iii), `upperRoman` (I, II, III), `lowerLetter` (a, b, c), `upperLetter` (A, B, C), dan beberapa format lainnya. Dalam `SectionExtractor` line 86-96, ekstraksi dilakukan dengan memeriksa attribute Format dan Start. Jika Format tidak dispesifikasikan, default "decimal" digunakan untuk kompatibilitas. Attribute Start menentukan nomor awal untuk section tersebut; jika null, penomoran melanjutkan dari section sebelumnya. Untuk tugas akhir, penggunaan umum adalah section awal (daftar isi, abstract) menggunakan `lowerRoman`, kemudian section konten utama memulai ulang dengan `decimal` start dari 1.

```csharp
// SectionExtractor.cs lines 86-96
var pageNumberType = sectPr.GetFirstChild<PageNumberType>();
if (pageNumberType != null)
{
    section.DsecPageNumFormat = pageNumberType.Format != null && pageNumberType.Format.HasValue
        ? pageNumberType.Format.Value.ToString().ToLower()
        : "decimal";
    
    if (pageNumberType.Start != null)
        section.DsecPageNumStart = (uint)pageNumberType.Start.Value;
}
```

### 4.3.4.4 Different Odd/Even Headers (Document-Level Setting)

Berbeda dengan Title Page yang merupakan setting per-section, fitur "Different Odd & Even Pages" adalah setting di level dokumen yang berlaku untuk semua sections. Setting ini ditemukan di settings.xml sebagai elemen w:evenAndOddHeaders. Dalam `DocxExtractionService` line 42, pengecekan dilakukan dengan method helper `IsEvenAndOddHeadersEnabled()` yang membaca DocumentSettingsPart. Nilai ini kemudian di-propagate ke setiap section melalui `SectionExtractor.UpdateOddEvenFromSettings()` pada line 110. Meskipun secara teknis ini adalah document-wide setting, penyimpanan per-section di kolom `dsec_different_odd_even` memberikan fleksibilitas untuk future enhancement jika spesifikasi berubah. Untuk tugas akhir, fitur ini digunakan jika pedoman mensyaratkan nomor halaman di posisi berbeda untuk halaman ganjil (kanan) dan genap (kiri) untuk format buku.

### 4.3.4.5 Columns untuk Multi-Column Layout

Layout multi-kolom didefinisikan melalui elemen w:cols di dalam w:sectPr. Dalam `SectionExtractor` line 98-107, jumlah kolom diekstrak dari attribute ColumnCount dengan default 1 jika tidak ada. Dokumen tugas akhir umumnya menggunakan single column, sehingga penggunaan multi-column layout mungkin mengindikasikan formatting yang perlu diperhatikan atau diperbaiki. Selain jumlah kolom, elemen w:cols juga memiliki attribute Space untuk jarak antar kolom dan kemungkinan child element w:col untuk konfigurasi per-kolom yang tidak diekstrak dalam implementasi saat ini karena tidak relevan untuk validasi tugas akhir standar. Penyimpanan di kolom `dsec_column_count` memungkinkan validasi sederhana untuk memastikan dokumen menggunakan layout single column sesuai requirement.

---

## 4.3.5 Pembuatan DokumenPart (Body, Header, Footer)

### 4.3.5.1 Konsep Part dalam Hierarchi Dokumen

Setelah section diekstrak, langkah berikutnya adalah membuat `DokumenPart` untuk setiap section. Part merepresentasikan container yang dapat menampung block elements (paragraf, tabel, dll.) dan memiliki tiga jenis: body untuk konten utama, header untuk header halaman, dan footer untuk footer halaman. Relasi antara section dan parts adalah one-to-many dimana satu section dapat memiliki satu body part dan multiple header/footer parts (untuk first, default, dan even pages). Dalam database, tabel `dokumen_part` memiliki foreign key ke `dokumen_section` melalui kolom `dsec_id`. Property `dpart_type` menyimpan jenis part ("body", "header", "footer"), dan `dpart_position` menyimpan posisi untuk header/footer ("default", "first", "even") atau null untuk body.

### 4.3.5.2 Body Part Creation per Section

Untuk setiap section yang berhasil diekstrak, satu body part dibuat secara otomatis. Dalam `DocxExtractionService` line 123-138, iterasi dilakukan atas `sectionIdMap` dan untuk setiap entry, `DokumenPart` baru dengan type "body" dan position null dibuat dan disimpan ke database. Mapping antara section ID ke body part ID disimpan dalam dictionary `partMap` dengan key "body". Structure ini memungkinkan lookup cepat ketika processing body elements untuk menentukan ke part mana element tersebut harus di-assign. Setiap elemen yang diproses dari body dokumen akan mendapat `dpart_id` yang sesuai berdasarkan section-nya, memungkinkan query yang terstruktur untuk retrieve konten per-section atau per-dokumen.

```csharp
// DocxExtractionService.cs lines 127-137
var bodyPart = new DokumenPart
{
    DsecId = dsecId,
    DpartType = "body",
    DpartPosition = null
};
_db.DokumenParts.Add(bodyPart);
await _db.SaveChangesAsync();
partMap[dsecId]["body"] = bodyPart.DpartId;
```

### 4.3.5.3 Header Part Extraction dari HeaderReference

Header dalam dokumen Word diakses melalui relationship yang didefinisikan dalam section properties menggunakan elemen w:headerReference. Setiap reference memiliki attribute r:id yang menunjuk ke HeaderPart dalam package dan w:type yang menentukan kapan header tersebut ditampilkan. Dalam `DocxExtractionService` line 146-175, iterasi dilakukan atas semua HeaderReference dalam sectPr. Type dikonversi menjadi position string ("default", "first", "even") menggunakan helper function `GetPartPosition()`. Part key seperti "header-default" digunakan untuk menghindari duplikasi dalam `partMap`. Header content diekstrak dengan mendapatkan HeaderPart melalui `GetPartById()` dan memproses elements di dalamnya menggunakan `ExtractPartContent()`.

```csharp
// DocxExtractionService.cs lines 147-173
foreach (var headerRef in sectPr.Elements<HeaderReference>())
{
    var headerType = headerRef.Type?.Value ?? HeaderFooterValues.Default;
    var position = GetPartPosition(headerType);
    var partKey = $"header-{position}";
    
    if (!partMap[dsecId].ContainsKey(partKey))
    {
        var headerPart = new DokumenPart
        {
            DsecId = dsecId,
            DpartType = "header",
            DpartPosition = position
        };
        // ... save and extract content
    }
}
```

### 4.3.5.4 Footer Part Extraction dari FooterReference

Footer di-handle dengan pattern yang identik dengan header menggunakan w:footerReference dan FooterPart. Dalam `DocxExtractionService` line 177-206, logic yang sama diaplikasikan: iterasi FooterReference, konversi type ke position, check duplikasi di partMap, create DokumenPart dengan type "footer", dan extract content. Footer umumnya berisi nomor halaman yang dalam dokumen Word biasanya menggunakan field PAGE yang akan di-render sebagai angka saat dokumen di-print atau di-view. Dalam ekstraksi, field ini direpresentasikan sebagai elemen dengan type khusus yang memungkinkan validasi keberadaan penomoran halaman. Konsistensi antara header dan footer handling memudahkan maintenance dan extension jika diperlukan penanganan khusus di masa depan.

### 4.3.5.5 Position Mapping: Default, First, Even

Mapping dari `HeaderFooterValues` enum ke string position dilakukan oleh helper function `GetPartPosition()` pada line 496-503 di `DocxExtractionService`. Nilai `HeaderFooterValues.First` dipetakan ke "first" untuk header/footer halaman pertama (jika Different First Page aktif). Nilai `HeaderFooterValues.Even` dipetakan ke "even" untuk halaman genap (jika Different Odd & Even aktif). Semua nilai lainnya termasuk `HeaderFooterValues.Default` dipetakan ke "default". Perlu dicatat bahwa tidak ada explicit "odd" karena "default" digunakan untuk halaman ganjil ketika Different Odd & Even aktif. Struktur tiga-position ini cukup untuk merepresentasikan semua kombinasi header/footer yang mungkin dalam dokumen Word standar.

```csharp
// DocxExtractionService.cs lines 496-503
private static string GetPartPosition(HeaderFooterValues headerFooterType)
{
    if (headerFooterType == HeaderFooterValues.First)
        return "first";
    if (headerFooterType == HeaderFooterValues.Even)
        return "even";
    return "default";
}
```

---

## 4.3.6 Penyimpanan Section ke Database

### 4.3.6.1 Model DokumenSection dan Kolom-kolomnya

Model `DokumenSection` didefinisikan dalam `Models/DokumenSection.cs` dengan mapping ke tabel database `dokumen_section`. Primary key `dsec_id` di-generate otomatis oleh database. Foreign key `dokumen_id` menghubungkan section ke dokumen induknya. Kolom `dsec_index` menyimpan urutan section dalam dokumen (0-based). Kolom-kolom utama meliputi: `dsec_type` untuk section type, `dsec_has_title_page` dan `dsec_different_odd_even` untuk konfigurasi header/footer, `dsec_page_num_format` dan `dsec_page_num_start` untuk penomoran, `dsec_page_width_twips` dan `dsec_page_height_twips` untuk ukuran halaman, `dsec_orientation` untuk orientasi, enam kolom margin (`top`, `bottom`, `left`, `right`, `header`, `footer`), serta `dsec_gutter_twips`, `dsec_gutter_position`, dan `dsec_column_count`. Total 16 kolom data yang komprehensif untuk merepresentasikan semua pengaturan halaman yang relevan.

### 4.3.6.2 Model DokumenPart dan Relasi ke Section

Model `DokumenPart` didefinisikan dalam `Models/DokumenPart.cs` dengan mapping ke tabel `dokumen_part`. Kolom `dsec_id` sebagai foreign key menghubungkan part ke section-nya. Kolom `dpart_type` menyimpan jenis part ("body", "header", "footer"). Kolom `dpart_position` menyimpan posisi header/footer ("default", "first", "even") atau null untuk body. Navigation property `Section` memungkinkan akses ke parent section dari part. Navigation property `Elements` menyediakan akses ke collection `DokumenElemen` yang terkandung dalam part tersebut. Relasi one-to-many dari Section ke Parts dan dari Part ke Elements membentuk hierarchi tiga tingkat yang clean untuk representasi struktur dokumen.

### 4.3.6.3 Proses Save dan Generate ID

Dalam `DocxExtractionService`, setiap section disimpan ke database segera setelah diekstrak pada line 111-112 dengan `_db.DokumenSections.Add(section)` diikuti `await _db.SaveChangesAsync()`. SaveChanges dipanggil secara synchronous per-section (bukan batch di akhir) karena ID yang di-generate oleh database diperlukan segera untuk tracking di `sectionIdMap`. Pattern yang sama digunakan untuk parts pada line 134-135 dan 161-162. Meskipun multiple SaveChanges call lebih slow dibanding single batch save, kebutuhan untuk ID segera mengharuskan pendekatan ini. Untuk dokumen dengan banyak sections (yang jarang terjadi untuk tugas akhir), optimasi bisa dilakukan dengan menggunakan database yang mendukung OUTPUT clause untuk mendapat IDs tanpa select terpisah.

### 4.3.6.4 Logging dan Tracking untuk Debugging

Extensive logging diterapkan untuk memudahkan debugging dan monitoring proses ekstraksi. Setiap section yang berhasil disimpan di-log pada line 114-115 dengan informasi index, ID yang di-generate, dan dokumen ID. Setiap body part yang dibuat di-log pada line 137. Setiap header dan footer part di-log pada line 164-165 dan 195-196 dengan informasi position. Log messages menggunakan structured logging dengan placeholders seperti `{DsecId}` yang memungkinkan filtering dan search di log aggregation tools. Level LogInformation digunakan untuk section creation (event penting), sementara LogDebug digunakan untuk part creation (detail implementasi). Pendekatan logging yang comprehensive ini sangat membantu dalam troubleshooting ketika ada mismatch antara konten yang diharapkan dengan hasil ekstraksi.

---

## Kesimpulan Subbab 4.3

Ekstraksi section properties merupakan tahap fundamental dalam proses parsing dokumen Word yang menentukan bagaimana dokumen secara keseluruhan akan diorganisasi dalam database. Dalam implementasi `DocxExtractionService`, proses ini melibatkan beberapa komponen kunci:

1. **Lokasi SectionProperties**: Section properties ditemukan di dua lokasi - sebagai child dari paragraph properties untuk section break, dan sebagai direct child dari body untuk section terakhir. Tracking index yang akurat diperlukan untuk mapping element ke section yang benar.

2. **Ekstraksi PageSize dan Orientation**: Ukuran halaman dan orientasi diekstrak dari elemen w:pgSz dengan penyimpanan dalam satuan native twips untuk menghindari loss of precision.

3. **Ekstraksi Margins**: Enam jenis margin (top, bottom, left, right, header, footer) plus gutter diekstrak dari elemen w:pgMar dengan handling khusus untuk nilai signed pada top/bottom.

4. **Pengaturan Section Lainnya**: Section type, title page setting, page numbering format, dan column count diekstrak untuk memberikan representasi lengkap pengaturan setiap section.

5. **Pembuatan Parts**: Setiap section memiliki body part wajib dan optional header/footer parts sesuai konfigurasi dengan position (default/first/even) yang sesuai.

6. **Penyimpanan Database**: Model `DokumenSection` dan `DokumenPart` menyimpan data dengan struktur relasional yang memungkinkan query terstruktur untuk kebutuhan validasi.

Pemahaman detail tentang ekstraksi section properties ini menjadi dasar untuk memahami bagaimana elemen-elemen konten (paragraf, tabel, gambar) akan di-assign ke section dan part yang sesuai pada tahap ekstraksi selanjutnya yang dibahas di subbab 4.4.
