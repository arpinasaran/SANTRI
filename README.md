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

## Fitur 4 Jenis Antrean

Pasien memilih jenis antrean di Token, resepsionis memilih antrean yang dipanggil:

- **A — Umum / Asuransi**
- **B — Online JKN (BPJS)**
- **C — Onsite JKN (BPJS)**
- **D — Helpdesk**

Tiap jenis punya penomoran sendiri (format `A-012`). Sudah di-backport ke WPF asli
(`SANTRI.Token/Client/Server`), status build lihat TODO di bawah.

## TODO

- [ ] **Kualitas suara huruf antrean**: `b.mp3`, `c.mp3`, `d.mp3` di
      `SANTRI.Server/Audios` sudah berpelafalan Indonesia ("be", "ce", "de")
      via gTTS `tl=id`, bukan lagi SAPI `Microsoft Zira` (en-US) yang terdengar
      "bee/see/dee". Spesifikasi disamakan ke `a.mp3` (MP3 24 kHz mono 64 kbps)
      dan puncak volume dinormalkan ke −5,5 dB seperti `a.mp3`, jadi bisa
      di-drop-in replace kapan saja tanpa ubah kode.
      Sisa pekerjaan: karakter suaranya masih TTS, beda dari rekaman manusia di
      file lama. Opsi: (1) minta paket rekaman asli ke vendor/sumber audio lama,
      atau (2) regenerate SEMUA audio dengan satu suara TTS konsisten
      (Google Cloud TTS / Azure Neural id-ID / Prosa.ai).
- [x] **Balikin ke Windows**: backport UI jenis-antrean dari `MacPreview` ke WPF asli
      (`SANTRI.Core`, `SANTRI.Token/Client/Server` `MainWindow.xaml(.cs)`) selesai —
      build sukses (`dotnet build`, termasuk kompilasi XAML, 0 error). Verifikasi
      runtime dengan Redis asli sudah dilakukan: penomoran per jenis terisolasi,
      panggilan loket tersinkron ke display TV lewat pub/sub, keempat file audio
      huruf terbaca WPF `MediaPlayer`. **Belum dicek di hardware asli**: cetak
      thermal, audio lewat speaker, penempatan multi-monitor — perlu uji langsung
      di PC Windows dengan printer/TV terpasang.
      Folder `MacPreview/` & branch `mac-preview` belum dihapus, menunggu konfirmasi.

### Catatan operasional

Jangan biarkan printer default PC kiosk berupa driver virtual (**Microsoft Print
to PDF**, Fax, XPS). Driver semacam itu memunculkan dialog "Save Print Output As"
yang memblokir UI thread `SANTRI.Token`, sehingga kiosk terlihat menggantung dan
nomor antrean gagal tersimpan ke Redis. Set printer thermal sebagai default.

### Build tanpa Visual Studio

Repo ini **sengaja tidak memuat `Directory.Build.props`**. File tersebut pernah
ada untuk menarik reference assemblies net472 dari NuGet supaya `dotnet build`
jalan di mesin tanpa Visual Studio, tapi efek sampingnya merusak PC yang justru
punya Visual Studio: satu `PackageReference` saja membuat NuGet menganggap
`SANTRI.Core` bergaya PackageReference, sehingga **15 paket di
`SANTRI.Core/packages.config` (termasuk `StackExchange.Redis`) tidak di-restore**
dan build gagal dengan `CS0246: The type or namespace name 'StackExchange'
could not be found`. Di clone baru (folder `packages/` masuk `.gitignore`) ini
pasti terjadi.

Jadi: PC dengan Visual Studio cukup buka `SANTRI.slnx` dan build seperti biasa.
Untuk build di mesin tanpa Visual Studio, pakai proyek shim SDK-style **di luar
repo** yang me-`Compile Include`/`Page Include` file sumber ini lewat path
relatif — tidak ada file yang di-track git yang perlu diubah.

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
