using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using H1Emu_Launcher.Classes;

namespace H1Emu_Launcher
{
    public partial class HubWindow : Window
    {
        private static readonly SolidColorBrush GoldBrush = new(Color.FromRgb(0xF2, 0xBC, 0x55));
        private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(0xE1, 0x1D, 0x2A));
        private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0x45, 0xD3, 0x8A));
        private static readonly SolidColorBrush OrangeBrush = new(Color.FromRgb(0xFF, 0x8C, 0x1A));
        private static readonly SolidColorBrush EdgeBrush = new(Color.FromRgb(0x2C, 0x35, 0x40));
        private static readonly SolidColorBrush PanelBrush = new(Color.FromRgb(0x15, 0x1A, 0x20));
        private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(0x8A, 0x96, 0xA3));

        private static readonly Dictionary<string, BitmapImage> ImageCache = new();

        private string activeTab = "battlepass";
        private long finalMissedDaysAllowed;
        private bool busy;

        public HubWindow(string startTab = "battlepass")
        {
            InitializeComponent();
            activeTab = startTab;
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseClick(object sender, RoutedEventArgs e) => Close();

        private void GamblingClick(object sender, RoutedEventArgs e) => OpenInBrowser("gambling");

        private void DonateClick(object sender, RoutedEventArgs e) => OpenInBrowser("donate");

        private static void OpenInBrowser(string path) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://jsemu.eu/{path}") { UseShellExecute = true });

        private async void WindowLoaded(object sender, RoutedEventArgs e)
        {
#if DEBUG
            // UI preview only: open on a given tab / store category
            if (Environment.GetEnvironmentVariable("JSEMU_UI_PREVIEW") == "1")
            {
                activeTab = Environment.GetEnvironmentVariable("JSEMU_UI_HUB_TAB") ?? activeTab;
                storeCategory = Environment.GetEnvironmentVariable("JSEMU_UI_STORE_CATEGORY") ?? storeCategory;
            }
#endif
            await ShowTab(activeTab);
#if DEBUG
            // UI preview only: JSEMU_UI_SNAPSHOT=<png> saves this window once the icons have loaded and closes the preview
            // (the launcher runs elevated, so a screenshot tool from outside cannot capture it)
            string? snapshot = Environment.GetEnvironmentVariable("JSEMU_UI_SNAPSHOT");
            if (Environment.GetEnvironmentVariable("JSEMU_UI_PREVIEW") == "1" && !string.IsNullOrEmpty(snapshot))
            {
                await System.Threading.Tasks.Task.Delay(6000);
                void Save(string path)
                {
                    UpdateLayout();
                    FrameworkElement root = (FrameworkElement)Content;
                    RenderTargetBitmap bitmap = new((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using System.IO.FileStream file = System.IO.File.Create(path);
                    encoder.Save(file);
                }
                Save(snapshot);
                // and once more scrolled to the bottom of the page
                DependencyObject? parent = content;
                while (parent != null && parent is not ScrollViewer)
                    parent = VisualTreeHelper.GetParent(parent);
                if (parent is ScrollViewer scroller)
                {
                    scroller.ScrollToBottom();
                    await System.Threading.Tasks.Task.Delay(500);
                    Save(System.IO.Path.ChangeExtension(snapshot, null) + "-bottom.png");
                }
                Application.Current.Shutdown();
            }
#endif
        }

        private async void TabClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string tab })
                await ShowTab(tab);
        }

        // ---------- helpers ----------

        private static BitmapImage? LoadImage(string? path, int width = 72)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            string key = $"{width}:{path}";
            if (ImageCache.TryGetValue(key, out BitmapImage? cached))
                return cached;
            try
            {
                BitmapImage image = new();
                image.BeginInit();
                image.UriSource = new Uri(DressingRoomApi.SiteRoot + path, UriKind.Absolute);
                image.DecodePixelWidth = width;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                ImageCache[key] = image;
                return image;
            }
            catch
            {
                return null;
            }
        }

        private Style ButtonStyle => (Style)FindResource("HubButton");

        private static string Fmt(long n) => n.ToString("N0", CultureInfo.GetCultureInfo("en-US"));

        private TextBlock Label(string text, double size = 14, Brush? brush = null, FontWeight? weight = null, Thickness? margin = null)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = brush ?? Brushes.White,
                FontWeight = weight ?? FontWeights.Normal,
                Margin = margin ?? new Thickness(0),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private void SetCoins(long coins) => coinsText.Text = Fmt(coins);

        private async System.Threading.Tasks.Task RefreshCoins()
        {
            long? coins = await HubApi.GetCoinsAsync();
            if (coins.HasValue)
                SetCoins(coins.Value);
        }

        private void HighlightTab()
        {
            foreach (Button tab in new[] { tabBattlepass, tabStore, tabBackpack, tabScrap, tabWipe })
            {
                bool active = (string)tab.Tag == activeTab;
                tab.Background = active ? (Brush)FindResource("Accent.SoftBrush") : Brushes.Transparent;
                if (active) tab.Foreground = Brushes.White; else tab.ClearValue(ForegroundProperty);
            }
        }

        private TextBlock messageBox = new();

        private bool Confirm(string question) =>
            CustomMessageBox.Show(question, this, okButtonVisibility: false, yesButtonVisibility: true, noButtonVisibility: true) == MessageBoxResult.Yes;

        private void ShowMessage(string text, bool ok)
        {
            messageBox.Text = text;
            messageBox.Foreground = ok ? GreenBrush : RedBrush;
            messageBox.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BeginView(string title, string subtitle)
        {
            content.Children.Clear();
            content.Children.Add(Label(title, 24, Brushes.White, FontWeights.Bold));
            content.Children.Add(Label(subtitle, 13, MutedBrush, null, new Thickness(0, 2, 0, 14)));
            messageBox = Label("", 13, GreenBrush, FontWeights.SemiBold, new Thickness(0, 0, 0, 12));
            messageBox.Visibility = Visibility.Collapsed;
            content.Children.Add(messageBox);
        }

        private async System.Threading.Tasks.Task ShowTab(string tab)
        {
            activeTab = tab;
            HighlightTab();
            HideCratePreview();
            content.Children.Clear();
            loadingText.Text = "Loading...";
            loadingText.Visibility = Visibility.Visible;

            switch (tab)
            {
                case "battlepass": await LoadBattlepass(); break;
                case "store": await LoadStore(); break;
                case "backpack": await LoadBackpack(); break;
                case "scrap": await LoadScrap(); break;
                case "wipe": await LoadWipe(); break;
            }
        }

        private bool FailedView(JsonElement data)
        {
            if (HubApi.Ok(data))
            {
                loadingText.Visibility = Visibility.Collapsed;
                return false;
            }

            loadingText.Text = HubApi.Error(data, "Could not load this page.");
            loadingText.Visibility = Visibility.Visible;
            return true;
        }

        // ---------- Battle Pass ----------

        private async System.Threading.Tasks.Task LoadBattlepass()
        {
            JsonElement data = await HubApi.CallAsync("battlepass");
            if (FailedView(data))
                return;

            if (data.TryGetProperty("coins", out JsonElement coinsEl) && coinsEl.ValueKind == JsonValueKind.Number)
                SetCoins(coinsEl.GetInt64());

            string season = data.TryGetProperty("season", out JsonElement s) ? HubApi.Str(s, "name", "Battle Pass") : "Battle Pass";
            long today = HubApi.Num(data, "todayDay");
            long total = data.TryGetProperty("season", out JsonElement s2) ? HubApi.Num(s2, "totalDays", 30) : 30;
            string startKey = data.TryGetProperty("season", out JsonElement s3) ? HubApi.Str(s3, "startDateKey") : "";
            finalMissedDaysAllowed = data.TryGetProperty("season", out JsonElement s4) ? HubApi.Num(s4, "finalMissedDaysAllowed", 0) : 0;
            bool seasonActive = HubApi.Bool(data, "seasonActive");
            bool canClaim = HubApi.Bool(data, "canClaim");
            bool claimedToday = HubApi.Bool(data, "claimedToday");
            bool isDonator = HubApi.Bool(data, "isDonator");
            long online = HubApi.Num(data, "onlineSecondsToday");
            long required = Math.Max(1, HubApi.Num(data, "minimumOnlineSeconds", 7200));

            HashSet<long> claimed = new();
            if (data.TryGetProperty("claimedDays", out JsonElement cd) && cd.ValueKind == JsonValueKind.Array)
                foreach (JsonElement d in cd.EnumerateArray())
                    claimed.Add(d.GetInt64());

            List<JsonElement> rewardList = new();
            if (data.TryGetProperty("rewards", out JsonElement rewards) && rewards.ValueKind == JsonValueKind.Array)
                rewardList.AddRange(rewards.EnumerateArray());
            long finalDay = rewardList.Count > 0 ? rewardList.Max(r => HubApi.Num(r, "day")) : total;

            // Season dates are Warsaw calendar days (the website's battlepass timezone)
            DateTime now = WarsawNow();
            DateTime? start = DateTime.TryParseExact(startKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed) ? parsed : null;
            bool upcoming = start.HasValue && now < start.Value;
            string status = upcoming ? $"STARTS IN {Span(start!.Value - now)}"
                : seasonActive && start.HasValue ? $"DAY {today} / {total}   •   ENDS IN {Span(start.Value.AddDays(total) - now)}"
                : seasonActive ? $"DAY {today} / {total}" : "SEASON ENDED";
            Brush statusBrush = upcoming ? new SolidColorBrush(Color.FromRgb(0x2F, 0x6B, 0xE0)) : seasonActive ? RedBrush : EdgeBrush;
            // English month names whatever the Windows language is, like the rest of the Hub
            CultureInfo en = CultureInfo.GetCultureInfo("en-US");
            string dates = start.HasValue ? $"{start.Value.ToString("d MMM", en)} - {start.Value.AddDays(total - 1).ToString("d MMM yyyy", en)}" : "";

            BeginView("", "");
            content.Children.Clear();
            content.Children.Add(CreateSeasonHeader(season, status, statusBrush, dates, claimed.Count, total, rewardList.FirstOrDefault(r => HubApi.Num(r, "day") == finalDay)));
            content.Children.Add(messageBox);

            // progress + claim
            Border card = new()
            {
                Background = new LinearGradientBrush(Color.FromRgb(0x1A, 0x20, 0x28), Color.FromRgb(0x13, 0x17, 0x1D), 0),
                BorderBrush = canClaim ? OrangeBrush : EdgeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid grid = new();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new();
            long percent = Math.Min(100, online * 100 / required);
            left.Children.Add(Label($"ONLINE TIME TODAY: {HubApi.Str(data, "onlineTimeFormatted", "0h 0m")} / {HubApi.Str(data, "requiredTimeFormatted", "2h 0m")}", 12, MutedBrush, FontWeights.Bold));
            Grid bar = Bar(percent, percent >= 100 ? new LinearGradientBrush(Color.FromRgb(0x2E, 0xB8, 0x6E), GreenBrush.Color, 0) : new LinearGradientBrush(RedBrush.Color, OrangeBrush.Color, 0), 10);
            bar.Margin = new Thickness(0, 8, 0, 10);
            left.Children.Add(bar);
            string todayText = "No reward today";
            if (data.TryGetProperty("todaysReward", out JsonElement tr) && tr.ValueKind == JsonValueKind.Object)
                todayText = RewardText(tr);
            else if (upcoming && rewardList.Count > 0)
                todayText = $"{RewardText(rewardList[0])} on day 1";
            left.Children.Add(Label(upcoming ? $"First reward: {todayText}" : $"Today's reward: {todayText}", 15, GoldBrush, FontWeights.SemiBold));
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            Button claim = new()
            {
                Content = claimedToday ? "Already claimed" : canClaim ? "Claim reward" : "Play to unlock",
                Height = 46,
                MinWidth = 190,
                Margin = new Thickness(18, 0, 0, 0),
                Style = ButtonStyle,
                IsEnabled = canClaim,
                Background = canClaim ? RedBrush : PanelBrush,
                BorderBrush = canClaim ? new SolidColorBrush(Color.FromRgb(0xF0, 0x2A, 0x36)) : EdgeBrush
            };
            claim.Click += async (_, _) => await ClaimBattlepass();
            Grid.SetColumn(claim, 1);
            grid.Children.Add(claim);
            card.Child = grid;
            content.Children.Add(card);

            if (!canClaim && !claimedToday && seasonActive)
                content.Children.Add(Label("You must be online on the game server and reach the daily online time, then press Claim while your character is connected.", 12, MutedBrush, null, new Thickness(0, -6, 0, 14)));

            // day tiles
            WrapPanel wrap = new();
            foreach (JsonElement reward in rewardList)
                wrap.Children.Add(CreateDayTile(reward, today, claimed, isDonator, HubApi.Num(reward, "day") == finalDay));
            content.Children.Add(wrap);
        }

        private static DateTime WarsawNow()
        {
            try { return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Central European Standard Time"); }
            catch { return DateTime.UtcNow.AddHours(1); }
        }

        private static string Span(TimeSpan t) =>
            t.TotalDays >= 1 ? $"{(int)t.TotalDays}D {t.Hours}H" : t.TotalHours >= 1 ? $"{t.Hours}H {t.Minutes}M" : $"{Math.Max(0, t.Minutes)}M";

        // Rounded progress bar; the default ProgressBar template draws square ends
        private static Grid Bar(double percent, Brush fill, double height = 8)
        {
            percent = Math.Clamp(percent, 0, 100);
            Grid g = new() { Height = height };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(percent, 0.001), GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - percent, 0.001), GridUnitType.Star) });
            Border track = new() { Background = new SolidColorBrush(Color.FromRgb(0x22, 0x29, 0x33)), CornerRadius = new CornerRadius(height / 2) };
            Grid.SetColumnSpan(track, 2);
            g.Children.Add(track);
            if (percent > 0)
                g.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(height / 2) });
            return g;
        }

        private static Border Chip(string text, Brush foreground, Brush background) => new()
        {
            Background = background,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(9, 3, 9, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = foreground }
        };

        private Border CreateSeasonHeader(string season, string status, Brush statusBrush, string dates, int claimedCount, long total, JsonElement finalReward)
        {
            Grid grid = new();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new() { VerticalAlignment = VerticalAlignment.Center };
            StackPanel chips = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            chips.Children.Add(Chip(status, Brushes.White, statusBrush));
            if (!string.IsNullOrEmpty(dates))
            {
                Border d = Chip(dates.ToUpperInvariant(), MutedBrush, new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00)));
                d.Margin = new Thickness(8, 0, 0, 0);
                chips.Children.Add(d);
            }
            left.Children.Add(chips);
            left.Children.Add(Label(season, 26, Brushes.White, FontWeights.Bold));
            left.Children.Add(Label($"SEASON PROGRESS  {claimedCount} / {total} CLAIMED", 11, MutedBrush, FontWeights.Bold, new Thickness(0, 10, 0, 6)));
            Grid progress = Bar(total > 0 ? claimedCount * 100.0 / total : 0, new LinearGradientBrush(GoldBrush.Color, OrangeBrush.Color, 0), 8);
            progress.MaxWidth = 420;
            progress.HorizontalAlignment = HorizontalAlignment.Left;
            progress.Width = 420;
            left.Children.Add(progress);
            grid.Children.Add(left);

            if (finalReward.ValueKind == JsonValueKind.Object)
            {
                StackPanel right = new() { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = 230 };
                TextBlock tag = Label("★ FINAL REWARD ★", 11, GoldBrush, FontWeights.Bold);
                tag.HorizontalAlignment = HorizontalAlignment.Center;
                right.Children.Add(tag);
                BitmapImage? img = LoadImage(HubApi.Str(finalReward, "icon"), 256);
                if (img != null)
                    right.Children.Add(new Image
                    {
                        Source = img,
                        Height = 84,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 2, 0, 0),
                        Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x4A, 0x9B, 0xFF), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.75 }
                    });
                TextBlock fname = Label(HubApi.Str(finalReward, "name", "Final reward"), 13, Brushes.White, FontWeights.SemiBold);
                fname.HorizontalAlignment = HorizontalAlignment.Center;
                fname.TextAlignment = TextAlignment.Center;
                right.Children.Add(fname);
                Grid.SetColumn(right, 1);
                grid.Children.Add(right);
            }

            return new Border
            {
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0x4A, 0x0E, 0x14), 0),
                    new GradientStop(Color.FromRgb(0x1E, 0x12, 0x20), 0.55),
                    new GradientStop(Color.FromRgb(0x10, 0x1A, 0x2C), 1)
                }, 0),
                BorderBrush = new LinearGradientBrush(Color.FromArgb(0xAA, 0xE1, 0x1D, 0x2A), Color.FromArgb(0x66, 0x4A, 0x9B, 0xFF), 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(22, 18, 22, 18),
                Margin = new Thickness(0, 0, 0, 14),
                Child = grid
            };
        }

        private static string RewardText(JsonElement reward)
        {
            string type = HubApi.Str(reward, "type");
            long amount = HubApi.Num(reward, "amount", 1);
            return type == "coins" ? $"{Fmt(amount)} Coins" : $"{amount}x {HubApi.Str(reward, "name", "Reward")}";
        }

        private Border CreateDayTile(JsonElement reward, long today, HashSet<long> claimed, bool isDonator, bool isFinal = false)
        {
            long day = HubApi.Num(reward, "day");
            string state = claimed.Contains(day) ? "claimed" : day < today ? "missed" : day == today ? "current" : "future";
            bool coins = HubApi.Str(reward, "type") == "coins";
            // Tint by reward kind: gold for coins and the final reward, blue for crates
            Color accent = isFinal || coins ? GoldBrush.Color : Color.FromRgb(0x4A, 0x9B, 0xFF);
            Brush edge = state switch
            {
                "claimed" => GreenBrush,
                "missed" => new SolidColorBrush(Color.FromRgb(0x8A, 0x3A, 0x3A)),
                "current" => OrangeBrush,
                _ => isFinal ? GoldBrush : EdgeBrush
            };

            StackPanel stack = new() { HorizontalAlignment = HorizontalAlignment.Center };
            Border pill = Chip(isFinal ? "★ FINAL" : $"DAY {day}", isFinal ? new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x02)) : new SolidColorBrush(accent), isFinal ? GoldBrush : new SolidColorBrush(Color.FromArgb(0x2E, accent.R, accent.G, accent.B)));
            pill.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(pill);

            string? icon = HubApi.Str(reward, "icon");
            BitmapImage? image = string.IsNullOrEmpty(icon) ? null : LoadImage(icon, isFinal ? 256 : 112);
            if (image != null)
                stack.Children.Add(new Image
                {
                    Source = image,
                    Width = isFinal ? 220 : 56,
                    Height = isFinal ? 64 : 56,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 8, 0, 6),
                    Effect = isFinal ? new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x4A, 0x9B, 0xFF), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.7 } : null
                });
            else
                stack.Children.Add(new TextBlock { Text = coins ? "\U0001FA99" : "?", FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 32, Foreground = GoldBrush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 6) });

            TextBlock name = Label(RewardText(reward), isFinal ? 13 : 11, isFinal ? GoldBrush : Brushes.White, isFinal ? FontWeights.Bold : FontWeights.SemiBold);
            name.TextAlignment = TextAlignment.Center;
            name.MaxHeight = 32;
            stack.Children.Add(name);

            string bonus = HubApi.Str(reward, "donatorBonus");
            if (!string.IsNullOrEmpty(bonus))
            {
                TextBlock b = Label(bonus + (isDonator ? "" : " (Donator)"), 10, isDonator ? GoldBrush : MutedBrush, FontWeights.SemiBold, new Thickness(0, 4, 0, 0));
                b.TextAlignment = TextAlignment.Center;
                stack.Children.Add(b);
            }

            string footer = state switch
            {
                "claimed" => "✓ CLAIMED",
                "current" => "▶ TODAY",
                "missed" => "MISSED",
                _ => !isFinal ? "LOCKED" : finalMissedDaysAllowed > 0 ? $"MISS MAX {finalMissedDaysAllowed} DAY{(finalMissedDaysAllowed > 1 ? "S" : "")}" : $"CLAIM DAYS 1-{day - 1}"
            };
            TextBlock f = Label(footer, 9, state == "future" ? MutedBrush : edge, FontWeights.Bold, new Thickness(0, 5, 0, 0));
            f.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(f);

            Grid layers = new();
            layers.Children.Add(stack);
            if (state == "claimed")
                layers.Children.Add(new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = GreenBrush,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -3, -1, 0),
                    Child = new TextBlock { Text = "✓", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x1F, 0x14)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                });

            Color glow = state == "current" ? OrangeBrush.Color : GoldBrush.Color;
            return new Border
            {
                Width = isFinal ? 258 : 124,
                Height = 180,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(7, 9, 7, 7),
                Background = state == "current"
                    ? new LinearGradientBrush(Color.FromRgb(0x3A, 0x22, 0x0C), Color.FromRgb(0x18, 0x14, 0x10), 90)
                    : isFinal
                        ? new LinearGradientBrush(Color.FromRgb(0x3A, 0x2C, 0x0E), Color.FromRgb(0x14, 0x16, 0x1E), 90)
                        : new LinearGradientBrush(Color.FromArgb(0xFF, (byte)(0x15 + accent.R / 14), (byte)(0x1A + accent.G / 14), (byte)(0x20 + accent.B / 14)), PanelBrush.Color, 90),
                BorderBrush = edge,
                BorderThickness = new Thickness(state == "current" || isFinal ? 2 : 1),
                CornerRadius = new CornerRadius(12),
                Opacity = state == "missed" ? 0.45 : state == "future" && !isFinal ? 0.85 : 1,
                Effect = state == "current" || isFinal ? new System.Windows.Media.Effects.DropShadowEffect { Color = glow, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55 } : null,
                Child = layers
            };
        }

        private async System.Threading.Tasks.Task ClaimBattlepass()
        {
            if (busy) return;
            busy = true;
            ShowMessage("Claiming...", true);
            JsonElement result = await HubApi.CallAsync("battlepass_claim");
            busy = false;

            if (HubApi.Ok(result))
            {
                string bonus = "";
                if (result.TryGetProperty("bonusReward", out JsonElement br) && br.ValueKind == JsonValueKind.Object && HubApi.Bool(br, "granted"))
                    bonus = $" + {HubApi.Num(br, "amount")} {HubApi.Str(br, "name")} (Donator bonus)";
                string reward = result.TryGetProperty("reward", out JsonElement r) && r.ValueKind == JsonValueKind.Object ? RewardText(r) : "reward";
                await LoadBattlepass();
                ShowMessage($"Claimed day {HubApi.Num(result, "day")}: {reward}{bonus}", true);
            }
            else
            {
                ShowMessage(HubApi.Error(result, "Could not claim the reward."), false);
            }
        }

        // ---------- Store ----------

        private long storeDiscount;
        private string storeCategory = "Crates";

        private async System.Threading.Tasks.Task LoadStore()
        {
            JsonElement data = await HubApi.CallAsync("store");
            if (FailedView(data))
                return;

            if (data.TryGetProperty("coins", out JsonElement coinsEl) && coinsEl.ValueKind == JsonValueKind.Number)
                SetCoins(coinsEl.GetInt64());

            storeDiscount = HubApi.Num(data, "discount");
            BeginView("Store", storeDiscount > 0
                ? $"Buy crates and skins with coins. Store discount: {storeDiscount}% off. Everything arrives in your account items in game."
                : "Buy crates and skins with coins. Everything arrives in your account items in game.");

            // Categories (Crates, Hoodies, Team T-Shirts, Storage Skins...). Older API answers only "crates".
            List<(string Name, JsonElement Items)> categories = new();
            if (data.TryGetProperty("categories", out JsonElement cats) && cats.ValueKind == JsonValueKind.Array)
                foreach (JsonElement cat in cats.EnumerateArray())
                    if (cat.TryGetProperty("items", out JsonElement catItems) && catItems.ValueKind == JsonValueKind.Array)
                        categories.Add((HubApi.Str(cat, "name", "Other"), catItems));
            if (categories.Count == 0 && data.TryGetProperty("crates", out JsonElement crates) && crates.ValueKind == JsonValueKind.Array)
                categories.Add(("Crates", crates));
            if (categories.Count == 0)
                return;
            if (!categories.Any(c => c.Name == storeCategory))
                storeCategory = categories[0].Name;

            if (categories.Count > 1)
            {
                WrapPanel chips = new() { Margin = new Thickness(0, 0, 0, 14) };
                foreach (var cat in categories)
                {
                    bool active = cat.Name == storeCategory;
                    Button chip = new()
                    {
                        Content = cat.Name,
                        Height = 32,
                        Margin = new Thickness(0, 0, 8, 8),
                        Padding = new Thickness(16, 0, 16, 0),
                        Style = ButtonStyle,
                        Background = active ? RedBrush : PanelBrush,
                        BorderBrush = active ? new SolidColorBrush(Color.FromRgb(0xF0, 0x2A, 0x36)) : EdgeBrush
                    };
                    string name = cat.Name;
                    chip.Click += async (_, _) =>
                    {
                        if (busy || name == storeCategory) return;
                        storeCategory = name;
                        await ShowTab("store");
                    };
                    chips.Children.Add(chip);
                }
                content.Children.Add(chips);
            }

            if (storeCategory == "Crates" && data.TryGetProperty("ticketCrate", out JsonElement ticketCrate) && ticketCrate.ValueKind == JsonValueKind.Object)
                content.Children.Add(CreateTicketExchange(ticketCrate, HubApi.Num(data, "eventTickets")));

            WrapPanel wrap = new();
            foreach (JsonElement item in categories.First(c => c.Name == storeCategory).Items.EnumerateArray())
                wrap.Children.Add(CreateCrateTile(item));
            content.Children.Add(wrap);
        }

        private Border CreateCrateTile(JsonElement crate)
        {
            // Crates (and items flagged allowQuantity) can be bought in bulk; skins one at a time.
            bool bulk = !crate.TryGetProperty("allowQuantity", out JsonElement aq) || aq.ValueKind != JsonValueKind.False;
            string itemid = HubApi.Str(crate, "itemid");
            string name = HubApi.Str(crate, "name", "Crate");
            long cost = HubApi.Num(crate, "cost");
            long unit = storeDiscount > 0 ? (long)Math.Floor(cost * (1 - storeDiscount / 100.0)) : cost;

            StackPanel stack = new() { HorizontalAlignment = HorizontalAlignment.Center };
            string preview = HubApi.Str(crate, "preview");
            Border iconArea = new()
            {
                Background = Brushes.Transparent,
                Child = new Image { Source = LoadImage(HubApi.Str(crate, "image")), Width = 72, Height = 72, Margin = new Thickness(0, 2, 0, 8) }
            };
            if (!string.IsNullOrEmpty(preview))
            {
                iconArea.MouseEnter += (_, _) => ShowCratePreviewSoon(name, preview);
                iconArea.MouseLeave += (_, _) => HideCratePreview();
            }
            stack.Children.Add(iconArea);
            TextBlock nameText = Label(name, 13, Brushes.White, FontWeights.SemiBold);
            nameText.TextAlignment = TextAlignment.Center;
            nameText.MaxHeight = 36;
            stack.Children.Add(nameText);
            TextBlock price = Label($"{Fmt(unit)} coins", 12, GoldBrush, null, new Thickness(0, 3, 0, 8));
            price.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(price);
            if (!string.IsNullOrEmpty(preview))
            {
                TextBlock hint = Label("Hold over the icon to preview", 11, MutedBrush, null, new Thickness(0, -4, 0, 8));
                hint.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(hint);
            }

            StackPanel buyRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            TextBox qty = new() { Text = "1", Width = 44, Height = 32, Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center, Background = PanelBrush, BorderBrush = EdgeBrush, Foreground = Brushes.White, CaretBrush = Brushes.White };
            Button buy = new() { Content = "Buy", Height = 32, Margin = new Thickness(8, 0, 0, 0), Style = ButtonStyle, Background = RedBrush, BorderBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x2A, 0x36)), Padding = new Thickness(18, 0, 18, 0) };
            buy.Click += async (_, _) => await BuyCrate(itemid, name, unit, qty.Text);
            if (bulk)
                buyRow.Children.Add(qty);
            else
                buy.Margin = new Thickness(0);
            buyRow.Children.Add(buy);
            stack.Children.Add(buyRow);

            return new Border
            {
                Width = 160,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(10, 12, 10, 12),
                Background = PanelBrush,
                BorderBrush = EdgeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = stack
            };
        }

        private System.Windows.Threading.DispatcherTimer? cratePreviewTimer;

        // The contents only open after holding the mouse over the crate for 3 seconds.
        private void ShowCratePreviewSoon(string name, string path)
        {
            HideCratePreview();
            BitmapImage? image = LoadImage(path, 1035);
            cratePreviewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            cratePreviewTimer.Tick += (_, _) =>
            {
                cratePreviewTimer?.Stop();
                cratePreviewName.Text = name;
                cratePreviewImage.Source = image;
                cratePreview.Visibility = Visibility.Visible;
            };
            cratePreviewTimer.Start();
        }

        private void HideCratePreview()
        {
            cratePreviewTimer?.Stop();
            cratePreviewTimer = null;
            cratePreview.Visibility = Visibility.Collapsed;
        }

        private Border CreateTicketExchange(JsonElement crate, long tickets)
        {
            string name = HubApi.Str(crate, "name", "Designer Crate");
            long perCrate = Math.Max(1, HubApi.Num(crate, "ticketsPerCrate", 1));

            Grid row = new();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new Image { Source = LoadImage(HubApi.Str(crate, "image")), Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left });

            StackPanel info = new() { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Label(name, 16, Brushes.White, FontWeights.SemiBold));
            info.Children.Add(Label($"You have {Fmt(tickets)} Event Ticket(s), {Fmt(perCrate)} per crate.", 13, GoldBrush, null, new Thickness(0, 2, 0, 2)));
            info.Children.Add(Label("You must be online in game to receive the crate.", 12, MutedBrush));
            Grid.SetColumn(info, 1);
            row.Children.Add(info);

            StackPanel actions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBox qty = new() { Text = "1", Width = 44, Height = 34, Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center, Background = PanelBrush, BorderBrush = EdgeBrush, Foreground = Brushes.White, CaretBrush = Brushes.White };
            Button exchange = new() { Content = "Exchange", Height = 34, Margin = new Thickness(8, 0, 0, 0), Style = ButtonStyle, Background = RedBrush, BorderBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x2A, 0x36)), Padding = new Thickness(18, 0, 18, 0), IsEnabled = tickets >= perCrate };
            exchange.Click += async (_, _) => await ExchangeTickets(name, perCrate, qty.Text);
            actions.Children.Add(qty);
            actions.Children.Add(exchange);
            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);

            return new Border
            {
                Margin = new Thickness(0, 0, 12, 14),
                Padding = new Thickness(14, 10, 14, 10),
                Background = PanelBrush,
                BorderBrush = EdgeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = row
            };
        }

        private async System.Threading.Tasks.Task ExchangeTickets(string name, long perCrate, string quantityText)
        {
            if (busy) return;
            if (!int.TryParse(quantityText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int quantity) || quantity < 1 || quantity > 50)
            {
                ShowMessage("Enter a quantity between 1 and 50.", false);
                return;
            }

            if (!Confirm($"Exchange {Fmt(quantity * perCrate)} Event Ticket(s) for {quantity}x {name}?"))
                return;

            busy = true;
            ShowMessage("Processing...", true);
            JsonElement result = await HubApi.CallAsync("event_exchange", new Dictionary<string, object> { ["quantity"] = quantity });
            busy = false;

            if (HubApi.Ok(result))
            {
                await LoadStore();
                ShowMessage($"Exchanged for {quantity}x {name}. It will be delivered to your account shortly.", true);
            }
            else
            {
                ShowMessage(HubApi.Error(result, "Could not complete the exchange."), false);
            }
        }

        private async System.Threading.Tasks.Task BuyCrate(string itemid, string name, long unit, string quantityText)
        {
            if (busy) return;
            if (!long.TryParse(quantityText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long quantity) || quantity < 1 || quantity > 99)
            {
                ShowMessage("Enter a quantity between 1 and 99.", false);
                return;
            }

            if (!Confirm($"Buy {quantity}x {name} for {Fmt(unit * quantity)} coins?"))
                return;

            busy = true;
            ShowMessage("Processing...", true);
            JsonElement result = await HubApi.CallAsync("store_buy", new Dictionary<string, object> { ["itemid"] = itemid, ["quantity"] = quantity });
            busy = false;

            if (HubApi.Ok(result))
            {
                if (result.TryGetProperty("newCoinBalance", out JsonElement nb) && nb.ValueKind == JsonValueKind.Number)
                    SetCoins(nb.GetInt64());
                else
                    await RefreshCoins();
                ShowMessage($"Bought {quantity}x {name}. It will be delivered to your account shortly.", true);
            }
            else
            {
                ShowMessage(HubApi.Error(result, "Could not complete the purchase."), false);
            }
        }

        // ---------- Wipe Rewards ----------

        private async System.Threading.Tasks.Task LoadWipe()
        {
            JsonElement data = await HubApi.CallAsync("wipe_rewards");
            if (FailedView(data))
                return;

            if (data.TryGetProperty("coins", out JsonElement coinsEl) && coinsEl.ValueKind == JsonValueKind.Number)
                SetCoins(coinsEl.GetInt64());

            BeginView("Wipe Rewards", "Rewards for finishing a wipe at the top of the Krakow leaderboards.");

            if (!HubApi.Bool(data, "linked"))
            {
                content.Children.Add(Label("Your account is not linked to a game account yet.", 13, OrangeBrush));
                return;
            }

            int shown = 0;
            if (data.TryGetProperty("rewards", out JsonElement rewards) && rewards.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement reward in rewards.EnumerateArray())
                {
                    content.Children.Add(CreateWipeCard(reward, data));
                    shown++;
                }
            }

            if (shown == 0)
                content.Children.Add(Label("No wipe rewards on your account. Finish a wipe at the top of a leaderboard.", 13, MutedBrush));
        }

        private Border CreateWipeCard(JsonElement reward, JsonElement data)
        {
            Border card = new() { Background = PanelBrush, BorderBrush = EdgeBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12) };
            StackPanel panel = new();
            card.Child = panel;

            string id = HubApi.Str(reward, "id"), kind = HubApi.Str(reward, "kind"), wipe = HubApi.Str(reward, "wipe"), value = HubApi.Str(reward, "value");
            bool golden = kind == "golden", claimed = HubApi.Str(reward, "status") == "claimed";
            string title = golden ? $"#1 {HubApi.Str(reward, "category")}" : $"#1 clan [{HubApi.Str(reward, "clanTag")}]";
            panel.Children.Add(Label(title, 15, GoldBrush, FontWeights.Bold));
            panel.Children.Add(Label(string.IsNullOrEmpty(value) ? wipe : $"{wipe} - {value}", 12, MutedBrush, null, new Thickness(0, 2, 0, 10)));

            if (claimed)
            {
                panel.Children.Add(Label("Claimed", 13, GreenBrush, FontWeights.SemiBold));
                return card;
            }

            if (!golden)
            {
                long amount = HubApi.Num(reward, "amount");
                Button coins = new() { Content = $"Claim {Fmt(amount)} coins", Height = 36, Style = ButtonStyle, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 0, 16, 0) };
                coins.Click += async (_, _) => await ClaimWipe(id, null, $"Claim {Fmt(amount)} coins?");
                panel.Children.Add(coins);
                return card;
            }

            panel.Children.Add(Label("Pick one golden skin. It goes straight to your account items.", 13, null, null, new Thickness(0, 0, 0, 8)));
            WrapPanel skinsPanel = new();
            if (data.TryGetProperty("skins", out JsonElement skins) && skins.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement skin in skins.EnumerateArray())
                {
                    long itemId = HubApi.Num(skin, "itemId");
                    string name = HubApi.Str(skin, "name");
                    Button pick = new() { Content = name, Height = 36, Style = ButtonStyle, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 0, 14, 0) };
                    pick.Click += async (_, _) => await ClaimWipe(id, itemId, $"Claim {name}? You can pick only one skin for this reward.");
                    skinsPanel.Children.Add(pick);
                }
            }
            panel.Children.Add(skinsPanel);
            return card;
        }

        private async System.Threading.Tasks.Task ClaimWipe(string grantId, long? itemId, string question)
        {
            if (!Confirm(question))
                return;

            Dictionary<string, object> extra = new() { ["grantId"] = grantId };
            if (itemId.HasValue)
                extra["itemId"] = itemId.Value;

            JsonElement result = await HubApi.CallAsync("wipe_reward_claim", extra);
            if (HubApi.Ok(result))
            {
                await LoadWipe();
                ShowMessage(HubApi.Str(result, "message", "Reward claimed."), true);
            }
            else
            {
                ShowMessage(HubApi.Error(result, "Could not claim this reward."), false);
            }
        }

        // ---------- Backpack ----------

        private sealed class BackpackItem
        {
            public long Id;
            public string Name = "";
            public string Image = "";
            public long Quantity;
            public bool Hidden;
            public bool Missing;
        }

        private List<BackpackItem> backpackItems = new();
        private string backpackFilter = "all";
        private StackPanel backpackFilters = new();
        private StackPanel backpackList = new();

        private async System.Threading.Tasks.Task LoadBackpack()
        {
            JsonElement data = await HubApi.CallAsync("backpack");
            if (FailedView(data))
                return;

            if (data.TryGetProperty("coins", out JsonElement coinsEl) && coinsEl.ValueKind == JsonValueKind.Number)
                SetCoins(coinsEl.GetInt64());

            backpackItems = new List<BackpackItem>();
            if (data.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
                foreach (JsonElement item in items.EnumerateArray())
                    backpackItems.Add(new BackpackItem
                    {
                        Id = HubApi.Num(item, "itemid"),
                        Name = HubApi.Str(item, "name", "Unknown Item"),
                        Image = HubApi.Str(item, "image"),
                        Quantity = HubApi.Num(item, "quantity", 1),
                        Hidden = HubApi.Bool(item, "hidden"),
                        Missing = HubApi.Bool(item, "missing")
                    });

            BeginView("Backpack", "Your account items. Hidden items stay on your account but do not show up in game, the change applies the next time you join a server.");
            backpackFilter = "all";
            backpackFilters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            backpackList = new StackPanel();
            content.Children.Add(backpackFilters);
            content.Children.Add(backpackList);
            RenderBackpack();
        }

        private void RenderBackpack()
        {
            backpackFilters.Children.Clear();
            backpackFilters.Children.Add(CreateBackpackFilter("all", "All", backpackItems.Count));
            backpackFilters.Children.Add(CreateBackpackFilter("visible", "Visible", backpackItems.Count(i => !i.Hidden)));
            backpackFilters.Children.Add(CreateBackpackFilter("hidden", "Hidden", backpackItems.Count(i => i.Hidden)));
            backpackFilters.Children.Add(CreateBackpackFilter("missing", "Missing", backpackItems.Count(i => i.Missing)));

            backpackList.Children.Clear();
            List<BackpackItem> shown = backpackItems.Where(i => backpackFilter switch
            {
                "visible" => !i.Hidden,
                "hidden" => i.Hidden,
                "missing" => i.Missing,
                _ => true
            }).ToList();

            if (shown.Count == 0)
                backpackList.Children.Add(Label(backpackItems.Count == 0 ? "Your backpack is empty." : "No items match this filter.", 14, MutedBrush));

            foreach (BackpackItem item in shown)
                backpackList.Children.Add(CreateBackpackRow(item));
        }

        private Button CreateBackpackFilter(string filter, string title, int count)
        {
            Button button = new() { Content = $"{title} ({Fmt(count)})", Height = 34, Margin = new Thickness(0, 0, 8, 0), Style = ButtonStyle, Padding = new Thickness(14, 0, 14, 0) };
            if (filter == backpackFilter)
                button.Background = RedBrush;
            button.Click += (_, _) =>
            {
                backpackFilter = filter;
                RenderBackpack();
            };
            return button;
        }

        private Border CreateBackpackRow(BackpackItem item)
        {
            Grid row = new();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Image icon = new() { Width = 44, Height = 44, Source = LoadImage(item.Image), HorizontalAlignment = HorizontalAlignment.Left, Opacity = item.Hidden ? 0.45 : 1 };
            row.Children.Add(icon);

            StackPanel info = new() { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Label(item.Name, 14, item.Hidden ? MutedBrush : Brushes.White, FontWeights.SemiBold));
            info.Children.Add(Label($"x{Fmt(item.Quantity)}  |  {(item.Hidden ? "Hidden" : "Visible")}", 12, MutedBrush));
            Grid.SetColumn(info, 1);
            row.Children.Add(info);

            Button toggle = new() { Content = item.Hidden ? "Show" : "Hide", Height = 34, MinWidth = 76, VerticalAlignment = VerticalAlignment.Center, Style = ButtonStyle, Padding = new Thickness(14, 0, 14, 0) };
            toggle.Click += async (_, _) => await ToggleBackpackItem(item, toggle);
            Grid.SetColumn(toggle, 2);
            row.Children.Add(toggle);

            return new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(14, 10, 14, 10),
                Background = PanelBrush,
                BorderBrush = EdgeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = row
            };
        }

        private async System.Threading.Tasks.Task ToggleBackpackItem(BackpackItem item, Button toggle)
        {
            if (busy) return;

            busy = true;
            toggle.IsEnabled = false;
            JsonElement result = await HubApi.CallAsync("backpack_hide", new Dictionary<string, object> { ["itemDefinitionId"] = item.Id, ["hidden"] = !item.Hidden });
            busy = false;
            toggle.IsEnabled = true;

            if (HubApi.Ok(result))
            {
                item.Hidden = !item.Hidden;
                RenderBackpack();
            }
            else
            {
                ShowMessage(HubApi.Error(result, "Could not update this item."), false);
            }
        }

        // ---------- Scrap Yard ----------

        private async System.Threading.Tasks.Task LoadScrap()
        {
            JsonElement data = await HubApi.CallAsync("scrap");
            if (FailedView(data))
                return;

            if (data.TryGetProperty("coins", out JsonElement coinsEl) && coinsEl.ValueKind == JsonValueKind.Number)
                SetCoins(coinsEl.GetInt64());

            BeginView("Scrap Yard", "Turn spare account items into coins. Rarer items pay more.");

            if (data.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
            {
                foreach (JsonElement item in items.EnumerateArray())
                    content.Children.Add(CreateScrapRow(item));
            }
            else
            {
                content.Children.Add(Label("You have nothing to scrap right now.", 14, MutedBrush));
            }
        }

        private Border CreateScrapRow(JsonElement item)
        {
            long id = HubApi.Num(item, "itemDefinitionId");
            long stack = HubApi.Num(item, "stackCount");
            long perUnit = HubApi.Num(item, "perUnitValue");

            Grid row = new();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            BitmapImage? image = LoadImage(HubApi.Str(item, "image"));
            Image icon = new() { Width = 44, Height = 44, Source = image, HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(icon);

            StackPanel info = new() { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Label(HubApi.Str(item, "name", $"Item {id}"), 14, Brushes.White, FontWeights.SemiBold));
            info.Children.Add(Label($"{HubApi.Str(item, "rarityLabel")}  |  {Fmt(perUnit)} coin(s) each  |  you have {Fmt(stack)}", 12, MutedBrush));
            Grid.SetColumn(info, 1);
            row.Children.Add(info);

            StackPanel actions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBox qty = new() { Text = "1", Width = 64, Height = 34, Padding = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center, Background = PanelBrush, BorderBrush = EdgeBrush, Foreground = Brushes.White, CaretBrush = Brushes.White };
            Button max = new() { Content = "Max", Height = 34, Margin = new Thickness(8, 0, 8, 0), Style = ButtonStyle, Padding = new Thickness(12, 0, 12, 0) };
            max.Click += (_, _) => qty.Text = stack.ToString(CultureInfo.InvariantCulture);
            Button scrap = new() { Content = "Scrap", Height = 34, Style = ButtonStyle, Background = RedBrush, BorderBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x2A, 0x36)), Padding = new Thickness(18, 0, 18, 0) };
            scrap.Click += async (_, _) => await ScrapItem(id, qty.Text, perUnit);
            actions.Children.Add(qty);
            actions.Children.Add(max);
            actions.Children.Add(scrap);
            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);

            return new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(14, 10, 14, 10),
                Background = PanelBrush,
                BorderBrush = EdgeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = row
            };
        }

        private async System.Threading.Tasks.Task ScrapItem(long itemDefinitionId, string quantityText, long perUnit)
        {
            if (busy) return;
            if (!long.TryParse(quantityText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long quantity) || quantity < 1 || quantity > 9999)
            {
                ShowMessage("Enter a quantity between 1 and 9999.", false);
                return;
            }

            if (!Confirm($"Scrap {Fmt(quantity)} item(s) for about {Fmt(quantity * perUnit)} coins? This cannot be undone."))
                return;

            busy = true;
            JsonElement result = await HubApi.CallAsync("scrap_item", new Dictionary<string, object> { ["itemDefinitionId"] = itemDefinitionId, ["quantity"] = quantity });
            busy = false;

            if (HubApi.Ok(result))
            {
                long earned = HubApi.Num(result, "coinsEarned");
                if (result.TryGetProperty("newBalance", out JsonElement nb) && nb.ValueKind == JsonValueKind.Number)
                    SetCoins(nb.GetInt64());
                await LoadScrap();
                ShowMessage($"Scrapped for {Fmt(earned)} coins.", true);
            }
            else
            {
                ShowMessage(HubApi.Error(result, "Could not scrap this item."), false);
            }
        }
    }
}
