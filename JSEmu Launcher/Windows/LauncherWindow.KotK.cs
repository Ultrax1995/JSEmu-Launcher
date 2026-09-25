using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Cranberry.Launcher.Core;
using H1Emu_Launcher.Classes;
using Microsoft.Toolkit.Uwp.Notifications;

namespace H1Emu_Launcher
{
    // KOTK edition: install / play / social through EditionKotK. See Classes\EditionKotK.cs.
    // The KOTK layout (kotkLayout in LauncherWindow.xaml) has its own Play button, status, progress
    // and folder; friends, party and invitations are the KotKSocialPanel on its right.
    public partial class LauncherWindow
    {
        private CancellationTokenSource kotkCancel;
        private DispatcherTimer kotkPoll;

        private static readonly Brush KotKGold = Frozen(Color.FromRgb(0xF2, 0xB2, 0x33));
        private static readonly Brush KotKGreen = Frozen(Color.FromRgb(0x5B, 0xC4, 0x4F));
        private static readonly Brush KotKRed = Frozen(Color.FromRgb(0xD8, 0x35, 0x2C));
        private static readonly Brush KotKDim = Frozen(Color.FromRgb(0x5E, 0x59, 0x4F));

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private void ShowKotKPanel(bool visible)
        {
            if (kotkPoll == null)
            {
                // Friends, invitations and party state, like the Cranberry launcher's 3 s poll.
                kotkPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                kotkPoll.Tick += async (_, _) => await EditionKotK.Poll();
                EditionKotK.Status += text => Dispatcher.BeginInvoke(() => kotkStatus.Text = text);
                EditionKotK.StateChanged += ShowKotKSocial;
                EditionKotK.PollFailed += ShowKotKServerDown;
                EditionKotK.InviteReceived += NotifyKotKInvite;
                kotkSocial.SignInRequested += () => EnsureKotKKey();
                // The game was closed to leave a match: start it again straight into the menu.
                EditionKotK.RestartRequested += async () =>
                {
                    kotkStatus.Text = "Returning to the menu - restarting KOTK...";
                    await RunKotK(false);
                };
            }
            if (!visible)
            {
                // Keep polling while the game runs: party invitations still arrive.
                if (!EditionKotK.IsRunning) kotkPoll.Stop();
                return;
            }
            if (kotkCancel == null && !EditionKotK.IsRunning)
                kotkProgress.Value = EditionKotK.IsInstalled(EditionKotK.GameDirectory) ? 100 : 0;
            kotkSocial.ApplyState();
#if DEBUG
            // Debug builds only, and only for a UI preview that explicitly asks for sample data.
            if (Environment.GetEnvironmentVariable("JSEMU_UI_PREVIEW") == "1" && Environment.GetEnvironmentVariable("JSEMU_UI_SAMPLE_DATA") == "1")
            {
                ShowKotKPreviewSample();
                return;
            }
#endif
            if (EditionKotK.HasAccountKey)
            {
                kotkPoll.Start();
                _ = EditionKotK.Poll();
                if (kotkTicketPoll == null)
                {
                    kotkTicketPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                    kotkTicketPoll.Tick += async (_, _) => await RefreshKotKTickets();
                }
                kotkTicketPoll.Start();
                _ = RefreshKotKTickets();
            }
            else
            {
                kotkServerDot.Fill = KotKDim;
                kotkServerText.Text = "KOTK SERVER  ·  EU  ·  SET YOUR ACCOUNT KEY TO CONNECT";
            }
        }

        // Game folder and install state of the KOTK layout.
        private void ShowKotKInstall()
        {
            string dir = EditionKotK.GameDirectory;
            bool installed = EditionKotK.IsInstalled(dir);
            kotkDirectoryBox.Text = dir;
            kotkDirectoryBox.ToolTip = dir;
            kotkGameVersion.Text = installed ? "INSTALLED  ·  PRE-SEASON 5" : "NOT INSTALLED";
            kotkVersionDot.Fill = installed ? KotKGreen : KotKDim;
            // Until the first download or launch reports its own progress.
            if (kotkCancel == null && !EditionKotK.IsRunning && (kotkStatus.Text == KotKNotInstalledText || kotkStatus.Text == KotKReadyText))
                kotkStatus.Text = installed ? KotKReadyText : KotKNotInstalledText;
        }

        private const string KotKNotInstalledText = "Press Play - the launcher downloads KOTK (about 14.6 GB) and signs you in with your account key.";
        private const string KotKReadyText = "Ready to drop. Press Play - the launcher signs you in with your account key and starts KOTK.";

        private DispatcherTimer kotkTicketPoll;

        private async Task RefreshKotKTickets()
        {
            if (!EditionKotK.HasAccountKey) return;
            try { ShowKotKTickets(await EditionKotK.Tickets()); }
            catch (Exception ex) { EditionKotK.Log("tickets: " + ex.Message); }
        }

        private void ShowKotKTickets(TicketStatus t)
        {
            bool capped = t.EarnedToday >= t.DailyCap;
            int minutes = t.SecondsToday % t.SecondsPerTicket / 60, perTicket = t.SecondsPerTicket / 60;
            kotkTicketToday.Text = $"TODAY {t.EarnedToday}/{t.DailyCap}" + (t.WinTickets > 0 ? $"   ·   WIN BONUS {t.WinTickets}" : "");
            kotkTicketProgress.Maximum = t.SecondsPerTicket;
            kotkTicketProgress.Value = capped ? t.SecondsPerTicket : t.SecondsToday % t.SecondsPerTicket;
            kotkTicketText.Text = capped
                ? "Daily play-time tickets done - a win with 6+ players still earns one."
                : $"{minutes} / {perTicket} min in matches toward the next ticket  ·  +1 for a win with 6+ players";
            kotkTicketClaim.IsEnabled = t.Available > 0;
            kotkTicketClaim.Content = t.Available > 0 ? $"CLAIM {t.Available}" : "CLAIM";
        }

        private async void KotKClaimTickets(object sender, RoutedEventArgs e)
        {
            kotkTicketClaim.IsEnabled = false;
            try
            {
                var result = await EditionKotK.ClaimTickets();
                ShowKotKTickets(result.Status);
                kotkStatus.Text = $"Claimed {result.Claimed} event ticket{(result.Claimed == 1 ? "" : "s")} - they are on your jsemu.eu account.";
            }
            catch (Exception ex)
            {
                kotkStatus.Text = ex.Message;
                await RefreshKotKTickets();
            }
        }

        private void ShowKotKSocial()
        {
            var state = EditionKotK.State;
            if (state == null)
                return;
            kotkServerDot.Fill = KotKGreen;
            kotkServerText.Text = "KOTK SERVER  ·  EU  ·  ONLINE";
            kotkServerText.ToolTip = null;
            int online = state.Friends.Count(p => p.Online);
            kotkFriendsOnline.Text = state.Friends.Count == 0 ? "" : $"|    {online} OF {state.Friends.Count} FRIENDS ONLINE";
        }

        private void ShowKotKServerDown(string error)
        {
            kotkServerDot.Fill = KotKRed;
            kotkServerText.Text = "KOTK SERVER  ·  EU  ·  UNREACHABLE";
            kotkServerText.ToolTip = error;
        }

        private void NotifyKotKInvite(SocialInvite invite)
        {
            string text = invite.Kind.Equals("Friend", StringComparison.OrdinalIgnoreCase)
                ? $"{invite.FromName} sent you a KOTK friend request."
                : $"{invite.FromName} invited you to their KOTK party.";
            // In-game party invitations already show in the game's own UI.
            if (EditionKotK.IsRunning && invite.Kind == "GameParty")
                return;
            try { new ToastContentBuilder().AddText("KOTK invitation").AddText(text + " Open the KOTK tab of the launcher to accept.").Show(); }
            catch (Exception ex) { EditionKotK.Log("toast: " + ex.Message); }
        }

        private bool EnsureKotKKey()
        {
            if (EditionKotK.HasAccountKey)
                return true;
            // Same flow as the JSEmu 2016 servers: offer to open the account key settings.
            if (CustomMessageBox.Show(FindResource("item153").ToString(), this, false, true, true) == MessageBoxResult.Yes)
            {
                SettingsWindow sw = new();
                sw.settingsTabControl.SelectedIndex = 2;
                sw.ShowDialog();
            }
            if (!EditionKotK.HasAccountKey)
                return false;
            kotkPoll.Start();
            _ = EditionKotK.Poll();
            return true;
        }

        private async Task LaunchKotK() => await RunKotK(false);

        private async void KotKPlayClick(object sender, RoutedEventArgs e) => await RunKotK(false);

        private async void KotKRepairClick(object sender, RoutedEventArgs e) => await RunKotK(true);

        // Both Play buttons: the KOTK layout's own, and the classic one that command-line
        // arguments press (ExecuteArguments raises its Click, which dispatches here in KOTK mode).
        private void SetKotKPlayEnabled(bool enabled)
        {
            kotkPlayButton.IsEnabled = enabled;
            playButton.IsEnabled = enabled;
            if (enabled)
            {
                kotkPlayButton.SetResourceReference(ContentProperty, "item8");
                playButton.SetResourceReference(ContentProperty, "item8");
            }
        }

        private async Task RunKotK(bool installOnly)
        {
            // UI previews next to the player's own launcher never install or start the game (a
            // click in a preview window started a 14 GB download to the default folder, 25.09).
            if (Environment.GetEnvironmentVariable("JSEMU_UI_PREVIEW") == "1")
            {
                kotkStatus.Text = "UI preview: install and play are disabled.";
                return;
            }
            try
            {
                if (kotkCancel != null || !EnsureKotKKey())
                    return;
                if (EditionKotK.IsRunning)
                {
                    CustomMessageBox.Show("KOTK is already running. Close the game first.", this);
                    return;
                }

                string dir = EditionKotK.GameDirectory;
                if (!EditionKotK.IsInstalled(dir) && !HasRoomFor(dir, 15L * 1024 * 1024 * 1024))
                {
                    CustomMessageBox.Show($"KOTK needs about 15 GB of free space and the drive of\n{dir}\nhas less.\n\n" +
                        "Choose another folder with the CHANGE button next to the game folder.", this);
                    return;
                }

                kotkCancel = new CancellationTokenSource();
                kotkCancelButton.Visibility = Visibility.Visible;
                kotkRepairButton.IsEnabled = false;
                kotkDirectoryButton.IsEnabled = false;
                SetKotKPlayEnabled(false);
                kotkPlayButton.Content = installOnly ? "VERIFYING" : "PREPARING";
                var progress = new Progress<InstallProgress>(p =>
                {
                    if (kotkCancel == null)
                        return;
                    kotkProgress.Value = p.Total == 0 ? 0 : Math.Clamp(p.Complete * 100.0 / p.Total, 0, 100);
                    kotkStatus.Text = $"{p.Message}  •  {p.Complete / 1073741824.0:0.00} / {p.Total / 1073741824.0:0.00} GB";
                });

                await EditionKotK.InstallAndPlay(installOnly, progress, kotkCancel.Token);
                kotkProgress.Value = 100;
                ShowKotKInstall();
                kotkPoll.Start();
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
                kotkRepairButton.IsEnabled = true;
                kotkDirectoryButton.IsEnabled = true;
                SetKotKPlayEnabled(true);
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

#if DEBUG
        // UI preview only (Debug, JSEMU_UI_PREVIEW=1 and JSEMU_UI_SAMPLE_DATA=1): made-up friends, a
        // party and invitations to lay the KOTK layout out against. Never compiled into Release.
        private static void ShowKotKPreviewSample()
        {
            var me = new Person("me", "Ultrax", true, "Menu");
            var friends = new[]
            {
                new Person("f1", "CrownHunter", true, "Menu"),
                new Person("f2", "Zone_Runner", true, "In match"),
                new Person("f3", "LastCircle", false),
                new Person("f4", "AirdropAndy", false),
                new Person("f5", "PanFryer", true, "Lobby"),
            };
            var invites = new[]
            {
                new SocialInvite("i1", "f6", "GasMaskGary", "Friend"),
                new SocialInvite("i2", "f2", "Zone_Runner", "Party"),
            };
            var lobby = new LobbyView("l1", "me", "Duos", new[]
            {
                new LobbyMember("me", "Ultrax", true, true, "Menu"),
                new LobbyMember("f1", "CrownHunter", false, true, "Menu"),
            });
            EditionKotK.ShowPreviewState(new LauncherState(me, friends, invites, lobby));
        }

#endif
        private void KotKCancelClick(object sender, RoutedEventArgs e) => kotkCancel?.Cancel();

        private void OpenWebsite(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Info.WEBSITE,
                UseShellExecute = true
            });
        }

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
