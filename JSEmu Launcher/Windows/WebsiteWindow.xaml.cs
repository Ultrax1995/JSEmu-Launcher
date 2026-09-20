using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using H1Emu_Launcher.Classes;
using Microsoft.Web.WebView2.Core;

namespace H1Emu_Launcher
{
    // Hosts jsemu.eu inside the launcher (Gambling, Scrap Yard, Store, ...).
    // Login is the website's normal Discord login; the session is kept in the
    // WebView2 profile under the launcher's AppData folder.
    public partial class WebsiteWindow : Window
    {
        private static WebsiteWindow? instance;

        private readonly string startPath;

        public static void ShowHub(Window owner, string path)
        {
            if (instance != null)
            {
                instance.NavigateTo(path);
                instance.Activate();
                return;
            }

            instance = new WebsiteWindow(path) { Owner = owner };
            instance.Show();
        }

        private WebsiteWindow(string path)
        {
            InitializeComponent();
            startPath = path;
        }

        private static bool IsAllowedHost(Uri uri)
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
                return false;

            string host = uri.Host.ToLowerInvariant();
            return host == "jsemu.eu" || host.EndsWith(".jsemu.eu") ||
                   host == "discord.com" || host.EndsWith(".discord.com") ||
                   host == "discordapp.com" || host.EndsWith(".discordapp.com");
        }

        private static void OpenExternal(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // No default browser - nothing sensible to do.
            }
        }

        private async void WindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string dataFolder = Path.Combine(Info.APPLICATION_DATA_PATH, "JSEmu Launcher", "WebView2");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
                await web.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException || ex is System.Runtime.InteropServices.COMException)
            {
                MessageBox.Show(
                    "The Microsoft Edge WebView2 Runtime is not installed, so the website cannot be shown inside the launcher.\n\nOpening it in your browser instead.",
                    "JSEmu Hub", MessageBoxButton.OK, MessageBoxImage.Information);
                OpenExternal(DressingRoomApi.SiteRoot + startPath);
                Close();
                return;
            }

            CoreWebView2 core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            core.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? target) || !IsAllowedHost(target))
                {
                    args.Cancel = true;
                    if (target != null && (target.Scheme == Uri.UriSchemeHttps || target.Scheme == Uri.UriSchemeHttp))
                        OpenExternal(args.Uri);
                }
            };

            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? target) && IsAllowedHost(target))
                    core.Navigate(args.Uri);
                else
                    OpenExternal(args.Uri);
            };

            core.NavigationCompleted += (_, _) =>
            {
                loadingText.Visibility = Visibility.Collapsed;
                backButton.IsEnabled = core.CanGoBack;
            };

            core.SourceChanged += (_, _) => statusText.Text = core.Source;

            core.Navigate(DressingRoomApi.SiteRoot + startPath);
        }

        private void NavigateTo(string path)
        {
            if (web.CoreWebView2 != null)
                web.CoreWebView2.Navigate(DressingRoomApi.SiteRoot + path);
        }

        private void NavClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string path })
                NavigateTo(path);
        }

        private void BackClick(object sender, RoutedEventArgs e)
        {
            if (web.CoreWebView2?.CanGoBack == true)
                web.CoreWebView2.GoBack();
        }

        private void ReloadClick(object sender, RoutedEventArgs e) => web.CoreWebView2?.Reload();

        private void OpenBrowserClick(object sender, RoutedEventArgs e)
        {
            OpenExternal(web.CoreWebView2?.Source ?? DressingRoomApi.SiteRoot);
        }

        private void CloseClick(object sender, RoutedEventArgs e) => Close();

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void WindowClosed(object? sender, EventArgs e)
        {
            web.Dispose();
            instance = null;
        }
    }
}
