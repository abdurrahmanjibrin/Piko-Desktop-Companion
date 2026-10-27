# Piko — Interactive AI Desktop Companion for Windows (v0.2.0)

**Piko — Your Tiny Desk Buddy.**
Piko adalah karakter digital desktop interaktif untuk Windows 10 dan 11 yang tinggal di floating notch bagian atas layar. Piko dirancang untuk terasa hidup, ramah, tidak mengganggu, dan membantu produktivitas harian Anda—terutama saat menunggu AI menyelesaikan generasi jawaban atau saat merapikan file laptop.

---

## 🌟 Fitur Utama v0.2.0

### 1. Living Notch & Living Eyes System
- **Floating Pill Notch:** Berada di bagian atas layar (`Topmost`, borderless, transparent), beranimasi mulus saat meluas (`Expand`) atau menyusut (`Collapse`).
- **Flicker-Free Hover:** Notch membuka otomatis saat kursor diarahkan, dan menutup dengan jeda terkonfigurasi (default 500ms) tanpa berkedip saat kursor berpindah ke panel.
- **Expressive Eye Engine:** Mata bukan gambar statis! Dibangun dengan elemen vektor WPF dengan state machine ekspresi:
  - `Idle`: Bernapas lembut dan berkedip secara natural dengan interval dinamis (2.4s – 5.6s).
  - `Hover`: Mata fokus menatap kursor secara halus dengan batas jangkauan natural.
  - `Thinking`: Mata menyipit dengan sudut berpikir saat sedang menganalisis.
  - `Listening`: Mata membesar reseptif dan ramah.
  - `Working`: Pupil beranimasi bergerak berirama saat memproses tugas latar belakang.
  - `Success`: Menampilkan lengkungan gembira (`^ ^`) disertai animasi *joyful bounce* dan border lime glow (`#A8F07A`).
  - `Error`: Ekspresi asimetris bingung/sedih jika operasi gagal.
  - `Sleepy`: Kelopak mata mengantuk (`- -`).
  - `Alert`: Mata membesar waspada untuk menarik perhatian.

### 2. Real-Time ChatGPT Completion Bridge
- **Browser Extension (Manifest V3):** Memantau status generasi ChatGPT di Chrome dan Edge.
  - Mendeteksi `generation_started` ➔ Piko berpindah ke state `Working`/`Thinking`.
  - Mendeteksi `generation_completed` ➔ Piko merayakan dengan lengkungan mata senang (`^ ^`), melompat riang (*bounce*), memainkan audio chime, dan menampilkan pesan: **"ChatGPT selesai! Jawabanmu sudah siap ✨"**.
  - Mendeteksi pembatalan manual ➔ Piko berpindah ke `Alert` tanpa memicu selebrasi palsu.
  - **100% Privacy-Preserving:** Tidak membaca teks prompt, isi jawaban, riwayat chat, atau cookie.
- **Secure Local Bridge:** Mendengarkan hanya pada loopback IPv4 `127.0.0.1:17321` dengan token autentikasi rahasia dan validasi origin ekstensi.

### 3. AI Storage Assistant & Smart Organizer
- **Drive Overview:** Pemantauan kapasitas drive (total, terpakai, dan sisa ruang kosong).
- **Targeted Folder Scan:** Memindai folder pengguna secara aman (Downloads, Desktop, Documents, Pictures, Videos).
- **File Breakdown:** Mengelompokkan otomatis ke dalam 6 kategori:
  - 📄 *Documents* (PDF, Word, Excel, PPT, Text, Markdown)
  - 🖼️ *Images* (Screenshots, Photos, Artwork)
  - 🎬 *Media* (Audio, Video)
  - 💻 *Code & Projects* (Source code, scripts, configs)
  - 📦 *Archives & Apps* (ZIP, RAR, 7z, Installers)
  - 📁 *Others*
- **Large Files Finder:** Mendeteksi file berukuran besar (> 50 MB) secara instan.
- **Duplicate Candidates Finder:** Mendeteksi file duplikat dengan ukuran identik dan verifikasi sidik jari konten (SHA256).
- **Smart Organize Plan & Preview:** Menampilkan tabel rencana penataan subfolder rapi sebelum dieksekusi.
- **Prinsip Keamanan Mutlak:**
  - **TIDAK PERNAH menghapus file.**
  - **Pencegahan Konflik:** Jika file tujuan sudah ada, otomatis menambahkan penomoran `_piko1`, `_piko2`.
  - **Reversible Undo:** Setiap perpindahan dicatat di `OperationJournal`. Anda dapat mengklik **"↩ Undo Move"** kapan saja untuk mengembalikan file ke lokasi aslinya.
  - **Extensible AI Interface:** Mendukung `IStorageClassifier` (Rule-Based bawaan + siap dihubungkan ke model lokal Ollama).

### 4. Productivity Suite Terintegrasi
- **Focus Timer:** Timer Pomodoro 25 menit dengan Start, Pause, Resume, Reset, dan notifikasi ekspresi saat sesi selesai.
- **Water Break Tracker:** Pelacak 8 gelas air per hari dengan progress bar dan reset otomatis setiap hari baru.
- **Little List (Tasks):** Manajemen tugas dengan batas waktu (*due date*), label *overdue*, coret selesai, dan penyimpanan otomatis.
- **Temporary File Shelf:** Area parkir referensi file cepat via drag-and-drop dari Windows Explorer (tanpa memindahkan/mengubah file asli).
- **Audio & DND Control:** Pengaturan suara aktif/senyap dan mode *Do Not Disturb*.

---

## 💻 Persyaratan Sistem

- Windows 10 atau Windows 11 (x64)
- .NET 8.0 SDK
- Google Chrome atau Microsoft Edge (untuk ekstensi ChatGPT)

---

## 🚀 Menjalankan Piko

### 1. Kompilasi & Jalankan Aplikasi Desktop

Buka PowerShell di folder workspace:

```powershell
# Jalankan aplikasi langsung
dotnet run --project PikoStarter/Piko/Piko.csproj
```

Atau kompilasi versi Release:

```powershell
dotnet build PikoStarter/Piko/Piko.csproj -c Release
```

### 2. Menjalankan Unit Tests

Seluruh logika bisnis (validasi bridge, scanner, duplicate detector, smart organizer, collision protection, reversible undo, dan state machine) telah diuji dengan xUnit:

```powershell
dotnet test PikoStarter/Piko.Tests/Piko.Tests.csproj
```

### 3. Memasang Ekstensi Browser di Chrome / Edge

1. Pastikan aplikasi Piko sudah berjalan (bridge aktif di `127.0.0.1:17321`).
2. Buka `chrome://extensions` (Chrome) atau `edge://extensions` (Edge).
3. Aktifkan **Developer mode** di pojok kanan/kiri atas.
4. Klik **Load unpacked** (*Muat yang belum dibongkar*).
5. Pilih folder: `d:\Piko_Desktop_Companion_v0.2\PikoStarter\PikoBrowserExtension`.
6. Buka `https://chatgpt.com/` dan kirim prompt apa saja.
7. Saat ChatGPT mulai menjawab, mata Piko akan masuk ke mode `Working`/`Thinking`. Saat selesai, Piko akan melompat gembira (`^ ^`) dan membunyikan chime!

---

## 🔒 Privasi dan Penyimpanan Lokal

- Semua data tugas, air, preferensi, dan riwayat operasi disimpan secara lokal di:
  `%LOCALAPPDATA%\Piko\state.json`
- Local bridge hanya menerima koneksi dari `127.0.0.1` (loopback PC Anda sendiri) dan tidak pernah mengirim data ke server luar.
