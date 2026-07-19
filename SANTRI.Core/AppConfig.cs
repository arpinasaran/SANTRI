using System;
using System.IO;

namespace SANTRI.Core
{
    public static class AppConfig
    {
        public static string GetRedisConnectionString()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.txt");
            string defaultConnection = "127.0.0.1:6379";

            if (File.Exists(configPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(configPath);
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("REDIS_CONNECTION="))
                            return line.Substring("REDIS_CONNECTION=".Length).Trim();
                    }
                }
                catch { }
            }
            else
            {
                WriteDefaultConfig();
            }
            return defaultConnection;
        }

        public static int GetLoketId()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.txt");
            if (File.Exists(configPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(configPath);
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("LOKET_ID="))
                        {
                            return int.Parse(line.Substring("LOKET_ID=".Length).Trim());
                        }
                    }
                }
                catch { }
            }
            return 1; // Default jika tidak diset
        }

        public static void WriteDefaultConfig()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.txt");
                string defaultConfig =
                    "=== PENGATURAN SISTEM ANTREAN ===\r\n" +
                    "REDIS_CONNECTION=127.0.0.1:6379\r\n" +
                    "LOKET_ID=1\r\n";
                File.WriteAllText(configPath, defaultConfig);
            }
            catch { }
        }
    }
}