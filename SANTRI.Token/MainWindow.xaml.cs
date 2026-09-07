using SANTRI.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;


namespace SANTRI.Token
{
    public partial class MainWindow : Window
    {
        private AntrianRedisManager _redisManager;
        private DispatcherTimer _resetTimer;
        private DispatcherTimer _syncTimer;

        private string _tiketYangDicetak = "";
        private string _jenisYangDicetak = JenisAntrean.Umum;
        private const string ADMIN_PASSWORD = "admin";

        // Variabel Offline Mode — counter terpisah per jenis antrean
        private Dictionary<string, long> _localTotals = new Dictionary<string, long>
        {
            { JenisAntrean.Umum, 0 },
            { JenisAntrean.OnlineJKN, 0 },
            { JenisAntrean.OnsiteJKN, 0 },
            { JenisAntrean.Helpdesk, 0 }
        };
        private string _localFilePath = "lokal_tiket.txt";
        private bool _needsSync = false;

        private string _runningDate = DateTime.Now.ToString("yyyy-MM-dd");
        private bool _triggerAutoResetOnConnect = false;

        public MainWindow()
        {
            InitializeComponent();

            LoadLocalState();

            _redisManager = new AntrianRedisManager();
            _redisManager.OnError += (err) => { Dispatcher.Invoke(() => lblStatus.Text = err); };

            // Timer untuk mengembalikan layar dari "Tiket Sedang Dicetak" ke tombol utama (3 detik)
            _resetTimer = new DispatcherTimer();
            _resetTimer.Interval = TimeSpan.FromSeconds(3);
            _resetTimer.Tick += (s, e) =>
            {
                _resetTimer.Stop();
                PanelHasil.Visibility = Visibility.Collapsed;
                PanelUtama.Visibility = Visibility.Visible;
                SetTombolEnabled(true);
            };

            // Timer Background untuk mencoba sinkronisasi setiap 5 detik
            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _syncTimer.Tick += async (s, e) =>
            {
                // Cek apakah tanggal hari ini sudah berubah dari tanggal memori (Melewati jam 00:00)
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                if (_runningDate != today)
                {
                    _runningDate = today;
                    ResetLocalTotals();
                    SaveLocalState();

                    // Perintahkan sinkronisasi untuk me-reset Redis
                    _triggerAutoResetOnConnect = true;
                }

                await AttemptSyncAsync();
            };
            _syncTimer.Start();

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PindahkanKeMonitor(1);

            string redisConfig = AppConfig.GetRedisConnectionString();
            bool isConnected = _redisManager.Connect(redisConfig);
            lblStatus.Text = isConnected ? "Status: Terhubung ke Sistem" : "Status: Gagal Terhubung!";

            _ = AttemptSyncAsync();
        }

        private void btnAmbilUmum_Click(object sender, RoutedEventArgs e) => _ = AmbilAntreanAsync(JenisAntrean.Umum);
        private void btnAmbilJKN_Click(object sender, RoutedEventArgs e) => _ = AmbilAntreanAsync(JenisAntrean.OnlineJKN);
        private void btnAmbilJKNOnsite_Click(object sender, RoutedEventArgs e) => _ = AmbilAntreanAsync(JenisAntrean.OnsiteJKN);
        private void btnAmbilHelp_Click(object sender, RoutedEventArgs e) => _ = AmbilAntreanAsync(JenisAntrean.Helpdesk);

        private async Task AmbilAntreanAsync(string jenis)
        {
            SetTombolEnabled(false);

            _localTotals[jenis]++;
            SaveLocalState();

            _jenisYangDicetak = jenis;
            _tiketYangDicetak = JenisAntrean.Format(jenis, _localTotals[jenis]);

            lblJenisCetak.Text = $"Antrian {JenisAntrean.Label(jenis)} — Nomor Anda:";
            lblNomorCetak.Text = _tiketYangDicetak;
            UpdateTotalLabel();

            PanelUtama.Visibility = Visibility.Collapsed;
            PanelHasil.Visibility = Visibility.Visible;

            CetakTiket();

            _needsSync = true;
            await AttemptSyncAsync();

            _resetTimer.Start();
        }

        private void SetTombolEnabled(bool enabled)
        {
            btnAmbilUmum.IsEnabled = enabled;
            btnAmbilJKN.IsEnabled = enabled;
            btnAmbilJKNOnsite.IsEnabled = enabled;
            btnAmbilHelp.IsEnabled = enabled;
        }

        private void UpdateTotalLabel()
        {
            lblTotalAntrean.Text =
                $"A {_localTotals[JenisAntrean.Umum]:D3} · " +
                $"B {_localTotals[JenisAntrean.OnlineJKN]:D3} · " +
                $"C {_localTotals[JenisAntrean.OnsiteJKN]:D3} · " +
                $"D {_localTotals[JenisAntrean.Helpdesk]:D3}";
        }

        private void ResetLocalTotals()
        {
            foreach (string jenis in JenisAntrean.Semua) _localTotals[jenis] = 0;
            UpdateTotalLabel();
        }

        private async Task AttemptSyncAsync()
        {
            if (_redisManager.IsConnected)
            {
                lblStatus.Text = "Status: Terhubung ke Sistem";

                // EKSEKUSI RESET OTOMATIS JIKA ADA PERINTAH GANTI HARI
                if (_triggerAutoResetOnConnect)
                {
                    await _redisManager.ResetAntrianAsync();
                    await _redisManager.PublishCommandAsync("1:RESET");

                    _triggerAutoResetOnConnect = false; // Matikan penanda
                    SaveLocalState(); // Perbarui file lokal dengan tanggal hari ini
                }

                // Eksekusi penambahan nomor seperti biasa
                if (_needsSync)
                {
                    try
                    {
                        foreach (string jenis in JenisAntrean.Semua)
                        {
                            await _redisManager.SetTotalCountAsync(jenis, _localTotals[jenis]);

                            long tiketAktif = await _redisManager.GetActiveCountAsync(jenis);
                            int sisa = (int)(_localTotals[jenis] - tiketAktif);
                            await _redisManager.SetSisaCountAsync(jenis, Math.Max(0, sisa));
                        }

                        await _redisManager.PublishCommandAsync("1:NEW_TICKET");
                        _needsSync = false;
                    }
                    catch { }
                }
            }
            else
            {
                lblStatus.Text = "Status: Terputus (Menyimpan di memori lokal)";
            }
        }

        private void CetakTiket()
        {
            try
            {
                PrintDocument pd = new PrintDocument();
                // Menggunakan Printer Default Windows. Pastikan Thermal Printer sudah diset Default.
                // Jika ingin spesifik: pd.PrinterSettings.PrinterName = "NamaPrinterThermal";

                pd.PrintPage += new PrintPageEventHandler(PrintPage_Format);
                pd.Print();
            }
            catch (Exception)
            {
                // Abaikan atau log error jika printer mati/kertas habis agar aplikasi tidak crash
            }
        }

        private void PrintPage_Format(object sender, PrintPageEventArgs e)
        {
            Graphics g = e.Graphics;
            SolidBrush brush = new SolidBrush(System.Drawing.Color.Black);

            // KUNCI PERBAIKAN: Ubah lebar menjadi 190 (Standar aman untuk lebar kertas 58mm)
            float paperWidth = 180;

            float yPos = 5;

            // Ukuran font diturunkan kembali agar proporsional dengan lebar 58mm dan tidak menabrak tepi
            Font fontHeader = new Font("Arial", 10, System.Drawing.FontStyle.Bold);
            Font fontSub = new Font("Arial", 8, System.Drawing.FontStyle.Regular);
            Font fontTiket = new Font("Arial", 34, System.Drawing.FontStyle.Bold);

            StringFormat centerFormat = new StringFormat();
            centerFormat.Alignment = StringAlignment.Center;

            int xpos = 0;

            // --- AREA HEADER ---
            RectangleF rectHeader1 = new RectangleF(xpos, yPos, paperWidth, fontHeader.Height);
            g.DrawString("RSU WIJAYAKUSUMA", fontHeader, brush, rectHeader1, centerFormat);
            yPos += fontHeader.Height + 2;

            RectangleF rectHeader2 = new RectangleF(xpos, yPos, paperWidth, fontHeader.Height);
            g.DrawString("KEBUMEN", fontHeader, brush, rectHeader2, centerFormat);
            yPos += fontHeader.Height + 8;

            // --- GARIS PEMISAH ATAS ---
            System.Drawing.Pen penBlack = new System.Drawing.Pen(System.Drawing.Color.Black, 2);
            // Garis melintang dari piksel 5 sampai 185
            g.DrawLine(penBlack, xpos + 5, yPos, paperWidth - 5, yPos);
            yPos += 8;

            // Tanggal
            RectangleF rectDate = new RectangleF(xpos, yPos, paperWidth, fontSub.Height);
            g.DrawString(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), fontSub, brush, rectDate, centerFormat);
            yPos += fontSub.Height + 10;

            // --- TULISAN TIPE ANTREAN ---
            RectangleF rectTipe = new RectangleF(xpos, yPos, paperWidth, fontHeader.Height);
            g.DrawString($"Antrian {JenisAntrean.Label(_jenisYangDicetak)}", fontHeader, brush, rectTipe, centerFormat);
            yPos += fontHeader.Height + 8;

            // --- AREA NOMOR ANTREAN ---
            RectangleF rectTiket = new RectangleF(xpos, yPos, paperWidth, fontTiket.Height);
            g.DrawString(_tiketYangDicetak, fontTiket, brush, rectTiket, centerFormat);
            yPos += fontTiket.Height + 10;

            // --- GARIS PEMISAH BAWAH ---
            g.DrawLine(penBlack, xpos + 5, yPos, paperWidth - 5, yPos);
            yPos += 8;

            // --- AREA FOOTER ---
            RectangleF rectFooter1 = new RectangleF(xpos, yPos, paperWidth, fontSub.Height);
            g.DrawString("Silakan menunggu sampai", fontSub, brush, rectFooter1, centerFormat);
            yPos += fontSub.Height + 2;

            RectangleF rectFooter2 = new RectangleF(xpos, yPos, paperWidth, fontSub.Height);
            g.DrawString("nomor antrean Anda dipanggil.", fontSub, brush, rectFooter2, centerFormat);
        }

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                txtPassword.Password = "";
                PanelPassword.Visibility = Visibility.Visible;
                txtPassword.Focus();
            }
        }

        private void btnCancelReset_Click(object sender, RoutedEventArgs e)
        {
            PanelPassword.Visibility = Visibility.Collapsed;
        }

        private async void btnConfirmReset_Click(object sender, RoutedEventArgs e)
        {
            if (txtPassword.Password == ADMIN_PASSWORD)
            {
                PanelPassword.Visibility = Visibility.Collapsed;

                ResetLocalTotals();
                SaveLocalState();

                if (_redisManager.IsConnected)
                {
                    await _redisManager.ResetAntrianAsync();
                    await _redisManager.PublishCommandAsync("1:RESET");
                }

                System.Windows.MessageBox.Show("Sistem Token berhasil di-reset.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show("Password salah!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtPassword.Password = "";
                txtPassword.Focus();
            }
        }

        // Format file: yyyy-MM-dd|totalUmum|totalJKN|totalJKNOnsite|totalHelp
        private void LoadLocalState()
        {
            try
            {
                if (File.Exists(_localFilePath))
                {
                    string[] parts = File.ReadAllText(_localFilePath).Split('|');
                    if (parts.Length == 5 && parts[0] == DateTime.Now.ToString("yyyy-MM-dd"))
                    {
                        _localTotals[JenisAntrean.Umum] = long.Parse(parts[1]);
                        _localTotals[JenisAntrean.OnlineJKN] = long.Parse(parts[2]);
                        _localTotals[JenisAntrean.OnsiteJKN] = long.Parse(parts[3]);
                        _localTotals[JenisAntrean.Helpdesk] = long.Parse(parts[4]);
                    }
                    else
                    {
                        // Hari telah berganti (atau format lama) — mulai dari nol
                        _triggerAutoResetOnConnect = true;
                    }
                }
            }
            catch { ResetLocalTotals(); }

            if (lblTotalAntrean != null) UpdateTotalLabel();
        }

        private void SaveLocalState()
        {
            try
            {
                File.WriteAllText(_localFilePath,
                    $"{DateTime.Now:yyyy-MM-dd}" +
                    $"|{_localTotals[JenisAntrean.Umum]}" +
                    $"|{_localTotals[JenisAntrean.OnlineJKN]}" +
                    $"|{_localTotals[JenisAntrean.OnsiteJKN]}" +
                    $"|{_localTotals[JenisAntrean.Helpdesk]}");
            }
            catch { }
        }


        private void PindahkanKeMonitor(int monitorIndex)
        {
            // Ambil daftar semua monitor yang terhubung ke PC
            Screen[] daftarLayar = Screen.AllScreens;

            // Pastikan monitor yang diminta benar-benar ada (mencegah error jika TV belum dicolok)
            if (daftarLayar.Length > monitorIndex)
            {
                Screen layarTarget = daftarLayar[monitorIndex];

                // Wajib ubah StartupLocation ke Manual agar posisi bisa dioverride
                this.WindowStartupLocation = WindowStartupLocation.Manual;

                // Kembalikan ke Normal dulu sebelum dipindah (mencegah bug grafis WPF)
                this.WindowState = WindowState.Normal;

                // Geser koordinat jendela ke ujung kiri atas monitor target
                this.Left = layarTarget.WorkingArea.Left;
                this.Top = layarTarget.WorkingArea.Top;

                // Maximize layar secara otomatis di monitor target
                this.WindowState = WindowState.Maximized;
            }
            else
            {
                // Jika monitor ke-2 tidak ditemukan, biarkan aplikasi terbuka di monitor utama
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
    }
}