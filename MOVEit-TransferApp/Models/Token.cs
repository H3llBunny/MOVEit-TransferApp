using System.Text.Json.Serialization;

namespace MOVEit_TransferApp.Models
{
    public class Token
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonPropertyName("obtained")]
        public DateTime Obtained { get; set; }

        public bool IsExpired()
        {
            return DateTime.UtcNow >= Obtained.AddSeconds(ExpiresIn);
        }
    }
}
