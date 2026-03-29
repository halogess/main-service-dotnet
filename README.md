# Main Service - Validasi Tugas Akhir

Service utama untuk sistem validasi tugas akhir menggunakan .NET 9.0 dengan integrasi Adobe PDF Services.

## Prerequisites

- .NET 9.0 SDK
- Docker & Docker Compose
- MySQL 8.0+
- Adobe PDF Services API credentials

## Tech Stack

- ASP.NET Core 9.0
- Entity Framework Core 9.0
- MySQL (Pomelo)
- Adobe PDF Services API
- WebSocket
- JWT Authentication

## Setup

### 1. Clone Repository

```bash
git clone <repository-url>
cd main-service-dotnet
```

### 2. Konfigurasi Environment Variables

Copy file `.env.example` menjadi `.env`:

```bash
# Windows
copy .env.example .env

# Linux/macOS
cp .env.example .env
```

Edit file `.env` dan sesuaikan konfigurasi:

```env
# Storage Path (sesuaikan dengan OS)
VOLUME_BASE_PATH=E:/docker-volumes/validasi-ta  # Windows
# VOLUME_BASE_PATH=/var/docker-volumes/validasi-ta  # Linux
# VOLUME_BASE_PATH=~/docker-volumes/validasi-ta  # macOS

# Database Connections
ConnectionStrings__SttsDbConnection=Server=host.docker.internal;Port=3306;Database=db_stts;User=root;Password=your_password
ConnectionStrings__KorektorBukuDbConnection=Server=host.docker.internal;Port=3307;Database=db_korektor_buku;User=root;Password=your_password

# JWT Configuration
Auth__JwtSecret=your-secret-key-min-32-characters-long
Auth__AccessTokenExpiryMinutes=15
Auth__RefreshTokenExpiryDays=7
Auth__AdminUsername=admin
Auth__ExternalApiUrl=https://ws.stts.edu/credential
Auth__ExternalApiToken=your-token
Auth__AppName=ta_korektor_buku

# Adobe PDF Services
Adobe__ClientId=your-adobe-client-id
Adobe__ClientSecret=your-adobe-client-secret
Adobe__ApiBaseUrl=https://pdf-services-ue1.adobe.io
```

### 3. Buat Storage Directory

```bash
# Windows
mkdir E:\docker-volumes\validasi-ta

# Linux
mkdir -p /var/docker-volumes/validasi-ta

# macOS
mkdir -p ~/docker-volumes/validasi-ta
```

### 4. Setup Database

Pastikan MySQL server berjalan dengan 2 database:
- `db_stts` (Port 3306)
- `db_korektor_buku` (Port 3307)

Database schema harus sudah ada dan dikonfigurasi sebelumnya.

### 5. Restore Dependencies

```bash
dotnet restore
```

## Running the Application

### Development (Local)

```bash
dotnet run
```

Service akan berjalan di `http://localhost:5062`

### Production (Docker)

```bash
docker-compose up --build
```

Service akan berjalan di `http://localhost:5062`

## API Endpoints

### Authentication
- `POST /api/auth/login` - Login
- `POST /api/auth/refresh` - Refresh token

### Mahasiswa
- `GET /api/mahasiswa` - Get all mahasiswa
- `GET /api/mahasiswa/{id}` - Get mahasiswa by ID

### Buku
- `GET /api/buku` - Get all buku
- `POST /api/buku` - Create buku
- `PUT /api/buku/{id}` - Update buku
- `DELETE /api/buku/{id}` - Delete buku

### Dokumen
- `POST /api/dokumen/upload` - Upload dokumen
- `GET /api/dokumen/{id}` - Get dokumen

### Jurusan
- `GET /api/jurusan` - Get all jurusan

### WebSocket
- `WS /ws` - WebSocket connection untuk real-time updates

## Project Structure

```
main-service-dotnet/
├── Controllers/        # API Controllers
├── Database/          # DbContext configurations
├── Middleware/        # Custom middleware (Auth)
├── Models/           # Entity models
├── Services/         # Business logic services
├── storage/          # File storage
├── .env              # Environment variables
├── docker-compose.yml
├── Dockerfile
└── Program.cs        # Application entry point
```

## Features

- JWT Authentication dengan refresh token
- Multi-database support (STTS & Korektor Buku)
- Adobe PDF Services integration untuk konversi dokumen
- WebSocket untuk real-time notifications
- Background service untuk PDF queue processing
- File upload & management
- CORS enabled untuk frontend integration

## Environment

- Development: `http://localhost:5062`
- Frontend CORS: `http://localhost:5173`, `http://localhost:8000`

## Troubleshooting

### Database Connection Error
- Pastikan MySQL server berjalan
- Cek connection string di `.env`
- Gunakan `host.docker.internal` untuk koneksi dari Docker ke host

### Storage Permission Error
- Pastikan directory storage sudah dibuat
- Cek permission directory (Linux/macOS)

### Adobe API Error
- Verifikasi Adobe credentials di `.env`
- Cek quota Adobe PDF Services

## License

[Your License]
