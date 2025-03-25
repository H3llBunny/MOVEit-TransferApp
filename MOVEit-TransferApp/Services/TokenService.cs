using MOVEit_TransferApp.Models;
using System.Text.Json;

namespace MOVEit_TransferApp.Services
{
    public class TokenService : ITokenService
    {
        private readonly HttpClient _client;
        private readonly ILogger<TokenService> _logger;
        private const string _url = "https://testserver.moveitcloud.com/api/v1/token";

        public TokenService(HttpClient client, ILogger<TokenService> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<bool> RequestTokenAsync(string username, string password)
        {
            var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password)
            ]);

            try
            {
                HttpResponseMessage response = await _client.PostAsync(_url, content);

                if (response.IsSuccessStatusCode)
                {
                    string filePath = Path.Combine(AppContext.BaseDirectory, "user_token.json");

                    string responseBody = await response.Content.ReadAsStringAsync();

                    var token = JsonSerializer.Deserialize<Token>(responseBody);

                    if (token != null)
                    {
                        token.Obtained = DateTime.UtcNow;
                        string jsonToken = JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(filePath, jsonToken);
                        return true;
                    }

                    _logger.LogError("Token deserialization failed. Token object is null.");
                    return false;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    _logger.LogWarning("Bad request: Invalid username or password.");
                    return false;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Error while trying to request token: {ex.Message}");
                throw;
            }
        }
    }
}
