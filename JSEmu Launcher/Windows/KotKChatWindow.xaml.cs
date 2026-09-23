using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Cranberry.Launcher.Core;
using H1Emu_Launcher.Classes;

namespace H1Emu_Launcher
{
    public partial class KotKChatWindow : Window
    {
        public sealed record Line(string Author, Brush AuthorBrush, string When, string Text);

        private static readonly Brush Mine = new SolidColorBrush(Color.FromRgb(0x6F, 0xB2, 0xF5));
        private static readonly Brush Theirs = new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x3D));

        private readonly string friendId;
        private readonly string friendDisplayName;
        private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
        private bool polling, sending;
        private long lastId = -1, readThrough;
        private MessageRequest pending;

        public KotKChatWindow(string friendId, string friendDisplayName)
        {
            InitializeComponent();
            this.friendId = friendId;
            this.friendDisplayName = friendDisplayName;
            friendName.Text = friendDisplayName;
            Title = friendDisplayName + " - KOTK messages";
            timer.Tick += async (_, _) => await Poll();
        }

        private async void WindowLoaded(object sender, RoutedEventArgs e)
        {
            timer.Start();
            input.Focus();
            await Poll();
        }

        private async void WindowActivated(object sender, EventArgs e) => await Poll();

        private void WindowClosed(object sender, EventArgs e) => timer.Stop();

        public async void Refresh() => await Poll();

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void CloseClick(object sender, RoutedEventArgs e) => Close();

        private async void SendClick(object sender, RoutedEventArgs e) => await Send();

        private async void InputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await Send();
        }

        private async Task Send()
        {
            if (sending || string.IsNullOrWhiteSpace(input.Text)) return;
            sending = true;
            input.IsEnabled = false;
            string body = input.Text.Trim();
            // The client id makes a retried send idempotent on the server.
            if (pending?.Text != body) pending = new MessageRequest(friendId, body, Guid.NewGuid().ToString("N"));
            try
            {
                await EditionKotK.Post<DirectMessage>("api/messages", pending);
                input.Clear();
                pending = null;
                status.Text = "Message sent.";
                await Poll();
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally
            {
                sending = false;
                input.IsEnabled = true;
                input.Focus();
            }
        }

        private async Task Poll()
        {
            if (polling || !IsLoaded) return;
            polling = true;
            try
            {
                var conversation = await EditionKotK.Get<Conversation>("api/messages/" + Uri.EscapeDataString(friendId));
                string me = EditionKotK.Session?.AccountId;
                long last = conversation.Messages.LastOrDefault()?.Id ?? 0;
                if (last != lastId)
                {
                    bool atBottom = historyScroll.VerticalOffset >= historyScroll.ScrollableHeight - 4;
                    history.ItemsSource = conversation.Messages.Select(m => new Line(
                        m.From == me ? "You" : friendDisplayName, m.From == me ? Mine : Theirs,
                        m.SentAt.ToLocalTime().ToString("g"), m.Text)).ToList();
                    if (atBottom || lastId < 0) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => historyScroll.ScrollToEnd());
                    lastId = last;
                }
                long incoming = conversation.Messages.Where(m => m.To == me).Select(m => m.Id).DefaultIfEmpty().Max();
                if (IsActive && incoming > readThrough)
                {
                    await EditionKotK.Post("api/messages/read", new ReadMessagesRequest(friendId, incoming));
                    readThrough = incoming;
                }
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { polling = false; }
        }
    }
}
