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
    // launcher's pages, driven by EditionKotK's shared session and 3 s state poll.
    public partial class KotKHubWindow : Window
    {
        public static KotKHubWindow Instance { get; private set; }

        public sealed class PersonItem
        {
            public string Id { get; init; }
            public string Name { get; init; }
            public string Detail { get; init; }
            public Brush Dot { get; init; }
            public ImageSource Avatar { get; init; }
            public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
            public object Tag { get; init; }
        }

        public sealed class BoardRow
        {
            public int Position { get; init; }
            public string Name { get; init; }
            public int TotalScore { get; init; }
            public string Kd { get; init; }
            public LeaderboardEntry Entry { get; init; }
        }

        private sealed record AudioDevice(int Id, string Name) { public override string ToString() => Name; }

        private static readonly Brush Online = new SolidColorBrush(Color.FromRgb(0x52, 0xB8, 0x4A));
        private static readonly Brush Offline = new SolidColorBrush(Color.FromRgb(0x4A, 0x52, 0x5A));
        private static readonly Brush Pending = new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x3D));
        private static readonly Dictionary<string, (string Version, ImageSource Image)> avatars = new();

        private readonly Dictionary<string, KotKChatWindow> chats = new();
        private string boardMode = "Solo";
        private DateTime boardNextRefresh;
        private string pendingPicture;
        private bool refreshingAvatars;

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
                meDot.Fill = Offline;
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
            foreach (var chat in chats.Values.ToArray()) chat.Close();
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
                button.BorderBrush = tag == page ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromRgb(0x2C, 0x35, 0x40));
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
            meDot.Fill = Online;

            string selectedFriend = (friendsList.SelectedItem as PersonItem)?.Id;
            friendsList.ItemsSource = state.Friends.Select(p => new PersonItem
            {
                Id = p.AccountId,
                Name = p.Name,
                Detail = (p.Online ? p.GameStatus : "Offline")
                    + (EditionKotK.Unread.TryGetValue(p.AccountId, out int unread) && unread > 0 ? $" · {unread} unread" : ""),
                Dot = p.Online ? Online : Offline,
                Avatar = AvatarOf(p.AccountId),
                Tag = p
            }).ToList();
            friendsList.SelectedItem = ((List<PersonItem>)friendsList.ItemsSource).FirstOrDefault(i => i.Id == selectedFriend);
            friendsHeading.Text = $"YOUR FRIENDS  ({state.Friends.Count(p => p.Online)}/{state.Friends.Count})";

            string selectedInvite = (invitesList.SelectedItem as PersonItem)?.Id;
            invitesList.ItemsSource = state.Invites.Select(i => new PersonItem
            {
                Id = i.Id,
                Name = i.FromName,
                Detail = i.Kind.Equals("Friend", StringComparison.OrdinalIgnoreCase) ? "Friend request" : "Party invitation",
                Dot = Pending,
                Tag = i
            }).ToList();
            invitesList.SelectedItem = ((List<PersonItem>)invitesList.ItemsSource).FirstOrDefault(i => i.Id == selectedInvite);
            invitesHeading.Text = state.Invites.Count == 0 ? "REQUESTS AND INVITATIONS" : $"REQUESTS AND INVITATIONS ({state.Invites.Count})";
            tabFriends.Content = state.Invites.Count == 0 ? "Friends" : $"Friends ({state.Invites.Count})";

            ApplyParty(state);
            UpdateFriendButtons();
            _ = RefreshAvatars(state);
            foreach (var chat in chats.Values) chat.Refresh();
        }

        private static ImageSource AvatarOf(string accountId) =>
            avatars.TryGetValue(accountId, out var avatar) ? avatar.Image : null;

        // Pictures are fetched only when a person's avatar version changes.
        private async Task RefreshAvatars(LauncherState state)
        {
            if (refreshingAvatars) return;
            refreshingAvatars = true;
            bool changed = false;
            try
            {
                foreach (var person in state.Friends.Prepend(state.Me))
                {
                    if (avatars.TryGetValue(person.AccountId, out var old) && old.Version == person.AvatarVersion) continue;
                    var view = person.AvatarVersion.Length == 0 ? new AvatarView("", "")
                        : await EditionKotK.Get<AvatarView>("api/profile/" + Uri.EscapeDataString(person.AccountId) + "/avatar");
                    avatars[person.AccountId] = (view.Version, ToImage(view.Pixels));
                    changed = true;
                }
            }
            catch (Exception ex) { EditionKotK.Log("avatars: " + ex.Message); }
            finally { refreshingAvatars = false; }
            if (pendingPicture == null)
                profilePicture.Source = AvatarOf(state.Me.AccountId);
            if (changed) ApplyState();
        }

        public static ImageSource ToImage(string pixels)
        {
            if (string.IsNullOrEmpty(pixels)) return null;
            AvatarPixels.Validate(pixels);
            byte[] rgb = Convert.FromHexString(pixels);
            var bitmap = BitmapSource.Create(AvatarPixels.Size, AvatarPixels.Size, 96, 96, PixelFormats.Rgb24, null, rgb, AvatarPixels.Size * 3);
            bitmap.Freeze();
            return bitmap;
        }

        private async Task Run(Func<Task> action, string done = null)
        {
            try
            {
                await action();
                if (done != null) ShowStatus(done);
                await EditionKotK.Poll();
            }
            catch (Exception ex) { ShowStatus(ex.Message); }
        }

        // ---- friends ----------------------------------------------------------------------------
        private void FriendSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFriendButtons();
        private void InviteSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFriendButtons();

        private void UpdateFriendButtons()
        {
            var friend = (friendsList.SelectedItem as PersonItem)?.Tag as Person;
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
            await Run(async () => { await EditionKotK.Post("api/friends", new TargetRequest(name)); addFriendBox.Text = ""; },
                $"Friend request sent to {name}.");
        }

        private void AddFriendKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddFriendClick(sender, e);
        }

        private async void RemoveFriendClick(object sender, RoutedEventArgs e)
        {
            if ((friendsList.SelectedItem as PersonItem)?.Tag is not Person friend) return;
            if (CustomMessageBox.Show($"Remove {friend.Name} from your KOTK friends?", this, false, true, true) != MessageBoxResult.Yes) return;
            await Run(() => EditionKotK.Post("api/friends/remove", new TargetRequest(friend.AccountId)), $"{friend.Name} removed.");
        }

        private async void InviteFriendClick(object sender, RoutedEventArgs e)
        {
            if ((friendsList.SelectedItem as PersonItem)?.Tag is not Person friend) return;
            await Run(() => EditionKotK.Post("api/party/invite", new TargetRequest(friend.AccountId)), $"Party invitation sent to {friend.Name}.");
        }

        private async void AcceptInviteClick(object sender, RoutedEventArgs e) => await RespondInvite(true);
        private async void DeclineInviteClick(object sender, RoutedEventArgs e) => await RespondInvite(false);

        private async Task RespondInvite(bool accept)
        {
            if ((invitesList.SelectedItem as PersonItem)?.Tag is not SocialInvite invite) return;
            bool party = !invite.Kind.Equals("Friend", StringComparison.OrdinalIgnoreCase);
            await Run(() => EditionKotK.Post("api/invites/respond", new RespondRequest(invite.Id, accept)),
                !accept ? "Declined." : party ? $"You joined {invite.FromName}'s party." : $"{invite.FromName} is now your friend.");
            if (accept && party) ShowPage("party");
        }

        private void MessageFriendClick(object sender, RoutedEventArgs e)
        {
            if ((friendsList.SelectedItem as PersonItem)?.Tag is not Person friend) return;
            if (chats.TryGetValue(friend.AccountId, out var open))
            {
                open.Activate();
                return;
            }
            var chat = new KotKChatWindow(friend.AccountId, friend.Name) { Owner = this };
            chats[friend.AccountId] = chat;
            chat.Closed += (_, _) => chats.Remove(friend.AccountId);
            chat.Show();
        }

        // ---- party ------------------------------------------------------------------------------
        private bool IsReady(LauncherState state) =>
            state.Lobby?.Members.Any(m => m.AccountId == state.Me.AccountId && m.Ready) == true;

        private void ApplyParty(LauncherState state)
        {
            var lobby = state.Lobby;
            bool leader = lobby == null || lobby.LeaderId == state.Me.AccountId;
            string mode = lobby?.Mode ?? "Duos";
            foreach (var button in new[] { modeSolo, modeDuos, modeFives })
            {
                button.IsEnabled = leader && lobby?.InGame != true;
                button.BorderBrush = (string)button.Tag == mode ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromRgb(0x2C, 0x35, 0x40));
            }
            var members = lobby?.Members.ToList() ?? new List<LobbyMember> { new(state.Me.AccountId, state.Me.Name, false, true, state.Me.GameStatus) };
            partyList.ItemsSource = members.Select(m => new PersonItem
            {
                Id = m.AccountId,
                Name = m.Name + (lobby != null && m.AccountId == lobby.LeaderId ? "  (leader)" : ""),
                Detail = (m.Ready ? "Ready" : "Not ready") + "  ·  " + (m.Online ? m.GameStatus : "Offline"),
                Dot = m.Ready ? Online : m.Online ? Pending : Offline,
                Avatar = AvatarOf(m.AccountId)
            }).ToList();
            partyHeading.Text = lobby == null ? "YOUR PARTY - invite friends from the Friends tab" : $"YOUR PARTY  ({members.Count})";
            readyButton.Content = IsReady(state) ? "UNREADY" : "READY";
            readyButton.IsEnabled = lobby?.InGame != true;
            queueButton.IsEnabled = leader && lobby != null && !lobby.InGame && lobby.Members.All(m => m.Ready && m.Online && m.GameStatus == "Menu");
            leaveButton.IsEnabled = lobby != null;
            tabParty.Content = lobby == null ? "Party" : $"Party ({members.Count})";
            if (lobby?.InGame == true)
                ShowStatus("You are in the same game lobby. The leader can select Duos or Fives and queue from the game menu.");
        }

        private async void ModeClick(object sender, RoutedEventArgs e) =>
            await Run(() => EditionKotK.Post("api/party/mode", new ModeRequest((string)((Button)sender).Tag)), "Party mode changed.");

        private async void ReadyClick(object sender, RoutedEventArgs e)
        {
            var state = EditionKotK.State;
            if (state == null) return;
            await Run(() => EditionKotK.Post("api/party/ready", new ReadyRequest(!IsReady(state))), "Party updated.");
        }

        private async void QueueClick(object sender, RoutedEventArgs e) =>
            await Run(() => EditionKotK.Post("api/party/queue", new { }), "Queued. Return to the game to join when it is ready.");

        private async void LeaveClick(object sender, RoutedEventArgs e) =>
            await Run(() => EditionKotK.Post("api/party/leave", new { }), "Party left.");

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
                button.BorderBrush = (string)button.Tag == boardMode ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromRgb(0x2C, 0x35, 0x40));
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

        // ---- profile picture --------------------------------------------------------------------
        private void ChoosePictureClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Choose your profile picture", Filter = "Pictures (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                pendingPicture = ProfilePictures.Load(dialog.FileName);
                profilePicture.Source = ToImage(pendingPicture);
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
            await Run(async () =>
            {
                await EditionKotK.Post<AvatarView>("api/profile/avatar", new AvatarRequest(pendingPicture));
                pendingPicture = null;
                savePictureButton.IsEnabled = false;
            }, "Profile picture saved. Your friends see it automatically.");
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
            ShowStatus("Voice settings saved.");
            await EditionKotK.RestartVoice();
        }

        private void ShowVoiceStatus() => Dispatcher.BeginInvoke(() => voiceStatus.Text = EditionKotK.VoiceStatus);
    }
}
