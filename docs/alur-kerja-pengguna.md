# BAB 4: Alur Kerja Pengguna

Bab ini menjelaskan alur kerja pengguna dalam sistem validasi dokumen tugas akhir. Sistem ini memiliki dua tipe pengguna utama, yaitu Mahasiswa dan Admin Biro Administrasi Akademik (BAA), yang masing-masing memiliki peran dan fitur yang berbeda.

---

## 4.1 Alur Kerja Mahasiswa

Mahasiswa adalah pengguna utama yang menggunakan sistem untuk memvalidasi dokumen tugas akhir dan mengakses template pendukung. Berikut adalah fitur-fitur yang tersedia untuk mahasiswa.

### 4.1.1 Dashboard Mahasiswa

Dashboard merupakan halaman utama yang ditampilkan setelah mahasiswa berhasil login ke sistem.

#### 4.1.1.1 Ringkasan Statistik Validasi

Dashboard menampilkan statistik validasi dokumen mahasiswa yang meliputi:
- **Total Validasi**: Jumlah seluruh dokumen yang pernah divalidasi
- **Menunggu**: Dokumen yang sedang dalam antrian atau proses validasi
- **Lolos**: Dokumen yang telah lolos validasi format
- **Perlu Perbaikan**: Dokumen yang memerlukan perbaikan format

#### 4.1.1.2 Aksi Cepat (Quick Actions)

Mahasiswa dapat mengakses fitur-fitur utama dengan cepat melalui tombol aksi:
- **Upload Dokumen**: Navigasi langsung ke halaman upload dokumen baru
- **Lihat Template**: Navigasi ke halaman template panduan format

#### 4.1.1.3 Riwayat Validasi Terbaru

Menampilkan 5 validasi dokumen terakhir yang dilakukan mahasiswa, meliputi:
- Nama file dokumen
- Tanggal upload
- Status validasi (Menunggu/Lolos/Tidak Lolos)
- Jumlah kesalahan yang ditemukan

#### 4.1.1.4 Notifikasi Real-Time

Sistem menggunakan WebSocket untuk memberikan notifikasi real-time kepada mahasiswa:
- Update status ketika validasi selesai diproses
- Notifikasi perubahan status dokumen
- Refresh otomatis data statistik

---

### 4.1.2 Cek Dokumen (Upload & Validasi)

Halaman ini digunakan untuk mengunggah dan memvalidasi dokumen tugas akhir atau per-bab.

#### 4.1.2.1 Upload Dokumen Baru

Proses upload dokumen baru melibatkan langkah-langkah:
- **Pemilihan File**: Mahasiswa memilih file DOCX yang akan divalidasi
- **Validasi Awal**: Sistem melakukan pengecekan format file sebelum upload
- **Feedback Upload**: Menampilkan progress dan status upload

#### 4.1.2.2 Daftar Riwayat Validasi Dokumen

Menampilkan seluruh riwayat validasi dokumen dengan informasi:
- **Nama File**: Nama dokumen yang diupload
- **Ukuran File**: Ukuran file dalam KB/MB
- **Tanggal Upload**: Waktu dokumen diupload
- **Status**: Dalam Antrian, Diproses, Lolos, Tidak Lolos, atau Dibatalkan
- **Skor**: Nilai skor validasi (jika sudah selesai)

#### 4.1.2.3 Filter dan Pencarian

Mahasiswa dapat memfilter dokumen berdasarkan:
- **Status Validasi**: Semua, Menunggu, Lolos, Tidak Lolos, Dibatalkan
- **Rentang Tanggal**: Filter berdasarkan periode waktu tertentu
- **Urutan**: Terbaru atau Terlama

#### 4.1.2.4 Aksi pada Dokumen

Untuk setiap dokumen dalam daftar, mahasiswa dapat:
- **Lihat Detail**: Melihat hasil validasi lengkap
- **Batalkan Validasi**: Membatalkan validasi yang sedang menunggu
- **Download Sertifikat**: Mengunduh sertifikat jika dokumen lolos validasi

---

### 4.1.3 Detail Hasil Validasi

Halaman ini menampilkan hasil validasi dokumen secara detail dan komprehensif.

#### 4.1.3.1 Informasi Umum Dokumen

Menampilkan metadata dokumen yang divalidasi:
- **Nama File**: Nama file dokumen
- **Ukuran**: Ukuran file dokumen
- **Tanggal Upload**: Waktu dokumen diupload
- **Status**: Status validasi terkini
- **Skor Keseluruhan**: Persentase kelulusan validasi

#### 4.1.3.2 Hasil Validasi per Halaman

Sistem menampilkan hasil validasi untuk setiap halaman dokumen:
- **Preview Halaman**: Tampilan visual halaman dokumen
- **Daftar Kesalahan**: Kesalahan format yang ditemukan di halaman tersebut
- **Navigasi Halaman**: Perpindahan antar halaman dokumen
- **Highlight Kesalahan**: Penandaan visual pada area yang bermasalah

#### 4.1.3.3 Kategori Kesalatan Format

Kesalahan format dikategorikan menjadi beberapa jenis:
- **Margin**: Kesalahan pada batas halaman (atas, bawah, kiri, kanan)
- **Font**: Kesalahan pada jenis, ukuran, atau gaya font
- **Spacing**: Kesalahan pada jarak antar baris atau paragraf
- **Heading**: Kesalahan pada format judul dan sub-judul
- **Tabel**: Kesalahan pada format tabel

#### 4.1.3.4 Rekomendasi Perbaikan

Untuk setiap kesalahan, sistem memberikan rekomendasi:
- **Deskripsi Kesalahan**: Penjelasan detail tentang kesalahan yang ditemukan
- **Nilai Aktual**: Nilai format yang terdeteksi pada dokumen
- **Nilai yang Diharapkan**: Nilai format yang seharusnya sesuai aturan
- **Cara Perbaikan**: Langkah-langkah untuk memperbaiki kesalahan

---

### 4.1.4 Validasi Buku Lengkap

Halaman untuk memvalidasi seluruh buku tugas akhir yang terdiri dari beberapa bab.

#### 4.1.4.1 Upload Buku Lengkap

Proses upload buku lengkap melibatkan:
- **Pemilihan File**: Memilih file DOCX buku lengkap
- **Judul Buku**: Input judul buku tugas akhir (diambil dari data SIAKAD)
- **Validasi Komprehensif**: Sistem memvalidasi keseluruhan dokumen

#### 4.1.4.2 Riwayat Validasi Buku

Menampilkan daftar riwayat validasi buku lengkap:
- **ID Validasi**: Nomor identifikasi validasi
- **Judul Buku**: Judul buku yang divalidasi
- **Jumlah Bab**: Total bab dalam buku
- **Status**: Status validasi buku
- **Skor Total**: Skor keseluruhan validasi

#### 4.1.4.3 Monitoring Progress Validasi

Mahasiswa dapat memantau progress validasi:
- **Status Real-time**: Update status melalui WebSocket
- **Estimasi Waktu**: Perkiraan waktu selesai validasi
- **Notifikasi**: Pemberitahuan ketika validasi selesai

#### 4.1.4.4 Download Sertifikat Validasi

Setelah buku lolos validasi:
- **Sertifikat Digital**: Mahasiswa dapat mengunduh sertifikat validasi
- **Bukti Kelulusan**: Dokumen bukti bahwa format buku telah sesuai
- **Periode Berlaku**: Informasi masa berlaku sertifikat

---

### 4.1.5 Template dan Panduan

Halaman untuk mengakses template format dokumen dan dokumen pendukung.

#### 4.1.5.1 Template Panduan Format

Mahasiswa dapat mengakses template referensi:
- **Preview Template**: Melihat contoh format dokumen yang benar
- **Download Template**: Mengunduh file template DOCX
- **Aturan Format**: Melihat detail aturan format yang berlaku

#### 4.1.5.2 Template Dokumen Pelengkap Buku

Sistem menyediakan template dokumen pelengkap:
- **Daftar Template**: Berbagai jenis template (Lembar Pengesahan, Pernyataan, dll.)
- **Status Template**: Aktif atau Non-aktif
- **Preview Dokumen**: Melihat preview template sebelum generate

#### 4.1.5.3 Generate Dokumen Otomatis

Fitur untuk menghasilkan dokumen dengan data otomatis:
- **Data dari SIAKAD**: Nama, NRP, jurusan, pembimbing diambil otomatis
- **Pemilihan Penguji**: Memilih dosen penguji dari dropdown (jika diperlukan)
- **Generate Dokumen**: Sistem menghasilkan dokumen dengan data yang terisi

#### 4.1.5.4 Download Dokumen Hasil Generate

Setelah dokumen di-generate:
- **Preview Hasil**: Melihat preview dokumen yang dihasilkan
- **Download DOCX**: Mengunduh dokumen dalam format DOCX
- **Regenerate**: Menghasilkan ulang dokumen jika diperlukan

---

## 4.2 Alur Kerja Admin BAA

Admin Biro Administrasi Akademik (BAA) bertanggung jawab mengelola sistem validasi, termasuk template aturan format dan monitoring validasi mahasiswa.

### 4.2.1 Dashboard Admin

Dashboard admin menampilkan ringkasan statistik dan monitoring sistem secara keseluruhan.

#### 4.2.1.1 Statistik Validasi Global

Menampilkan statistik agregat dari seluruh validasi:
- **Total Validasi**: Jumlah seluruh validasi di sistem
- **Menunggu**: Validasi yang dalam antrian atau sedang diproses
- **Lolos**: Validasi yang berhasil lolos
- **Tidak Lolos**: Validasi yang memerlukan perbaikan

#### 4.2.1.2 Statistik per Program Studi

Menampilkan distribusi validasi berdasarkan program studi:
- **Kode Jurusan**: Kode singkatan program studi
- **Jumlah Mahasiswa**: Total mahasiswa yang sudah melakukan validasi
- **Visualisasi**: Grafik atau chart distribusi per jurusan

#### 4.2.1.3 Statistik Kesalahan Umum

Analisis kesalahan format yang paling sering ditemukan:
- **Jenis Kesalahan**: Kategori kesalahan (margin, font, spacing, dll.)
- **Frekuensi**: Jumlah kemunculan kesalahan
- **Trend**: Pola kesalahan dari waktu ke waktu

#### 4.2.1.4 Monitoring Sistem

Informasi status sistem secara real-time:
- **Antrian Validasi**: Jumlah dokumen yang menunggu diproses
- **Status Layanan**: Kesehatan layanan backend dan integrasi
- **Kapasitas**: Penggunaan resource sistem

---

### 4.2.2 Template Panduan (Aturan Format)

Halaman untuk mengelola template aturan validasi format dokumen.

#### 4.2.2.1 Manajemen Template Aturan

Admin dapat mengelola template aturan format:
- **Daftar Template**: Melihat semua versi template aturan
- **Upload Template**: Mengunggah template baru sebagai referensi
- **Edit Nama**: Mengubah nama/label template
- **Hapus Template**: Menghapus template yang tidak diperlukan

#### 4.2.2.2 Aktivasi Template

Hanya satu template yang dapat aktif pada satu waktu:
- **Aktifkan Template**: Menetapkan template sebagai aturan aktif
- **Nonaktifkan Template**: Menonaktifkan template tertentu
- **Riwayat Aktivasi**: Melihat riwayat perubahan template aktif

#### 4.2.2.3 Pengaturan Aturan Format

Admin dapat mengkonfigurasi aturan validasi:
- **Page Settings**: Pengaturan margin, ukuran kertas, orientasi
- **Component Rules**: Aturan untuk heading, paragraf, tabel
- **Font Rules**: Aturan jenis, ukuran, dan gaya font
- **Enable/Disable Rule**: Mengaktifkan atau menonaktifkan aturan tertentu

#### 4.2.2.4 Pengaturan Skor Minimum

Konfigurasi batas kelulusan validasi:
- **Skor Minimum**: Menetapkan persentase minimum untuk lolos
- **Penyimpanan**: Menyimpan perubahan skor ke database
- **Preview Impact**: Melihat dampak perubahan terhadap validasi

---

### 4.2.3 Template Isian Dokumen

Halaman untuk mengelola template dokumen yang bisa di-generate oleh mahasiswa.

#### 4.2.3.1 Manajemen Template Isian

Admin mengelola template dokumen isian:
- **Daftar Template**: Melihat semua template yang tersedia
- **Upload Template**: Mengunggah file template DOCX baru
- **Rename Template**: Mengubah nama template
- **Delete Template**: Menghapus template

#### 4.2.3.2 Status Template

Setiap template memiliki status:
- **Draft**: Template masih dalam pembuatan/edit
- **Ready**: Template sudah siap dan mapping selesai
- **Active**: Template aktif dan dapat digunakan mahasiswa
- **Inactive**: Template dinonaktifkan sementara

#### 4.2.3.3 Konfigurasi Template

Detail pengaturan template:
- **Kategori**: Kategori dokumen (Pelengkap Buku, Administrasi, dll.)
- **Tanggal Dibuat**: Waktu template diupload
- **Jumlah Field**: Total placeholder yang perlu di-mapping
- **Preview**: Melihat preview dokumen template

#### 4.2.3.4 Aksi pada Template

Aksi yang dapat dilakukan admin:
- **Download**: Mengunduh file template asli
- **Mapping Field**: Navigasi ke halaman mapping field
- **Toggle Status**: Mengubah status aktif/nonaktif
- **Delete**: Menghapus template dari sistem

---

### 4.2.4 Template Field Mapping

Halaman untuk mapping field placeholder dalam template ke sumber data.

#### 4.2.4.1 Ekstraksi Placeholder

Sistem mengekstrak placeholder dari template:
- **Deteksi Otomatis**: Placeholder dengan format {{field_name}} terdeteksi otomatis
- **Daftar Field**: Menampilkan semua placeholder yang ditemukan
- **Grouping**: Placeholder dikelompokkan berdasarkan bagian dokumen

#### 4.2.4.2 Drag-and-Drop Mapping

Interface intuitif untuk mapping field:
- **Field Tersedia**: Panel berisi field seperti nama, nrp, judul, pembimbing
- **Target Placeholder**: Area target untuk meletakkan field
- **Drag & Drop**: Menarik field ke placeholder yang sesuai
- **Visual Feedback**: Indikator visual saat mapping berhasil

#### 4.2.4.3 Kategori Field yang Tersedia

Field dikelompokkan berdasarkan kategori:
- **Dokumen**: TA/Tesis, Judul, Jenjang, Program Studi, Fakultas
- **Waktu**: Tanggal, Bulan, Tahun
- **Dosen**: Pembimbing, Co-Pembimbing, Penguji

#### 4.2.4.4 Validasi dan Penyimpanan Mapping

Proses validasi sebelum penyimpanan:
- **Validasi Field**: Mengecek apakah semua field required sudah ter-mapping
- **Catatan (Notes)**: Menambahkan catatan untuk field tertentu
- **Preview Hasil**: Melihat preview dokumen dengan data contoh
- **Simpan Mapping**: Menyimpan konfigurasi mapping ke database

---

### 4.2.5 Riwayat Validasi Buku

Halaman untuk melihat dan memonitor semua riwayat validasi buku dari seluruh mahasiswa.

#### 4.2.5.1 Daftar Validasi Semua Mahasiswa

Menampilkan riwayat validasi buku lengkap:
- **ID Validasi**: Nomor identifikasi unik
- **Nama Mahasiswa**: Nama lengkap mahasiswa
- **NRP**: Nomor Pokok Mahasiswa
- **Jurusan**: Program studi mahasiswa
- **Judul Buku**: Judul tugas akhir

#### 4.2.5.2 Filter dan Pencarian Advanced

Admin dapat memfilter data dengan berbagai kriteria:
- **Status Validasi**: Semua, Menunggu, Lolos, Tidak Lolos, Dibatalkan
- **Program Studi**: Filter berdasarkan kode jurusan
- **Rentang Tanggal**: Filter berdasarkan periode waktu
- **Pencarian**: Cari berdasarkan nama, NRP, atau judul
- **Urutan**: Sortir terbaru atau terlama

#### 4.2.5.3 Detail Validasi Mahasiswa

Akses ke detail validasi lengkap:
- **Lihat Detail**: Navigasi ke halaman detail hasil validasi
- **Skor Validasi**: Melihat skor dan jumlah kesalahan
- **Riwayat Proses**: Waktu upload dan waktu selesai proses

#### 4.2.5.4 Ekspor Data Riwayat

Fitur untuk ekspor data:
- **Download Sertifikat**: Mengunduh sertifikat validasi mahasiswa
- **Export Report**: Ekspor data validasi ke format tertentu
- **Statistik Periode**: Generate laporan statistik periode tertentu

---

### 4.2.6 Hapus Riwayat Validasi

Halaman untuk mengelola penghapusan data riwayat validasi.

#### 4.2.6.1 Pemilihan Data untuk Dihapus

Admin dapat memilih data yang akan dihapus:
- **Filter Data**: Memfilter data berdasarkan kriteria tertentu
- **Seleksi Individual**: Memilih dokumen satu per satu
- **Seleksi Batch**: Memilih beberapa dokumen sekaligus

#### 4.2.6.2 Kriteria Penghapusan

Berbagai kriteria untuk penghapusan:
- **Berdasarkan Tanggal**: Hapus data yang lebih lama dari periode tertentu
- **Berdasarkan Status**: Hapus data dengan status tertentu (misalnya dibatalkan)
- **Berdasarkan Jurusan**: Hapus data dari jurusan tertentu

#### 4.2.6.3 Konfirmasi Penghapusan

Proses konfirmasi sebelum penghapusan:
- **Dialog Konfirmasi**: Menampilkan ringkasan data yang akan dihapus
- **Warning Message**: Peringatan bahwa data tidak dapat dikembalikan
- **Tombol Konfirmasi**: Konfirmasi final untuk melanjutkan penghapusan

#### 4.2.6.4 Log Penghapusan

Pencatatan aktivitas penghapusan:
- **Siapa**: Admin yang melakukan penghapusan
- **Kapan**: Tanggal dan waktu penghapusan
- **Apa**: Detail data yang dihapus
- **Audit Trail**: Riwayat seluruh aktivitas penghapusan

---

### 4.2.7 Service Eksternal

Halaman untuk mengkonfigurasi integrasi dengan layanan eksternal.

#### 4.2.7.1 Manajemen API Gemini

Konfigurasi untuk layanan AI Gemini:
- **API Key**: Menambah atau mengubah API key
- **Status**: Melihat status aktif/nonaktif
- **Kuota**: Melihat sisa kuota penggunaan
- **Rate Limit**: Pengaturan batasan request

#### 4.2.7.2 Manajemen API Adobe PDF Services

Konfigurasi untuk layanan Adobe PDF:
- **Client ID & Secret**: Kredensial untuk autentikasi
- **Status Layanan**: Status koneksi ke Adobe
- **Penggunaan**: Statistik penggunaan layanan
- **Reset Tanggal**: Tanggal reset kuota bulanan

#### 4.2.7.3 CRUD Kredensial Service

Mengelola kredensial layanan eksternal:
- **Create**: Menambahkan kredensial baru
- **Read**: Melihat daftar kredensial (dengan masking)
- **Update**: Mengubah kredensial yang ada
- **Delete**: Menghapus kredensial yang tidak digunakan

#### 4.2.7.4 Monitoring dan Status

Monitoring kesehatan layanan eksternal:
- **Health Check**: Pengecekan otomatis status layanan
- **Error Log**: Log kesalahan koneksi atau request
- **Usage Statistics**: Statistik penggunaan per layanan
- **Alert**: Notifikasi jika layanan bermasalah

---

## Ringkasan Alur Kerja

| Role | Fitur Utama | Tujuan |
|------|-------------|--------|
| **Mahasiswa** | Dashboard, Cek Dokumen, Validasi Buku, Template | Memvalidasi format dokumen TA dan generate dokumen pendukung |
| **Admin BAA** | Dashboard, Template Panduan, Template Isian, Riwayat, Hapus Riwayat, Service | Mengelola aturan validasi, monitoring, dan konfigurasi sistem |

---

*Dokumentasi ini dihasilkan berdasarkan implementasi frontend web React pada sistem Cek TA.*
