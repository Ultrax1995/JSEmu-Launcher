using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H1Emu_Launcher.Classes;

namespace H1Emu_Launcher
{
    // Discord login (8.11.2026): the login runs in the player's DEFAULT BROWSER (user: there they are usually logged in
    // to Discord already). jsemu.eu/api/launcher/discord/start sends them to Discord and back to "You are logged in";
    // meanwhile this small window polls for the launcher's session token with the nonce only it knows, then closes.
    public sealed class DiscordLoginWindow : Window
    {
        private readonly string nonce = DiscordLogin.NewNonce();
        private readonly CancellationTokenSource cts = new();
        private bool loggedIn;

        private DiscordLoginWindow()
        {
            Title = "Login with Discord - JSEmu";
            Width = 440;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x15, 0x22));

            StackPanel panel = new() { Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock
            {
                Text = "Finish the Discord login in your browser.",
                Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = "This window closes by itself when you are logged in.",
                Foreground = new SolidColorBrush(Color.FromRgb(0xAE, 0xB4, 0xD7)), FontSize = 13, Margin = new Thickness(0, 8, 0, 16),
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
            });
            StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            Button reopen = new() { Content = "OPEN AGAIN", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(5, 0, 5, 0) };
            reopen.Click += (_, _) => OpenBrowser();
            Button cancel = new() { Content = "CANCEL", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(5, 0, 5, 0) };
            cancel.Click += (_, _) => Close();
            if (Application.Current.TryFindResource("PrimaryButton") is Style style) { reopen.Style = style; cancel.Style = style; }
            buttons.Children.Add(reopen);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;

            Loaded += (_, _) => { OpenBrowser(); _ = PollLoop(); };
            Closed += (_, _) => cts.Cancel();
        }

        /// <summary>Shows the login; true when the launcher got its session token.</summary>
        public static async Task<bool> LoginAsync(Window? owner)
        {
            DiscordLoginWindow w = new() { Owner = owner };
            w.ShowDialog();
            if (w.loggedIn) return true;
            // a last look: the login may have finished just as the window was closed
            return await DiscordLogin.PollAsync(w.nonce, TimeSpan.FromSeconds(1), CancellationToken.None);
        }

        private void OpenBrowser()
        {
            try { Process.Start(new ProcessStartInfo(DiscordLogin.StartUrl(nonce)) { UseShellExecute = true }); }
            catch { MessageBox.Show(this, "Could not open your browser.", "JSEmu", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private async Task PollLoop()
        {
            if (await DiscordLogin.PollAsync(nonce, TimeSpan.FromMinutes(10), cts.Token))
            {
                loggedIn = true;
                Dispatcher.Invoke(() => { if (IsLoaded) { Activate(); Close(); } });
            }
        }
    }
}
