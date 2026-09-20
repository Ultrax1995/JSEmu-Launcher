using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace H1Emu_Launcher.Classes
{
    public class DressingItem
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("slot")] public string Slot { get; set; } = "";
        [JsonPropertyName("image")] public string? Image { get; set; }
        [JsonPropertyName("owned")] public bool Owned { get; set; }
    }

    public class DressingRoomData
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }
        [JsonPropertyName("items")] public List<DressingItem> Items { get; set; } = new();
        [JsonPropertyName("outfit")] public Dictionary<string, int?> Outfit { get; set; } = new();
    }

    // Talks to the JSEmu website. Only SHA-256(account key) is ever sent - the same
    // value the launcher already sends to the game server as the session id.
    internal static class DressingRoomApi
    {
        public const string Endpoint = "https://jsemu.eu/api/launcher/dressingroom";
        public const string SiteRoot = "https://jsemu.eu";

        private static async Task<string> PostAsync(object body)
        {
            string json = JsonSerializer.Serialize(body);
            using StringContent content = new(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await SplashWindow.httpClient.PostAsync(Endpoint, content);
            return await response.Content.ReadAsStringAsync();
        }

        public static async Task<DressingRoomData> GetAsync(string keyHash)
        {
            try
            {
                string raw = await PostAsync(new { authKey = keyHash, action = "get" });
                return JsonSerializer.Deserialize<DressingRoomData>(raw) ?? new DressingRoomData { Error = "Empty response." };
            }
            catch (Exception ex)
            {
                return new DressingRoomData { Success = false, Error = $"Could not reach jsemu.eu ({ex.Message})" };
            }
        }

        public static async Task<(bool Ok, string? Error)> SaveAsync(string keyHash, Dictionary<string, int> outfit)
        {
            try
            {
                string raw = await PostAsync(new { authKey = keyHash, action = "save", outfit });
                using JsonDocument doc = JsonDocument.Parse(raw);
                bool ok = doc.RootElement.TryGetProperty("success", out JsonElement s) && s.ValueKind == JsonValueKind.True;
                string? error = doc.RootElement.TryGetProperty("error", out JsonElement e) ? e.GetString() : null;
                return (ok, error);
            }
            catch (Exception ex)
            {
                return (false, $"Could not reach jsemu.eu ({ex.Message})");
            }
        }
    }
}
