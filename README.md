# SANTRI — Sistem Antrean Terintegrasi RSU Wijayakusuma Kebumen

Sistem antrean pendaftaran berbasis **C# WPF (.NET Framework 4.7.2)** dengan **Redis**
sebagai pusat state & komunikasi antar-PC (pub/sub). Target produksi: **Windows**.

## Komponen

| Proyek | Peran |
|---|---|
| `SANTRI.Core` | Library bersama: koneksi Redis, state antrean (key `DARMA:TAG:*`), pub/sub |
| `SANTRI.Token` | Kiosk pasien: ambil nomor antrean + cetak tiket thermal 58mm |
| `SANTRI.Client` | Panel petugas loket: panggil / panggil ulang antrean |
| `SANTRI.Server` | Display TV: nomor per loket, sisa antrean, suara panggilan, video |
| `SANTRI.Setup` | Installer Windows: pasang Redis + salin aplikasi ke `C:\SANTRI` |
| `MacPreview/` *(branch `mac-preview`)* | Port Avalonia **sementara** untuk preview GUI di macOS — bukan deliverable |

## Fitur 3 Jenis Antrean

Pasien memilih jenis antrean di Token, resepsionis memilih antrean yang dipanggil:

- **A — Umum**
- **B — Online JKN (BPJS)**
- **C — Helpdesk**

Tiap jenis punya penomoran sendiri (format `A-012`). Sudah di-backport ke WPF asli
(`SANTRI.Token/Client/Server`), status build lihat TODO di bawah.

## TODO

- [ ] **Suara huruf antrean**: `b.mp3` & `c.mp3` di `SANTRI.Server/Audios` masih hasil
      gTTS (suara Google) — beda karakter dengan rekaman manusia di file lama.
      Opsi: (1) minta paket rekaman asli ke vendor/sumber audio lama (kemungkinan besar
      paketnya sudah punya huruf A/B/C), atau (2) regenerate SEMUA audio dengan satu
      suara TTS konsisten (Google Cloud TTS / Azure Neural id-ID / Prosa.ai).
- [x] **Balikin ke Windows**: backport UI 3-jenis-antrean dari `MacPreview` ke WPF asli
      (`SANTRI.Core`, `SANTRI.Token/Client/Server` `MainWindow.xaml(.cs)`) selesai —
      build sukses (`dotnet build`, termasuk kompilasi XAML, 0 error). **Belum
      dicek di hardware asli**: cetak thermal, audio lewat speaker, penempatan
      multi-monitor — perlu uji langsung di PC Windows dengan printer/TV terpasang.
      Folder `MacPreview/` & branch `mac-preview` belum dihapus, menunggu konfirmasi.

### Backlog (nanti, disengaja belum dikerjakan)

- Keamanan: Redis `bind 0.0.0.0` + `protected-mode no` tanpa password, password admin
  hardcode, race condition update sisa antrean (get-then-set, bukan atomic).

## Menjalankan Preview di macOS (sementara)

```bash
git checkout mac-preview
MacPreview/run-preview.sh        # start Redis + Server + Token + Client loket 1-3
MacPreview/run-preview.sh stop   # matikan semuanya
```

Prasyarat: `brew install redis dotnet` (dan `lame` bila perlu generate audio).
