using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cranberry.Launcher.Client;
using Cranberry.Launcher.Core;
using Cranberry.Launcher.Core.Voice;

namespace H1Emu_Launcher.Classes
{
    // Proximity voice for the 2016 game on the JSEmu servers, with the voice client of the Cranberry launcher
    // (Cranberry.Launcher.Core.Voice / Cranberry.Launcher.Client.ProximityAudio).
    //
    // The router is the proximityVoice plugin inside each 2016 zone server (JSEmu-Server repo,
    // plugins/proximityVoice). The launcher does not know which server the player picks in the
    // game's server list, so it connects to every known voice endpoint with the same key the game
    // logs in with (SHA-256 of the account key). Only the server the character is on answers with
    // a character id; that connection gets the microphone and speakers.
    //
    // Everything here runs on the UI thread except the connection loops.
    public static class Voice2016
    {
        public sealed record Endpoint(string Name, string Url);

        public static readonly Endpoint[] DefaultEndpoints =
        {
            new("Krakow", "https://135.125.173.208:8890/"),
            new("TestServer", "https://135.125.173.208:8891/"),
        };

        // Self-signed certificate of the voice routers (/home/ubuntu/voice-tls on the game host);
        // the pin is what makes the connection trusted.
        public const string CertificateSha256 = "6078572855BE23AD07BA35F242E4B627C715291DAC320F8187DB458548DD4DA4";

        // JSEMU_VOICE2016_ENDPOINTS="Name=https://host:port/;Name2=..." replaces the list (testing).
        public static Endpoint[] Endpoints
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("JSEMU_VOICE2016_ENDPOINTS");
                if (string.IsNullOrWhiteSpace(env)) return DefaultEndpoints;
                return env.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(e => e.Split('=', 2))
                    .Where(p => p.Length == 2)
                    .Select(p => new Endpoint(p[0], p[1]))
                    .ToArray();
            }
        }

        public static bool Enabled => Properties.Settings.Default.voice2016Enabled;

        private static LauncherSettings SettingsFor(Endpoint endpoint) => new()
        {
            ServerUrl = endpoint.Url,
            CertificateSha256 = CertificateSha256,
            InstallDirectory = Properties.Settings.Default.activeDirectory ?? "",
            ProximityVoiceEnabled = Properties.Settings.Default.voice2016Enabled,
            VoiceInputDevice = Properties.Settings.Default.voice2016Input,
            VoiceOutputDevice = Properties.Settings.Default.voice2016Output,
            VoiceVolume = Properties.Settings.Default.voice2016Volume,
            VoicePushToTalkKey = Properties.Settings.Default.voice2016Key ?? ""
        };

        // ---- state -------------------------------------------------------------------------------
        public static string Status { get; private set; } = "Voice connects when you play on a JSEmu server.";
        public static event Action StatusChanged;
        /// <summary>Who the player hears right now (own name included while talking). UI thread.</summary>
        public static event Action<VoiceTalker[], ulong> TalkersChanged;
        public static bool Running => _stop != null;

        private sealed class Link
        {
            public Endpoint Endpoint;
            public ProximityVoiceClient Client;
            public Task Loop;
        }

        private static readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(20) };
        private static bool _timerHooked;
        private static CancellationTokenSource _stop;
        private static readonly List<Link> links = new();
        private static Process _game;
        private static int _inputPid;
        private static long _nextPidCheck;
        private static GameInput _input;
        private static ProximityAudio _audio;
        private static Link _active;
        private static string _talkersKey = "";

        private static void SetStatus(string text)
        {
            if (Status == text) return;
            Status = text;
            StatusChanged?.Invoke();
        }

        /// <summary>Starts voice for a 2016 game the launcher just started. Call on the UI thread.</summary>
        public static void Start(Process game)
        {
            Stop();
            if (!Enabled) { SetStatus("Proximity voice is turned off (Settings > Voice)."); return; }
            string key = Properties.Settings.Default.sessionIdKey?.Trim() ?? "";
            string token = key.Length == 0 ? null : AccountKeyUtil.EncryptStringSHA256(key);
#if DEBUG
            // UI preview only: a test-mode token of the voice plugin instead of the account key.
            if (Environment.GetEnvironmentVariable("JSEMU_UI_PREVIEW") == "1"
                && Environment.GetEnvironmentVariable("JSEMU_VOICE2016_TOKEN") is { Length: > 0 } testToken)
                token = testToken;
#endif
            if (token == null) { SetStatus("Proximity voice needs your JSEmu account key."); return; }

            _game = game;
            _stop = new CancellationTokenSource();
            if (!_timerHooked) { timer.Tick += (_, _) => Tick(); _timerHooked = true; }
            foreach (var endpoint in Endpoints)
            {
                var link = new Link { Endpoint = endpoint };
                link.Loop = RunLink(link, token, _stop.Token);
                links.Add(link);
            }
            _nextPidCheck = 0;
            timer.Start();
            SetStatus("Connecting proximity voice...");
        }

        /// <summary>Stops voice (game closed, voice turned off). Call on the UI thread.</summary>
        public static void Stop()
        {
            timer.Stop();
            var stop = _stop;
            _stop = null;
            stop?.Cancel();
            DropAudio();
            _input?.Dispose(); _input = null; _inputPid = 0;
            foreach (var link in links)
            {
                var client = link.Client;
                link.Client = null;
                if (client != null) _ = client.DisposeAsync().AsTask().ContinueWith(_ => { });
            }
            links.Clear();
            _game = null;
            PublishTalkers(Array.Empty<VoiceTalker>(), 0);
            stop?.Dispose();
            SetStatus("Voice connects when you play on a JSEmu server.");
        }

        /// <summary>Voice settings changed while playing: reconnect with the new devices and keys.</summary>
        public static void Restart()
        {
            var game = _game;
            if (game == null || !Running) return;
            try { if (game.HasExited) { Stop(); return; } } catch (InvalidOperationException) { Stop(); return; }
            Start(game);
        }

        private static void DropAudio()
        {
            _audio?.Dispose(); _audio = null; _active = null; _audioClient = null;
        }

        private static async Task RunLink(Link link, string token, CancellationToken stop)
        {
            int failures = 0;
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var client = new ProximityVoiceClient();
                    try
                    {
                        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop))
                        {
                            deadline.CancelAfter(6000);
                            await client.Connect(SettingsFor(link.Endpoint), token, deadline.Token);
                        }
                        stop.ThrowIfCancellationRequested();
                        failures = 0;
                        link.Client = client;
                        LauncherLog.Write($"voice2016 {link.Endpoint.Name}: connected");
                        await client.Completion.WaitAsync(stop);
                        LauncherLog.Write($"voice2016 {link.Endpoint.Name}: disconnected");
                    }
                    catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or OperationCanceledException
                        or IOException or InvalidOperationException or InvalidDataException or System.Net.Http.HttpRequestException)
                    {
                        if (stop.IsCancellationRequested) break;
                        if (failures++ == 0) LauncherLog.Write($"voice2016 {link.Endpoint.Name}: {ex.Message}");
                    }
                    finally
                    {
                        link.Client = null;
                        await client.DisposeAsync();
                    }
                    // A server without voice (or a closed port) is retried slowly.
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Max(1, failures))), stop);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }

        // The window of the running game. H1Z1.exe normally owns it; the helper executables of some
        // client patches (H1Z1_FP/H1Z1_BE) are checked as well.
        private static int GamePid()
        {
            try
            {
                if (_game != null && !_game.HasExited)
                {
                    _game.Refresh();
                    if (_game.MainWindowHandle != IntPtr.Zero) return _game.Id;
                }
            }
            catch (InvalidOperationException) { }
            foreach (string name in new[] { "H1Z1", "H1Z1_FP", "H1Z1_BE" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        try { if (p.MainWindowHandle != IntPtr.Zero) return p.Id; } catch (InvalidOperationException) { }
                    }
                }
            }
            try { return _game != null && !_game.HasExited ? _game.Id : 0; } catch (InvalidOperationException) { return 0; }
        }

        private static void Tick()
        {
            if (!Running) return;
            try
            {
                long now = Environment.TickCount64;
                if (now >= _nextPidCheck)
                {
                    _nextPidCheck = now + 2000;
                    int pid = GamePid();
                    if (pid == 0) { Stop(); return; } // the game is gone
                    if (pid != _inputPid)
                    {
                        DropAudio();
                        _input?.Dispose();
                        _input = new GameInput(pid);
                        _inputPid = pid;
                    }
                }

                // The server the character is on is the one that names it.
                var active = links.FirstOrDefault(l => l.Client is { Connected: true, CharacterId: not 0 });
                if ((active != _active || (active != null && active.Client != _audioClient)) && now >= _audioRetryAt)
                {
                    DropAudio();
                    if (active != null && _input != null)
                    {
                        _audio = new ProximityAudio(active.Client, SettingsFor(active.Endpoint), _inputPid, _input);
                        _audioClient = active.Client;
                        _active = active;
                    }
                }

                if (_audio != null && _active?.Client is { } client)
                {
                    _audio.Tick();
                    SetStatus(_audio.Error ?? (client.Deafened ? $"Voice muted ({_active.Endpoint.Name}). Press your voice mute key (Alt+M) to enable it."
                        : _audio.Talking ? $"Transmitting proximity voice on {_active.Endpoint.Name}."
                        : client.CanTalk ? $"Proximity voice ready on {_active.Endpoint.Name}. Hold {_audio.BindingLabel} to talk."
                        : $"Voice connected to {_active.Endpoint.Name}. Available while your character is alive."));
                    PublishTalkers(client.Talkers, client.CharacterId);
                }
                else
                {
                    bool any = links.Any(l => l.Client is { Connected: true });
                    SetStatus(any ? "Voice connected. Waiting for your character to enter a JSEmu server with voice."
                        : "Connecting proximity voice...");
                    PublishTalkers(Array.Empty<VoiceTalker>(), 0);
                }
            }
            catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                LauncherLog.Write("voice2016 audio: " + ex.Message);
                SetStatus("Audio device unavailable. Choose your microphone and headphones in Settings > Voice.");
                DropAudio();
                _audioRetryAt = Environment.TickCount64 + 5000;
            }
        }

#if DEBUG
        internal static void ShowPreviewSample(System.Windows.Window owner)
        {
            var settings = new SettingsWindow { Owner = owner };
            settings.settingsTabControl.SelectedIndex = 3;
            settings.Show();
            // With JSEMU_UI_VOICE_CONNECT=1 the launcher itself stands in for the game window and
            // voice really connects (JSEMU_VOICE2016_ENDPOINTS / JSEMU_VOICE2016_TOKEN).
            if (Environment.GetEnvironmentVariable("JSEMU_UI_VOICE_CONNECT") == "1")
            {
                Start(Process.GetCurrentProcess());
                return;
            }
        }

#endif
        private static ProximityVoiceClient _audioClient;
        private static long _audioRetryAt;

        private static void PublishTalkers(VoiceTalker[] talkers, ulong self)
        {
            string key = string.Join(",", talkers.Select(t => t.CharacterId));
            if (key == _talkersKey) return;
            _talkersKey = key;
            // Settings > Voice only; the in-game "who is talking" widget comes from the patched HUD (Assets_263).
            TalkersChanged?.Invoke(talkers, self);
        }
    }
}
