
using MOVEit_TransferApp.Models;
using System.Text.Json;

namespace MOVEit_TransferApp.Services
{
    public class TokenRefresher : BackgroundService
    {
        private readonly HttpClient _client;
        private readonly ILogger<TokenRefresher> _logger;
        private string _tokenPath = Path.Combine(AppContext.BaseDirectory, "user_token.json");
        private const string _url = "https://testserver.moveitcloud.com/api/v1/token";

        public TokenRefresher(HttpClient client, ILogger<TokenRefresher> logger)
        {
            _client = client;
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    string tokenJson = await File.ReadAllTextAsync(_tokenPath);

                    if (!string.IsNullOrEmpty(tokenJson))
                    {
                        var token = JsonSerializer.Deserialize<Token>(tokenJson);

                        if (token != null && !token.IsExpired())
                        {
                            await DoWorkAsync(token, stoppingToken);
                        }
                        
                        await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

                    }

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occured while executing the DownloadExchangeRates background task.");
                    await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
                }
            }
        }

        private async Task DoWorkAsync(Token token, CancellationToken stoppingToken)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                 new KeyValuePair<string, string>("grant_type", "refresh_token"),
                 new KeyValuePair<string, string>("refresh_token", token.RefreshToken)
            });

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, _url)
                {
                    Content = content
                };

                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = await _client.SendAsync(request, stoppingToken);

                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();

                    var newToken = JsonSerializer.Deserialize<Token>(responseBody);

                    if (newToken != null)
                    {
                        newToken.Obtained = DateTime.UtcNow;
                        string jsonToken = JsonSerializer.Serialize<Token>(newToken, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(_tokenPath, jsonToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occured while executing the DownloadExchangeRates background task.");
                await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
            }
        }
    }
}
