using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cranberry.Launcher.Client;
using Cranberry.Launcher.Core;
using Cranberry.Launcher.Core.Voice;

namespace H1Emu_Launcher.Classes
{
    // "KOTK" edition: the August 2017 King of the Kill client on the JSEmu KOTK server (Cranberry).
    // Unlike 2016/2018 the game does not talk to the server directly. The launcher signs in with the
    // JSEmu account key, then carries the game's UDP over one pinned TLS websocket (GameTunnel), so
    // the launcher has to stay open while the game runs. Friends, party, messages, leaderboard,
    // profile picture, proximity voice and the Shift+Tab overlay use the same server API.
    // Everything here runs on the UI thread except where Task.Run is explicit.
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

        // Voice, install folder and keys as the Cranberry core expects them.
        public static LauncherSettings Settings => Server with
        {
            InstallDirectory = GameDirectory,
            ProximityVoiceEnabled = Properties.Settings.Default.kotkVoiceEnabled,
            VoiceInputDevice = Properties.Settings.Default.kotkVoiceInput,
            VoiceOutputDevice = Properties.Settings.Default.kotkVoiceOutput,
            VoiceVolume = Properties.Settings.Default.kotkVoiceVolume,
            VoicePushToTalkKey = Properties.Settings.Default.kotkVoiceKey ?? ""
        };

        // ---- session ---------------------------------------------------------------------------
        public static AuthSession Session { get; private set; }
        public static LauncherState State { get; private set; }
        public static Dictionary<string, int> Unread { get; } = new();
        public static event Action StateChanged;
        public static event Action<SocialInvite> InviteReceived;
        public static event Action<string> Status;

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private static readonly HashSet<string> notifiedInvites = new();
        private static HttpClient _http;
        private static string _sessionKey;
        private static Task<AuthSession> _signingIn;
        private static bool _polling;

        private static HttpClient Http => _http ??= LauncherConnection.CreateHttp(Server);

        private static string AccountKey => Properties.Settings.Default.sessionIdKey?.Trim() ?? "";

        public static bool HasAccountKey => AccountKey.Length > 0;

        // Signs in with the current account key, once; a changed key signs in again.
        public static async Task<AuthSession> SignIn()
        {
            string key = AccountKey;
            if (key.Length == 0)
                throw new InvalidOperationException("Set your JSEmu account key in Settings first.");
            if (Session != null && _sessionKey == key)
                return Session;
            if (_signingIn != null)
                return await _signingIn;
            try
            {
                _signingIn = Send<AuthSession>(HttpMethod.Post, "api/auth/key",
                    new KeyCredentials(AccountKeyUtil.EncryptStringSHA256(key), Properties.Settings.Default.kotkName ?? ""), null, CancellationToken.None);
                var session = await _signingIn;
                if (session.AccountId != Session?.AccountId)
                {
                    State = null;
                    Unread.Clear();
                    notifiedInvites.Clear();
                }
                Session = session;
                _sessionKey = key;
                return session;
            }
            finally { _signingIn = null; }
        }

        public static void SignOut()
        {
            Session = null;
            _sessionKey = null;
            State = null;
            Unread.Clear();
            notifiedInvites.Clear();
            StateChanged?.Invoke();
        }

        // Authenticated API call. Sessions live 12 h in server memory and end on a server restart:
        // a 401 signs in again once.
        public static async Task<T> Api<T>(HttpMethod method, string route, object body = null, CancellationToken ct = default)
        {
            var session = await SignIn();
            try { return await Send<T>(method, route, body, session.Token, ct); }
            catch (UnauthorizedAccessException)
            {
                Session = null;
                session = await SignIn();
                return await Send<T>(method, route, body, session.Token, ct);
            }
        }

        public static Task<T> Get<T>(string route) => Api<T>(HttpMethod.Get, route);
        public static Task<T> Post<T>(string route, object body) => Api<T>(HttpMethod.Post, route, body);
        public static Task Post(string route, object body) => Api<JsonElement>(HttpMethod.Post, route, body);

        // Called every 3 s by the launcher window while the KOTK edition is selected.
        public static async Task Poll()
        {
            if (_polling || !HasAccountKey)
                return;
            _polling = true;
            try
            {
                var state = await Get<LauncherState>("api/state");
                var unread = await Get<MessageUnread[]>("api/messages/unread");
                State = state;
                Unread.Clear();
                foreach (var item in unread)
                    Unread[item.AccountId] = item.Count;

                notifiedInvites.IntersectWith(state.Invites.Select(i => i.Id));
                foreach (var invite in state.Invites.Where(i => notifiedInvites.Add(i.Id)).ToArray())
                    InviteReceived?.Invoke(invite);
                StateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                // Offline server or a network hiccup: keep the last state, retry on the next tick.
                Log("poll: " + ex.Message);
            }
            finally { _polling = false; }
        }

        // ---- install and play -------------------------------------------------------------------
        private static GameTunnel _tunnel;
        private static FileStream _gameLease;
        private static Process _game;
        private static GameInput _gameInput;
        private static GameOverlayHotkey _overlayHotkey;
        private static bool _togglingOverlay;

        // Install or repair (only changed files are downloaded), then start the game unless installOnly.
        public static async Task InstallAndPlay(bool installOnly, IProgress<InstallProgress> progress, CancellationToken ct)
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

            Status?.Invoke("Signing in with your account key...");
            var manifest = await Api<GameManifest>(HttpMethod.Get, "api/manifest", null, ct);
            var downloads = await DownloadConfiguration.ReadTrustedAsync(Http, Server, ct);
            using (var installer = GameInstaller.Create(Http, Server with { ContentBaseUrl = downloads.ContentBaseUrl }))
                await installer.Install(manifest, dir, progress, ct);
            if (installOnly)
            {
                Status?.Invoke("KOTK installed and verified.");
                return;
            }

            Status?.Invoke("Connecting to the KOTK server...");
            try
            {
                _gameLease = new FileStream(GameInstaller.SafePath(dir, ".cranberry.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var session = await SignIn();
                _tunnel = new GameTunnel();
                await _tunnel.Connect(Server, session.Token, ct);
                var launch = await Api<GameLaunch>(HttpMethod.Post, "api/launch",
                    new LaunchRequest(_tunnel.GatewayPort, BidirectionalDoors.ProtocolVersion), ct);
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
                _ = Task.Run(() => DoorsReady(game, dir, launch));

                // Raw input, Shift+Tab overlay and proximity voice need this (UI) thread's message loop.
                _gameInput = new GameInput(game.Id);
                var dispatcher = System.Windows.Application.Current.Dispatcher;
                try { _overlayHotkey = new GameOverlayHotkey(game.Id, _gameInput, () => dispatcher.BeginInvoke(async () => await ToggleOverlay())); }
                catch (System.ComponentModel.Win32Exception ex) { Log("overlay hotkey: " + ex.Message); }
                StartVoice(game.Id);

                game.EnableRaisingEvents = true;
                game.Exited += (_, _) => dispatcher.BeginInvoke(async () => await Shutdown());
                Status?.Invoke("KOTK started. Keep the launcher open while you play - Shift+Tab in game opens friends.");
            }
            catch
            {
                await Shutdown();
                throw;
            }
        }

        // The server holds players out of matches until the door patch is confirmed.
        private static async Task DoorsReady(Process game, string dir, GameLaunch launch)
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
                        await Post("api/client/doors-ready", new DoorClientReadyRequest(launch.Ticket, BidirectionalDoors.ProtocolVersion));
                        Log("doors ready");
                        return;
                    }
                    catch when (attempt < 2) { await Task.Delay(1000); }
                }
            }
            catch (Exception ex)
            {
                Log("doors: " + ex.Message);
                Status?.Invoke("KOTK: " + ex.Message);
            }
        }

        public static async Task ToggleOverlay()
        {
            if (_togglingOverlay) return;
            if (!IsRunning) { Status?.Invoke("Launch KOTK and reach the menu first."); return; }
            _togglingOverlay = true;
            try
            {
                await Post("api/overlay/toggle", new TargetRequest(Guid.NewGuid().ToString("N")));
                Status?.Invoke("Friends overlay toggled. Return to the game to see it.");
            }
            catch (Exception ex) { Status?.Invoke("Friends overlay: " + ex.Message); }
            finally { _togglingOverlay = false; }
        }

        public static async Task Shutdown()
        {
            await StopVoice();
            _overlayHotkey?.Dispose(); _overlayHotkey = null;
            _gameInput?.Dispose(); _gameInput = null;
            var tunnel = Interlocked.Exchange(ref _tunnel, null);
            if (tunnel != null)
            {
                try { await tunnel.DisposeAsync(); } catch { }
            }
            Interlocked.Exchange(ref _gameLease, null)?.Dispose();
        }

        // ---- proximity voice --------------------------------------------------------------------
        public static string VoiceStatus { get; private set; } = "Voice connects when you launch the game.";
        public static event Action VoiceStatusChanged;
        private static readonly System.Windows.Threading.DispatcherTimer voiceTimer = new() { Interval = TimeSpan.FromMilliseconds(20) };
        private static CancellationTokenSource _voiceStop;
        private static Task _voiceLoop;
        private static ProximityAudio _audio;
        private static ProximityVoiceClient _voiceClient;
        private static bool _voiceTimerHooked;

        private static void SetVoiceStatus(string text)
        {
            if (VoiceStatus == text) return;
            VoiceStatus = text;
            VoiceStatusChanged?.Invoke();
        }

        private static void StartVoice(int gamePid)
        {
            if (!Properties.Settings.Default.kotkVoiceEnabled) { SetVoiceStatus("Proximity voice is turned off."); return; }
            if (!_voiceTimerHooked)
            {
                voiceTimer.Tick += (_, _) => VoiceTick();
                _voiceTimerHooked = true;
            }
            _voiceStop = new();
            voiceTimer.Start();
            _voiceLoop = RunVoice(gamePid, _voiceStop.Token);
        }

        private static void VoiceTick()
        {
            if (_audio is null || _voiceClient is null) return;
            try
            {
                _audio.Tick();
                SetVoiceStatus(_audio.Error ?? (_voiceClient.Deafened ? "Voice muted. Press your voice mute key to enable it."
                    : _audio.Talking ? "Transmitting proximity voice."
                    : _voiceClient.CanTalk ? "Proximity voice ready. Hold " + _audio.BindingLabel + " to talk."
                    : "Voice connected. Available when your character is alive in a match."));
            }
            catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException)
            {
                SetVoiceStatus("Audio device unavailable. Choose your microphone and headphones in the KOTK hub.");
                _audio.Dispose(); _audio = null;
            }
        }

        private static async Task RunVoice(int gamePid, CancellationToken stop)
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await using var client = new ProximityVoiceClient();
                    try
                    {
                        SetVoiceStatus("Connecting proximity voice...");
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
                        deadline.CancelAfter(6000);
                        var session = await SignIn();
                        await client.Connect(Settings, session.Token, deadline.Token);
                        stop.ThrowIfCancellationRequested();
                        _voiceClient = client;
                        _audio = new ProximityAudio(client, Settings, gamePid, _gameInput);
                        await client.Completion.WaitAsync(stop);
                        SetVoiceStatus("Voice disconnected. Reconnecting...");
                    }
                    catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or OperationCanceledException
                        or IOException or NAudio.MmException or InvalidOperationException)
                    {
                        if (stop.IsCancellationRequested) break;
                        Log("voice: " + ex.Message);
                        SetVoiceStatus(ex is NAudio.MmException
                            ? "Cannot open the selected audio device. Check the voice devices in the KOTK hub. Retrying..."
                            : "Cannot connect to voice. Retrying...");
                    }
                    finally { _audio?.Dispose(); _audio = null; _voiceClient = null; }
                    await Task.Delay(5000, stop);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }

        private static async Task StopVoice()
        {
            voiceTimer.Stop();
            _voiceStop?.Cancel();
            _audio?.Dispose(); _audio = null;
            if (_voiceLoop != null)
            {
                try { await _voiceLoop; } catch { }
            }
            _voiceLoop = null; _voiceStop?.Dispose(); _voiceStop = null;
            SetVoiceStatus("Voice connects when you launch the game.");
        }

        // Voice settings changed while playing: reconnect with the new devices.
        public static async Task RestartVoice()
        {
            if (!IsRunning) return;
            int pid = _game.Id;
            await StopVoice();
            StartVoice(pid);
        }

        // ---- HTTP -------------------------------------------------------------------------------
        private static async Task<T> Send<T>(HttpMethod method, string route, object body, string token, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(method, route);
            if (body != null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            if (token != null) request.Headers.Authorization = new("Bearer", token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            HttpResponseMessage response;
            try { response = await Http.SendAsync(request, timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { throw new TimeoutException("The KOTK server did not respond within 15 seconds. Try again later."); }
            catch (HttpRequestException)
            { throw new InvalidOperationException("Cannot connect to the KOTK server. It may be offline - try again later."); }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && token != null)
                    throw new UnauthorizedAccessException("Sign in again.");
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
