using System;
using System.Collections.Generic;
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
        private string _jenisYangDicetak = JenisAntrean.Umum;
        private const string ADMIN_PASSWORD = "admin";

        // Counter offline per jenis antrean
        private Dictionary<string, long> _localTotals = new Dictionary<string, long>
        {
            { JenisAntrean.Umum, 0 },
            { JenisAntrean.OnlineJKN, 0 },
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
            _redisManager.OnError += (err) => { Dispatcher.UIThread.Post(() => lblStatus.Text = err); };

            _resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _resetTimer.Tick += (s, e) =>
            {
                _resetTimer.Stop();
                PanelHasil.IsVisible = false;
                PanelUtama.IsVisible = true;
                SetTombolEnabled(true);
            };

            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _syncTimer.Tick += async (s, e) =>
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                if (_runningDate != today)
                {
                    _runningDate = today;
                    ResetLocalTotals();
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

        private void btnAmbilUmum_Click(object sender, RoutedEventArgs e) => _ = AmbilAntreanAsync(JenisAntrean.Umum);
        private void btnAmbilJKN_Click(object sender, RoutedEventArgs e) => _ = AmbilAntreanAsync(JenisAntrean.OnlineJKN);
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

            PanelUtama.IsVisible = false;
            PanelHasil.IsVisible = true;

            CetakTiket();

            _needsSync = true;
            await AttemptSyncAsync();

            _resetTimer.Start();
        }

        private void SetTombolEnabled(bool enabled)
        {
            btnAmbilUmum.IsEnabled = enabled;
            btnAmbilJKN.IsEnabled = enabled;
            btnAmbilHelp.IsEnabled = enabled;
        }

        private void UpdateTotalLabel()
        {
            lblTotalAntrean.Text =
                $"A {_localTotals[JenisAntrean.Umum]:D3} · " +
                $"B {_localTotals[JenisAntrean.OnlineJKN]:D3} · " +
                $"C {_localTotals[JenisAntrean.Helpdesk]:D3}";
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
            // Preview Mac: cetak thermal 58mm disimulasikan.
            lblStatus.Text = $"[SIMULASI CETAK] Tiket {_tiketYangDicetak} (Antrian {JenisAntrean.Label(_jenisYangDicetak)}) — RSU WIJAYAKUSUMA KEBUMEN, {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
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

                ResetLocalTotals();
                SaveLocalState();

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

        // Format file: yyyy-MM-dd|totalUmum|totalJKN|totalHelp
        private void LoadLocalState()
        {
            try
            {
                if (File.Exists(_localFilePath))
                {
                    string[] parts = File.ReadAllText(_localFilePath).Split('|');
                    if (parts.Length == 4 && parts[0] == DateTime.Now.ToString("yyyy-MM-dd"))
                    {
                        _localTotals[JenisAntrean.Umum] = long.Parse(parts[1]);
                        _localTotals[JenisAntrean.OnlineJKN] = long.Parse(parts[2]);
                        _localTotals[JenisAntrean.Helpdesk] = long.Parse(parts[3]);
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
                    $"{DateTime.Now:yyyy-MM-dd}|{_localTotals[JenisAntrean.Umum]}|{_localTotals[JenisAntrean.OnlineJKN]}|{_localTotals[JenisAntrean.Helpdesk]}");
            }
            catch { }
        }
    }
}
