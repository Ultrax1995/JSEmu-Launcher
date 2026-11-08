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
            return !ShowBlockedWindow(owner, status);
        }

        // The ban / not-registered window. Returns false (nothing shown) for an account that may play.
        public static bool ShowBlockedWindow(Window owner, Status status)
        {
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
                    "Your AuthKey is not registered yet.\n\nEasiest way: click LOGIN WITH DISCORD below (or the profile tile in the top left) and log in with Discord in your browser. Your AuthKey is then registered automatically. You need the Member role on our Discord (discord.gg/yeDTjJHrQV) - verify there first.\n\nOr the old way: go to #support on our Discord, click the Auth Key button and paste the AuthKey from Settings > Account Key.");
            }
            else
            {
                return false;
            }

            // 8.11.2026: not registered and not logged in -> the button logs in with Discord (the key gets registered then),
            // the text is laid out as two options (formatted, not one block of text)
            bool offerLogin = !status.Banned && !DiscordLogin.IsLoggedIn;
            MessageBoxResult pressed = offerLogin
                ? CustomMessageBox.ShowWithJoinDiscord(FillUnregistered, "LOGIN WITH DISCORD", owner)
                : CustomMessageBox.ShowWithJoinDiscord(message, Text(owner, "item237", "JOIN DISCORD"), owner);
            if (pressed == MessageBoxResult.Yes)
            {
                if (offerLogin) { _ = LoginAfterBlocked(owner); return true; }
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = DiscordInvite, UseShellExecute = true });
                }
                catch { }
            }

            return true;
        }

        // "Not registered" as two clear options: log in with Discord (recommended), or register the key in #support
        private static void FillUnregistered(System.Windows.Controls.TextBlock t)
        {
            var dim = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0xA0, 0xA8));
            var blurple = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8C, 0x95, 0xFF));
            System.Windows.Documents.Run R(string s, double size = 15, bool bold = false, System.Windows.Media.Brush? brush = null)
            {
                var r = new System.Windows.Documents.Run(s) { FontSize = size };
                if (bold) r.FontWeight = FontWeights.SemiBold;
                if (brush != null) r.Foreground = brush;
                return r;
            }
            t.Inlines.Clear();
            t.Inlines.Add(R("Your AuthKey isn't registered yet", 20, true));
            t.Inlines.Add(new System.Windows.Documents.LineBreak());
            t.Inlines.Add(R("You need it once to play on our servers.", 13, false, dim));
            t.Inlines.Add(new System.Windows.Documents.LineBreak());
            t.Inlines.Add(new System.Windows.Documents.LineBreak());
            t.Inlines.Add(R("1.  Log in with Discord", 16, true, blurple));
            t.Inlines.Add(R("   recommended", 12, false, dim));
            t.Inlines.Add(new System.Windows.Documents.LineBreak());
            t.Inlines.Add(R("Click LOGIN WITH DISCORD below. Your key is registered automatically."));
            t.Inlines.Add(new System.Windows.Documents.LineBreak());
            t.Inlines.Add(R("Requires the Member role on our Discord (verify there first).", 13, false, dim));
            t.Inlines.Add(new System.Windows.Documents.LineBreak());
            t.Inlines.Add(new System.Windows.Documents.LineBreak());
            t.Inlines.Add(R("2.  Or register it yourself", 16, true));
            t.Inlines.Add(new System.Windows.Documents.LineBreak());
            t.Inlines.Add(R("Copy your key in Settings > Account Key and paste it with the"));
            t.Inlines.Add(new System.Windows.Documents.LineBreak());
            t.Inlines.Add(R("Auth Key button in #support on our Discord."));
        }

        private static async Task LoginAfterBlocked(Window owner)
        {
            if (!await DiscordLoginWindow.LoginAsync(owner)) return;
            DiscordLogin.Result r = await DiscordLogin.RefreshAsync();
            if (r.Ok && !string.IsNullOrEmpty(r.AuthKey))
                CustomMessageBox.Show("You are logged in and your AuthKey is registered. Press Play.", owner);
            else if (r.Ok && !r.HasRole)
                CustomMessageBox.Show("You are logged in. Now verify on our Discord to get the Member role, then press Play again.", owner, true, false, false, false, true);
            else if (!r.Ok && r.Error != null)
                CustomMessageBox.Show(r.Error, owner);
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
