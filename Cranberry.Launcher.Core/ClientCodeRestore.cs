using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cranberry.Launcher.Core;

/// <summary>
/// Puts patched client functions back to the bytes of H1Z1.exe on disk. Every launcher fix
/// leaves the executable untouched and only rewrites a few instructions in memory, so the
/// disk copy is the original. Used before the client's in-process re-login after a match,
/// which crashes (H1Z1.exe+0xE427E3) while any rewritten code is present: tested 23.09.2026 with
/// the doors alone, the gameplay fixes alone and each single fix, and without any.
/// </summary>
public static class ClientCodeRestore
{
    /// <summary>Every function a launcher fix rewrites: (RVA, size).</summary>
    public static readonly (string Name, long Rva, int Size)[] PatchedFunctions =
    [
        ("doors", BidirectionalDoors.FunctionRva, BidirectionalDoors.FunctionSize),
        ("throwable cleanup", ThrowableCleanupFix.FunctionRva, ThrowableCleanupFix.FunctionSize),
        ("own tracers", OwnBulletTracers.FunctionRva, OwnBulletTracers.FunctionSize),
        ("loot reload", LootReloadFix.FunctionRva, LootReloadFix.FunctionSize),
        ("loot reload throttle", LootReloadFix.ThrottleRva, LootReloadFix.ThrottleSize),
        ("binocular scope", BinocularScopeFix.FunctionRva, BinocularScopeFix.FunctionSize),
    ];

    /// <summary>A rewrite never touches more than this many bytes of one function.</summary>
    private const int MaxChangedBytes = 16;

    /// <returns>True when every listed function now matches the executable on disk.</returns>
    public static bool Restore(Process game, string exePath, IEnumerable<(string Name, long Rva, int Size)> functions, Action<string> log)
    {
        if (!OperatingSystem.IsWindows()) return false;
        bool complete = true;
        try
        {
            long image = game.MainModule!.BaseAddress.ToInt64();
            byte[] headers = new byte[4096];
            using var disk = File.OpenRead(exePath);
            disk.ReadExactly(headers);

            using var handle = OpenProcess(0xC38, false, game.Id);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());

            // Work out every difference first, without touching the game.
            var writes = new List<(string Name, long Address, byte[] Bytes)>();
            foreach (var (name, rva, size) in functions)
            {
                byte[] original = new byte[size];
                disk.Position = FileOffset(headers, rva);
                disk.ReadExactly(original);
                byte[] live = Read(handle, image + rva, size);
                int changed = 0, first = -1, last = -1;
                for (int i = 0; i < size; i++)
                    if (live[i] != original[i]) { changed++; if (first < 0) first = i; last = i; }
                if (changed == 0) continue;
                if (changed > MaxChangedBytes)
                {
                    log($"restore: {name} differs from disk in {changed} bytes - left as is.");
                    complete = false;
                    continue;
                }
                writes.Add((name, image + rva + first, original[first..(last + 1)]));
            }
            if (writes.Count == 0) return complete;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                int status = NtSuspendProcess(handle);
                if (status < 0) throw new IOException($"Could not pause the game to restore code ({status:X8}).");
                try
                {
                    // A thread standing inside bytes about to change would resume mid-instruction.
                    if (writes.Any(w => ThreadWithin(game, w.Address, w.Bytes.Length))) continue;
                    foreach (var (name, address, bytes) in writes)
                    {
                        if (!VirtualProtectEx(handle, (nint)address, (nuint)bytes.Length, 0x40, out uint protection))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        try
                        {
                            if (!WriteProcessMemory(handle, (nint)address, bytes, (nuint)bytes.Length, out nuint written) || written != (nuint)bytes.Length)
                                throw new Win32Exception(Marshal.GetLastWin32Error());
                        }
                        finally
                        {
                            VirtualProtectEx(handle, (nint)address, (nuint)bytes.Length, protection, out _);
                            FlushInstructionCache(handle, (nint)address, (nuint)bytes.Length);
                        }
                        log($"restore: {name} back to original ({bytes.Length} bytes).");
                    }
                    return complete;
                }
                finally { NtResumeProcess(handle); }
            }
            log("restore: gave up, a client thread kept executing patched code.");
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or InvalidDataException or EndOfStreamException)
        {
            log("restore failed: " + ex.Message);
        }
        return false;
    }

    private static long FileOffset(byte[] headers, long rva)
    {
        int pe = BitConverter.ToInt32(headers, 0x3C);
        int sections = BitConverter.ToUInt16(headers, pe + 6);
        int table = pe + 24 + BitConverter.ToUInt16(headers, pe + 20);
        for (int i = 0; i < sections; i++)
        {
            int s = table + i * 40;
            uint virtualSize = BitConverter.ToUInt32(headers, s + 8), virtualAddress = BitConverter.ToUInt32(headers, s + 12);
            uint rawPointer = BitConverter.ToUInt32(headers, s + 20);
            if (rva >= virtualAddress && rva < virtualAddress + virtualSize)
                return rawPointer + (rva - virtualAddress);
        }
        throw new InvalidDataException($"RVA {rva:X} is not in any section of H1Z1.exe.");
    }

    private static bool ThreadWithin(Process game, long start, int length)
    {
        game.Refresh();
        foreach (ProcessThread thread in game.Threads)
        {
            using var handle = OpenThread(0x8, false, thread.Id);
            if (handle.IsInvalid) continue;
            nint storage = Marshal.AllocHGlobal(1248);
            try
            {
                nint context = (nint)(((long)storage + 15) & ~15L);
                Marshal.Copy(new byte[1232], 0, context, 1232);
                Marshal.WriteInt32(context, 48, 0x100001); // AMD64 CONTEXT_CONTROL
                if (!GetThreadContext(handle, context)) continue;
                long rip = Marshal.ReadInt64(context, 248);
                if (rip >= start && rip < start + length) return true;
            }
            finally { Marshal.FreeHGlobal(storage); }
        }
        return false;
    }

    private static byte[] Read(SafeProcessHandle handle, long address, int size)
    {
        byte[] bytes = new byte[size];
        if (!ReadProcessMemory(handle, (nint)address, bytes, (nuint)size, out nuint count) || count != (nuint)size)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return bytes;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, [Out] byte[] bytes, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] bytes, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualProtectEx(SafeProcessHandle process, nint address, nuint size, uint protect, out uint previous);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FlushInstructionCache(SafeProcessHandle process, nint address, nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeWaitHandle OpenThread(uint access, bool inherit, int threadId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadContext(SafeWaitHandle thread, nint context);
    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(SafeProcessHandle process);
    [DllImport("ntdll.dll")] private static extern int NtResumeProcess(SafeProcessHandle process);
}
