using SANTRI.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SANTRI.Server
{
    public partial class MainWindow : Window
    {
        private AntrianRedisManager _redisManager;

        DoubleAnimation textAnimation;
        private List<string> _playlist = new List<string>();
        private int _currentVideoIndex = 0;        

        private class PanggilanRequest
        {
            public long TicketNumber { get; set; }
            public int LoketId { get; set; }
            public string Jenis { get; set; }
        }

        private Queue<PanggilanRequest> _panggilanQueue = new Queue<PanggilanRequest>();
        private Queue<string> _audioQueue = new Queue<string>();
        private MediaPlayer _audioPlayer;        
        private bool _isPlayingAudio = false;
        private string _audioFolderPath;

        public MainWindow()
        {
            InitializeComponent();

            _redisManager = new AntrianRedisManager();
            _redisManager.OnCommandReceived += RedisManager_OnCommandReceived;

            // Inisialisasi Audio Player
            _audioFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audios");
            _audioPlayer = new MediaPlayer();
            _audioPlayer.MediaEnded += AudioPlayer_MediaEnded;

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PindahkanKeMonitor(0);

            string redisConfig = AppConfig.GetRedisConnectionString();
            bool isConnected = _redisManager.Connect(redisConfig);

            if (isConnected)
            {
                await SyncUIFromRedisAsync();
            }

            StartMarqueeAnimation();

            LoadVideoPlaylist();
            PlayNextVideo();
        }

        private async Task SyncUIFromRedisAsync()
        {
            try
            {
                await UpdateSisaLabelAsync();

                lblLoket1Count.Text = await FormatLoketAsync(1);
                lblLoket2Count.Text = await FormatLoketAsync(2);
                lblLoket3Count.Text = await FormatLoketAsync(3);
            }
            catch { }
        }

        private async Task<string> FormatLoketAsync(int loketId)
        {
            long num = await _redisManager.GetLoketNumberAsync(loketId);
            if (num == 0) return "---";
            string jenis = await _redisManager.GetLoketJenisAsync(loketId);
            return JenisAntrean.Format(jenis, num);
        }

        private async Task UpdateSisaLabelAsync()
        {
            int sisaUmum = await _redisManager.GetSisaCountAsync(JenisAntrean.Umum);
            int sisaJKN = await _redisManager.GetSisaCountAsync(JenisAntrean.OnlineJKN);
            int sisaJKNOnsite = await _redisManager.GetSisaCountAsync(JenisAntrean.OnsiteJKN);
            int sisaHelp = await _redisManager.GetSisaCountAsync(JenisAntrean.Helpdesk);
            lblLoketSisa.Text = $"A {sisaUmum} · B {sisaJKN} · C {sisaJKNOnsite} · D {sisaHelp}";
        }

        private void RedisManager_OnCommandReceived(string message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(async () =>
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
                    string jenis = await _redisManager.GetLoketJenisAsync(loketId);

                    await UpdateSisaLabelAsync();

                    // Perbarui TextBlock Loket spesifik dengan format huruf-nomor
                    string formattedTicket = JenisAntrean.Format(jenis, targetTicket);
                    if (loketId == 1) lblLoket1Count.Text = formattedTicket;
                    else if (loketId == 2) lblLoket2Count.Text = formattedTicket;
                    else if (loketId == 3) lblLoket3Count.Text = formattedTicket;

                    // === MASUKKAN PERMINTAAN SUARA KE ANTRIAN GLOBAL ===
                    _panggilanQueue.Enqueue(new PanggilanRequest { TicketNumber = targetTicket, LoketId = loketId, Jenis = jenis });

                    // Jika audio sedang menganggur, langsung eksekusi panggilan pertama
                    if (!_isPlayingAudio)
                    {
                        ProsesPanggilanBerikutnya();
                    }
                }
                else if (command == "NEW_TICKET")
                {
                    await UpdateSisaLabelAsync();
                }
                else if (command == "RESET")
                {
                    lblLoket1Count.Text = "---";
                    lblLoket2Count.Text = "---";
                    lblLoket3Count.Text = "---";
                    lblLoketSisa.Text = "A 0 · B 0 · C 0 · D 0";
                }
            });
        }

        private void ProsesPanggilanBerikutnya()
        {
            if (_panggilanQueue.Count > 0)
            {
                _isPlayingAudio = true;

                // Ambil antrean panggilan terdepan
                var request = _panggilanQueue.Dequeue();

                // Isi susunan file mp3 panggilannya
                BuildAudioSequence(request.TicketNumber, request.LoketId, request.Jenis);
            }
            else
            {
                // Jika antrean loket benar-benar habis, matikan status dan tutup pop-up TV
                _isPlayingAudio = false;                
            }
        }

        private void BuildAudioSequence(long ticketNumber, int loketId, string jenis)
        {
            _audioQueue.Clear();

            _audioQueue.Enqueue("tingtung.mp3");
            _audioQueue.Enqueue("nomor_antrian.mp3");

            // Sebut huruf jenis antrean: a.mp3 / b.mp3 / c.mp3 / d.mp3
            _audioQueue.Enqueue($"{JenisAntrean.Huruf(jenis).ToLower()}.mp3");

            List<string> numberFiles = GetNumberAudioFiles((int)ticketNumber);
            foreach (string file in numberFiles) _audioQueue.Enqueue(file);

            // Arahkan tujuan loket secara dinamis memanfaatkan file audio angka yang ada
            _audioQueue.Enqueue("silakan_menuju_ke_loket.mp3");
            _audioQueue.Enqueue($"{loketId}.mp3"); // Memutar "1.mp3", "2.mp3", atau "3.mp3"

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

        private void PlayNextAudio()
        {
            if (_audioQueue.Count > 0)
            {
                string fileName = _audioQueue.Dequeue();
                string fullPath = Path.Combine(_audioFolderPath, fileName);

                if (File.Exists(fullPath))
                {
                    _audioPlayer.Open(new Uri(fullPath, UriKind.Absolute));
                    _audioPlayer.Play();
                }
                else
                {
                    PlayNextAudio(); // Lompat jika file hilang
                }
            }
            else
            {
                // Sesi pembacaan nomor ini selesai, cek apakah ada antrean dari loket lain
                ProsesPanggilanBerikutnya();
            }
        }

        private void AudioPlayer_MediaEnded(object sender, EventArgs e)
        {
            PlayNextAudio();
        }

        private void StartMarqueeAnimation()
        {
            this.UpdateLayout();
            if (lblMarque.ActualWidth == 0 || this.ActualWidth == 0) return;

            textAnimation = new DoubleAnimation();
            textAnimation.From = this.Width;
            textAnimation.To = -lblMarque.ActualWidth - 500;
            textAnimation.RepeatBehavior = RepeatBehavior.Forever;
            textAnimation.Duration = new Duration(TimeSpan.FromSeconds(50));
            lblMarque.BeginAnimation(System.Windows.Controls.Canvas.LeftProperty, textAnimation);
        }

        private void LoadVideoPlaylist()
        {
            // Mengambil direktori tempat file .exe berada, lalu menambahkan folder "Videos"
            string videoFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Videos");

            // Jika folder "Videos" belum ada, buatkan otomatis agar aplikasi tidak error
            if (!Directory.Exists(videoFolder))
            {
                Directory.CreateDirectory(videoFolder);
                return;
            }

            // Ambil semua file video (di sini dicontohkan .mp4, Anda bisa sesuaikan)
            string[] files = Directory.GetFiles(videoFolder, "*.mp4");

            if (files.Length > 0)
            {
                _playlist.AddRange(files);
            }
        }

        private void PlayNextVideo()
        {
            // Jika tidak ada video di dalam folder, hentikan proses
            if (_playlist.Count == 0) return;

            // Jika index sudah mencapai batas akhir playlist, kembalikan ke 0 (Looping ke awal)
            if (_currentVideoIndex >= _playlist.Count)
            {
                _currentVideoIndex = 0;
            }

            // Ambil path video saat ini, lalu naikkan index untuk giliran selanjutnya
            string nextVideoPath = _playlist[_currentVideoIndex];
            _currentVideoIndex++;

            // Putar video
            videoPlayer.Source = new Uri(nextVideoPath, UriKind.Absolute);
            videoPlayer.Play();
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Hentikan video yang baru selesai (untuk membersihkan buffer)
            videoPlayer.Stop();

            // Putar video selanjutnya di dalam playlist
            PlayNextVideo();
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