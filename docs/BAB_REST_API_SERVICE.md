# REST API Service

## 1. Pendahuluan

REST API Service merupakan komponen backend yang menyediakan antarmuka pemrograman aplikasi (Application Programming Interface) berbasis arsitektur REST (Representational State Transfer) untuk mendukung operasi CRUD (Create, Read, Update, Delete) dan integrasi dengan aplikasi frontend. Service ini dibangun menggunakan ASP.NET Core 9.0 dengan berbagai endpoint yang terorganisir berdasarkan domain fungsional.

REST API Service berperan sebagai jembatan antara aplikasi frontend (React/Vite) dengan database dan service-service lainnya dalam sistem validasi tugas akhir. Service ini menangani autentikasi pengguna, manajemen data master, pengelolaan dokumen, serta konfigurasi aturan validasi.

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

REST API Service menggunakan pola arsitektur Controller-Service-Repository yang memisahkan tanggung jawab masing-masing komponen:

```
┌─────────────────────────────────────────────────────────────┐
│                     Frontend (React)                         │
└─────────────────────────┬───────────────────────────────────┘
                          │ HTTP Request
┌─────────────────────────▼───────────────────────────────────┐
│                      Controllers                             │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────┐   │
│  │   Auth   │ │ Mahasiswa│ │   Buku   │ │   Dokumen    │   │
│  └──────────┘ └──────────┘ └──────────┘ └──────────────┘   │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────┐   │
│  │  Dosen   │ │ Jurusan  │ │ Template │ │   Aturan     │   │
│  └──────────┘ └──────────┘ └──────────┘ └──────────────┘   │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                       Services                               │
│  (AuthService, BukuService, DokumenService, AturanService)  │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                  Database Context                            │
│     (KorektorBukuDbContext, SttsDbContext)                  │
└─────────────────────────────────────────────────────────────┘
```

## 3. Endpoint API

### 3.1 Authentication API (`/api/auth`)

API autentikasi menangani proses login, refresh token, dan informasi pengguna yang sedang aktif. Sistem menggunakan JWT dengan mekanisme refresh token yang disimpan dalam HTTP-only cookie untuk keamanan.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| POST | `/api/auth/login` | Login pengguna dengan username dan password |
| POST | `/api/auth/refresh` | Memperbarui access token menggunakan refresh token |
| GET | `/api/auth/me` | Mendapatkan informasi pengguna yang sedang login |

**Fitur Keamanan:**
- Access token dengan masa berlaku terbatas (default: 15 menit)
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
| GET | `/api/buku/stats` | Mendapatkan statistik buku (total, pending, selesai) |
| GET | `/api/buku/per-jurusan` | Mendapatkan statistik buku per jurusan |
| GET | `/api/buku/{id}` | Mendapatkan detail buku berdasarkan ID |
| GET | `/api/buku/admin` | Mendapatkan daftar buku untuk admin (dengan filter) |
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
| GET | `/api/dokumen/{id}/image/{imageName}` | Mendapatkan gambar dari dokumen |

---

### 3.5 Dosen API (`/api/dosen`)

API dosen menyediakan akses ke data dosen aktif dari database STTS.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| GET | `/api/dosen` | Mendapatkan daftar dosen aktif (join dengan tabel karyawan) |

---

### 3.6 Jurusan API (`/api/jurusan`)

API jurusan menyediakan akses ke data jurusan yang aktif.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| GET | `/api/jurusan` | Mendapatkan daftar jurusan aktif (Admin only) |

---

### 3.7 Template API (`/api/template`)

API template mengelola template dokumen pelengkap buku tugas akhir seperti lembar pengesahan, lembar pernyataan, dan dokumen administratif lainnya. API ini juga menyediakan fitur generate otomatis dokumen berdasarkan data mahasiswa.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| GET | `/api/template` | Mendapatkan semua template (Admin: semua, Mahasiswa: aktif saja) |
| GET | `/api/template/{id}` | Mendapatkan detail template dengan field mapping |
| POST | `/api/template` | Upload template baru (dengan ekstraksi highlighted text) |
| PATCH | `/api/template/{id}` | Update template (nama, status, details) |
| DELETE | `/api/template/{id}` | Menghapus template |
| GET | `/api/template/details` | Mendapatkan semua template details |
| DELETE | `/api/template/details/{id}` | Menghapus template detail |
| PATCH | `/api/template/details/{id}` | Update template detail |
| GET | `/api/template/{id}/pdf` | Download/view file PDF template |
| GET | `/api/template/{id}/docx` | Download file DOCX template |
| POST | `/api/template/{id}/generate` | Generate dokumen terisi berdasarkan data mahasiswa |

**Fitur Generate Template:**
- Mengganti highlighted text dengan data dinamis
- Mendukung format tanggal Indonesia (1 Januari 2024)
- Konversi case otomatis (Title Case, UPPERCASE)
- Integrasi dengan data proposal, pembimbing, dan penguji

---

### 3.8 Aturan API (`/api/aturan`)

API aturan mengelola konfigurasi aturan validasi dokumen tugas akhir termasuk aturan untuk format halaman, paragraf, gambar, tabel, dan elemen lainnya.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| GET | `/api/aturan` | Mendapatkan semua aturan (Admin only) |
| GET | `/api/aturan/{id}` | Mendapatkan aturan dengan details |
| GET | `/api/aturan/aktif` | Mendapatkan aturan yang aktif (Public) |
| POST | `/api/aturan` | Membuat aturan baru |
| PATCH | `/api/aturan/{id}` | Update aturan |
| PATCH | `/api/aturan/{id}/detail` | Update detail aturan |

---

### 3.9 Rules API (`/api/rules`)

API rules merupakan endpoint development untuk testing konfigurasi aturan tanpa autentikasi.

| Method | Endpoint | Deskripsi |
|--------|----------|-----------|
| GET | `/api/rules` | Mendapatkan semua aturan dengan details |
| GET | `/api/rules/{id}` | Mendapatkan aturan spesifik |
| POST | `/api/rules` | Membuat aturan dengan details |
| PATCH | `/api/rules/{id}` | Update aturan dengan details |
| DELETE | `/api/rules/{id}` | Hapus aturan dan details |
| DELETE | `/api/rules/{id}/detail/{detailId}` | Hapus detail spesifik |

---

## 4. Database Connection

REST API Service terhubung ke dua database:

### 4.1 Database Korektor Buku (`KorektorBukuDbContext`)

Database utama yang menyimpan data sistem validasi tugas akhir:
- **Buku** - Data buku tugas akhir
- **Dokumen** - Dokumen BAB buku
- **DokumenValidasi** - Hasil validasi dokumen
- **Template** - Template dokumen pelengkap
- **Aturan** - Konfigurasi aturan validasi
- Data ekstraksi dokumen (section, paragraf, tabel, gambar)

### 4.2 Database STTS (`SttsDbContext`)

Database institut yang menyimpan data master:
- **Mahasiswa** - Data mahasiswa
- **Dosen** - Data dosen
- **Jurusan** - Data jurusan/program studi
- **Proposal** - Data proposal tugas akhir
- **Bimbingan** - Data pembimbing dan penguji

---

## 5. Fitur Pendukung

### 5.1 WebSocket Service

REST API Service juga menyediakan WebSocket untuk komunikasi real-time dengan frontend. WebSocket digunakan untuk:
- Notifikasi progress upload dokumen
- Update status konversi PDF
- Notifikasi hasil validasi real-time
- Progress antrian validasi

### 5.2 PDF Conversion Service

Service konversi PDF menggunakan Adobe PDF Services API untuk mengkonversi dokumen DOCX menjadi PDF dan mengekstrak gambar halaman dokumen.

### 5.3 Email Service

Service email untuk mengirim notifikasi kepada mahasiswa mengenai status validasi dan hasil koreksi dokumen.

---

## 6. Keamanan

### 6.1 Authentication

- **JWT Token**: Access token dengan algoritma HS256
- **Refresh Token**: Disimpan dalam HTTP-only cookie
- **Token Expiry**: Access token (15 menit), Refresh token (7 hari)

### 6.2 Authorization

Role-based access control dengan dua peran:
- **Admin (BAA)**: Akses penuh ke semua endpoint
- **Mahasiswa**: Akses terbatas ke data sendiri

### 6.3 CORS

Cross-Origin Resource Sharing dikonfigurasi untuk mengizinkan akses dari:
- `http://localhost:5173` (Development frontend)
- `http://localhost:8000` (Alternative frontend)

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
| TemplateController | 11 | Template dokumen |
| AturanController | 6 | Aturan validasi |
| RulesController | 6 | Dev aturan |
| **Total** | **50** | - |

