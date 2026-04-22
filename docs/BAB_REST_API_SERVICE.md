# REST API Service

## 1. Pendahuluan

REST API Service merupakan komponen backend yang menyediakan antarmuka pemrograman aplikasi berbasis REST untuk mendukung operasi CRUD, autentikasi, pemrosesan dokumen, dan integrasi frontend pada sistem validasi tugas akhir.

Service ini dibangun menggunakan ASP.NET Core 9.0 dan berperan sebagai jembatan antara frontend, database, dan service-service pendukung seperti validasi, konversi PDF, email, dan WebSocket.

## 2. Arsitektur REST API

### 2.1 Teknologi yang Digunakan

| Komponen | Teknologi |
|----------|-----------|
| Framework | ASP.NET Core 9.0 |
| ORM | Entity Framework Core 9.0 |
| Database | MySQL 8.0 (Pomelo Provider) |
| Autentikasi | JWT (JSON Web Token) |
| Real-time | WebSocket |
| Dokumentasi | Swagger/OpenAPI |

### 2.2 Struktur Controller

REST API Service menggunakan pola Controller-Service-Repository yang memisahkan tanggung jawab masing-masing komponen.

```text
Frontend (React)
  |
  | HTTP Request
  v
Controllers
  |- AuthController
  |- MahasiswaController
  |- BukuController
  |- DokumenController
  |- DosenController
  |- JurusanController
  |- AturanController
  |- RulesController
  v
Services
  |- AuthService
  |- BukuService
  |- DokumenService
  |- AturanService
  v
Database Context
  |- KorektorBukuDbContext
  |- SttsDbContext
```

## 3. Endpoint API

### 3.1 Authentication API (`/api/auth`)

API autentikasi menangani proses login, refresh token, dan informasi pengguna yang sedang aktif.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| POST | `/api/auth/login` | Login pengguna dengan username dan password |
| POST | `/api/auth/refresh` | Memperbarui access token menggunakan refresh token |
| GET | `/api/auth/me` | Mendapatkan informasi pengguna yang sedang login |

**Fitur keamanan:**
- Access token dengan masa berlaku terbatas
- Refresh token disimpan dalam HTTP-only cookie
- Secure cookie pada environment production
- Role-based access control (Admin/Mahasiswa)

---

### 3.2 Mahasiswa API (`/api/mahasiswa`)

API mahasiswa menyediakan akses ke data mahasiswa dari database STTS dan mengelola status buku tugas akhir mahasiswa yang tidak aktif.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| GET | `/api/mahasiswa/nonaktif/angkatan` | Mendapatkan daftar angkatan mahasiswa nonaktif |
| GET | `/api/mahasiswa/nonaktif/jurusan` | Mendapatkan daftar jurusan mahasiswa nonaktif |
| GET | `/api/mahasiswa/nonaktif/buku` | Mendapatkan daftar buku mahasiswa nonaktif |
| DELETE | `/api/mahasiswa/nonaktif/buku` | Menghapus buku mahasiswa nonaktif |

---

### 3.3 Buku API (`/api/buku`)

API buku mengelola data buku tugas akhir yang diupload oleh mahasiswa termasuk upload, pembatalan, statistik, dan pengambilan data.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| POST | `/api/buku/upload` | Upload buku tugas akhir baru |
| GET | `/api/buku/can-upload` | Memeriksa apakah mahasiswa dapat mengupload |
| GET | `/api/buku/judul` | Mendapatkan judul buku berdasarkan NRP |
| POST | `/api/buku/{id}/batal` | Membatalkan buku yang sudah diupload |
| GET | `/api/buku/stats` | Mendapatkan statistik buku |
| GET | `/api/buku/per-jurusan` | Mendapatkan statistik buku per jurusan |
| GET | `/api/buku/{id}` | Mendapatkan detail buku berdasarkan ID |
| GET | `/api/buku/admin` | Mendapatkan daftar buku untuk admin |
| GET | `/api/buku/mahasiswa` | Mendapatkan daftar buku untuk mahasiswa yang login |

---

### 3.4 Dokumen API (`/api/dokumen`)

API dokumen mengelola dokumen-dokumen pendukung buku tugas akhir seperti BAB I, BAB II, dan sebagainya.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| POST | `/api/dokumen/upload` | Upload dokumen baru |
| POST | `/api/dokumen/{id}/batal` | Membatalkan dokumen yang sudah diupload |
| GET | `/api/dokumen/can-upload` | Memeriksa apakah dapat mengupload dokumen |
| GET | `/api/dokumen/stats` | Mendapatkan statistik dokumen |
| GET | `/api/dokumen` | Mendapatkan daftar dokumen |
| GET | `/api/dokumen/{id}` | Mendapatkan detail dokumen dengan hasil validasi |
| GET | `/api/dokumen/{id}/image/{imageName}` | Mendapatkan gambar hasil konversi dokumen |

---

### 3.5 Dosen API (`/api/dosen`)

API dosen menyediakan akses ke data dosen aktif dari database STTS.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| GET | `/api/dosen` | Mendapatkan daftar dosen aktif |

---

### 3.6 Jurusan API (`/api/jurusan`)

API jurusan menyediakan akses ke data jurusan yang aktif.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| GET | `/api/jurusan` | Mendapatkan daftar jurusan aktif |

---

### 3.7 Aturan API (`/api/aturan`)

API aturan mengelola konfigurasi aturan validasi dokumen tugas akhir termasuk aturan untuk format halaman, paragraf, gambar, tabel, dan elemen lainnya.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| GET | `/api/aturan` | Mendapatkan semua aturan |
| GET | `/api/aturan/{id}` | Mendapatkan aturan dengan detail |
| GET | `/api/aturan/aktif` | Mendapatkan aturan yang aktif |
| POST | `/api/aturan` | Membuat aturan baru |
| PATCH | `/api/aturan/{id}` | Update aturan |
| PATCH | `/api/aturan/{id}/detail` | Update detail aturan |

---

### 3.8 Rules API (`/api/rules`)

API rules merupakan endpoint development untuk testing konfigurasi aturan tanpa autentikasi.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| GET | `/api/rules` | Mendapatkan semua aturan dengan detail |
| GET | `/api/rules/{id}` | Mendapatkan aturan spesifik |
| POST | `/api/rules` | Membuat aturan dengan detail |
| PATCH | `/api/rules/{id}` | Update aturan dengan detail |
| DELETE | `/api/rules/{id}` | Hapus aturan dan detail |
| DELETE | `/api/rules/{id}/detail/{detailId}` | Hapus detail spesifik |

---

## 4. Database Connection

REST API Service terhubung ke dua database.

### 4.1 Database Korektor Buku (`KorektorBukuDbContext`)

Database utama yang menyimpan data sistem validasi tugas akhir:
- **Buku** - Data buku tugas akhir
- **Dokumen** - Dokumen BAB buku
- **DokumenValidasi** - Hasil validasi dokumen
- **Aturan** - Konfigurasi aturan validasi
- **Data ekstraksi dokumen** - Section, paragraf, tabel, gambar, note, dan metadata visual

### 4.2 Database STTS (`SttsDbContext`)

Database institut yang menyimpan data master:
- **Mahasiswa** - Data mahasiswa
- **Dosen** - Data dosen
- **Jurusan** - Data jurusan/program studi
- **Proposal** - Data proposal tugas akhir
- **Karyawan** - Status kepegawaian yang dipakai pada filter dosen aktif

---

## 5. Fitur Pendukung

### 5.1 WebSocket Service

WebSocket digunakan untuk:
- Notifikasi progress upload dokumen
- Update status konversi PDF
- Notifikasi hasil validasi real-time
- Progress antrian validasi

### 5.2 PDF Conversion Service

Service konversi PDF menggunakan Adobe PDF Services API untuk mengonversi dokumen DOCX menjadi PDF dan mengekstrak gambar halaman dokumen.

### 5.3 Email Service

Service email digunakan untuk mengirim notifikasi kepada mahasiswa mengenai status validasi dan hasil koreksi dokumen.

---

## 6. Keamanan

### 6.1 Authentication

- **JWT Token**: Access token dengan algoritma HS256
- **Refresh Token**: Disimpan dalam HTTP-only cookie
- **Token Expiry**: Access token dan refresh token memiliki masa berlaku terpisah

### 6.2 Authorization

Role-based access control dengan dua peran:
- **Admin (BAA)**: Akses penuh ke semua endpoint administratif
- **Mahasiswa**: Akses terbatas ke data sendiri

### 6.3 CORS

Cross-Origin Resource Sharing dikonfigurasi untuk mengizinkan akses dari origin frontend yang diatur pada konfigurasi aplikasi.

---

## 7. Ringkasan Endpoint

| Controller | Jumlah Endpoint | Deskripsi |
|------------|-----------------|-----------|
| AuthController | 3 | Autentikasi dan session |
| MahasiswaController | 6 | Data mahasiswa |
| BukuController | 9 | Manajemen buku TA |
| DokumenController | 7 | Manajemen dokumen |
| DosenController | 1 | Data dosen |
| JurusanController | 1 | Data jurusan |
| AturanController | 6 | Aturan validasi |
| RulesController | 6 | Endpoint development aturan |
| **Total** | **39** | - |
