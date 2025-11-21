# API Jurusan - Admin Documentation

## Base URL
```
http://localhost:5123/api/jurusan
```

## Authentication
Endpoint ini hanya untuk admin. Memerlukan Bearer token dengan role admin:
```
Authorization: Bearer <admin_token>
```

---

## GET /api/jurusan - List Semua Jurusan

Mengambil daftar semua jurusan yang tersedia.

### Request
```bash
GET /api/jurusan
Authorization: Bearer <admin_token>
```

### Response Success (200 OK)
```json
[
  {
    "kode": "IF",
    "nama": "Teknik Informatika"
  },
  {
    "kode": "SI",
    "nama": "Sistem Informasi"
  },
  {
    "kode": "TI",
    "nama": "Teknik Industri"
  }
]
```

### Response Error (403 Forbidden)
Jika bukan admin:
```json
{
  "message": "Forbidden"
}
```

### Response Error (401 Unauthorized)
Jika token tidak valid atau tidak ada:
```json
{
  "message": "Token tidak valid"
}
```

---

## Catatan
- Endpoint ini hanya bisa diakses oleh admin
- Data diambil dari tabel `aka_jurusan` di database STTS
- Digunakan untuk filter atau dropdown di dashboard admin
