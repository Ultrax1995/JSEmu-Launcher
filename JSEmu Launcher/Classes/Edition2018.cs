using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace H1Emu_Launcher.Classes
{
    // Just Survive 2018 (final Steam build) on the JSEmu 2018 server.
    //
    // 2018 is the same Steam app/depot as 2016 (295110/295111), just the current manifest.
    // Order of preference:
    //   1. the game is already in a Steam library -> use it and launch through Steam,
    //   2. not installed -> download from the depot straight into a Steam library and write
    //      appmanifest_295110.acf so Steam treats it as installed (needs ownership on Steam),
    //   3. no Steam at all -> start LaunchPad.exe directly, same as 2016 starts H1Z1.exe.
    //
    // Our LaunchPad.exe (embedded resource, source C:\Tools\LaunchPad-zrodlo.cs) replaces the
    // Daybreak one: it reads the JSEmu account key (sessionid=auto) and patches the client in
    // memory so '/' commands reach the server.
    internal static class Edition2018
    {
        public const uint AppId = 295110;
        public const uint DepotId = 295111;
        public const ulong Manifest = 4318541451853767651;
        public const string BuildId = "3052008";
        public const long ExeSize = 71755720;          // H1Z1.exe of build 2.1.886508
        public const string InstallDirName = "Just Survive";
        public const string Server = "135.125.173.208:1215";

        public static bool Selected => Properties.Settings.Default.gameEdition == "2018";

        public static string SteamPath()
        {
            try
            {
                string p = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (string.IsNullOrEmpty(p))
                    p = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                    return Path.GetFullPath(p.Replace('/', '\\'));
            }
            catch { }
            return null;
        }

        public static List<string> SteamLibraries()
        {
            List<string> libs = new();
            string steam = SteamPath();
            if (steam == null)
                return libs;

            libs.Add(steam);
            try
            {
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    {
                        string lib = Path.GetFullPath(m.Groups[1].Value.Replace("\\\\", "\\"));
                        if (Directory.Exists(lib) && !libs.Any(x => string.Equals(x, lib, StringComparison.OrdinalIgnoreCase)))
                            libs.Add(lib);
                    }
                }
            }
            catch { }
            return libs;
        }

        // Game folder of an installed Just Survive in any Steam library, or null.
        public static string FindInSteam()
        {
            foreach (string lib in SteamLibraries())
            {
                string acf = Path.Combine(lib, "steamapps", $"appmanifest_{AppId}.acf");
                if (!File.Exists(acf))
                    continue;

                string installDir = InstallDirName;
                Match m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\"");
                if (m.Success)
                    installDir = m.Groups[1].Value;

                string dir = Path.Combine(lib, "steamapps", "common", installDir);
                if (File.Exists(Path.Combine(dir, "H1Z1.exe")))
                    return dir;
            }
            return null;
        }

        // Where a fresh 2018 download goes: the main Steam library, so Steam can see it.
        public static string DownloadTarget()
        {
            string steam = SteamPath();
            return steam == null ? null : Path.Combine(steam, "steamapps", "common", InstallDirName);
        }

        public static bool IsInSteamLibrary(string gameDir)
        {
            string common = Path.GetDirectoryName(Path.GetFullPath(gameDir).TrimEnd('\\'));
            string steamapps = common == null ? null : Path.GetDirectoryName(common);
            return steamapps != null && File.Exists(Path.Combine(steamapps, $"appmanifest_{AppId}.acf"));
        }

        public static bool IsValidInstall(string gameDir)
        {
            if (string.IsNullOrEmpty(gameDir))
                return false;
            FileInfo exe = new(Path.Combine(gameDir, "H1Z1.exe"));
            return exe.Exists && exe.Length == ExeSize;
        }

        // After a depot download into <library>\steamapps\common\Just Survive: register the game
        // with Steam. Steam only launches it for accounts that own Just Survive.
        public static void WriteAppManifest(string gameDir)
        {
            string steamapps = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(gameDir).TrimEnd('\\')));
            if (steamapps == null)
                return;
            string acf = Path.Combine(steamapps, $"appmanifest_{AppId}.acf");
            if (File.Exists(acf))
                return;

            long size = 0;
            try { size = new DirectoryInfo(gameDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { }
            string steamExe = Path.Combine(SteamPath() ?? "", "steam.exe").Replace("\\", "\\\\");

            File.WriteAllText(acf,
$@"""AppState""
{{
	""appid""		""{AppId}""
	""universe""		""1""
	""LauncherPath""		""{steamExe}""
	""name""		""Just Survive""
	""StateFlags""		""4""
	""installdir""		""{InstallDirName}""
	""SizeOnDisk""		""{size}""
	""buildid""		""{BuildId}""
	""UpdateResult""		""0""
	""TargetBuildID""		""{BuildId}""
	""AutoUpdateBehavior""		""0""
	""InstalledDepots""
	{{
		""{DepotId}""
		{{
			""manifest""		""{Manifest}""
			""size""		""{size}""
		}}
	}}
}}
");
        }

        // Puts our LaunchPad.exe and its settings into the game folder. Runs on every launch,
        // because a Steam "verify files" restores the original Daybreak LaunchPad.
        public static void PrepareGameDir(string gameDir)
        {
            string launchPad = Path.Combine(gameDir, "LaunchPad.exe");
            string original = Path.Combine(gameDir, "LaunchPad.exe.daybreak-oryginal");

            byte[] ours;
            using (Stream s = typeof(Edition2018).Assembly.GetManifestResourceStream("LaunchPad2018.exe"))
            {
                if (s == null)
                    throw new Exception("LaunchPad2018.exe is missing from the launcher resources.");
                using MemoryStream ms = new();
                s.CopyTo(ms);
                ours = ms.ToArray();
            }

            bool same = File.Exists(launchPad) && new FileInfo(launchPad).Length == ours.Length && File.ReadAllBytes(launchPad).SequenceEqual(ours);
            if (!same)
            {
                if (File.Exists(launchPad) && !File.Exists(original))
                    File.Copy(launchPad, original);
                File.WriteAllBytes(launchPad, ours);
            }

            // LaunchPad-user.ini: server and account; any other lines (e.g. komendy=0) are kept.
            string ini = Path.Combine(gameDir, "LaunchPad-user.ini");
            List<string> lines = File.Exists(ini) ? File.ReadAllLines(ini).ToList() : new List<string> { "# Written by the JSEmu Launcher (2018 mode)." };
            SetIniValue(lines, "server", Server);
            SetIniValue(lines, "sessionid", "auto");
            File.WriteAllLines(ini, lines);
        }

        private static void SetIniValue(List<string> lines, string key, string value)
        {
            int i = lines.FindIndex(l => l.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
                lines[i] = $"{key}={value}";
            else
                lines.Add($"{key}={value}");
        }

        // Through Steam when the game is registered there, otherwise LaunchPad.exe directly.
        public static void Launch(string gameDir)
        {
            if (IsInSteamLibrary(gameDir) && SteamPath() != null)
            {
                Process.Start(new ProcessStartInfo($"steam://rungameid/{AppId}") { UseShellExecute = true });
                return;
            }

            Process.Start(new ProcessStartInfo(Path.Combine(gameDir, "LaunchPad.exe"))
            {
                WorkingDirectory = gameDir,
                UseShellExecute = true
            });
        }
    }
}
