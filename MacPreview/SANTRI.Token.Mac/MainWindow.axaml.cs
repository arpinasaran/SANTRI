using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SANTRI.Core;

namespace SANTRI.Token.Mac
{
    // Port preview macOS dari SANTRI.Token/MainWindow.xaml.cs.
    // Perbedaan dgn versi Windows: cetak thermal disimulasikan (log status),
    // tanpa MessageBox & multi-monitor. Logika antrean identik.
    public partial class MainWindow : Window
    {
        private AntrianRedisManager _redisManager;
        private DispatcherTimer _resetTimer;
        private DispatcherTimer _syncTimer;

        private string _tiketYangDicetak = "";
        private const string ADMIN_PASSWORD = "admin";

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
            _redisManager.OnError += (err) => { Dispatcher.UIThread.Post(() => lblStatus.Text = err); };

            _resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _resetTimer.Tick += (s, e) =>
            {
                _resetTimer.Stop();
                PanelHasil.IsVisible = false;
                PanelUtama.IsVisible = true;
                btnAmbilAntrean.IsEnabled = true;
            };

            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _syncTimer.Tick += async (s, e) =>
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                if (_runningDate != today)
                {
                    _runningDate = today;
                    _localTotal = 0;
                    lblTotalAntrean.Text = "000";
                    SaveLocalState();
                    _triggerAutoResetOnConnect = true;
                }

                await AttemptSyncAsync();
            };
            _syncTimer.Start();

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
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

            _tiketYangDicetak = _localTotal.ToString("D3");

            lblNomorCetak.Text = _tiketYangDicetak;
            lblTotalAntrean.Text = _tiketYangDicetak;

            PanelUtama.IsVisible = false;
            PanelHasil.IsVisible = true;

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

                if (_triggerAutoResetOnConnect)
                {
                    await _redisManager.ResetAntrianAsync();
                    await _redisManager.PublishCommandAsync("1:RESET");

                    _triggerAutoResetOnConnect = false;
                    SaveLocalState();
                }

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
            // Preview Mac: cetak thermal 58mm disimulasikan.
            lblStatus.Text = $"[SIMULASI CETAK] Tiket {_tiketYangDicetak} — RSU WIJAYAKUSUMA KEBUMEN, Antrian Pendaftaran, {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
        }

        private void Header_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                txtPassword.Text = "";
                PanelPassword.IsVisible = true;
                txtPassword.Focus();
            }
        }

        private void btnCancelReset_Click(object sender, RoutedEventArgs e)
        {
            PanelPassword.IsVisible = false;
        }

        private async void btnConfirmReset_Click(object sender, RoutedEventArgs e)
        {
            if (txtPassword.Text == ADMIN_PASSWORD)
            {
                PanelPassword.IsVisible = false;

                _localTotal = 0;
                SaveLocalState();

                lblTotalAntrean.Text = "000";

                if (_redisManager.IsConnected)
                {
                    await _redisManager.ResetAntrianAsync();
                    await _redisManager.PublishCommandAsync("1:RESET");
                }

                lblStatus.Text = "Sistem Token berhasil di-reset.";
            }
            else
            {
                lblStatus.Text = "Password salah!";
                txtPassword.Text = "";
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
                            _localTotal = long.Parse(parts[1]);
                        }
                        else
                        {
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
    }
}
