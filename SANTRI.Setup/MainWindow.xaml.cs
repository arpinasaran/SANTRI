using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace SANTRI.Setup
{
    public partial class MainWindow : Window
    {
        // Tentukan lokasi target instalasi di PC klien
        private readonly string TARGET_DIR = @"C:\SANTRI";

        // Lokasi file mentahan installer yang menyertai Setup.exe ini
        private readonly string BASE_APP_DIR = AppDomain.CurrentDomain.BaseDirectory;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void btnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (chkServer.IsChecked != true && chkClient.IsChecked != true && chkToken.IsChecked != true)
            {
                MessageBox.Show("Silakan pilih minimal satu komponen untuk diinstal.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnInstall.IsEnabled = false;
            chkServer.IsEnabled = false;
            chkClient.IsEnabled = false;
            chkToken.IsEnabled = false;
            chkShortcut.IsEnabled = false;
            chkAutoRun.IsEnabled = false;
            progressBar.IsIndeterminate = true;

            // List untuk menyimpan aplikasi apa saja yang harus dijalankan nanti
            List<string> appsToRun = new List<string>();

            try
            {
                if (!Directory.Exists(TARGET_DIR))
                {
                    Directory.CreateDirectory(TARGET_DIR);
                }

                // --- 1. INSTALASI SERVER ---
                if (chkServer.IsChecked == true)
                {
                    lblStatus.Text = "Menginstal Redis Server (Latar Belakang)...";
                    await InstallRedisAsync();

                    lblStatus.Text = "Menyalin file SANTRI Server...";
                    string serverDir = Path.Combine(TARGET_DIR, "Server");
                    await CopyDirectoryAsync(Path.Combine(BASE_APP_DIR, @"Payloads\Server"), serverDir);

                    string serverExe = Path.Combine(serverDir, "SANTRI.Server.exe");
                    appsToRun.Add(serverExe);

                    if (chkShortcut.IsChecked == true)
                        CreateDesktopShortcut(serverExe, "SANTRI Server", "Sistem Pusat Antrean TV");
                }

                // --- 2. INSTALASI CLIENT ---
                if (chkClient.IsChecked == true)
                {
                    lblStatus.Text = "Menyalin file SANTRI Client...";
                    string clientDir = Path.Combine(TARGET_DIR, "Client");
                    await CopyDirectoryAsync(Path.Combine(BASE_APP_DIR, @"Payloads\Client"), clientDir);

                    string clientExe = Path.Combine(clientDir, "SANTRI.Client.exe");
                    appsToRun.Add(clientExe);

                    if (chkShortcut.IsChecked == true)
                        CreateDesktopShortcut(clientExe, "SANTRI Client", "Aplikasi Pemanggil Loket");
                }

                // --- 3. INSTALASI TOKEN ---
                if (chkToken.IsChecked == true)
                {
                    lblStatus.Text = "Menyalin file SANTRI Token...";
                    string tokenDir = Path.Combine(TARGET_DIR, "Token");
                    await CopyDirectoryAsync(Path.Combine(BASE_APP_DIR, @"Payloads\Token"), tokenDir);

                    string tokenExe = Path.Combine(tokenDir, "SANTRI.Token.exe");
                    appsToRun.Add(tokenExe);

                    if (chkShortcut.IsChecked == true)
                        CreateDesktopShortcut(tokenExe, "SANTRI Token", "Mesin Pengambil Nomor Antrean");
                }

                progressBar.IsIndeterminate = false;
                progressBar.Value = 100;
                lblStatus.Text = "Instalasi Selesai!";

                MessageBox.Show("Semua komponen berhasil diinstal di " + TARGET_DIR, "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);

                // --- EKSEKUSI AUTO-RUN ---
                if (chkAutoRun.IsChecked == true)
                {
                    foreach (string exePath in appsToRun)
                    {
                        if (File.Exists(exePath))
                        {
                            // Jalankan aplikasi dengan direktori kerjanya masing-masing
                            ProcessStartInfo startInfo = new ProcessStartInfo
                            {
                                FileName = exePath,
                                WorkingDirectory = Path.GetDirectoryName(exePath)
                            };
                            Process.Start(startInfo);
                        }
                    }

                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Terjadi kesalahan saat instalasi: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                lblStatus.Text = "Instalasi Gagal.";
                progressBar.IsIndeterminate = false;
            }
            finally
            {
                btnInstall.Content = "TUTUP";
                btnInstall.Click -= btnInstall_Click;
                btnInstall.Click += (s, ev) => this.Close();
                btnInstall.IsEnabled = true;
            }
        }

        // --- METHOD BANTUAN ASINKRON ---

        private Task InstallRedisAsync()
        {
            return Task.Run(() =>
            {
                string redisInstallerPath = Path.Combine(BASE_APP_DIR, @"Prerequisites\Redis-x64.msi");

                if (!File.Exists(redisInstallerPath))
                {
                    throw new FileNotFoundException("File installer Redis tidak ditemukan di folder Prerequisites.");
                }

                // -------------------------------------------------------------
                // LANGKAH 1: Jalankan Installer MSI Redis secara Silent
                // -------------------------------------------------------------
                ProcessStartInfo processInfo = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/i \"{redisInstallerPath}\" /quiet /norestart",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process process = Process.Start(processInfo))
                {
                    process.WaitForExit(); // Tunggu sampai instalasi MSI selesai penuh
                }

                // Beri jeda 2 detik memastikan service Redis sudah didaftarkan ke Windows
                Task.Delay(2000).Wait();

                // -------------------------------------------------------------
                // LANGKAH 2: Otomatisasi Modifikasi File Konfigurasi (redis.windows-service.conf)
                // -------------------------------------------------------------
                string redisConfigPath = @"C:\Program Files\Redis\redis.windows-service.conf";

                if (File.Exists(redisConfigPath))
                {
                    try
                    {
                        string configText = File.ReadAllText(redisConfigPath);

                        // Ganti bind 127.0.0.1 menjadi bind 0.0.0.0
                        if (configText.Contains("bind 127.0.0.1"))
                        {
                            configText = configText.Replace("bind 127.0.0.1", "bind 0.0.0.0");
                        }

                        // Ganti protected-mode yes menjadi protected-mode no
                        if (configText.Contains("protected-mode yes"))
                        {
                            configText = configText.Replace("protected-mode yes", "protected-mode no");
                        }

                        File.WriteAllText(redisConfigPath, configText);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Gagal memodifikasi konfigurasi Redis: {ex.Message}");
                    }
                }

                // -------------------------------------------------------------
                // LANGKAH 3: Otomatisasi Kebijakan Windows Firewall (Port 6379)
                // -------------------------------------------------------------
                try
                {
                    ProcessStartInfo firewallInfo = new ProcessStartInfo
                    {
                        FileName = "netsh.exe",
                        // Perintah untuk membuat Inbound Rule baru di Windows Firewall
                        Arguments = "advfirewall firewall add rule name=\"SANTRI Redis Port 6379\" dir=in action=allow protocol=TCP localport=6379",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using (Process fwProcess = Process.Start(firewallInfo))
                    {
                        fwProcess.WaitForExit();
                    }
                }
                catch (Exception ex)
                {
                    // Log atau abaikan jika aturan firewall sudah ada
                }

                // -------------------------------------------------------------
                // LANGKAH 4: Restart Service Redis agar Konfigurasi Baru Aktif
                // -------------------------------------------------------------
                try
                {
                    // Hentikan Service Redis
                    ProcessStartInfo stopService = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c net stop redis",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (Process p = Process.Start(stopService)) p.WaitForExit();

                    // Jalankan Kembali Service Redis
                    ProcessStartInfo startService = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c net start redis",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (Process p = Process.Start(startService)) p.WaitForExit();
                }
                catch (Exception ex)
                {
                    throw new Exception($"Gagal me-restart service Redis: {ex.Message}");
                }
            });
        }

        private Task CopyDirectoryAsync(string sourceDir, string targetDir)
        {
            return Task.Run(() =>
            {
                if (!Directory.Exists(sourceDir))
                {
                    throw new DirectoryNotFoundException($"Folder payload tidak ditemukan: {sourceDir}");
                }

                Directory.CreateDirectory(targetDir);

                // Salin semua file
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                    File.Copy(file, destFile, true);
                }

                // Salin sub-folder (rekursif)
                foreach (var directory in Directory.GetDirectories(sourceDir))
                {
                    string destDir = Path.Combine(targetDir, Path.GetFileName(directory));
                    CopyDirectoryRecursive(directory, destDir);
                }
            });
        }

        private void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                string destDir = Path.Combine(targetDir, Path.GetFileName(directory));
                CopyDirectoryRecursive(directory, destDir);
            }
        }

        // --- METHOD PEMBUATAN SHORTCUT ---
        private void CreateDesktopShortcut(string targetExePath, string shortcutName, string description)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutLocation = Path.Combine(desktopPath, shortcutName + ".lnk");

                // Menggunakan dynamic reflection ke COM object agar tidak perlu add reference manual di Visual Studio
                Type wshShellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic wshShell = Activator.CreateInstance(wshShellType);
                dynamic shortcut = wshShell.CreateShortcut(shortcutLocation);

                shortcut.TargetPath = targetExePath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetExePath);
                shortcut.Description = description;
                shortcut.Save();
            }
            catch
            {
                // Abaikan jika pembuatan shortcut gagal (misal karena restriksi akses Windows)
            }
        }
    }
}