using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace H1Emu_Launcher.Classes
{
    // "Login with Discord" (8.11.2026). The website gives the launcher its own session token (90 days from the last use,
    // refreshed on every start), kept DPAPI-encrypted in user.config. With the Member role on Discord the website
    // returns the account's AuthKey and the launcher puts it in by itself; a key already typed in the launcher is sent
    // along and kept (registered to that Discord) so old players keep their characters. The role is NOT given here -
    // the player verifies on Discord as before.
    internal static class DiscordLogin
    {
        public const string Root = DressingRoomApi.SiteRoot;

        public sealed class Result
        {
            public bool Ok;
            public bool LoggedOut;
            public bool Banned;
            public string? Username;
            public string? Avatar;
            public bool InGuild;
            public bool HasRole;
            public string? AuthKey;
            public bool KeyCreated;
            public bool KeyUnavailable;
            public string? Error;
        }

        public static event Action<Result>? Refreshed;
        public static Result? Last { get; private set; }

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("JSEmu Launcher Discord session");

        private static string? Token
        {
            get
            {
                string stored = Properties.Settings.Default.discordSession ?? "";
                if (stored.Length == 0) return null;
                try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser)); }
                catch { return null; }
            }
            set
            {
                Properties.Settings.Default.discordSession = string.IsNullOrEmpty(value) ? ""
                    : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
                Properties.Settings.Default.Save();
            }
        }

        public static bool IsLoggedIn => !string.IsNullOrEmpty(Token);

        public static string NewNonce() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string StartUrl(string nonce) => $"{Root}/api/launcher/discord/start?nonce={nonce}";

        private static async Task<(HttpStatusCode, JsonElement?)> PostAsync(string path, object body, CancellationToken ct = default)
        {
            using StringContent content = new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await SplashWindow.httpClient.PostAsync(Root + path, content, ct);
            string raw = await response.Content.ReadAsStringAsync(ct);
            try { return (response.StatusCode, JsonDocument.Parse(raw).RootElement.Clone()); }
            catch { return (response.StatusCode, null); }
        }

        private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;
        private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary>Waits for the token after the Discord login in the window/browser (given out once per nonce).</summary>
        public static async Task<bool> PollAsync(string nonce, TimeSpan timeout, CancellationToken ct)
        {
            DateTime until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
            {
                try
                {
                    var (_, json) = await PostAsync("/api/launcher/discord/poll", new { nonce }, ct);
                    if (json is JsonElement j && Bool(j, "success") && Str(j, "token") is string token && token.Length > 0)
                    {
                        Token = token;
                        return true;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* network hiccup - try again */ }
                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
            }
            return false;
        }

        /// <summary>Refreshes the session; a returned AuthKey is put into the launcher.</summary>
        public static async Task<Result> RefreshAsync()
        {
            Result r = new();
            string? token = Token;
            if (string.IsNullOrEmpty(token)) { r.LoggedOut = true; return Publish(r); }
            try
            {
                string localKey = (Properties.Settings.Default.sessionIdKey ?? "").Trim();
                var (status, json) = await PostAsync("/api/launcher/session", new { token, action = "refresh", localKey });
                if (json is not JsonElement j) { r.Error = $"The website answered {(int)status}."; return Publish(r); }
                r.Ok = Bool(j, "success");
                r.LoggedOut = Bool(j, "loggedOut") || status == HttpStatusCode.Unauthorized;
                r.Banned = Bool(j, "banned");
                r.Username = Str(j, "username");
                r.Avatar = Str(j, "avatar");
                r.InGuild = Bool(j, "inGuild");
                r.HasRole = Bool(j, "hasRole");
                r.AuthKey = Str(j, "authKey");
                r.KeyCreated = Bool(j, "keyCreated");
                r.KeyUnavailable = Bool(j, "keyUnavailable");
                r.Error = r.Ok ? null : Str(j, "error");
                if (r.LoggedOut) Token = null;
                if (r.Ok && !string.IsNullOrEmpty(r.AuthKey) && r.AuthKey != localKey)
                {
                    Properties.Settings.Default.sessionIdKey = r.AuthKey;
                    Properties.Settings.Default.Save();
                }
            }
            catch (Exception e)
            {
                r.Error = "Could not reach jsemu.eu: " + e.Message;
            }
            return Publish(r);
        }

        public static async Task LogoutAsync()
        {
            string? token = Token;
            Token = null;
            if (!string.IsNullOrEmpty(token))
            {
                try { await PostAsync("/api/launcher/session", new { token, action = "logout" }); } catch { /* the token is gone locally anyway */ }
            }
            Publish(new Result { LoggedOut = true });
        }

        private static Result Publish(Result r)
        {
            Last = r;
            try { Refreshed?.Invoke(r); } catch { /* UI handlers */ }
            return r;
        }
    }
}
