using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cranberry.Launcher.Client;
using Cranberry.Launcher.Core;
using H1Emu_Launcher.Classes;
using Microsoft.Win32;
using NAudio.Wave;

namespace H1Emu_Launcher
{
    // KOTK hub: friends, party, leaderboard, profile picture and proximity voice - the Cranberry
    // launcher's pages, driven by EditionKotK's shared session and 3 s state poll. Friends, party
    // and invitations are also in the launcher window (KotKSocialPanel); both use KotKSocial.
    public partial class KotKHubWindow : Window
    {
        public static KotKHubWindow Instance { get; private set; }

        public sealed class BoardRow
        {
            public int Position { get; init; }
            public string Name { get; init; }
            public int TotalScore { get; init; }
            public string Kd { get; init; }
            public LeaderboardEntry Entry { get; init; }
        }

        private sealed record AudioDevice(int Id, string Name) { public override string ToString() => Name; }

        private string boardMode = "Solo";
        private DateTime boardNextRefresh;
        private string pendingPicture;

        public KotKHubWindow()
        {
            InitializeComponent();
        }

        public static void Open(Window owner, string page = "friends")
        {
            if (Instance == null)
            {
                Instance = new KotKHubWindow { Owner = owner };
                Instance.Show();
            }
            Instance.ShowPage(page);
            if (Instance.WindowState == WindowState.Minimized) Instance.WindowState = WindowState.Normal;
            Instance.Activate();
        }

        private async void WindowLoaded(object sender, RoutedEventArgs e)
        {
            EditionKotK.StateChanged += ApplyState;
            EditionKotK.Status += ShowStatus;
            EditionKotK.VoiceStatusChanged += ShowVoiceStatus;
            KotKSocial.AvatarsChanged += ApplyState;
            LoadVoiceSettings();
            ShowVoiceStatus();
            ShowPage("friends");
            try
            {
                var session = await EditionKotK.SignIn();
                meName.Text = session.Name;
                profileName.Text = session.Name;
            }
            catch (Exception ex)
            {
                meName.Text = "Not signed in";
                meDot.Fill = KotKSocial.Offline;
                ShowStatus(ex.Message);
            }
            ApplyState();
            await EditionKotK.Poll();
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            EditionKotK.StateChanged -= ApplyState;
            EditionKotK.Status -= ShowStatus;
            EditionKotK.VoiceStatusChanged -= ShowVoiceStatus;
            KotKSocial.AvatarsChanged -= ApplyState;
            Instance = null;
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void CloseClick(object sender, RoutedEventArgs e) => Close();

        private void ShowStatus(string text) => Dispatcher.BeginInvoke(() => statusText.Text = text);

        private void TabClick(object sender, RoutedEventArgs e) => ShowPage((string)((Button)sender).Tag);

        public async void ShowPage(string page)
        {
            pageFriends.Visibility = page == "friends" ? Visibility.Visible : Visibility.Collapsed;
            pageParty.Visibility = page == "party" ? Visibility.Visible : Visibility.Collapsed;
            pageLeaderboard.Visibility = page == "leaderboard" ? Visibility.Visible : Visibility.Collapsed;
            pageProfile.Visibility = page == "profile" ? Visibility.Visible : Visibility.Collapsed;
            pageVoice.Visibility = page == "voice" ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (button, tag) in new[] { (tabFriends, "friends"), (tabParty, "party"), (tabLeaderboard, "leaderboard"), (tabProfile, "profile"), (tabVoice, "voice") })
                KotKSocial.MarkTab(button, tag == page);
            if (page == "leaderboard")
                await RefreshBoard(false);
        }

        // ---- state ------------------------------------------------------------------------------
        private void ApplyState()
        {
            var state = EditionKotK.State;
            if (state == null)
                return;
            meName.Text = state.Me.Name;
            profileName.Text = state.Me.Name;
            meDot.Fill = KotKSocial.Online;

            KotKSocial.Fill(friendsList, KotKSocial.Friends(state));
            friendsHeading.Text = $"YOUR FRIENDS  ({state.Friends.Count(p => p.Online)}/{state.Friends.Count})";

            KotKSocial.Fill(invitesList, KotKSocial.Invites(state));
            invitesHeading.Text = state.Invites.Count == 0 ? "REQUESTS AND INVITATIONS" : $"REQUESTS AND INVITATIONS ({state.Invites.Count})";
            tabFriends.Content = state.Invites.Count == 0 ? "FRIENDS" : $"FRIENDS ({state.Invites.Count})";

            ApplyParty(state);
            UpdateFriendButtons();
            if (pendingPicture == null)
                profilePicture.Source = KotKSocial.AvatarOf(state.Me.AccountId);
            _ = KotKSocial.RefreshAvatars(state);
        }

        private async Task Run(Task<string> action)
        {
            string text = await action;
            if (text != null) ShowStatus(text);
        }

        // ---- friends ----------------------------------------------------------------------------
        private void FriendSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFriendButtons();
        private void InviteSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFriendButtons();

        private void UpdateFriendButtons()
        {
            var friend = (friendsList.SelectedItem as KotKSocial.PersonItem)?.Tag as Person;
            messageButton.IsEnabled = friend != null;
            removeButton.IsEnabled = friend != null;
            inviteButton.IsEnabled = friend?.Online == true;
            bool invite = invitesList.SelectedItem != null;
            acceptButton.IsEnabled = invite;
            declineButton.IsEnabled = invite;
        }

        private async void AddFriendClick(object sender, RoutedEventArgs e)
        {
            string name = addFriendBox.Text.Trim();
            if (name.Length == 0) return;
            await Run(KotKSocial.AddFriend(name, () => addFriendBox.Text = ""));
        }

        private void AddFriendKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddFriendClick(sender, e);
        }

        private async void RemoveFriendClick(object sender, RoutedEventArgs e)
        {
            if ((friendsList.SelectedItem as KotKSocial.PersonItem)?.Tag is not Person friend) return;
            if (CustomMessageBox.Show($"Remove {friend.Name} from your KOTK friends?", this, false, true, true) != MessageBoxResult.Yes) return;
            await Run(KotKSocial.RemoveFriend(friend));
        }

        private async void InviteFriendClick(object sender, RoutedEventArgs e)
        {
            if ((friendsList.SelectedItem as KotKSocial.PersonItem)?.Tag is not Person friend) return;
            await Run(KotKSocial.InviteToParty(friend));
        }

        private async void AcceptInviteClick(object sender, RoutedEventArgs e) => await RespondInvite(true);
        private async void DeclineInviteClick(object sender, RoutedEventArgs e) => await RespondInvite(false);

        private async Task RespondInvite(bool accept)
        {
            if ((invitesList.SelectedItem as KotKSocial.PersonItem)?.Tag is not SocialInvite invite) return;
            await Run(KotKSocial.Respond(invite, accept));
            if (accept && !KotKSocial.IsFriendRequest(invite)) ShowPage("party");
        }

        private void MessageFriendClick(object sender, RoutedEventArgs e)
        {
            if ((friendsList.SelectedItem as KotKSocial.PersonItem)?.Tag is Person friend)
                KotKSocial.OpenChat(friend);
        }

        // ---- party ------------------------------------------------------------------------------
        private void ApplyParty(LauncherState state)
        {
            var lobby = state.Lobby;
            bool leader = KotKSocial.IsLeader(state);
            string mode = lobby?.Mode ?? "Duos";
            foreach (var button in new[] { modeSolo, modeDuos, modeFives })
            {
                button.IsEnabled = leader && lobby?.InGame != true;
                KotKSocial.MarkChoice(button, (string)button.Tag == mode);
            }
            var members = KotKSocial.PartyMembers(state);
            partyList.ItemsSource = members;
            partyHeading.Text = lobby == null ? "YOUR PARTY - invite friends from the Friends tab" : $"YOUR PARTY  ({members.Count})";
            readyButton.Content = KotKSocial.IsReady(state) ? "UNREADY" : "READY";
            readyButton.IsEnabled = lobby?.InGame != true;
            queueButton.IsEnabled = KotKSocial.CanQueue(state);
            leaveButton.IsEnabled = lobby != null;
            tabParty.Content = lobby == null ? "PARTY" : $"PARTY ({members.Count})";
            if (lobby?.InGame == true)
                ShowStatus("You are in the same game lobby. The leader can select Duos or Fives and queue from the game menu.");
        }

        private async void ModeClick(object sender, RoutedEventArgs e) =>
            await Run(KotKSocial.SetMode((string)((Button)sender).Tag));

        private async void ReadyClick(object sender, RoutedEventArgs e)
        {
            if (EditionKotK.State is { } state) await Run(KotKSocial.ToggleReady(state));
        }

        private async void QueueClick(object sender, RoutedEventArgs e) => await Run(KotKSocial.Queue());

        private async void LeaveClick(object sender, RoutedEventArgs e) => await Run(KotKSocial.Leave());

        private async void OverlayClick(object sender, RoutedEventArgs e) => await EditionKotK.ToggleOverlay();

        // ---- leaderboard ------------------------------------------------------------------------
        private async void BoardModeClick(object sender, RoutedEventArgs e)
        {
            boardMode = (string)((Button)sender).Tag;
            await RefreshBoard(true);
        }

        private async void BoardRefreshClick(object sender, RoutedEventArgs e) => await RefreshBoard(true);

        private async Task RefreshBoard(bool force)
        {
            foreach (var button in new[] { boardSolo, boardDuos, boardFives })
                KotKSocial.MarkChoice(button, (string)button.Tag == boardMode);
            if (!force && DateTime.UtcNow < boardNextRefresh) return;
            boardNextRefresh = DateTime.UtcNow.AddSeconds(15);
            try
            {
                var view = await EditionKotK.Get<LeaderboardView>("api/leaderboard/" + boardMode);
                boardList.ItemsSource = view.Players.Select(p => new BoardRow
                {
                    Position = p.Position,
                    Name = p.Name,
                    TotalScore = p.TotalScore,
                    Kd = Kd(p),
                    Entry = p
                }).ToList();
                boardMe.Text = view.Me == null ? $"{view.Season} · {view.TotalPlayers} players · you are not ranked yet"
                    : $"{view.Season} · YOU #{view.Me.Position} of {view.TotalPlayers} · {view.Me.TotalScore} points";
                ShowBoardPlayer(view.Me);
            }
            catch (Exception ex) { ShowStatus("Leaderboard: " + ex.Message); }
        }

        private static string Kd(LeaderboardEntry p) =>
            p.KdDeaths == 0 ? p.KdKills.ToString() : (p.KdKills / (double)p.KdDeaths).ToString("0.00");

        private void BoardSelectionChanged(object sender, SelectionChangedEventArgs e) =>
            ShowBoardPlayer((boardList.SelectedItem as BoardRow)?.Entry);

        private void ShowBoardPlayer(LeaderboardEntry entry)
        {
            if (entry == null)
            {
                boardPlayer.Text = "SELECT A PLAYER";
                boardStats.Text = "See their ten highest scores.";
                boardBest.ItemsSource = null;
                return;
            }
            boardPlayer.Text = $"#{entry.Position}  {entry.Name}";
            boardStats.Text = $"{entry.Tier} · {entry.Matches} matches · {entry.Wins} wins · {entry.Kills} kills · K/D {Kd(entry)}";
            boardBest.ItemsSource = entry.Best;
        }

        // ---- account name -----------------------------------------------------------------------
        // ---- profile picture --------------------------------------------------------------------
        private void ChoosePictureClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Choose your profile picture", Filter = "Pictures (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                pendingPicture = ProfilePictures.Load(dialog.FileName);
                profilePicture.Source = KotKSocial.ToImage(pendingPicture);
                savePictureButton.IsEnabled = true;
            }
            catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or System.IO.IOException or InvalidOperationException)
            {
                ShowStatus(ex is OutOfMemoryException or ArgumentException ? "That picture could not be opened. Choose another PNG or JPEG." : ex.Message);
            }
        }

        private void RemovePictureClick(object sender, RoutedEventArgs e)
        {
            pendingPicture = "";
            profilePicture.Source = null;
            savePictureButton.IsEnabled = true;
        }

        private async void SavePictureClick(object sender, RoutedEventArgs e)
        {
            if (pendingPicture == null) return;
            await Run(KotKSocial.Run(async () =>
            {
                await EditionKotK.Post<AvatarView>("api/profile/avatar", new AvatarRequest(pendingPicture));
                pendingPicture = null;
                savePictureButton.IsEnabled = false;
            }, "Profile picture saved. Your friends see it automatically."));
        }

        // ---- voice ------------------------------------------------------------------------------
        private void LoadVoiceSettings()
        {
            var settings = Properties.Settings.Default;
            voiceEnabled.IsChecked = settings.kotkVoiceEnabled;
            voiceKey.Text = settings.kotkVoiceKey ?? "";
            voiceVolume.Value = Math.Clamp(settings.kotkVoiceVolume, 0, 100);
            voiceInput.Items.Clear();
            voiceOutput.Items.Clear();
            voiceInput.Items.Add(new AudioDevice(-1, "Windows default microphone"));
            voiceOutput.Items.Add(new AudioDevice(-1, "Windows default speakers / headphones"));
            try
            {
                for (int i = 0; i < WaveIn.DeviceCount; i++) voiceInput.Items.Add(new AudioDevice(i, WaveIn.GetCapabilities(i).ProductName));
                for (int i = 0; i < WaveOut.DeviceCount; i++) voiceOutput.Items.Add(new AudioDevice(i, WaveOut.GetCapabilities(i).ProductName));
            }
            catch (NAudio.MmException) { voiceStatus.Text = "Audio devices unavailable. Check Windows sound settings."; }
            voiceInput.SelectedIndex = Math.Clamp(settings.kotkVoiceInput + 1, 0, voiceInput.Items.Count - 1);
            voiceOutput.SelectedIndex = Math.Clamp(settings.kotkVoiceOutput + 1, 0, voiceOutput.Items.Count - 1);
        }

        private void VoiceVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (voiceVolumeLabel != null) voiceVolumeLabel.Text = $"VOICE VOLUME  {(int)e.NewValue}%";
        }

        private async void SaveVoiceClick(object sender, RoutedEventArgs e)
        {
            var settings = Properties.Settings.Default;
            settings.kotkVoiceEnabled = voiceEnabled.IsChecked == true;
            settings.kotkVoiceInput = (voiceInput.SelectedItem as AudioDevice)?.Id ?? -1;
            settings.kotkVoiceOutput = (voiceOutput.SelectedItem as AudioDevice)?.Id ?? -1;
            settings.kotkVoiceVolume = (int)voiceVolume.Value;
            settings.kotkVoiceKey = voiceKey.Text.Trim();
            settings.Save();
            ShowStatus("Settings saved.");
            await EditionKotK.RestartVoice();
        }

        private void ShowVoiceStatus() => Dispatcher.BeginInvoke(() => voiceStatus.Text = EditionKotK.VoiceStatus);
    }
}
