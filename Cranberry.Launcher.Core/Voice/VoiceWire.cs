using System.Buffers.Binary;

namespace Cranberry.Launcher.Core.Voice;

/// <summary>Cranberry companion protocol, version 1. This is not a native Vivox/SOE packet.</summary>
public static class VoiceWire
{
    public const int SampleRate = 16000, FrameSamples = 320, FrameMs = 20;
    public const int MaxOpusBytes = 256, AudioHeader = 32, MaxMessageBytes = AudioHeader + MaxOpusBytes;
    public const byte Upload = 1, Audio = 2, State = 3, Deafen = 4;
    /// <summary>JSEmu 2016 router only: who the listener hears right now (u8 count, then u64 character,
    /// u8 name length, UTF-8 name per row). The KOTK server never sends it.</summary>
    public const byte Talking = 5;
    public const int MaxTalkingRows = 10, MaxNameBytes = 48;

    public static byte[] EncodeUpload(uint sequence, ReadOnlySpan<byte> opus)
    {
        if (opus.Length is < 1 or > MaxOpusBytes) throw new ArgumentOutOfRangeException(nameof(opus));
        byte[] message = Header(Upload, 8 + opus.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), sequence);
        opus.CopyTo(message.AsSpan(8));
        return message;
    }

    public static bool TryUpload(ReadOnlySpan<byte> message, out uint sequence)
    {
        sequence = 0;
        if (!Is(message, Upload) || message.Length is < 9 or > 8 + MaxOpusBytes) return false;
        sequence = BinaryPrimitives.ReadUInt32LittleEndian(message[4..]);
        return true;
    }

    public static byte[] EncodeAudio(ulong speaker, uint sequence, long sentMs, float left, float right, ReadOnlySpan<byte> opus)
    {
        if (speaker == 0 || !ValidGain(left) || !ValidGain(right) || opus.Length is < 1 or > MaxOpusBytes)
            throw new ArgumentException("Invalid voice frame");
        byte[] message = Header(Audio, AudioHeader + opus.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(8), speaker);
        BinaryPrimitives.WriteInt64LittleEndian(message.AsSpan(16), sentMs);
        BinaryPrimitives.WriteSingleLittleEndian(message.AsSpan(24), left);
        BinaryPrimitives.WriteSingleLittleEndian(message.AsSpan(28), right);
        opus.CopyTo(message.AsSpan(AudioHeader));
        return message;
    }

    public static bool TryAudio(ReadOnlySpan<byte> message, out VoiceFrameHeader frame)
    {
        frame = default;
        if (!Is(message, Audio) || message.Length is <= AudioHeader or > MaxMessageBytes) return false;
        frame = new(BinaryPrimitives.ReadUInt64LittleEndian(message[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(message[4..]), BinaryPrimitives.ReadInt64LittleEndian(message[16..]),
            BinaryPrimitives.ReadSingleLittleEndian(message[24..]), BinaryPrimitives.ReadSingleLittleEndian(message[28..]));
        return frame.Speaker != 0 && ValidGain(frame.Left) && ValidGain(frame.Right);
    }

    public static byte[] EncodeState(ulong character, bool allowed)
    {
        byte[] message = Header(State, 13);
        BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(4), character);
        message[12] = allowed ? (byte)1 : (byte)0;
        return message;
    }
    public static byte[] EncodeDeafen(bool deafened)
    {
        byte[] message = Header(Deafen, 5); message[4] = deafened ? (byte)1 : (byte)0; return message;
    }
    public static bool TryTalking(ReadOnlySpan<byte> message, out VoiceTalker[] talkers)
    {
        talkers = [];
        if (!Is(message, Talking) || message.Length < 5 || message[4] > MaxTalkingRows) return false;
        var rows = new VoiceTalker[message[4]];
        int offset = 5;
        for (int i = 0; i < rows.Length; i++)
        {
            if (offset + 9 > message.Length) return false;
            ulong character = BinaryPrimitives.ReadUInt64LittleEndian(message[offset..]);
            int length = message[offset + 8];
            if (length > MaxNameBytes || offset + 9 + length > message.Length) return false;
            string name;
            try { name = new System.Text.UTF8Encoding(false, true).GetString(message.Slice(offset + 9, length)); }
            catch (ArgumentException) { return false; }
            rows[i] = new(character, name);
            offset += 9 + length;
        }
        if (offset != message.Length) return false;
        talkers = rows;
        return true;
    }
    public static bool Is(ReadOnlySpan<byte> message, byte type) =>
        message.Length >= 4 && message[0] == 'C' && message[1] == 'V' && message[2] == 1 && message[3] == type;
    private static bool ValidGain(float gain) => float.IsFinite(gain) && gain is >= 0 and <= 1;
    private static byte[] Header(byte type, int size)
    {
        byte[] bytes = new byte[size]; bytes[0] = (byte)'C'; bytes[1] = (byte)'V'; bytes[2] = 1; bytes[3] = type; return bytes;
    }
}

public readonly record struct VoiceTalker(ulong CharacterId, string Name);

public readonly record struct VoiceFrameHeader(ulong Speaker, uint Sequence, long SentMs, float Left, float Right);
