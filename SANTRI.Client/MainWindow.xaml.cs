using SANTRI.Core;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SANTRI.Client
{
    public partial class MainWindow : Window
    {
        private AntrianRedisManager _redisManager;
        private DispatcherTimer _syncTimer;
        private int _myLoketId;

        public MainWindow()
        {
            InitializeComponent();

            // Baca ID Loket dari file konfigurasi lokal
            _myLoketId = AppConfig.GetLoketId();            
            lblLoketNo.Text = $"LOKET {_myLoketId}";

            _redisManager = new AntrianRedisManager();
            _redisManager.OnError += (err) => { Dispatcher.Invoke(() => lblStatus.Text = err); };

            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _syncTimer.Tick += SyncTimer_Tick;

            Loaded += MainWindow_Loaded;
            Closing += (s, e) => _syncTimer.Stop();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            string redisConfig = AppConfig.GetRedisConnectionString();
            bool isConnected = _redisManager.Connect(redisConfig);

            if (isConnected)
            {
                lblStatus.Text = $"Status: Terhubung | Mengontrol Loket {_myLoketId}";
                _syncTimer.Start();
            }
            else
            {
                lblStatus.Text = "Status: Terputus dari Redis Server!";
            }
        }

        private async void SyncTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                int sisaUmum = await _redisManager.GetSisaCountAsync(JenisAntrean.Umum);
                int sisaJKN = await _redisManager.GetSisaCountAsync(JenisAntrean.OnlineJKN);
                int sisaHelp = await _redisManager.GetSisaCountAsync(JenisAntrean.Helpdesk);

                long myCurrentNumber = await _redisManager.GetLoketNumberAsync(_myLoketId);
                string myCurrentJenis = await _redisManager.GetLoketJenisAsync(_myLoketId);

                lblSisaUmum.Text = sisaUmum.ToString();
                lblSisaJKN.Text = sisaJKN.ToString();
                lblSisaHelp.Text = sisaHelp.ToString();
                lblCurrentCall.Text = myCurrentNumber == 0 ? "---" : JenisAntrean.Format(myCurrentJenis, myCurrentNumber);

                btnCallUmum.IsEnabled = sisaUmum > 0;
                btnCallJKN.IsEnabled = sisaJKN > 0;
                btnCallHelp.IsEnabled = sisaHelp > 0;
                btnRecall.IsEnabled = myCurrentNumber > 0;
            }
            catch { }
        }

        private void btnCallUmum_Click(object sender, RoutedEventArgs e) => _ = PanggilAsync(JenisAntrean.Umum, btnCallUmum);
        private void btnCallJKN_Click(object sender, RoutedEventArgs e) => _ = PanggilAsync(JenisAntrean.OnlineJKN, btnCallJKN);
        private void btnCallHelp_Click(object sender, RoutedEventArgs e) => _ = PanggilAsync(JenisAntrean.Helpdesk, btnCallHelp);

        private async Task PanggilAsync(string jenis, System.Windows.Controls.Button tombol)
        {
            tombol.IsEnabled = false;

            try
            {
                int sisaSaatIni = await _redisManager.GetSisaCountAsync(jenis);
                if (sisaSaatIni <= 0) return;

                // Ambil nomor antrean berikutnya dari Pool terpusat khusus jenis ini
                long nextNumber = await _redisManager.IncrementActiveCountAsync(jenis);

                // Update data loket spesifik dan kurangi sisa antrean
                await _redisManager.SetLoketNumberAsync(_myLoketId, nextNumber);
                await _redisManager.SetLoketJenisAsync(_myLoketId, jenis);
                int sisaBaru = sisaSaatIni - 1;
                await _redisManager.SetSisaCountAsync(jenis, sisaBaru);

                // Update tampilan UI lokal langsung tanpa jeda
                lblCurrentCall.Text = JenisAntrean.Format(jenis, nextNumber);

                // Kirim perintah Pub/Sub dengan format: "ID_LOKET:CALL"
                await _redisManager.PublishCommandAsync($"{_myLoketId}:CALL");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal memanggil: {ex.Message}");
            }
            finally
            {
                await Task.Delay(1000);
                tombol.IsEnabled = true;
            }
        }

        private async void btnRecall_Click(object sender, RoutedEventArgs e)
        {
            btnRecall.IsEnabled = false;
            // Kirim perintah panggil ulang: "ID_LOKET:RECALL"
            await _redisManager.PublishCommandAsync($"{_myLoketId}:RECALL");
            await Task.Delay(1500);
            btnRecall.IsEnabled = true;
        }
    }
}