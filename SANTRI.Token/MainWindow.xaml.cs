using SANTRI.Core;
using System;
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
        private const string ADMIN_PASSWORD = "admin";

        // Variabel Offline Mode
        private long _localTotal = 0;
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
                btnAmbilAntrean.IsEnabled = true;
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
                    _localTotal = 0;
                    lblTotalAntrean.Text = "000";
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

        private async void btnAmbilAntrean_Click(object sender, RoutedEventArgs e)
        {
            btnAmbilAntrean.IsEnabled = false;

            _localTotal++;
            SaveLocalState();

            // Pastikan variabel _tiketYangDicetak juga diset ke format 3 digit
            _tiketYangDicetak = _localTotal.ToString("D3");

            lblNomorCetak.Text = _tiketYangDicetak;

            // TAMBAHKAN BARIS INI: Perbarui info total antrean
            lblTotalAntrean.Text = _tiketYangDicetak;

            PanelUtama.Visibility = Visibility.Collapsed;
            PanelHasil.Visibility = Visibility.Visible;

            CetakTiket();

            _needsSync = true;
            await AttemptSyncAsync();

            _resetTimer.Start();
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
                        await _redisManager.SetTotalCountAsync(_localTotal);

                        long tiketAktif = await _redisManager.GetActiveCountAsync();
                        int sisa = (int)(_localTotal - tiketAktif);
                        await _redisManager.SetSisaCountAsync(Math.Max(0, sisa));

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
            Font fontTiket = new Font("Arial", 40, System.Drawing.FontStyle.Bold);

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
            g.DrawString("Antrian Pendaftaran", fontHeader, brush, rectTipe, centerFormat);
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

                _localTotal = 0;
                SaveLocalState();

                // TAMBAHKAN BARIS INI: Set total kembali ke 000
                lblTotalAntrean.Text = "000";

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

        private void LoadLocalState()
        {
            try
            {
                if (File.Exists(_localFilePath))
                {
                    string[] parts = File.ReadAllText(_localFilePath).Split('|');
                    if (parts.Length == 2)
                    {
                        string savedDate = parts[0];
                        if (savedDate == DateTime.Now.ToString("yyyy-MM-dd"))
                        {
                            // Hari masih sama, lanjutkan angka terakhir
                            _localTotal = long.Parse(parts[1]);
                        }
                        else
                        {
                            // HARI TELAH BERGANTI saat aplikasi dibuka!
                            _localTotal = 0;
                            _triggerAutoResetOnConnect = true;
                        }
                    }
                }
            }
            catch { _localTotal = 0; }

            if (lblTotalAntrean != null) lblTotalAntrean.Text = _localTotal.ToString("D3");
        }

        private void SaveLocalState()
        {
            try { File.WriteAllText(_localFilePath, $"{DateTime.Now:yyyy-MM-dd}|{_localTotal}"); } catch { }
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