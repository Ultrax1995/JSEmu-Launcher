using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using H1Emu_Launcher.Classes;

namespace H1Emu_Launcher
{
    public partial class SplashWindow : Window
    {
        public static SplashWindow splashInstance;
        public static HttpClient httpClient = new();

        private static Version latestVersion = new(0, 0, 0, 0);
        private static Version localVersion = new(0, 0, 0, 0);

        // Enabled by default. App.xaml.cs can still disable it with -skipupdatecheck.
        public static bool checkForUpdates = true;

        public SplashWindow()
        {
            InitializeComponent();
            splashInstance = this;

            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(SetLanguageFile.LoadFile());

            if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("JSEmu-Launcher-Updater/1.0");

            if (!httpClient.DefaultRequestHeaders.Accept.Any())
                httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        private async void SplashScreenWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (checkForUpdates)
                await CheckVersion(this);
            else
                Close();
        }

        public static Version NormalizeVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new Version(0, 0, 0, 0);

            value = value.Trim().TrimStart('v', 'V');

            if (!Version.TryParse(value, out Version parsed))
                throw new FormatException($"Invalid launcher version: {value}");

            return new Version(
                Math.Max(parsed.Major, 0),
                Math.Max(parsed.Minor, 0),
                Math.Max(parsed.Build, 0),
                Math.Max(parsed.Revision, 0)
            );
        }

        public static async Task<bool> CheckVersion(Window owner)
        {
            try
            {
                if (owner is LauncherWindow)
                {
                    LauncherWindow.launcherInstance.playButton.SetResourceReference(ContentProperty, "item214");
                    LauncherWindow.launcherInstance.taskbarIcon.ProgressState =
                        System.Windows.Shell.TaskbarItemProgressState.Indeterminate;
                }
                else if (splashInstance != null)
                {
                    splashInstance.taskbarIcon.ProgressState =
                        System.Windows.Shell.TaskbarItemProgressState.Indeterminate;
                }

                using HttpResponseMessage response = await httpClient.GetAsync(
                    Info.LAUNCHER_JSON_API,
                    HttpCompletionOption.ResponseHeadersRead
                );

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    string reason = response.ReasonPhrase ?? response.StatusCode.ToString();
                    throw new Exception(reason);
                }

                string jsonLauncher = await response.Content.ReadAsStringAsync();

                JsonEndPoints.H1EmuLauncherJson.Root jsonLauncherDes =
                    JsonSerializer.Deserialize<JsonEndPoints.H1EmuLauncherJson.Root>(jsonLauncher);

                if (jsonLauncherDes == null || string.IsNullOrWhiteSpace(jsonLauncherDes.tag_name))
                    throw new Exception("GitHub returned invalid launcher release information.");

                latestVersion = NormalizeVersion(jsonLauncherDes.tag_name);

                Version assemblyVersion =
                    Assembly.GetExecutingAssembly().GetName().Version ??
                    new Version(0, 0, 0, 0);

                localVersion = NormalizeVersion(assemblyVersion.ToString());

                var launcherAsset = jsonLauncherDes.assets?.FirstOrDefault(asset =>
                {
                    if (string.IsNullOrWhiteSpace(asset.name))
                        return false;

                    string normalized = asset.name
                        .Replace(" ", "")
                        .Replace(".", "")
                        .Replace("-", "")
                        .Replace("_", "")
                        .ToLowerInvariant();

                    return normalized == "jsemulauncherexe";
                });

                if (launcherAsset == null ||
                    string.IsNullOrWhiteSpace(launcherAsset.browser_download_url))
                {
                    throw new Exception(
                        "The latest GitHub release does not contain JSEmu Launcher.exe."
                    );
                }

                UpdateWindow.installerDownloadURL = launcherAsset.browser_download_url;
                UpdateWindow.installerFileName = launcherAsset.name;
                UpdateWindow.expectedVersion = latestVersion;

                if (owner is LauncherWindow)
                {
                    if (!Properties.Settings.Default.developerMode)
                        LauncherWindow.launcherInstance.playButton.SetResourceReference(ContentProperty, "item217");
                    else
                        LauncherWindow.launcherInstance.playButton.SetResourceReference(ContentProperty, "item8");

                    LauncherWindow.launcherInstance.taskbarIcon.ProgressState =
                        System.Windows.Shell.TaskbarItemProgressState.None;
                }
                else if (splashInstance != null)
                {
                    splashInstance.taskbarIcon.ProgressState =
                        System.Windows.Shell.TaskbarItemProgressState.None;
                }

                if (localVersion < latestVersion)
                {
                    owner.Hide();
                    UpdateWindow uw = new();
                    uw.ShowDialog();
                }

                if (owner is SplashWindow)
                    owner.Close();
            }
            catch (AggregateException e)
            {
                string exceptionList = string.Empty;

                foreach (Exception exception in e.InnerExceptions)
                    exceptionList += $"\n\n{exception.GetType().Name}: {exception.Message}";

                if (e.InnerException is HttpRequestException ex && ex.StatusCode == null)
                    exceptionList += $"\n\n{owner.FindResource("item137")}";

                if (owner is SplashWindow)
                    owner.Hide();
                else if (owner is LauncherWindow)
                {
                    if (LauncherWindow.launcherInstance.serverSelector.SelectedIndex != 1)
                    {
                        LauncherWindow.launcherInstance.playButton.IsEnabled = true;
                        LauncherWindow.launcherInstance.playButton.SetResourceReference(ContentProperty, "item8");
                        LauncherWindow.launcherInstance.taskbarIcon.ProgressState =
                            System.Windows.Shell.TaskbarItemProgressState.None;
                    }
                }

                CustomMessageBox.Show(
                    $"{owner.FindResource("item66")} {owner.FindResource("item16")}{exceptionList}\n\n{owner.FindResource("item49")}",
                    owner
                );

                if (owner is SplashWindow)
                {
                    if (splashInstance != null)
                        splashInstance.taskbarIcon.ProgressState =
                            System.Windows.Shell.TaskbarItemProgressState.None;

                    owner.Close();
                }

                return false;
            }
            catch (Exception ex)
            {
                if (owner is SplashWindow)
                    owner.Hide();
                else if (owner is LauncherWindow)
                {
                    if (LauncherWindow.launcherInstance.serverSelector.SelectedIndex != 1)
                    {
                        LauncherWindow.launcherInstance.playButton.IsEnabled = true;
                        LauncherWindow.launcherInstance.playButton.SetResourceReference(ContentProperty, "item8");
                        LauncherWindow.launcherInstance.taskbarIcon.ProgressState =
                            System.Windows.Shell.TaskbarItemProgressState.None;
                    }
                }

                CustomMessageBox.Show(
                    $"{owner.FindResource("item66")} \"{ex.Message}\"\n\n{owner.FindResource("item49")}",
                    owner
                );

                if (owner is SplashWindow)
                {
                    if (splashInstance != null)
                        splashInstance.taskbarIcon.ProgressState =
                            System.Windows.Shell.TaskbarItemProgressState.None;

                    owner.Close();
                }

                return false;
            }

            return true;
        }

        private void MoveSplashScreenWindow(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void SplashScreenWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Hide();

            if (localVersion < latestVersion)
            {
                UpdateWindow uw = new();
                uw.Show();
            }
            else
            {
                if (Properties.Settings.Default.firstTimeUse ||
                    Properties.Settings.Default.agreedToTOSIteration < Info.TOS_ITERATION)
                {
                    DisclaimerWindow dc = new();

                    if (!Properties.Settings.Default.firstTimeUse &&
                        Properties.Settings.Default.agreedToTOSIteration < Info.TOS_ITERATION)
                    {
                        dc.welcomeMessage.Visibility = Visibility.Collapsed;
                        dc.TOSHeader.Text = FindResource("item5").ToString();
                    }

                    dc.ShowDialog();
                }

                LauncherWindow lw = new();
                lw.Show();
            }

            splashInstance = null;
        }
    }
}
