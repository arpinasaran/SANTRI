using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SANTRI.Core;

namespace SANTRI.Server.Mac
{
    // Port preview macOS dari SANTRI.Server/MainWindow.xaml.cs.
    // Perbedaan dgn versi Windows: audio via afplay (bukan MediaPlayer),
    // video playlist dinonaktifkan, marquee via DispatcherTimer,
    // tanpa multi-monitor. Logika antrean & urutan audio identik.
    public partial class MainWindow : Window
    {
        private AntrianRedisManager _redisManager;

        private class PanggilanRequest
        {
            public long TicketNumber { get; set; }
            public int LoketId { get; set; }
        }

        private Queue<PanggilanRequest> _panggilanQueue = new Queue<PanggilanRequest>();
        private Queue<string> _audioQueue = new Queue<string>();
        private bool _isPlayingAudio = false;
        private string _audioFolderPath;

        private DispatcherTimer _marqueeTimer;

        public MainWindow()
        {
            InitializeComponent();

            _redisManager = new AntrianRedisManager();
            _redisManager.OnCommandReceived += RedisManager_OnCommandReceived;

            _audioFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audios");

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            string redisConfig = AppConfig.GetRedisConnectionString();
            bool isConnected = _redisManager.Connect(redisConfig);

            if (isConnected)
            {
                await SyncUIFromRedisAsync();
            }

            StartMarqueeAnimation();
        }

        private async Task SyncUIFromRedisAsync()
        {
            try
            {
                int sisa = await _redisManager.GetSisaCountAsync();
                lblLoketSisa.Text = sisa.ToString();

                long num1 = await _redisManager.GetLoketNumberAsync(1);
                long num2 = await _redisManager.GetLoketNumberAsync(2);
                long num3 = await _redisManager.GetLoketNumberAsync(3);

                lblLoket1Count.Text = num1 == 0 ? "000" : num1.ToString("D3");
                lblLoket2Count.Text = num2 == 0 ? "000" : num2.ToString("D3");
                lblLoket3Count.Text = num3 == 0 ? "000" : num3.ToString("D3");
            }
            catch { }
        }

        private void RedisManager_OnCommandReceived(string message)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                string[] parts = message.Split(':');
                if (parts.Length < 2) return;

                string strSender = parts[0];
                string command = parts[1];

                if (command == "CALL" || command == "RECALL")
                {
                    int loketId = int.Parse(strSender);
                    long targetTicket = await _redisManager.GetLoketNumberAsync(loketId);
                    if (targetTicket == 0) return;

                    int sisa = await _redisManager.GetSisaCountAsync();
                    lblLoketSisa.Text = sisa.ToString();

                    string formattedTicket = targetTicket.ToString("D3");
                    if (loketId == 1) lblLoket1Count.Text = formattedTicket;
                    else if (loketId == 2) lblLoket2Count.Text = formattedTicket;
                    else if (loketId == 3) lblLoket3Count.Text = formattedTicket;

                    _panggilanQueue.Enqueue(new PanggilanRequest { TicketNumber = targetTicket, LoketId = loketId });

                    if (!_isPlayingAudio)
                    {
                        ProsesPanggilanBerikutnya();
                    }
                }
                else if (command == "NEW_TICKET")
                {
                    int sisa = await _redisManager.GetSisaCountAsync();
                    lblLoketSisa.Text = sisa.ToString();
                }
                else if (command == "RESET")
                {
                    lblLoket1Count.Text = "000";
                    lblLoket2Count.Text = "000";
                    lblLoket3Count.Text = "000";
                    lblLoketSisa.Text = "0";
                }
            });
        }

        private void ProsesPanggilanBerikutnya()
        {
            if (_panggilanQueue.Count > 0)
            {
                _isPlayingAudio = true;

                var request = _panggilanQueue.Dequeue();

                BuildAudioSequence(request.TicketNumber, request.LoketId);
            }
            else
            {
                _isPlayingAudio = false;
            }
        }

        private void BuildAudioSequence(long ticketNumber, int loketId)
        {
            _audioQueue.Clear();

            _audioQueue.Enqueue("tingtung.mp3");
            _audioQueue.Enqueue("nomor_antrian.mp3");

            List<string> numberFiles = GetNumberAudioFiles((int)ticketNumber);
            foreach (string file in numberFiles) _audioQueue.Enqueue(file);

            _audioQueue.Enqueue("silakan_menuju_ke_loket.mp3");
            _audioQueue.Enqueue($"{loketId}.mp3");

            PlayNextAudio();
        }

        private List<string> GetNumberAudioFiles(int number)
        {
            List<string> files = new List<string>();
            if (number >= 100)
            {
                int ratusan = number / 100;
                if (ratusan == 1) files.Add("100.mp3");
                else { files.Add($"{ratusan}.mp3"); files.Add("ratus.mp3"); }
                number %= 100;
            }
            if (number >= 20)
            {
                int puluhan = number / 10;
                files.Add($"{puluhan}.mp3"); files.Add("puluh.mp3");
                number %= 10;
                if (number > 0) files.Add($"{number}.mp3");
            }
            else if (number >= 12 && number <= 19)
            {
                files.Add($"{number % 10}.mp3"); files.Add("belas.mp3");
            }
            else if (number == 11) files.Add("11.mp3");
            else if (number == 10) files.Add("10.mp3");
            else if (number > 0) files.Add($"{number}.mp3");

            return files;
        }

        private async void PlayNextAudio()
        {
            if (_audioQueue.Count > 0)
            {
                string fileName = _audioQueue.Dequeue();
                string fullPath = Path.Combine(_audioFolderPath, fileName);

                if (File.Exists(fullPath))
                {
                    // Preview Mac: putar mp3 lewat afplay bawaan macOS
                    try
                    {
                        var psi = new ProcessStartInfo("/usr/bin/afplay") { UseShellExecute = false };
                        psi.ArgumentList.Add(fullPath);
                        using (Process p = Process.Start(psi))
                        {
                            await p.WaitForExitAsync();
                        }
                    }
                    catch { }
                }
                PlayNextAudio();
            }
            else
            {
                ProsesPanggilanBerikutnya();
            }
        }

        private void StartMarqueeAnimation()
        {
            _marqueeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _marqueeTimer.Tick += (s, e) =>
            {
                double left = Canvas.GetLeft(lblMarque);
                if (double.IsNaN(left)) left = Bounds.Width;

                left -= 1.5;
                if (left < -lblMarque.Bounds.Width - 100) left = Bounds.Width;

                Canvas.SetLeft(lblMarque, left);
            };
            _marqueeTimer.Start();
        }
    }
}
