using System;
using System.IO;

namespace H1Emu_Launcher.Classes
{
    // Small diagnostics log (%APPDATA%\JSEmu Launcher\Logs\launcher.log), used by the 2016 proximity voice.
    public static class LauncherLog
    {
        public static void Write(string message)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JSEmu Launcher", "Logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "launcher.log"), $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
