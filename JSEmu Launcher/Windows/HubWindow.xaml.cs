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

        private void GamblingClick(object sender, RoutedEventArgs e) => WebsiteWindow.ShowHub(this, "/gambling");

        private async void WindowLoaded(object sender, RoutedEventArgs e)
        {
            await ShowTab(activeTab);
        }

        private async void TabClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string tab })
                await ShowTab(tab);
        }

        // ---------- helpers ----------

        private static BitmapImage? LoadImage(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            if (ImageCache.TryGetValue(path, out BitmapImage? cached))
                return cached;
            try
            {
                BitmapImage image = new();
                image.BeginInit();
                image.UriSource = new Uri(DressingRoomApi.SiteRoot + path, UriKind.Absolute);
                image.DecodePixelWidth = 72;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                ImageCache[path] = image;
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
            foreach (Button tab in new[] { tabBattlepass, tabStore, tabPimp, tabScrap })
            {
                bool active = (string)tab.Tag == activeTab;
                tab.BorderBrush = active ? RedBrush : EdgeBrush;
                tab.Background = active ? new SolidColorBrush(Color.FromRgb(0x2A, 0x10, 0x14)) : PanelBrush;
            }
        }

        private TextBlock messageBox = new();

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
            content.Children.Clear();
            loadingText.Text = "Loading...";
            loadingText.Visibility = Visibility.Visible;

            switch (tab)
            {
                case "battlepass": await LoadBattlepass(); break;
                case "store": await LoadStore(); break;
                case "pimp": await LoadPimp(); break;
                case "scrap": await LoadScrap(); break;
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

            BeginView(season, seasonActive ? $"Day {today} of {total} - claim one reward per day." : "This season is not active right now.");

            // progress + claim
            Border card = new() { Background = PanelBrush, BorderBrush = EdgeBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 16) };
            Grid grid = new();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new();
            long percent = Math.Min(100, online * 100 / required);
            left.Children.Add(Label($"ONLINE TIME TODAY: {HubApi.Str(data, "onlineTimeFormatted", "0h 0m")} / {HubApi.Str(data, "requiredTimeFormatted", "2h 0m")}", 12, MutedBrush, FontWeights.Bold));
            ProgressBar bar = new() { Minimum = 0, Maximum = 100, Value = percent, Height = 8, Margin = new Thickness(0, 8, 0, 8), Foreground = RedBrush, Background = new SolidColorBrush(Color.FromRgb(0x20, 0x27, 0x30)), BorderThickness = new Thickness(0) };
            left.Children.Add(bar);
            string todayText = "No reward today";
            if (data.TryGetProperty("todaysReward", out JsonElement tr) && tr.ValueKind == JsonValueKind.Object)
                todayText = RewardText(tr);
            left.Children.Add(Label($"Today's reward: {todayText}", 15, GoldBrush, FontWeights.SemiBold));
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
            if (data.TryGetProperty("rewards", out JsonElement rewards) && rewards.ValueKind == JsonValueKind.Array)
                foreach (JsonElement reward in rewards.EnumerateArray())
                    wrap.Children.Add(CreateDayTile(reward, today, claimed, isDonator));
            content.Children.Add(wrap);
        }

        private static string RewardText(JsonElement reward)
        {
            string type = HubApi.Str(reward, "type");
            long amount = HubApi.Num(reward, "amount", 1);
            return type == "coins" ? $"{Fmt(amount)} Coins" : $"{amount}x {HubApi.Str(reward, "name", "Reward")}";
        }

        private Border CreateDayTile(JsonElement reward, long today, HashSet<long> claimed, bool isDonator)
        {
            long day = HubApi.Num(reward, "day");
            string state = claimed.Contains(day) ? "claimed" : day < today ? "missed" : day == today ? "current" : "future";
            Brush edge = state switch { "claimed" => GreenBrush, "missed" => new SolidColorBrush(Color.FromRgb(0x8A, 0x3A, 0x3A)), "current" => OrangeBrush, _ => EdgeBrush };

            StackPanel stack = new() { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(Label($"DAY {day}", 11, MutedBrush, FontWeights.Bold));

            string? icon = HubApi.Str(reward, "icon");
            BitmapImage? image = string.IsNullOrEmpty(icon) ? null : LoadImage(icon);
            if (image != null)
                stack.Children.Add(new Image { Source = image, Width = 44, Height = 44, Margin = new Thickness(0, 8, 0, 4) });
            else
                stack.Children.Add(new TextBlock { Text = HubApi.Str(reward, "type") == "coins" ? "\U0001FA99" : "?", FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 30, Foreground = GoldBrush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 4) });

            TextBlock name = Label(RewardText(reward), 11, Brushes.White);
            name.TextAlignment = TextAlignment.Center;
            name.MaxHeight = 30;
            stack.Children.Add(name);

            string bonus = HubApi.Str(reward, "donatorBonus");
            if (!string.IsNullOrEmpty(bonus))
            {
                TextBlock b = Label(bonus + (isDonator ? "" : " (Donator)"), 10, isDonator ? GoldBrush : MutedBrush, FontWeights.SemiBold, new Thickness(0, 4, 0, 0));
                b.TextAlignment = TextAlignment.Center;
                stack.Children.Add(b);
            }

            stack.Children.Add(Label(state.ToUpperInvariant(), 9, edge, FontWeights.Bold, new Thickness(0, 5, 0, 0)));
            ((TextBlock)stack.Children[^1]).HorizontalAlignment = HorizontalAlignment.Center;

            return new Border
            {
                Width = 124,
                Height = 158,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(6, 9, 6, 6),
                Background = state == "current" ? new SolidColorBrush(Color.FromRgb(0x2A, 0x1C, 0x0C)) : PanelBrush,
                BorderBrush = edge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Opacity = state == "future" ? 0.75 : 1,
                Child = stack
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

        // ---------- Crate Store ----------

        private long storeDiscount;

        private async System.Threading.Tasks.Task LoadStore()
        {
            JsonElement data = await HubApi.CallAsync("store");
            if (FailedView(data))
                return;

            if (data.TryGetProperty("coins", out JsonElement coinsEl) && coinsEl.ValueKind == JsonValueKind.Number)
                SetCoins(coinsEl.GetInt64());

            storeDiscount = HubApi.Num(data, "discount");
            BeginView("Crate Store", storeDiscount > 0
                ? $"Buy crates with coins. Store discount: {storeDiscount}% off. Crates arrive in your account items in game."
                : "Buy crates with coins. Crates arrive in your account items in game.");

            WrapPanel wrap = new();
            if (data.TryGetProperty("crates", out JsonElement crates) && crates.ValueKind == JsonValueKind.Array)
                foreach (JsonElement crate in crates.EnumerateArray())
                    wrap.Children.Add(CreateCrateTile(crate));
            content.Children.Add(wrap);
        }

        private Border CreateCrateTile(JsonElement crate)
        {
            string itemid = HubApi.Str(crate, "itemid");
            string name = HubApi.Str(crate, "name", "Crate");
            long cost = HubApi.Num(crate, "cost");
            long unit = storeDiscount > 0 ? (long)Math.Floor(cost * (1 - storeDiscount / 100.0)) : cost;

            StackPanel stack = new() { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new Image { Source = LoadImage(HubApi.Str(crate, "image")), Width = 72, Height = 72, Margin = new Thickness(0, 2, 0, 8) });
            TextBlock nameText = Label(name, 13, Brushes.White, FontWeights.SemiBold);
            nameText.TextAlignment = TextAlignment.Center;
            nameText.MaxHeight = 36;
            stack.Children.Add(nameText);
            TextBlock price = Label($"{Fmt(unit)} coins", 12, GoldBrush, null, new Thickness(0, 3, 0, 8));
            price.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(price);

            StackPanel buyRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            TextBox qty = new() { Text = "1", Width = 44, Height = 32, Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center, Background = PanelBrush, BorderBrush = EdgeBrush, Foreground = Brushes.White, CaretBrush = Brushes.White };
            Button buy = new() { Content = "Buy", Height = 32, Margin = new Thickness(8, 0, 0, 0), Style = ButtonStyle, Background = RedBrush, BorderBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x2A, 0x36)), Padding = new Thickness(18, 0, 18, 0) };
            buy.Click += async (_, _) => await BuyCrate(itemid, name, unit, qty.Text);
            buyRow.Children.Add(qty);
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

        private async System.Threading.Tasks.Task BuyCrate(string itemid, string name, long unit, string quantityText)
        {
            if (busy) return;
            if (!long.TryParse(quantityText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long quantity) || quantity < 1 || quantity > 99)
            {
                ShowMessage("Enter a quantity between 1 and 99.", false);
                return;
            }

            if (MessageBox.Show(this, $"Buy {quantity}x {name} for {Fmt(unit * quantity)} coins?", "Crate Store", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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

        // ---------- Pimp My Car ----------

        private async System.Threading.Tasks.Task LoadPimp()
        {
            JsonElement data = await HubApi.CallAsync("shaders");
            if (FailedView(data))
                return;

            if (data.TryGetProperty("coins", out JsonElement coinsEl) && coinsEl.ValueKind == JsonValueKind.Number)
                SetCoins(coinsEl.GetInt64());

            bool isDonator = HubApi.Bool(data, "isDonator");
            BeginView("Pimp My Car", "Repaint your vehicle for coins. You must be sitting in your vehicle in game when you apply a color.");

            if (!isDonator)
                content.Children.Add(Label("Pimp My Car is a Donator perk. Become a Donator to unlock repainting.", 13, OrangeBrush, FontWeights.SemiBold, new Thickness(0, 0, 0, 14)));

            WrapPanel wrap = new();
            if (data.TryGetProperty("shaders", out JsonElement shaders) && shaders.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement shader in shaders.EnumerateArray())
                    wrap.Children.Add(CreateShaderTile(shader, isDonator));
            }
            content.Children.Add(wrap);
        }

        private Border CreateShaderTile(JsonElement shader, bool isDonator)
        {
            string hex = HubApi.Str(shader, "colorHex", "#888888");
            Brush fill;
            try { fill = (Brush)new BrushConverter().ConvertFromString(hex)!; }
            catch { fill = Brushes.Gray; }

            long id = HubApi.Num(shader, "shaderGroupId");
            long price = HubApi.Num(shader, "price");
            string name = HubApi.Str(shader, "name", "Color");

            StackPanel stack = new() { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new Ellipse { Width = 52, Height = 52, Fill = fill, Stroke = EdgeBrush, StrokeThickness = 2, Margin = new Thickness(0, 4, 0, 8) });
            TextBlock nameText = Label(name, 13, Brushes.White, FontWeights.SemiBold);
            nameText.TextAlignment = TextAlignment.Center;
            nameText.MaxHeight = 36;
            stack.Children.Add(nameText);
            stack.Children.Add(Label($"{Fmt(price)} coins", 12, GoldBrush, null, new Thickness(0, 3, 0, 8)));
            ((TextBlock)stack.Children[^1]).HorizontalAlignment = HorizontalAlignment.Center;

            Button apply = new() { Content = isDonator ? "Apply" : "Donator only", Height = 32, Style = ButtonStyle, IsEnabled = isDonator, Padding = new Thickness(18, 0, 18, 0) };
            apply.Click += async (_, _) => await ApplyShader(id, name, price);
            stack.Children.Add(apply);

            return new Border
            {
                Width = 150,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(10, 12, 10, 12),
                Background = PanelBrush,
                BorderBrush = EdgeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = stack
            };
        }

        private async System.Threading.Tasks.Task ApplyShader(long shaderGroupId, string name, long price)
        {
            if (busy) return;
            if (MessageBox.Show(this, $"Repaint your vehicle {name} for {Fmt(price)} coins?\n\nYou must be sitting in your vehicle in game.", "Pimp My Car", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            busy = true;
            ShowMessage("Applying...", true);
            JsonElement result = await HubApi.CallAsync("shader_apply", new Dictionary<string, object> { ["shaderGroupId"] = shaderGroupId });
            busy = false;

            if (HubApi.Ok(result))
            {
                ShowMessage(HubApi.Str(result, "message", "Paint applied."), true);
                await RefreshCoins();
            }
            else
            {
                ShowMessage(HubApi.Error(result, "Could not apply the paint."), false);
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

            if (MessageBox.Show(this, $"Scrap {Fmt(quantity)} item(s) for about {Fmt(quantity * perUnit)} coins? This cannot be undone.", "Scrap Yard", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
