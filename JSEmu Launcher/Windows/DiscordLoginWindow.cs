using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using H1Emu_Launcher.Classes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace H1Emu_Launcher
{
    // Discord login inside the launcher (8.11.2026): WebView2 on jsemu.eu/api/launcher/discord/start, the website sends
    // the player to Discord and back; meanwhile the launcher polls for its session token with the nonce only it knows.
    // Same WebView2 profile as WebsiteWindow, so Discord remembers the player next time.
    // Without the WebView2 runtime the login runs in the default browser instead.
    public sealed class DiscordLoginWindow : Window
    {
        private readonly WebView2 web = new();
        private readonly string nonce = DiscordLogin.NewNonce();
        private readonly CancellationTokenSource cts = new();
        private bool loggedIn;

        private DiscordLoginWindow()
        {
            Title = "Login with Discord - JSEmu";
            Width = 520;
            Height = 760;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x15, 0x22));
            Content = web;
            Loaded += WindowLoaded;
            Closed += (_, _) => { cts.Cancel(); web.Dispose(); };
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

        private async void WindowLoaded(object sender, RoutedEventArgs e)
        {
            _ = PollLoop();
            try
            {
                string dataFolder = Path.Combine(Info.APPLICATION_DATA_PATH, "JSEmu Launcher", "WebView2");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
                await web.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException || ex is System.Runtime.InteropServices.COMException)
            {
                // no WebView2: finish the login in the browser, this window waits (polls) and closes itself
                try { Process.Start(new ProcessStartInfo(DiscordLogin.StartUrl(nonce)) { UseShellExecute = true }); } catch { }
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = "Finish the Discord login in your browser.\nThis window closes by itself.",
                    Foreground = Brushes.White, FontSize = 16, TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
                };
                return;
            }
            CoreWebView2 core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, args) => { args.Handled = true; core.Navigate(args.Uri); };
            core.SourceChanged += async (_, _) =>
            {
                // the website's "you are logged in" page -> pick the token up right away
                if (Uri.TryCreate(core.Source, UriKind.Absolute, out Uri? u) && u.Host.EndsWith("jsemu.eu", StringComparison.OrdinalIgnoreCase)
                    && u.AbsolutePath.StartsWith("/launcher/login-done", StringComparison.OrdinalIgnoreCase))
                {
                    if (await DiscordLogin.PollAsync(nonce, TimeSpan.FromSeconds(6), cts.Token)) Finish();
                }
            };
            core.Navigate(DiscordLogin.StartUrl(nonce));
        }

        private async Task PollLoop()
        {
            if (await DiscordLogin.PollAsync(nonce, TimeSpan.FromMinutes(10), cts.Token)) Finish();
        }

        private void Finish()
        {
            if (loggedIn) return;
            loggedIn = true;
            Dispatcher.Invoke(() => { if (IsLoaded) Close(); });
        }
    }
}
