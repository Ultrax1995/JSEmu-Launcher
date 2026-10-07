#nullable enable
using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace H1Emu_Launcher.Classes
{
    // Pre-launch account check for the JSEmu 2016 servers. The game itself shows nothing when the
    // login server rejects a banned or unregistered key ("Join Server" just does nothing), so the
    // launcher asks jsemu.eu first. Only SHA-256(account key) is sent - the value the game sends anyway.
    // Any failure (site down, timeout, bad JSON) returns null and the game launches as before.
    internal static class AccountStatusApi
    {
        public const string Endpoint = "https://jsemu.eu/api/launcher/account-status";
        public const string DiscordInvite = "https://discord.gg/yeDTjJHrQV";

        public sealed class Status
        {
            public bool Registered { get; init; }
            public bool Banned { get; init; }
            public bool Permanent { get; init; }
            public string BanReason { get; init; } = string.Empty;
            public DateTime? ExpiresAt { get; init; }
        }

        public static async Task<Status?> CheckAsync(string accountKey)
        {
            if (string.IsNullOrWhiteSpace(accountKey))
                return null;

            try
            {
                string body = JsonSerializer.Serialize(new { authKey = AccountKeyUtil.EncryptStringSHA256(accountKey.Trim()) });
                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(6));
                using StringContent content = new(body, Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await SplashWindow.httpClient.PostAsync(Endpoint, content, cts.Token);
                if (!response.IsSuccessStatusCode)
                    return null;

                using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
                JsonElement root = doc.RootElement;
                if (!HubApi.Ok(root) || !root.TryGetProperty("registered", out _))
                    return null;

                DateTime? expires = null;
                string expiresRaw = HubApi.Str(root, "expiresAt");
                if (DateTime.TryParse(expiresRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
                    expires = parsed.ToLocalTime();

                return new Status
                {
                    Registered = HubApi.Bool(root, "registered"),
                    Banned = HubApi.Bool(root, "banned"),
                    Permanent = HubApi.Bool(root, "permanent") || (HubApi.Bool(root, "banned") && expires == null),
                    BanReason = HubApi.Str(root, "banReason"),
                    ExpiresAt = expires
                };
            }
            catch (Exception ex)
            {
                Log($"account status check skipped: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        // Returns true when the launch should go ahead. Shows the ban / not-registered window otherwise.
        public static async Task<bool> AllowLaunchAsync(Window owner, string accountKey)
        {
            Status? status = await CheckAsync(accountKey);
            if (status == null)
                return true;

            string message;
            if (status.Banned)
            {
                string reason = string.IsNullOrWhiteSpace(status.BanReason) ? Text(owner, "item240", "not specified") : status.BanReason.Trim();
                string duration = status.Permanent || status.ExpiresAt == null
                    ? Text(owner, "item241", "permanent")
                    : string.Format(Text(owner, "item242", "until {0}"), status.ExpiresAt.Value.ToString("yyyy-MM-dd HH:mm"));
                message = string.Format(
                    Text(owner, "item239", "Your account is banned.\n\nReason: {0}\nDuration: {1}\n\nTo appeal, open a ticket on our Discord: discord.gg/yeDTjJHrQV"),
                    reason, duration);
            }
            else if (!status.Registered)
            {
                message = Text(owner, "item238",
                    "Your AuthKey is not registered yet.\n\nJoin our Discord (discord.gg/yeDTjJHrQV) and get the Member role, then register your AuthKey: press the \"Auth Key\" button on Discord or paste it at jsemu.eu/authkey. After that, press Play again.");
            }
            else
            {
                return true;
            }

            if (CustomMessageBox.ShowWithJoinDiscord(message, Text(owner, "item237", "JOIN DISCORD"), owner) == MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = DiscordInvite, UseShellExecute = true });
                }
                catch { }
            }

            return false;
        }

        // Language files other than en/pl may not have these keys yet - fall back to English.
        private static string Text(Window owner, string key, string fallback) =>
            (owner?.TryFindResource(key) ?? Application.Current?.TryFindResource(key)) is string s && s.Length > 0 ? s : fallback;

        private static void Log(string line)
        {
            try
            {
                string dir = $"{Info.APPLICATION_DATA_PATH}\\JSEmu Launcher";
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText($"{dir}\\jsemu-account-status.txt", $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
