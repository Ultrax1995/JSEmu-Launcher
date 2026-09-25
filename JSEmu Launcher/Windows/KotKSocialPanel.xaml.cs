using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Cranberry.Launcher.Core;
using H1Emu_Launcher.Classes;

namespace H1Emu_Launcher
{
    // KOTK side panel of the launcher window: the signed-in player, party, friends and invitations,
    // driven by EditionKotK's shared session and 3 s state poll. The actions are KotKSocial's, the
    // same ones the KOTK hub window uses.
    public partial class KotKSocialPanel : UserControl
    {
        /// <summary>Raised when the player has no account key yet and asks to set one.</summary>
        public event Action SignInRequested;

        private string page = "party";
        private int lastInvites;

        public KotKSocialPanel()
        {
            InitializeComponent();
        }

        private void PanelLoaded(object sender, RoutedEventArgs e)
        {
            EditionKotK.StateChanged += ApplyState;
            KotKSocial.AvatarsChanged += ApplyState;
            ShowPage(page);
            ApplyState();
        }

        private void PanelUnloaded(object sender, RoutedEventArgs e)
        {
            EditionKotK.StateChanged -= ApplyState;
            KotKSocial.AvatarsChanged -= ApplyState;
        }

        private void ShowStatus(string text)
        {
            if (text != null) statusText.Text = text;
        }

        private async Task Show(Task<string> action) => ShowStatus(await action);

        // ---- pages ------------------------------------------------------------------------------
        private void TabClick(object sender, RoutedEventArgs e) => ShowPage((string)((Button)sender).Tag);

        public void ShowPage(string name)
        {
            page = name;
            pageParty.Visibility = name == "party" ? Visibility.Visible : Visibility.Collapsed;
            pageFriends.Visibility = name == "friends" ? Visibility.Visible : Visibility.Collapsed;
            pageInvites.Visibility = name == "invites" ? Visibility.Visible : Visibility.Collapsed;
            KotKSocial.MarkTab(tabParty, name == "party");
            KotKSocial.MarkTab(tabFriends, name == "friends");
            KotKSocial.MarkTab(tabInvites, name == "invites");
        }

        private void HubPageClick(object sender, RoutedEventArgs e)
        {
            if (!EditionKotK.HasAccountKey)
            {
                SignInRequested?.Invoke();
                if (!EditionKotK.HasAccountKey) return;
            }
            KotKHubWindow.Open(Window.GetWindow(this), (string)((Button)sender).Tag);
        }

        private async void OverlayClick(object sender, RoutedEventArgs e) => await EditionKotK.ToggleOverlay();

        private void SignInClick(object sender, RoutedEventArgs e)
        {
            SignInRequested?.Invoke();
            ApplyState();
        }

        // ---- state ------------------------------------------------------------------------------
        public void ApplyState()
        {
            var state = EditionKotK.State;
            if (state == null)
            {
                bool hasKey = EditionKotK.HasAccountKey;
                signInPanel.Visibility = hasKey ? Visibility.Collapsed : Visibility.Visible;
                meName.Text = hasKey ? "SIGNING IN..." : "NOT SIGNED IN";
                meStatus.Text = hasKey ? "CONNECTING TO THE KOTK SERVER" : "ACCOUNT KEY NEEDED";
                meDot.Fill = KotKSocial.Offline;
                meInitial.Text = "?";
                meAvatar.Source = null;
                return;
            }
            signInPanel.Visibility = Visibility.Collapsed;

            var me = state.Me;
            meName.Text = me.Name.ToUpperInvariant();
            meInitial.Text = string.IsNullOrEmpty(me.Name) ? "?" : me.Name[..1].ToUpperInvariant();
            meAvatar.Source = KotKSocial.AvatarOf(me.AccountId);
            meDot.Fill = KotKSocial.Online;
            bool inLauncher = string.IsNullOrEmpty(me.GameStatus) || me.GameStatus.Equals("Offline", StringComparison.OrdinalIgnoreCase)
                || me.GameStatus.Equals("Not running", StringComparison.OrdinalIgnoreCase);
            meStatus.Text = inLauncher && !EditionKotK.IsRunning ? "ONLINE  ·  IN LAUNCHER" : "ONLINE  ·  " + (inLauncher ? "IN GAME" : me.GameStatus.ToUpperInvariant());

            // friends
            var friends = KotKSocial.Friends(state);
            KotKSocial.Fill(friendsList, friends);
            int online = 0;
            foreach (var friend in state.Friends) if (friend.Online) online++;
            friendsHeading.Text = $"FRIENDS  ·  {online} ONLINE OF {state.Friends.Count}";
            friendsEmpty.Visibility = friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            tabFriends.Content = online == 0 ? "FRIENDS" : $"FRIENDS ({online})";

            // invitations
            var invites = KotKSocial.Invites(state);
            KotKSocial.Fill(invitesList, invites);
            invitesEmpty.Visibility = invites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            invitesBadge.Visibility = invites.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            invitesCount.Text = invites.Count.ToString();
            // A new invitation brings its tab forward once.
            if (invites.Count > lastInvites && page != "invites")
                ShowPage("invites");
            lastInvites = invites.Count;

            ApplyParty(state);
            UpdateButtons();
            _ = KotKSocial.RefreshAvatars(state);
        }

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
            partyHeading.Text = lobby == null ? "NO PARTY  ·  INVITE FRIENDS FROM THE FRIENDS TAB" : $"YOUR PARTY  ·  {members.Count} PLAYERS";
            tabParty.Content = lobby == null ? "PARTY" : $"PARTY ({members.Count})";
            readyButton.Content = KotKSocial.IsReady(state) ? "UNREADY" : "READY";
            readyButton.IsEnabled = lobby?.InGame != true;
            queueButton.IsEnabled = KotKSocial.CanQueue(state);
            leaveButton.IsEnabled = lobby != null;
            if (lobby?.InGame == true)
                ShowStatus("Your party is in the same game lobby. The leader queues from the game menu.");
        }

        // ---- friends ----------------------------------------------------------------------------
        private void SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

        private void UpdateButtons()
        {
            var friend = (friendsList.SelectedItem as KotKSocial.PersonItem)?.Tag as Person;
            messageButton.IsEnabled = friend != null;
            removeButton.IsEnabled = friend != null;
            inviteButton.IsEnabled = friend?.Online == true;
            bool invite = invitesList.SelectedItem != null;
            acceptButton.IsEnabled = invite;
            declineButton.IsEnabled = invite;
        }

        private void AddFriendTextChanged(object sender, TextChangedEventArgs e) =>
            addFriendHint.Visibility = addFriendBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        private async void AddFriendClick(object sender, RoutedEventArgs e)
        {
            string name = addFriendBox.Text.Trim();
            if (name.Length == 0) return;
            await Show(KotKSocial.AddFriend(name, () => addFriendBox.Text = ""));
        }

        private void AddFriendKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddFriendClick(sender, e);
        }

        private void MessageClick(object sender, RoutedEventArgs e)
        {
            if ((friendsList.SelectedItem as KotKSocial.PersonItem)?.Tag is Person friend)
                KotKSocial.OpenChat(friend);
        }

        private async void InviteClick(object sender, RoutedEventArgs e)
        {
            if ((friendsList.SelectedItem as KotKSocial.PersonItem)?.Tag is Person friend)
                await Show(KotKSocial.InviteToParty(friend));
        }

        private async void RemoveClick(object sender, RoutedEventArgs e)
        {
            if ((friendsList.SelectedItem as KotKSocial.PersonItem)?.Tag is not Person friend) return;
            if (CustomMessageBox.Show($"Remove {friend.Name} from your KOTK friends?", Window.GetWindow(this), false, true, true) != MessageBoxResult.Yes) return;
            await Show(KotKSocial.RemoveFriend(friend));
        }

        // ---- invitations ------------------------------------------------------------------------
        private async void AcceptClick(object sender, RoutedEventArgs e) => await Respond(true);
        private async void DeclineClick(object sender, RoutedEventArgs e) => await Respond(false);

        private async Task Respond(bool accept)
        {
            if ((invitesList.SelectedItem as KotKSocial.PersonItem)?.Tag is not SocialInvite invite) return;
            await Show(KotKSocial.Respond(invite, accept));
            if (accept && !KotKSocial.IsFriendRequest(invite)) ShowPage("party");
        }

        // ---- party ------------------------------------------------------------------------------
        private async void ModeClick(object sender, RoutedEventArgs e) => await Show(KotKSocial.SetMode((string)((Button)sender).Tag));

        private async void ReadyClick(object sender, RoutedEventArgs e)
        {
            if (EditionKotK.State is { } state) await Show(KotKSocial.ToggleReady(state));
        }

        private async void QueueClick(object sender, RoutedEventArgs e) => await Show(KotKSocial.Queue());

        private async void LeaveClick(object sender, RoutedEventArgs e) => await Show(KotKSocial.Leave());
    }
}
