using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace SANTRI.Core
{
    public class AntrianRedisManager
    {
        private ConnectionMultiplexer _redis;
        private IDatabase _db;
        private ISubscriber _sub;

        // Menggunakan skema prefix DARMA:TAG: yang konsisten
        private const string PREFIX = "DARMA:TAG:";

        public event Action<string> OnError;
        public event Action<string> OnCommandReceived;

        public bool IsConnected => _redis != null && _redis.IsConnected;

        public bool Connect(string connectionString)
        {
            try
            {
                _redis = ConnectionMultiplexer.Connect(connectionString);
                _db = _redis.GetDatabase();
                _sub = _redis.GetSubscriber();

                // Subscribe ke channel broadcast terpusat
                _sub.Subscribe(new RedisChannel($"{PREFIX}CHANNEL", RedisChannel.PatternMode.Literal), (channel, message) =>
                {
                    OnCommandReceived?.Invoke(message);
                });

                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Koneksi Gagal: {ex.Message}");
                return false;
            }
        }

        public async Task PublishCommandAsync(string command)
        {
            if (!IsConnected) return;
            await _sub.PublishAsync(new RedisChannel($"{PREFIX}CHANNEL", RedisChannel.PatternMode.Literal), command);
        }

        // --- MANAJEMEN STATE POOL ANTREAN ---

        public async Task<long> IncrementTotalCountAsync()
        {
            return await _db.StringIncrementAsync($"{PREFIX}TOTAL");
        }

        public async Task SetTotalCountAsync(long total)
        {
            await _db.StringSetAsync($"{PREFIX}TOTAL", total);
        }

        public async Task<long> IncrementActiveCountAsync()
        {
            return await _db.StringIncrementAsync($"{PREFIX}ACTIVE");
        }

        public async Task<long> GetActiveCountAsync()
        {
            var val = await _db.StringGetAsync($"{PREFIX}ACTIVE");
            return val.HasValue ? (long)val : 0;
        }

        public async Task SetSisaCountAsync(int sisa)
        {
            await _db.StringSetAsync($"{PREFIX}SISA", sisa);
        }

        public async Task<int> GetSisaCountAsync()
        {
            var val = await _db.StringGetAsync($"{PREFIX}SISA");
            return val.HasValue ? (int)val : 0;
        }

        // --- LOKET SPECIFIC STATE ---

        public async Task SetLoketNumberAsync(int loketId, long number)
        {
            await _db.StringSetAsync($"{PREFIX}LOKET:{loketId}", number);
        }

        public async Task<long> GetLoketNumberAsync(int loketId)
        {
            var val = await _db.StringGetAsync($"{PREFIX}LOKET:{loketId}");
            return val.HasValue ? (long)val : 0;
        }

        public async Task ResetAntrianAsync()
        {
            await _db.KeyDeleteAsync($"{PREFIX}TOTAL");
            await _db.KeyDeleteAsync($"{PREFIX}ACTIVE");
            await _db.KeyDeleteAsync($"{PREFIX}SISA");
            await _db.KeyDeleteAsync($"{PREFIX}LOKET:1");
            await _db.KeyDeleteAsync($"{PREFIX}LOKET:2");
            await _db.KeyDeleteAsync($"{PREFIX}LOKET:3");
        }
    }
}