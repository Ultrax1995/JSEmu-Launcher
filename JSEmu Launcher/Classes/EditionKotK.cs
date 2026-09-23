using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cranberry.Launcher.Core;

namespace H1Emu_Launcher.Classes
{
    // "KOTK" edition: the August 2017 King of the Kill client on the JSEmu KOTK server (Cranberry).
    // Unlike 2016/2018 the game does not talk to the server directly. The launcher signs in with the
    // JSEmu account key, then carries the game's UDP over one pinned TLS websocket (GameTunnel), so
    // the launcher has to stay open while the game runs.
    public static class EditionKotK
    {
        public const string Edition = "KOTK";

        // The server's certificate is self-signed; the pin is what makes the connection trusted.
        public static readonly LauncherSettings Server = new()
        {
            ServerUrl = "https://135.125.173.208:20040/",
            CertificateSha256 = "FB54485EC6287B368B945C79A6E2B70EBE52BB733EA39225B203C81C894D43C1",
            ConcurrentDownloads = 4
        };

        public static bool Selected => Properties.Settings.Default.gameEdition == Edition;

        public static string DefaultDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JSEmu Launcher", "KotK");

        public static string GameDirectory => string.IsNullOrWhiteSpace(Properties.Settings.Default.activeDirectoryKotK)
            ? DefaultDirectory : Properties.Settings.Default.activeDirectoryKotK;

        // Written by GameInstaller after every file of a manifest verified.
        public static bool IsInstalled(string dir) =>
            File.Exists(Path.Combine(dir, ".cranberry-install.json")) && File.Exists(Path.Combine(dir, "H1Z1.exe"));

        public static bool IsRunning => _game is { HasExited: false };

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private static HttpClient _http;
        private static GameTunnel _tunnel;
        private static FileStream _gameLease;
        private static Process _game;

        private static HttpClient Http => _http ??= LauncherConnection.CreateHttp(Server);

        // Install or repair (only changed files are downloaded), then start the game unless installOnly.
        public static async Task InstallAndPlay(string accountKey, string name, bool installOnly,
            IProgress<InstallProgress> progress, Action<string> status, CancellationToken ct)
        {
            if (IsRunning) throw new InvalidOperationException("KOTK is already running.");
            string dir = GameDirectory;
            string exe = Path.Combine(dir, "H1Z1.exe");
            foreach (var running in Process.GetProcessesByName("H1Z1"))
            {
                using (running)
                {
                    string path = null;
                    try { path = running.MainModule?.FileName; } catch { }
                    if (string.Equals(path, exe, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Close the running KOTK game first.");
                }
            }

            status("Signing in with your account key...");
            var session = await Post<AuthSession>("api/auth/key",
                new KeyCredentials(AccountKeyUtil.EncryptStringSHA256(accountKey), name ?? ""), ct);

            var manifest = await Get<GameManifest>("api/manifest", session.Token, ct);
            var downloads = await DownloadConfiguration.ReadTrustedAsync(Http, Server, ct);
            using (var installer = GameInstaller.Create(Http, Server with { ContentBaseUrl = downloads.ContentBaseUrl }))
                await installer.Install(manifest, dir, progress, ct);
            if (installOnly)
            {
                status("KOTK installed and verified.");
                return;
            }

            status("Connecting to the KOTK server...");
            try
            {
                _gameLease = new FileStream(GameInstaller.SafePath(dir, ".cranberry.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                _tunnel = new GameTunnel();
                await _tunnel.Connect(Server, session.Token, ct);
                var launch = await Post<GameLaunch>("api/launch",
                    new LaunchRequest(_tunnel.GatewayPort, BidirectionalDoors.ProtocolVersion), ct, session.Token);
                if (launch.BuildId != manifest.BuildId)
                    throw new InvalidOperationException("The KOTK game files changed on the server. Press Play again to update.");

                var info = GameProcess.StartInfo(dir, launch with { LoginAddress = "127.0.0.1:" + _tunnel.LoginPort });
                _game = Process.Start(info) ?? throw new InvalidOperationException("Could not start the game.");
                var game = _game;

                // Client fixes run against the started process; the door fix also unlocks match entry.
                _ = Task.Run(() => LootReloadFix.ApplyAfterStartup(game, dir, Log));
                _ = Task.Run(() => ThrowableCleanupFix.ApplyAfterStartup(game, dir, Log));
                _ = Task.Run(() => BinocularScopeFix.ApplyAfterStartup(game, dir, Log));
                _ = Task.Run(() => OwnBulletTracers.ApplyAfterStartup(game, dir, Log));
                _ = Task.Run(() => DoorsReady(game, dir, launch, session.Token, status));

                game.EnableRaisingEvents = true;
                game.Exited += async (_, _) => await Shutdown();
                status("KOTK started. Keep the launcher open while you play.");
            }
            catch
            {
                await Shutdown();
                throw;
            }
        }

        // The server holds players out of matches until the door patch is confirmed.
        private static async Task DoorsReady(Process game, string dir, GameLaunch launch, string token, Action<string> status)
        {
            try
            {
                if (!await BidirectionalDoors.ApplyAfterStartup(game, dir, Log))
                    throw new InvalidOperationException("the game update could not initialize. Close the game and press Play again.");
                for (int attempt = 0; ; attempt++)
                {
                    if (game.HasExited) return;
                    try
                    {
                        await Post<JsonElement>("api/client/doors-ready",
                            new DoorClientReadyRequest(launch.Ticket, BidirectionalDoors.ProtocolVersion), CancellationToken.None, token);
                        Log("doors ready");
                        return;
                    }
                    catch when (attempt < 2) { await Task.Delay(1000); }
                }
            }
            catch (Exception ex)
            {
                Log("doors: " + ex.Message);
                status("KOTK: " + ex.Message);
            }
        }

        public static async Task Shutdown()
        {
            var tunnel = Interlocked.Exchange(ref _tunnel, null);
            if (tunnel != null)
            {
                try { await tunnel.DisposeAsync(); } catch { }
            }
            Interlocked.Exchange(ref _gameLease, null)?.Dispose();
        }

        private static async Task<T> Get<T>(string route, string token, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Authorization = new("Bearer", token);
            using var response = await Send(request, ct);
            return await Read<T>(response);
        }

        private static async Task<T> Post<T>(string route, object body, CancellationToken ct, string token = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body, body.GetType(), options: Json) };
            if (token != null) request.Headers.Authorization = new("Bearer", token);
            using var response = await Send(request, ct);
            return await Read<T>(response);
        }

        private static async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { return await Http.SendAsync(request, timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { throw new TimeoutException("The KOTK server did not respond within 15 seconds. Try again later."); }
            catch (HttpRequestException)
            { throw new InvalidOperationException("Cannot connect to the KOTK server. It may be offline - try again later."); }
        }

        private static async Task<T> Read<T>(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 429) throw new InvalidOperationException("Too many attempts. Wait a minute and try again.");
                string text = await response.Content.ReadAsStringAsync();
                string error = null;
                try { error = JsonSerializer.Deserialize<ApiError>(text, Json)?.Error; } catch (JsonException) { }
                throw new InvalidOperationException(error ?? $"The KOTK server returned {(int)response.StatusCode}.");
            }
            return await response.Content.ReadFromJsonAsync<T>(Json) ?? throw new InvalidDataException("Empty server response.");
        }

        public static void Log(string message)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JSEmu Launcher", "Logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "kotk.log"), $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
