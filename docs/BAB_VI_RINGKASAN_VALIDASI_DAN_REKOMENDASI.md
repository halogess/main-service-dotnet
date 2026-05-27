# RINGKASAN BAB VI - VALIDASI DAN REKOMENDASI PERBAIKAN

## Gambaran Umum Bab VI

Bab VI menjelaskan proses validasi format dokumen dan pembentukan rekomendasi perbaikan pada sistem. Pembahasan dimulai dari posisi validasi dalam pipeline, data masukan yang digunakan, mekanisme pemeriksaan, pengolahan hasil, rekomendasi berbasis model, sampai penyimpanan dan penyajian hasil kepada pengguna. Fokus bab ini adalah proses setelah ekstraksi OpenXML dan analisis visual selesai, sehingga detail ekstraksi dokumen tidak dibahas ulang secara mendalam.

Secara umum, validasi bekerja sebagai tahap pengambilan keputusan berbasis aturan. Sistem membandingkan data struktur dokumen, data visual, dan aturan aktif untuk menentukan apakah format dokumen sudah sesuai dengan pedoman. Rekomendasi perbaikan ditempatkan setelah keputusan validasi terbentuk, sehingga rekomendasi hanya memperjelas pesan kesalahan dan langkah perbaikan.

## Alur Validasi Dokumen

Validasi dijalankan oleh `ValidationQueueBackgroundService` setelah dokumen berada pada tahap siap diperiksa. Pada tahap ini, sistem tidak membaca ulang dokumen mentah dari awal, tetapi menggunakan hasil pemrosesan yang sudah tersimpan. Data yang sudah tersedia dari ekstraksi dan analisis visual menjadi dasar untuk menjalankan pemeriksaan secara konsisten.

Alur validasi dilakukan per bab dokumen. Setiap bab diperiksa dengan aturan aktif yang sama, lalu hasilnya digabungkan untuk membentuk hasil validasi tingkat dokumen atau buku. Pola ini membuat sistem dapat menunjukkan kesalahan pada bagian yang lebih spesifik dan tetap menghitung status dokumen berdasarkan agregasi hasil per bab.

## Data Masukan Validasi

Masukan utama validasi terdiri dari hasil ekstraksi struktur dokumen, hasil analisis visual, dan aturan validasi aktif. Hasil ekstraksi struktur disimpan antara lain pada tabel `dokumen_elemen`, sedangkan informasi visual disimpan pada tabel `dokumen_elemen_visual`. Kedua kelompok data tersebut saling melengkapi karena sebagian aturan membutuhkan informasi struktur dokumen dan sebagian lain membutuhkan posisi atau tampilan elemen pada halaman.

Aturan validasi dibaca dari tabel `aturan` dan `aturan_detail`. Tabel `aturan` menyimpan versi pedoman, status aktif, dan konfigurasi utama validasi. Tabel `aturan_detail` menyimpan rincian parameter pemeriksaan berdasarkan key aturan, kategori, dan nilai JSON yang digunakan runtime. Dengan pola ini, sistem dapat memakai aturan aktif tanpa menanam seluruh parameter pemeriksaan secara tetap di kode validasi.

## Mekanisme Pemeriksaan

Mekanisme pemeriksaan dimulai dari validasi pengaturan halaman, kemudian dilanjutkan dengan klasifikasi elemen dokumen. Setelah elemen diklasifikasikan, sistem menjalankan modul pemeriksaan untuk judul bab, judul subbab, paragraf, item daftar, footnote, gambar, tabel, rumus, dan kode atau segmen program. Urutan ini mengikuti kebutuhan runtime karena beberapa pemeriksaan bergantung pada hasil klasifikasi elemen.

Setiap modul validasi membandingkan data aktual dengan parameter aturan yang relevan. Pemeriksaan dapat mencakup font, ukuran, alignment, margin, indentasi, spacing, numbering, caption, posisi elemen, atau relasi antar elemen. Hasil pemeriksaan tidak hanya berupa benar atau salah, tetapi juga membawa konteks kesalahan seperti kategori, field, nilai yang diharapkan, nilai aktual, bukti, dan lokasi bila tersedia.

## Pengolahan Hasil

Hasil validasi diolah menjadi daftar kesalahan yang dapat disimpan dan ditampilkan. Sistem hanya memasukkan kesalahan yang memiliki lokasi yang dapat digunakan, karena kesalahan tanpa lokasi sulit ditunjukkan kepada pengguna secara jelas. Lokasi tersebut berasal dari hasil validasi internal dan analisis visual, bukan dari model rekomendasi.

Skor validasi dihitung berdasarkan jumlah pemeriksaan unik yang lolos dibandingkan total pemeriksaan unik. Perhitungan ini tidak memakai pembobotan parameter. Selain skor, sistem juga memperhatikan hard constraint. Jika hard constraint dilanggar, bab atau dokumen dapat dinyatakan tidak lolos meskipun nilai skor memenuhi batas minimum.

## Rekomendasi Perbaikan

Rekomendasi perbaikan dibuat setelah daftar kesalahan validasi terbentuk. Sistem menggunakan `GeminiService` untuk menyusun penjelasan dan langkah perbaikan dalam bahasa yang lebih mudah dipahami pengguna. Model utama yang dikonfigurasi untuk deployment adalah `gemma-3-27b-it`, yang dipanggil melalui Google Generative Language API.

Prompt rekomendasi berisi data kesalahan, aturan aktif yang relevan, ringkasan konteks OpenXML bila tersedia, serta batasan aksi seperti `allowed_actions` dan `disallowed_actions`. Model hanya memperkaya pesan kesalahan menjadi judul, penjelasan, dan langkah perbaikan. Keputusan benar atau salah, status lolos atau tidak lolos, dan skor tetap ditentukan oleh backend berbasis aturan.

## Penyimpanan dan Penyajian Hasil

Hasil akhir validasi disimpan dalam bentuk `kesalahan` dan `kesalahan_detail`. Tabel `kesalahan` berperan sebagai kelompok kesalahan, sedangkan `kesalahan_detail` menyimpan penjelasan, rekomendasi, langkah perbaikan, dan informasi hard constraint pada detail kesalahan. Data ini kemudian digunakan oleh frontend untuk menampilkan hasil validasi secara terstruktur.

Selain penyimpanan ke database, sistem juga menyiapkan laporan validasi, pembaruan status proses, dan notifikasi hasil. Dengan demikian, pengguna tidak hanya menerima status akhir, tetapi juga daftar kesalahan dan arahan perbaikan yang dapat ditindaklanjuti. Penyajian hasil tetap mengikuti data validasi yang sudah diputuskan oleh backend.

## Catatan Kesesuaian Implementasi

Ringkasan Bab VI ini mengikuti alur sistem yang berjalan saat ini. Validasi dilakukan setelah ekstraksi OpenXML dan analisis visual selesai, memakai `aturan` dan `aturan_detail`, serta memanfaatkan `dokumen_elemen` dan `dokumen_elemen_visual` sebagai sumber data pemeriksaan. Validasi dilakukan per bab, kemudian hasil dokumen dihitung dari agregasi hasil bab.

Model Gemma pada tahap rekomendasi hanya digunakan untuk menyusun bahasa perbaikan. Perhitungan skor dan keputusan kesalahan tetap berada pada validator berbasis aturan. Dengan batasan tersebut, Bab VI menempatkan validasi sebagai proses deterministik berbasis aturan dan rekomendasi sebagai lapisan bantuan untuk menjelaskan hasil kepada pengguna.
