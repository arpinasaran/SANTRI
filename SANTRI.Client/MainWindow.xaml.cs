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
                // Ambil sisa antrean global dan status nomor terakhir khusus loket ini
                int sisa = await _redisManager.GetSisaCountAsync();
                long myCurrentNumber = await _redisManager.GetLoketNumberAsync(_myLoketId);

                lblSisa.Text = sisa.ToString();
                lblCurrentCall.Text = myCurrentNumber == 0 ? "000" : myCurrentNumber.ToString("D3");

                btnCall.IsEnabled = sisa > 0;
                btnRecall.IsEnabled = myCurrentNumber > 0;
            }
            catch { }
        }

        private async void btnCall_Click(object sender, RoutedEventArgs e)
        {
            btnCall.IsEnabled = false;

            try
            {
                int sisaSaatIni = await _redisManager.GetSisaCountAsync();
                if (sisaSaatIni <= 0) return;

                // Ambil nomor antrean berikutnya dari Pool terpusat
                long nextNumber = await _redisManager.IncrementActiveCountAsync();

                // Update data loket spesifik dan kurangi sisa antrean
                await _redisManager.SetLoketNumberAsync(_myLoketId, nextNumber);
                int sisaBaru = sisaSaatIni - 1;
                await _redisManager.SetSisaCountAsync(sisaBaru);

                // Update tampilan UI lokal langsung tanpa jeda (format 3 digit)
                lblSisa.Text = sisaBaru.ToString();
                lblCurrentCall.Text = nextNumber.ToString("D3");

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
                btnCall.IsEnabled = true;
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