# API Buku - Admin Documentation

## Base URL
```
http://localhost:5123/api/buku
```

## Authentication
Semua endpoint memerlukan Bearer token dengan role admin:
```
Authorization: Bearer <admin_token>
```

---

## 1. GET /api/buku/stats - Statistik Dashboard

### Request
```bash
# Semua mahasiswa
GET /api/buku/stats

# Filter mahasiswa tertentu
GET /api/buku/stats?nrp=222117032
```

### Response
```json
{
  "total": 50,
  "dalam_antrian": 10,
  "diproses": 5,
  "lolos": 33,
  "tidak_lolos": 2,
  "menunggu_validasi": 15
}
```

---

## 2. GET /api/buku - List Buku dengan Detail Mahasiswa

### Request
```bash
# Semua buku
GET /api/buku?limit=10&offset=0

# Filter status
GET /api/buku?status=dalam_antrian,diproses&limit=10&offset=0

# Filter NRP
GET /api/buku?nrp=222117032&limit=10&offset=0

# Kombinasi
GET /api/buku?nrp=222117032&status=lolos,tidak_lolos&sort=asc&limit=20&offset=0
```

### Query Parameters
- `nrp` (optional) - NRP mahasiswa
- `status` (optional) - Status: dalam_antrian, diproses, lolos, tidak_lolos (comma-separated)
- `sort` (optional) - asc/desc (default: desc)
- `limit` (optional) - Jumlah data (default: 10)
- `offset` (optional) - Pagination offset (default: 0)

### Response
```json
{
  "data": [
    {
      "id": 5,
      "judul": "Sistem Informasi Perpustakaan",
      "nrp": "222117032",
      "nama": "John Doe",
      "jurusan": "IF",
      "tanggal_upload": "2024-01-15T08:30:00",
      "jumlah_bab": 5,
      "status": "lolos",
      "skor": 92,
      "jumlah_kesalahan": 2
    }
  ],
  "total": 1,
  "limit": 10,
  "offset": 0
}
```

---

## 3. GET /api/buku/can-upload - Cek Status Upload

### Request
```bash
GET /api/buku/can-upload
```

### Response
```json
{
  "can_upload": true
}
```

---

## 4. POST /api/buku - Upload Buku

### Request
```bash
POST /api/buku
Content-Type: multipart/form-data

Form Data:
- judul: "Judul Buku"
- files: [file1.docx, file2.docx, file3.docx]
```

### Response Success
```json
{
  "message": "Buku berhasil diupload",
  "buku_id": 10
}
```

### Response Error
```json
{
  "message": "Masih ada buku dalam antrian"
}
```

---

## Status Buku
- `dalam_antrian` - Buku baru diupload, menunggu diproses
- `diproses` - Sedang dikonversi ke PDF
- `lolos` - Validasi berhasil
- `tidak_lolos` - Validasi gagal

---

## Keistimewaan Admin
1. Bisa lihat semua buku dari semua mahasiswa
2. Mendapat data tambahan: nama dan jurusan mahasiswa
3. Bisa filter berdasarkan NRP mahasiswa tertentu
4. Statistik dashboard dengan field menunggu_validasi
