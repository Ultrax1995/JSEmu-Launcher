using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace H1Emu_Launcher.Classes
{
    // Battle Pass, Crate Store, Scrap Yard and coin balance for the launcher.
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
#if DEBUG
            // UI preview only (JSEMU_UI_PREVIEW=1, JSEMU_UI_DIALOG=hub): a made-up battle pass on day 5, nothing is sent
            if (action == "battlepass" && Environment.GetEnvironmentVariable("JSEMU_UI_PREVIEW") == "1" && Environment.GetEnvironmentVariable("JSEMU_UI_DIALOG") == "hub")
                return SampleBattlepass();
#endif
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

#if DEBUG
        private static JsonElement SampleBattlepass()
        {
            (long id, string name, string icon)[] crates =
            {
                (8297, "Just Survive Crate", "Just_Survive_Crate_8297"), (8298, "Outbreak Crate", "Outbreak_Crate_8298"), (8299, "Virus Crate", "Virus_Crate_8299"),
                (0, "", ""), (8195, "Racer Crate", "Racer_Crate_8195"), (8198, "Road Warrior Crate", "Road_Warrior_Crate_8198"), (8196, "Outlaw Crate", "Outlaw_Crate_8196"),
                (0, "", ""), (8197, "High Seas Crate", "High_Seas_Crate_8197"), (8194, "Vixen Crate", "Vixen_Crate_8194"), (9202, "Girl Crate", "Girl_Crate_9202"),
                (0, "", ""), (9203, "Military Camo Crate", "Military_Camo_Crate_9203"), (8199, "Assassin Crate", "Assassin_Crate_8199"),
                (8141, "Stormcaller Riot Shotgun", "Stormcaller_Riot_Shotgun_8141")
            };
            List<object> rewards = new();
            for (int i = 0; i < crates.Length; i++)
            {
                int day = i + 1;
                string bonus = day % 4 == 0 ? "+1 Event Ticket" : "+200 Coins";
                rewards.Add(crates[i].id == 0
                    ? new { day, type = "coins", amount = 300, name = "300 Coins", icon = (string?)null, donatorBonus = bonus }
                    : new { day, type = "item", amount = 1, name = crates[i].name, icon = (string?)$"/images/items/{crates[i].icon}.png", donatorBonus = bonus });
            }
            string start = DateTime.Now.AddDays(-4).ToString("yyyy-MM-dd");
            string json = JsonSerializer.Serialize(new
            {
                success = true, coins = 12345,
                season = new { name = "15-Day Battle Pass - Season 6", startDateKey = start, totalDays = 15, finalMissedDaysAllowed = 1 },
                todayDay = 5, seasonActive = true, canClaim = true, claimedToday = false, isDonator = true,
                onlineSecondsToday = 7300, minimumOnlineSeconds = 7200, onlineTimeFormatted = "2h 1m", requiredTimeFormatted = "2h 0m",
                claimedDays = new[] { 1, 2, 4 }, todaysReward = rewards[4], rewards
            });
            return JsonDocument.Parse(json).RootElement.Clone();
        }
#endif

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
