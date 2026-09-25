using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cranberry.Launcher.Client;
using Cranberry.Launcher.Core;

namespace H1Emu_Launcher.Classes
{
    // KOTK friends, party and invitations as the launcher window's side panel and the KOTK hub
    // show them: list rows, avatars, open message windows and the social actions. Network calls go
    // through EditionKotK; the state comes from its 3 s poll. UI thread only.
    public static class KotKSocial
    {
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

        public static readonly Brush Online = Frozen(0x5B, 0xC4, 0x4F);
        public static readonly Brush Offline = Frozen(0x4A, 0x47, 0x42);
        public static readonly Brush Pending = Frozen(0xF2, 0xB2, 0x33);
        public static readonly Brush Gold = Frozen(0xF2, 0xB2, 0x33);
        public static readonly Brush Idle = Frozen(0x2B, 0x28, 0x23);

        private static readonly Dictionary<string, (string Version, ImageSource Image)> avatars = new();
        private static readonly Dictionary<string, KotKChatWindow> chats = new();
        private static bool refreshingAvatars;

        /// <summary>Raised after a newly downloaded profile picture is available.</summary>
        public static event Action AvatarsChanged;

        static KotKSocial()
        {
            // Open conversations follow the shared poll whichever window opened them.
            EditionKotK.StateChanged += () =>
            {
                foreach (var chat in chats.Values) chat.Refresh();
            };
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        // ---- rows -------------------------------------------------------------------------------
        public static List<PersonItem> Friends(LauncherState state) => state.Friends
            .OrderByDescending(p => p.Online).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PersonItem
            {
                Id = p.AccountId,
                Name = p.Name,
                Detail = (p.Online ? p.GameStatus : "Offline")
                    + (EditionKotK.Unread.TryGetValue(p.AccountId, out int unread) && unread > 0 ? $" · {unread} unread" : ""),
                Dot = p.Online ? Online : Offline,
                Avatar = AvatarOf(p.AccountId),
                Tag = p
            }).ToList();

        public static List<PersonItem> Invites(LauncherState state) => state.Invites.Select(i => new PersonItem
        {
            Id = i.Id,
            Name = i.FromName,
            Detail = IsFriendRequest(i) ? "Friend request" : "Party invitation",
            Dot = Pending,
            Avatar = AvatarOf(i.FromId),
            Tag = i
        }).ToList();

        public static List<PersonItem> PartyMembers(LauncherState state)
        {
            var lobby = state.Lobby;
            var members = lobby?.Members.ToList() ?? new List<LobbyMember> { new(state.Me.AccountId, state.Me.Name, false, true, state.Me.GameStatus) };
            return members.Select(m => new PersonItem
            {
                Id = m.AccountId,
                Name = m.Name + (lobby != null && m.AccountId == lobby.LeaderId ? "  (leader)" : ""),
                Detail = (m.Ready ? "Ready" : "Not ready") + "  ·  " + (m.Online ? m.GameStatus : "Offline"),
                Dot = m.Ready ? Online : m.Online ? Pending : Offline,
                Avatar = AvatarOf(m.AccountId),
                Tag = m
            }).ToList();
        }

        public static bool IsFriendRequest(SocialInvite invite) => invite.Kind.Equals("Friend", StringComparison.OrdinalIgnoreCase);

        public static bool IsReady(LauncherState state) =>
            state.Lobby?.Members.Any(m => m.AccountId == state.Me.AccountId && m.Ready) == true;

        public static bool IsLeader(LauncherState state) => state.Lobby == null || state.Lobby.LeaderId == state.Me.AccountId;

        // The leader queues once every member is ready and sits in the game menu.
        public static bool CanQueue(LauncherState state) => state.Lobby is { InGame: false } lobby && IsLeader(state)
            && lobby.Members.All(m => m.Ready && m.Online && m.GameStatus == "Menu");

        // Keeps the selection across a refresh of the list.
        public static void Fill(ListBox list, List<PersonItem> items)
        {
            string selected = (list.SelectedItem as PersonItem)?.Id;
            list.ItemsSource = items;
            list.SelectedItem = items.FirstOrDefault(i => i.Id == selected);
        }

        // Tab strip / segmented choice: gold for the active one.
        public static void MarkTab(Button tab, bool active)
        {
            if (active) { tab.BorderBrush = Gold; tab.Foreground = Gold; }
            else { tab.ClearValue(Control.BorderBrushProperty); tab.ClearValue(Control.ForegroundProperty); }
        }

        public static void MarkChoice(Button chip, bool active)
        {
            if (active) { chip.BorderBrush = Gold; chip.Foreground = Gold; }
            else { chip.ClearValue(Control.BorderBrushProperty); chip.ClearValue(Control.ForegroundProperty); }
        }

        // ---- avatars ----------------------------------------------------------------------------
        public static ImageSource AvatarOf(string accountId) =>
            accountId != null && avatars.TryGetValue(accountId, out var avatar) ? avatar.Image : null;

        // Pictures are fetched only when a person's avatar version changes.
        public static async Task RefreshAvatars(LauncherState state)
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
            if (changed) AvatarsChanged?.Invoke();
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

        // ---- actions ----------------------------------------------------------------------------
        // Runs a social action, then polls so every view shows the result. Returns the text for the
        // caller's status line: `done` on success, the error otherwise.
        public static async Task<string> Run(Func<Task> action, string done = null)
        {
            try
            {
                await action();
                await EditionKotK.Poll();
                return done;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static Task<string> AddFriend(string name, Action sent) =>
            Run(async () => { await EditionKotK.Post("api/friends", new TargetRequest(name)); sent(); }, $"Friend request sent to {name}.");

        public static Task<string> RemoveFriend(Person friend) =>
            Run(() => EditionKotK.Post("api/friends/remove", new TargetRequest(friend.AccountId)), $"{friend.Name} removed.");

        public static Task<string> InviteToParty(Person friend) =>
            Run(() => EditionKotK.Post("api/party/invite", new TargetRequest(friend.AccountId)), $"Party invitation sent to {friend.Name}.");

        public static Task<string> Respond(SocialInvite invite, bool accept) =>
            Run(() => EditionKotK.Post("api/invites/respond", new RespondRequest(invite.Id, accept)),
                !accept ? "Declined." : IsFriendRequest(invite) ? $"{invite.FromName} is now your friend." : $"You joined {invite.FromName}'s party.");

        public static Task<string> SetMode(string mode) =>
            Run(() => EditionKotK.Post("api/party/mode", new ModeRequest(mode)), "Party mode changed.");

        public static Task<string> ToggleReady(LauncherState state) =>
            Run(() => EditionKotK.Post("api/party/ready", new ReadyRequest(!IsReady(state))), "Party updated.");

        public static Task<string> Queue() =>
            Run(() => EditionKotK.Post("api/party/queue", new { }), "Queued. Return to the game to join when it is ready.");

        public static Task<string> Leave() =>
            Run(() => EditionKotK.Post("api/party/leave", new { }), "Party left.");

        // One message window per friend, owned by the launcher window so it outlives the hub.
        public static void OpenChat(Person friend)
        {
            if (chats.TryGetValue(friend.AccountId, out var open))
            {
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.Activate();
                return;
            }
            var chat = new KotKChatWindow(friend.AccountId, friend.Name) { Owner = LauncherWindow.launcherInstance };
            chats[friend.AccountId] = chat;
            chat.Closed += (_, _) => chats.Remove(friend.AccountId);
            chat.Show();
        }
    }
}
