using Cranberry.Launcher.Core.Voice;

namespace Voice2016.Tests;

// The 2016 router (JSEmu-Server plugins/proximityVoice, TypeScript) and this launcher share the
// KOTK voice wire format. The byte strings below were produced by the plugin's wire.ts.
public sealed class VoiceWire2016Tests
{
    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    [Fact]
    public void TalkingListFromThePluginParses()
    {
        byte[] message = Hex("4356010502010000000000000004416e6e6188776655443322110a5a61c5bcc3b3c582c487");
        Assert.True(VoiceWire.TryTalking(message, out var talkers));
        Assert.Equal([new VoiceTalker(1, "Anna"), new VoiceTalker(0x1122334455667788, "Zażółć")], talkers);
    }

    [Fact]
    public void EmptyTalkingListParses()
    {
        Assert.True(VoiceWire.TryTalking(Hex("4356010500"), out var talkers));
        Assert.Empty(talkers);
    }

    [Theory]
    [InlineData("4356010501010000000000000004416e6e")]           // name cut short
    [InlineData("4356010501010000000000000004416e6e6100")]       // trailing byte
    [InlineData("435601050b")]                                   // more rows than the HUD has
    [InlineData("4356010501010000000000000002c328")]             // invalid UTF-8
    [InlineData("4356010301")]                                   // another message type
    public void MalformedTalkingListsAreRejected(string hex)
    {
        Assert.False(VoiceWire.TryTalking(Hex(hex), out var talkers));
        Assert.Empty(talkers);
    }

    [Fact]
    public void AudioAndStateFromThePluginMatchTheKotkLayout()
    {
        Assert.True(VoiceWire.TryAudio(Hex("4356010207000000080706050403020140e20100000000000000003f0000803e08010203"), out var frame));
        Assert.Equal(new VoiceFrameHeader(0x0102030405060708, 7, 123456, 0.5f, 0.25f), frame);
        byte[] state = Hex("43560103080706050403020101");
        Assert.Equal(VoiceWire.EncodeState(0x0102030405060708, true), state);
    }

    [Fact]
    public void KotkMixerIgnoresTalkingLists()
    {
        using var mixer = new VoiceMixer();
        Assert.False(mixer.Receive(Hex("4356010502010000000000000004416e6e6188776655443322110a5a61c5bcc3b3c582c487"), 1000));
        Assert.Equal(0, mixer.BufferedSpeakers);
    }
}
