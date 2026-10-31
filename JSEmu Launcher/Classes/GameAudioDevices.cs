using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NAudio.Wave;

namespace H1Emu_Launcher.Classes
{
    // The microphone and speakers picked in the game's own options ([VoiceChat] in UserOptions.ini).
    // The game writes the Windows device name in the system ANSI code page. Windows cuts wave device
    // names at 31 characters and the game keeps a longer one for outputs, so names match by prefix.
    internal static class GameAudioDevices
    {
        // voice2016Input / voice2016Output: 0+ is a device picked in the launcher
        public const int FollowGame = -1;
        public const int WindowsDefault = -2;

        private static string ReadOption(string key)
        {
            try
            {
                string dir = Properties.Settings.Default.activeDirectory;
#if DEBUG
                // UI preview only: the preview has its own settings, without a game folder.
                dir = Environment.GetEnvironmentVariable("JSEMU_UI_GAME_DIR") ?? dir;
#endif
                string file = Path.Combine(dir ?? "", "UserOptions.ini");
                if (string.IsNullOrEmpty(dir) || !File.Exists(file))
                    return "";

                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                Encoding ansi = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
                bool inSection = false;
                foreach (string raw in File.ReadLines(file, ansi))
                {
                    string line = raw.Trim();
                    if (line.StartsWith('['))
                        inSection = line.Equals("[VoiceChat]", StringComparison.OrdinalIgnoreCase);
                    else if (inSection && line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(key.Length + 1).Trim();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            return "";
        }

        private static int Find(string wanted, IEnumerable<(int Id, string Name)> devices)
        {
            if (wanted.Length == 0)
                return -1;
            List<(int Id, string Name)> list = new(devices);
            foreach ((int id, string name) in list)
            {
                if (name.Length > 0 && (wanted.StartsWith(name, StringComparison.OrdinalIgnoreCase) || name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)))
                    return id;
            }
            // The same headset in another mode: "Mikrofon (G522 LIGHTSPEED - USB" / "... - Wireless"
            string model = Model(wanted);
            foreach ((int id, string name) in list)
            {
                if (model.Length > 0 && Model(name).Equals(model, StringComparison.OrdinalIgnoreCase))
                    return id;
            }
            return -1;
        }

        private static string Model(string name)
        {
            int cut = name.LastIndexOf(" - ", StringComparison.Ordinal);
            return cut > 0 ? name.Substring(0, cut) : "";
        }

        private static IEnumerable<(int, string)> Inputs()
        {
            for (int i = 0; i < WaveIn.DeviceCount; i++)
                yield return (i, WaveIn.GetCapabilities(i).ProductName);
        }

        private static IEnumerable<(int, string)> Outputs()
        {
            for (int i = 0; i < WaveOut.DeviceCount; i++)
                yield return (i, WaveOut.GetCapabilities(i).ProductName);
        }

        /// <summary>Index of the game's microphone on this PC, or -1 when it has none or the device is gone.</summary>
        public static int GameInput() => Find(ReadOption("InputDevice"), Inputs());

        /// <summary>Index of the game's speakers on this PC, or -1 when it has none or the device is gone.</summary>
        public static int GameOutput() => Find(ReadOption("OutputDevice"), Outputs());

        // The device voice should open: -1 is the Windows default for the voice client.
        public static int ResolveInput(int setting) => setting == FollowGame ? GameInput() : setting == WindowsDefault ? -1 : setting;
        public static int ResolveOutput(int setting) => setting == FollowGame ? GameOutput() : setting == WindowsDefault ? -1 : setting;
    }
}
