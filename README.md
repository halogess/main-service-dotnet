### **Langkah 2: Menjalankan Proyek untuk Pertama Kali**

Untuk memastikan semuanya berfungsi, mari kita jalankan API contoh yang sudah dibuatkan.

1.  **Buka Terminal** di dalam VS Code (`Ctrl + ``).
2.  **Jalankan perintah `dotnet run`**:

    ```bash
    dotnet run
    ```

    Terminal akan menampilkan output yang mirip seperti ini, yang menunjukkan bahwa server Anda sedang berjalan:

    ```
    Building...
    info: Microsoft.Hosting.Lifetime
          Now listening on: https://localhost:7123
    info: Microsoft.Hosting.Lifetime
          Now listening on: http://localhost:5123
    ```
    *   Proyek .NET secara default berjalan di HTTPS (port 7000-an) dan HTTP (port 5000-an).

3.  **Uji Endpoint**: Buka browser Anda dan navigasi ke `https://localhost:7123/weatherforecast`. Anda akan melihat respons JSON berisi data cuaca acak.

    Selamat! Anda telah berhasil membuat dan menjalankan Web API pertama Anda di C#. Tekan `Ctrl + C` di terminal untuk menghentikan server.

### **Langkah 3: Membersihkan dan Membuat Controller Anda Sendiri**

Sekarang, mari kita hapus contoh bawaan dan buat *endpoint* "Hello World" kita sendiri.

1.  **Hapus File Contoh**:
    *   Hapus file `WeatherForecastController.cs` dari dalam folder `Controllers`.
    *   Hapus file `WeatherForecast.cs` dari *root* proyek (jika ada).

2.  **Buat Controller Baru**:
    *   Buat file baru di dalam folder `Controllers` bernama `ValidationController.cs`.

3.  **Tulis Kode Controller**:
    Salin dan tempel kode berikut ke dalam `ValidationController.cs`.

    ```csharp
    using Microsoft.AspNetCore.Mvc;

    namespace ValidasiTugasAkhir.MainService.Controllers;

    [ApiController]
    [Route("api/[controller]")] // Ini akan membuat URL dasar menjadi /api/validation
    public class ValidationController : ControllerBase
    {
        // Ini setara dengan: app.get('/api/validation/hello', ...)
        [HttpGet("hello")]
        public IActionResult GetHello()
        {
            // Mengembalikan respons JSON dengan status 200 OK
            return Ok(new { message = "Halo dari Layanan Utama C#!" });
        }
    }
    ```
    *   `[ApiController]`: Atribut yang mengaktifkan beberapa fitur API yang berguna.
    *   `[Route("api/[controller]")]`: Mendefinisikan *routing* dasar. `[controller]` akan secara otomatis diganti dengan nama *controller* tanpa kata "Controller" (jadi, `Validation`).
    *   `ControllerBase`: Kelas dasar yang menyediakan fungsionalitas untuk menangani permintaan HTTP (mirip objek `req` dan `res` di Express).
    *   `[HttpGet("hello")]`: Mendefinisikan *endpoint* ini untuk merespons permintaan GET di sub-path `/hello`.
    *   `Ok(...)`: Fungsi pembantu untuk mengembalikan respons HTTP 200 OK dengan *body* JSON.

### **Langkah 4: Jalankan dan Uji Lagi**

1.  Kembali ke terminal di VS Code.
2.  Jalankan `dotnet run`.
3.  Buka browser dan navigasi ke URL baru: `https://localhost:7123/api/validation/hello`.

Anda sekarang akan melihat respons: `{"message":"Halo dari Layanan Utama C#!"}`.

---

**Selamat! Anda telah berhasil:**
*   Membuat proyek ASP.NET Core Web API dari awal.
*   Memahami struktur dasar proyek.
*   Menjalankan server pengembangan.
*   Membuat *controller* dan *endpoint* kustom Anda sendiri.

Langkah berikutnya adalah mulai menambahkan paket NuGet (seperti Entity Framework Core dan Open XML SDK) dan membangun logika bisnis Anda di dalam *controller* atau *service class* yang baru.