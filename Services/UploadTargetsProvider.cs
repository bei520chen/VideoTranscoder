using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VideoConverter
{
    internal sealed class UploadTargetsProvider
    {
        private readonly string _apiUrl;
        private static readonly HttpClient Http = new HttpClient();
        private List<string>? _cache;
        private DateTime _cacheTime;

        public UploadTargetsProvider(string apiUrl = "https://api.example.com/upload/targets")
        {
            _apiUrl = apiUrl;
        }

        public async Task<List<string>> GetTargetsAsync()
        {
            if (_cache != null && (DateTime.Now - _cacheTime).TotalSeconds <= 60)
                return _cache;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var txt = await Http.GetStringAsync(_apiUrl, cts.Token);
                _cache = txt.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                _cacheTime = DateTime.Now;
            }
            catch
            {
                _cache = new List<string> { "门头", "菜单特写", "节日菜品", "店内氛围" };
                _cacheTime = DateTime.Now;
            }

            return _cache;
        }
    }
}