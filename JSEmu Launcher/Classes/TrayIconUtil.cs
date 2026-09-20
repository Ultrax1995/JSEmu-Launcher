using System;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace H1Emu_Launcher.Classes
{
    // Windows 11 tucks new tray icons into the overflow flyout, so a launcher that
    // hides itself when the game starts looks like it vanished. This marks the
    // launcher's own icon as "promoted" (shown next to the clock) - only if the user
    // never chose a setting for it, so a manual choice is always respected.
    internal static class TrayIconUtil
    {
        // True only when this call changed the setting (the icon then needs re-registering).
        public static async Task<bool> PromoteOwnIconAsync()
        {
            // The shell writes the icon's registry entry shortly after the icon is shown.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                await Task.Delay(500);
                if (TryPromote(out bool changed))
                    return changed;
            }

            return false;
        }

        private static bool TryPromote(out bool changed)
        {
            changed = false;
            try
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                    return false;

                using RegistryKey? root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", false);
                if (root == null)
                    return false; // Windows 10 or older: nothing to do.

                foreach (string name in root.GetSubKeyNames())
                {
                    using RegistryKey? key = root.OpenSubKey(name, true);
                    if (key?.GetValue("ExecutablePath") is not string stored)
                        continue;

                    if (!PathMatches(stored, exe))
                        continue;

                    if (key.GetValue("IsPromoted") == null)
                    {
                        key.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                        changed = true;
                    }

                    return true;
                }
            }
            catch
            {
                // Cosmetic only - never let this affect the launcher.
            }

            return false;
        }

        // Paths under known folders (e.g. Program Files) are stored as "{GUID}\Folder\app.exe".
        private static bool PathMatches(string stored, string exe)
        {
            if (string.Equals(stored, exe, StringComparison.OrdinalIgnoreCase))
                return true;

            if (stored.StartsWith('{'))
            {
                int slash = stored.IndexOf('\\');
                if (slash > 0)
                    return exe.EndsWith(stored[slash..], StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}
