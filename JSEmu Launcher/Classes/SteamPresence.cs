using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace H1Emu_Launcher.Classes
{
    /// <summary>
    /// Tells Steam that a Just Survive session is running, so the player gets playtime and
    /// shows up as playing it.
    /// <para>
    /// This is deliberately a separate little process (JSEmuSteam.exe) instead of letting the
    /// game announce itself. Launcher 3.5.40 did the latter, by dropping a steam_appid.txt next
    /// to the game, and it broke the game for players whose Steam status was Invisible: the
    /// client could not reach Steam, so it quit on startup - they saw it flash and disappear.
    /// A helper cannot do that. When Steam is closed, refuses, or the account does not own the
    /// game, the helper simply exits and the game never knows.
    /// </para>
    /// </summary>
    public static class SteamPresence
    {
        private const string HelperName = "JSEmuSteam.exe";

        /// <summary>
        /// Starts the helper beside a running game. Never throws: no presence is a small loss,
        /// a failed launch is not.
        /// </summary>
        public static void Start(Process game, string gameDirectory)
        {
            try
            {
                if (game == null || string.IsNullOrEmpty(gameDirectory))
                    return;

                // The game ships steam_api64.dll; without it there is nothing to talk through.
                if (!File.Exists(Path.Combine(gameDirectory, "steam_api64.dll")))
                    return;

                string helper = Unpack();
                if (helper == null)
                    return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = helper,
                    Arguments = $"{game.Id} \"{gameDirectory}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch { }
        }

        /// <summary>
        /// Writes the helper next to our own settings, not into the game folder - an unknown
        /// executable there is exactly what anti-cheat is supposed to notice. Returns its path,
        /// or null when it could not be written.
        /// </summary>
        private static string Unpack()
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "JSEmu Launcher");
                Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, HelperName);

                using Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(HelperName);
                if (source == null)
                    return null;
                using MemoryStream buffer = new();
                source.CopyTo(buffer);
                byte[] ours = buffer.ToArray();

                // Rewrite only when it differs, so a running helper from an earlier session is
                // not fought over on every launch.
                if (!File.Exists(target) || new FileInfo(target).Length != ours.Length)
                    File.WriteAllBytes(target, ours);

                return target;
            }
            catch { return null; }
        }
    }
}
