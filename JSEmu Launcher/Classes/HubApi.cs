using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace H1Emu_Launcher.Classes
{
    // Battle Pass, Pimp My Car, Scrap Yard and coin balance for the launcher.
    // Like the Dressing Room it only ever sends SHA-256(account key).
    internal static class HubApi
    {
        public const string Endpoint = "https://jsemu.eu/api/launcher/hub";

        public static string? KeyHash()
        {
            string key = Properties.Settings.Default.sessionIdKey?.Trim() ?? string.Empty;
            return string.IsNullOrEmpty(key) ? null : AccountKeyUtil.EncryptStringSHA256(key);
        }

        // Returns the parsed JSON body, or a synthetic { success:false, error } on network problems.
        public static async Task<JsonElement> CallAsync(string action, Dictionary<string, object>? extra = null)
        {
            string? hash = KeyHash();
            if (hash == null)
                return Fail("Enter your Account Key in Settings first.");

            try
            {
                Dictionary<string, object> body = new() { ["authKey"] = hash, ["action"] = action };
                if (extra != null)
                    foreach (var kv in extra)
                        body[kv.Key] = kv.Value;

                using StringContent content = new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await SplashWindow.httpClient.PostAsync(Endpoint, content);
                string raw = await response.Content.ReadAsStringAsync();
                return JsonDocument.Parse(raw).RootElement.Clone();
            }
            catch (Exception ex)
            {
                return Fail($"Could not reach jsemu.eu ({ex.Message})");
            }
        }

        private static JsonElement Fail(string message)
        {
            string json = JsonSerializer.Serialize(new { success = false, error = message });
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        public static bool Ok(JsonElement e) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty("success", out JsonElement s) && s.ValueKind == JsonValueKind.True;

        public static string Str(JsonElement e, string name, string fallback = "") =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? fallback
                : fallback;

        public static long Num(JsonElement e, string name, long fallback = 0) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt64()
                : fallback;

        public static bool Bool(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

        public static string Error(JsonElement e, string fallback = "Something went wrong.") =>
            Str(e, "error", Str(e, "message", fallback));

        // Coin balance for the main window; null when there is no key or the call fails.
        public static async Task<long?> GetCoinsAsync()
        {
            JsonElement profile = await CallAsync("profile");
            return Ok(profile) ? Num(profile, "coins") : null;
        }
    }
}
