using System;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Cranberry.Launcher.Core;
using H1Emu_Launcher.Classes;

namespace H1Emu_Launcher
{
    // KOTK edition: install / play through EditionKotK. See Classes\EditionKotK.cs.
    public partial class LauncherWindow
    {
        private CancellationTokenSource kotkCancel;

        private void ShowKotKPanel(bool visible)
        {
            if (!visible)
                return;
            kotkNameBox.Text = Properties.Settings.Default.kotkName ?? "";
            if (kotkCancel == null && !EditionKotK.IsRunning)
                kotkProgress.Value = EditionKotK.IsInstalled(EditionKotK.GameDirectory) ? 100 : 0;
        }

        private async Task LaunchKotK()
        {
            try
            {
                string key = Properties.Settings.Default.sessionIdKey?.Trim();
                if (string.IsNullOrEmpty(key))
                {
                    // Same flow as the JSEmu 2016 servers: offer to open the account key settings.
                    if (CustomMessageBox.Show(FindResource("item153").ToString(), this, false, true, true) == MessageBoxResult.Yes)
                    {
                        SettingsWindow sw = new();
                        sw.settingsTabControl.SelectedIndex = 2;
                        sw.ShowDialog();
                    }
                    return;
                }
                if (kotkCancel != null)
                    return;

                string dir = EditionKotK.GameDirectory;
                if (!EditionKotK.IsInstalled(dir) && !HasRoomFor(dir, 15L * 1024 * 1024 * 1024))
                {
                    CustomMessageBox.Show($"KOTK needs about 15 GB of free space and the drive of\n{dir}\nhas less.\n\n" +
                        "Choose another folder with the arrow next to the game folder.", this);
                    return;
                }

                kotkCancel = new CancellationTokenSource();
                kotkCancelButton.Visibility = Visibility.Visible;
                var progress = new Progress<InstallProgress>(p =>
                {
                    if (kotkCancel == null)
                        return;
                    kotkProgress.Value = p.Total == 0 ? 0 : Math.Clamp(p.Complete * 100.0 / p.Total, 0, 100);
                    kotkStatus.Text = $"{p.Message}  •  {p.Complete / 1073741824.0:0.00} / {p.Total / 1073741824.0:0.00} GB";
                });
                void Status(string text) => Dispatcher.BeginInvoke(() => kotkStatus.Text = text);

                await EditionKotK.InstallAndPlay(key, Properties.Settings.Default.kotkName, false, progress, Status, kotkCancel.Token);
                kotkProgress.Value = 100;
                currentGame.Text = "Current Game Version: KOTK Pre-Season 5";
            }
            catch (OperationCanceledException)
            {
                kotkStatus.Text = "Cancelled. Download progress is kept - press Play to continue.";
            }
            catch (Exception ex)
            {
                EditionKotK.Log("play: " + ex);
                kotkStatus.Text = ex.Message;
                CustomMessageBox.Show($"{FindResource("item142")} {ex.Message}", this);
            }
            finally
            {
                kotkCancel?.Dispose();
                kotkCancel = null;
                kotkCancelButton.Visibility = Visibility.Collapsed;
                playButton.IsEnabled = true;
                playButton.SetResourceReference(ContentProperty, "item8");
            }
        }

        private static bool HasRoomFor(string dir, long bytes)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))).AvailableFreeSpace >= bytes; }
            catch { return true; } // Unknown drive: let the installer report a real error.
        }

        private void SelectKotKDirectory(string folder)
        {
            if (kotkCancel != null || EditionKotK.IsRunning)
            {
                CustomMessageBox.Show("Wait until KOTK finishes downloading or closes before changing its folder.", this);
                return;
            }
            // Files are installed into a KotK subfolder unless the folder already holds a KOTK install.
            string dir = EditionKotK.IsInstalled(folder) || Path.GetFileName(folder.TrimEnd('\\')).Equals("KotK", StringComparison.OrdinalIgnoreCase)
                ? folder : Path.Combine(folder, "KotK");
            Properties.Settings.Default.activeDirectoryKotK = dir;
            Properties.Settings.Default.Save();
            ShowEdition();
        }

        private void KotKNameChanged(object sender, RoutedEventArgs e)
        {
            string name = kotkNameBox.Text.Trim();
            if (name.Length > 0 && !Regex.IsMatch(name, "^[a-zA-Z0-9_]{3,24}$"))
            {
                kotkStatus.Text = "The KOTK name must be 3-24 letters, digits or _.";
                return;
            }
            Properties.Settings.Default.kotkName = name;
            Properties.Settings.Default.Save();
        }

        private void KotKCancelClick(object sender, RoutedEventArgs e) => kotkCancel?.Cancel();

        // The game's connection runs through this process: closing the launcher ends the match.
        private void LauncherWindowClosing(object sender, CancelEventArgs e)
        {
            if (!EditionKotK.IsRunning)
                return;
            if (CustomMessageBox.Show("KOTK is still running and its connection goes through the launcher.\n" +
                "Closing the launcher disconnects you from the game. Close anyway?", this, false, true, true) != MessageBoxResult.Yes)
                e.Cancel = true;
        }
    }
}
