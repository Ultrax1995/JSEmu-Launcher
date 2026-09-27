using System.Net.Http.Json;
using System.Text.Json;
using Cranberry.Launcher.Core;
using Cranberry.Launcher.Core.Voice;
using Xunit.Abstractions;

namespace Voice2016.Tests;

// End to end against a running 2016 zone server with the proximityVoice plugin in test mode:
// three launcher voice clients (the real ProximityVoiceClient, Concentus Opus, pinned TLS) stand
// at a live server entity, 25 m and 150 m away from it. Runs only when configured:
//   JSEMU_VOICE_E2E_URL    = https://host:port/   (e.g. an SSH tunnel to the TestServer, 8891)
//   JSEMU_VOICE_E2E_SECRET = testMode.secret of proximity-voice-config.yaml
//   JSEMU_VOICE_E2E_PIN    = certificate SHA-256 (optional, defaults to the launcher's pin)
public sealed class LiveRouterTests(ITestOutputHelper output)
{
    private const string LauncherPin = "6078572855BE23AD07BA35F242E4B627C715291DAC320F8187DB458548DD4DA4";

    private static short[] Tone(int frame) => Enumerable.Range(0, VoiceWire.FrameSamples)
        .Select(i => (short)(Math.Sin(2 * Math.PI * 440 * (i + frame * VoiceWire.FrameSamples) / VoiceWire.SampleRate) * 12000)).ToArray();

    private sealed record Entity(string Key, string Kind, double X, double Y, double Z);
    private sealed record EntityList(double Range, Entity[] Entities);

    private sealed class Listener
    {
        public double Left, Right;
        public bool HeardSpeakerName;
    }

    [Fact]
    public async Task FramesReachOnlyPlayersInRangeWithDirection()
    {
        string? url = Environment.GetEnvironmentVariable("JSEMU_VOICE_E2E_URL");
        string? secret = Environment.GetEnvironmentVariable("JSEMU_VOICE_E2E_SECRET");
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(secret))
        {
            output.WriteLine("JSEMU_VOICE_E2E_URL / JSEMU_VOICE_E2E_SECRET not set - live test not run.");
            return;
        }
        var settings = new LauncherSettings
        {
            ServerUrl = url,
            CertificateSha256 = Environment.GetEnvironmentVariable("JSEMU_VOICE_E2E_PIN") ?? LauncherPin
        };

        using var http = LauncherConnection.CreateHttp(settings);
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/voice/test/entities");
        request.Headers.Add("X-Voice-Test", secret);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var list = await response.Content.ReadFromJsonAsync<EntityList>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var anchor = Assert.IsType<Entity>(list?.Entities.FirstOrDefault());
        output.WriteLine($"anchor {anchor.Kind} {anchor.Key} at {anchor.X:F1} {anchor.Y:F1} {anchor.Z:F1}, range {list!.Range} m");

        // A wrong key must not get a voice connection.
        await using (var intruder = new ProximityVoiceClient())
            await Assert.ThrowsAnyAsync<Exception>(() => intruder.Connect(settings, new string('0', 64)));

        await using var speaker = new ProximityVoiceClient();
        await using var near = new ProximityVoiceClient();
        await using var far = new ProximityVoiceClient();
        await speaker.Connect(settings, $"test.{secret}.{anchor.Key}~0~0");
        await near.Connect(settings, $"test.{secret}.{anchor.Key}~20~15");
        await far.Connect(settings, $"test.{secret}.{anchor.Key}~150~0");
        for (int i = 0; i < 150 && !(speaker.CanTalk && near.CanTalk && far.CanTalk); i++) await Task.Delay(20);
        Assert.True(speaker.CanTalk && near.CanTalk && far.CanTalk, "the server allows all three test players");
        Assert.NotEqual(0UL, speaker.CharacterId);

        var nearHeard = new Listener();
        var farHeard = new Listener();
        using var stopReading = new CancellationTokenSource();
        async Task Read(ProximityVoiceClient client, Listener into)
        {
            float[] stereo = new float[VoiceWire.FrameSamples * 2];
            while (!stopReading.IsCancellationRequested)
            {
                client.Mixer.Read(stereo, Environment.TickCount64);
                for (int i = 0; i < stereo.Length; i += 2) { into.Left += stereo[i] * stereo[i]; into.Right += stereo[i + 1] * stereo[i + 1]; }
                if (client.Talkers.Any(t => t.CharacterId == speaker.CharacterId)) into.HeardSpeakerName = true;
                await Task.Delay(VoiceWire.FrameMs);
            }
        }
        var readers = new[] { Read(near, nearHeard), Read(far, farHeard) };

        speaker.SetTransmitting(true);
        int accepted = 0;
        for (int frame = 0; frame < 75; frame++)
        {
            speaker.SetTransmitting(true);
            if (speaker.Submit(Tone(frame))) accepted++;
            await Task.Delay(VoiceWire.FrameMs);
        }
        speaker.SetTransmitting(false);
        await Task.Delay(400);
        stopReading.Cancel();
        await Task.WhenAll(readers);

        output.WriteLine($"accepted {accepted}/75; near L{nearHeard.Left:F1} R{nearHeard.Right:F1}; far L{farHeard.Left:F1} R{farHeard.Right:F1}; " +
            $"near saw the speaker's name: {nearHeard.HeardSpeakerName}; speaker talkers: {string.Join(",", speaker.Talkers.Select(t => t.Name))}");
        Assert.True(accepted >= 60);
        Assert.True(nearHeard.Left > 50, "the player 25 m away hears the tone");
        // Speaker at (-20, -15) from the listener, heading 0: pan -0.8, left gain three times the right.
        Assert.InRange(nearHeard.Left / Math.Max(nearHeard.Right, 1e-9), 5, 14);
        Assert.Equal(0, farHeard.Left + farHeard.Right);
        Assert.True(nearHeard.HeardSpeakerName, "the talking list names the speaker");
        Assert.False(farHeard.HeardSpeakerName);
    }
}
