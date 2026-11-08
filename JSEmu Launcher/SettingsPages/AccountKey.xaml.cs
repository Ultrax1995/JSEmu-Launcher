using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using H1Emu_Launcher.Classes;

namespace H1Emu_Launcher.SettingsPages
{
    public partial class AccountKey : Page
    {
        public static AccountKey accountKeyInstance;

        public AccountKey()
        {
            InitializeComponent();
            accountKeyInstance = this;

            // Adds the correct language file to the resource dictionary and then loads it
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(SetLanguageFile.LoadFile());
        }

        private void GenerateAccountKeyButtonClick(object sender, RoutedEventArgs e)
        {
            accountKeyBoxPassword.Password = AccountKeyUtil.GenerateNewAccountKey();
        }

        private void AccountKeyBoxTextTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(accountKeyBoxText.Text))
            {
                accountKeyBoxHint.Visibility = Visibility.Hidden;
                ToggleGenerateButtonVisibility(Visibility.Collapsed);
            }
            else
                ToggleGenerateButtonVisibility(Visibility.Visible);

            if (accountKeyBoxPassword.Password != accountKeyBoxText.Text)
                accountKeyBoxPassword.Password = accountKeyBoxText.Text;

            if (accountKeyBoxText.Visibility == Visibility.Visible)
            {
                Properties.Settings.Default.sessionIdKey = accountKeyBoxText.Text.Trim();
                Properties.Settings.Default.Save();
            }
        }

        private void AccountKeyBoxPasswordPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(accountKeyBoxPassword.Password))
            {
                accountKeyBoxHint.Visibility = Visibility.Hidden;
                ToggleGenerateButtonVisibility(Visibility.Collapsed);
            }
            else
                ToggleGenerateButtonVisibility(Visibility.Visible);

            if (accountKeyBoxText.Text != accountKeyBoxPassword.Password)
                accountKeyBoxText.Text = accountKeyBoxPassword.Password;

            if (accountKeyBoxPassword.Visibility == Visibility.Visible)
            {
                Properties.Settings.Default.sessionIdKey = accountKeyBoxPassword.Password.Trim();
                Properties.Settings.Default.Save();
            }
        }

        private void AccountKeyBoxGotFocus(object sender, RoutedEventArgs e)
        {
            accountKeyBoxHint.Visibility = Visibility.Hidden;
        }

        private void AccountKeyBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(accountKeyBoxPassword.Password) && string.IsNullOrEmpty(accountKeyBoxText.Text))
                accountKeyBoxHint.Visibility = Visibility.Visible;
        }

        private void DiscordInviteLinkCopy(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(Info.DISCORD_LINK);
        }

        private void DiscordInviteLinkClick(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Info.DISCORD_LINK,
                UseShellExecute = true
            });
        }

        private void AccountKeyLoaded(object sender, RoutedEventArgs e)
        {
            accountKeyBoxPassword.Password = Properties.Settings.Default.sessionIdKey;

            if (string.IsNullOrEmpty(Properties.Settings.Default.sessionIdKey))
                generateAccountKeyButtonRow.Visibility = Visibility.Visible;
            else
                generateAccountKeyButtonRow.Visibility = Visibility.Collapsed;

            DiscordLogin.Refreshed -= OnDiscordRefreshed;
            DiscordLogin.Refreshed += OnDiscordRefreshed;
            if (DiscordLogin.Last != null) ShowDiscord(DiscordLogin.Last);
            else if (DiscordLogin.IsLoggedIn) _ = RefreshDiscord();
            else ShowDiscord(new DiscordLogin.Result { LoggedOut = true });
        }

        // ---- 8.11.2026 Login with Discord ----

        private void OnDiscordRefreshed(DiscordLogin.Result r) => Dispatcher.Invoke(() => ShowDiscord(r));

        private async Task RefreshDiscord()
        {
            discordStatusText.Text = "Checking your Discord account...";
            await DiscordLogin.RefreshAsync();
        }

        private void ShowDiscord(DiscordLogin.Result r)
        {
            bool loggedIn = !r.LoggedOut && DiscordLogin.IsLoggedIn;
            discordLoginButton.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
            discordLogoutButton.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
            discordCheckButton.Visibility = loggedIn && !(r.Ok && r.HasRole && !string.IsNullOrEmpty(r.AuthKey)) ? Visibility.Visible : Visibility.Collapsed;
            string who = string.IsNullOrEmpty(r.Username) ? "" : $"Logged in as {r.Username}. ";

            if (r.Banned) discordStatusText.Text = "This account is banned.";
            else if (!loggedIn) discordStatusText.Text = r.Error ?? "Log in with Discord and your AuthKey is set up automatically.";
            else if (!r.Ok) discordStatusText.Text = who + (r.Error ?? "Something went wrong. Try CHECK AGAIN.");
            else if (!r.InGuild) discordStatusText.Text = who + "Join our Discord and verify there to get the Member role, then click CHECK AGAIN.";
            else if (!r.HasRole) discordStatusText.Text = who + "Verify on our Discord to get the Member role, then click CHECK AGAIN.";
            else if (r.KeyUnavailable) discordStatusText.Text = who + "Your AuthKey was registered earlier - keep the key you already have below.";
            else if (!string.IsNullOrEmpty(r.AuthKey))
            {
                discordStatusText.Text = who + (r.KeyCreated ? "Your AuthKey is registered and set - you are ready to play." : "Your AuthKey is set automatically - you are ready to play.");
                if (accountKeyBoxPassword.Password != r.AuthKey) accountKeyBoxPassword.Password = r.AuthKey;
            }
            else discordStatusText.Text = who;
        }

        private async void DiscordLoginClick(object sender, RoutedEventArgs e)
        {
            discordLoginButton.IsEnabled = false;
            try
            {
                if (await DiscordLoginWindow.LoginAsync(Window.GetWindow(this))) await RefreshDiscord();
                else discordStatusText.Text = "Discord login was not finished. Try again.";
            }
            finally { discordLoginButton.IsEnabled = true; }
        }

        private async void DiscordCheckClick(object sender, RoutedEventArgs e)
        {
            discordCheckButton.IsEnabled = false;
            try { await RefreshDiscord(); }
            finally { discordCheckButton.IsEnabled = true; }
        }

        private async void DiscordLogoutClick(object sender, RoutedEventArgs e) => await DiscordLogin.LogoutAsync();

        private void ToggleKeyVisibility(object sender, RoutedEventArgs e)
        {
            System.Windows.Shapes.Path toggleKeyVisibilityButtonPath = new()
            {
                Stretch = Stretch.Uniform
            };

            Binding bindingToggleKeyVisibilityButton = new("Foreground")
            {
                Source = toggleKeyVisibilityButton,
                Mode = BindingMode.OneWay
            };
            BindingOperations.SetBinding(toggleKeyVisibilityButtonPath, System.Windows.Shapes.Path.FillProperty, bindingToggleKeyVisibilityButton);

            if (accountKeyBoxPassword.Visibility == Visibility.Visible)
            {
                accountKeyBoxPassword.Visibility = Visibility.Hidden;
                accountKeyBoxText.Visibility = Visibility.Visible;
                toggleKeyVisibilityButtonPath.Data = (PathGeometry)FindResource("EyeHideIcon");
            }
            else
            {
                accountKeyBoxPassword.Visibility = Visibility.Visible;
                accountKeyBoxText.Visibility = Visibility.Hidden;
                toggleKeyVisibilityButtonPath.Data = (PathGeometry)FindResource("EyeIcon");
            }

            toggleKeyVisibilityButton.Content = toggleKeyVisibilityButtonPath;
        }

        public void ToggleGenerateButtonVisibility(Visibility visibility)
        {
            if (visibility == Visibility.Visible)
            {
                generateAccountKeyButtonRow.Visibility = Visibility.Visible;
                generateAccountKeyButtonRowContent.Measure(new Size(generateAccountKeyButtonRow.MaxWidth, generateAccountKeyButtonRow.MaxHeight));
                DoubleAnimation show = new(0, generateAccountKeyButtonRowContent.DesiredSize.Height, new Duration(TimeSpan.FromMilliseconds(150)))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut }
                };
                generateAccountKeyButtonRow.BeginAnimation(HeightProperty, show);
            }
            else
            {
                generateAccountKeyButtonRowContent.Measure(new Size(generateAccountKeyButtonRow.MaxWidth, generateAccountKeyButtonRow.MaxHeight));
                DoubleAnimation hide = new(generateAccountKeyButtonRowContent.DesiredSize.Height, 0, new Duration(TimeSpan.FromMilliseconds(150)))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn }
                };
                hide.Completed += (s, o) => generateAccountKeyButtonRow.Visibility = Visibility.Collapsed;
                generateAccountKeyButtonRow.BeginAnimation(HeightProperty, hide);
            }
        }
    }
}
