using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H1Emu_Launcher.Classes;

namespace H1Emu_Launcher
{
    public partial class DressingRoomWindow : Window
    {
        private static readonly (string Key, string Label, string Title, bool Gear)[] Slots =
        {
            ("head", "Head", "Headwear", false),
            ("face", "Face", "Face", false),
            ("eyes", "Glasses", "Glasses", false),
            ("chest", "Shirt", "Shirts", false),
            ("hands", "Hands", "Gloves", false),
            ("legs", "Pants", "Pants", false),
            ("feet", "Feet", "Boots", false),
            ("conveys", "Conveys", "Conveys / Zeds / Gators skins", true),
            ("armor", "Body Armor", "Body armor skins", true),
            ("helmet", "Helmet", "Helmet skins", true),
            ("sniper", "Sniper", "Sniper rifle skins", true),
            ("shotgun", "Shotgun", "Shotgun skins", true),
            ("ar", "AR-15", "AR-15 skins", true),
            ("ak", "AK-47", "AK-47 skins", true),
            ("satchel", "Satchel", "Satchel skins", true),
            ("backpack", "Backpack", "Backpack skins", true),
            ("military", "Military Backpack", "Military backpack skins", true),
        };

        private static readonly Dictionary<string, BitmapImage> ImageCache = new();

        private static readonly SolidColorBrush GoldBrush = new(Color.FromRgb(0xF2, 0xBC, 0x55));
        private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(0xE1, 0x1D, 0x2A));
        private static readonly SolidColorBrush EdgeBrush = new(Color.FromRgb(0x2C, 0x35, 0x40));
        private static readonly SolidColorBrush PanelBrush = new(Color.FromRgb(0x15, 0x1A, 0x20));
        private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(0x8A, 0x96, 0xA3));

        private readonly string keyHash;
        private List<DressingItem> items = new();
        private Dictionary<string, int> saved = new();
        private Dictionary<string, int> outfit = new();
        private string activeSlot = "chest";
        private bool ready;
        private bool busy;

        private readonly Dictionary<string, Button> slotButtons = new();
        private readonly Dictionary<string, TextBlock> slotCurrent = new();

        public DressingRoomWindow(string keyHash)
        {
            InitializeComponent();
            this.keyHash = keyHash;
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseClick(object sender, RoutedEventArgs e) => Close();

        private async void WindowLoaded(object sender, RoutedEventArgs e)
        {
            SetControlsEnabled(false);
            DressingRoomData data = await DressingRoomApi.GetAsync(keyHash);

            if (!data.Success)
            {
                loadingText.Text = data.Error ?? "Could not load your Dressing Room.";
                loadingText.Foreground = RedBrush;
                return;
            }

            items = data.Items;
            saved = data.Outfit.Where(kv => kv.Value.HasValue).ToDictionary(kv => kv.Key, kv => kv.Value!.Value);
            outfit = new Dictionary<string, int>(saved);
            userLabel.Text = string.IsNullOrEmpty(data.Username) ? "" : $"Signed in as {data.Username}";

            BuildSlotList();
            loadingText.Visibility = Visibility.Collapsed;
            ready = true;
            SetControlsEnabled(true);
            RefreshAll();
        }

        private void SetControlsEnabled(bool enabled)
        {
            unequipAllButton.IsEnabled = enabled;
            resetButton.IsEnabled = enabled;
            saveButton.IsEnabled = enabled;
        }

        private void BuildSlotList()
        {
            bool gearHeaderAdded = false;
            foreach (var slot in Slots)
            {
                if (slot.Gear && !gearHeaderAdded)
                {
                    gearHeaderAdded = true;
                    slotList.Children.Add(new TextBlock
                    {
                        Text = "GROUND GEAR SKINS",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = GoldBrush,
                        Margin = new Thickness(6, 16, 6, 2)
                    });
                    slotList.Children.Add(new TextBlock
                    {
                        Text = "Applied to gear you pick up from the ground straight onto its slot.",
                        FontSize = 11,
                        Foreground = MutedBrush,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(6, 0, 6, 8)
                    });
                }

                TextBlock label = new() { Text = slot.Label, FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Brushes.White };
                TextBlock current = new() { FontSize = 11, Foreground = MutedBrush, TextTrimming = TextTrimming.CharacterEllipsis };
                slotCurrent[slot.Key] = current;

                StackPanel content = new();
                content.Children.Add(label);
                content.Children.Add(current);

                string key = slot.Key;
                Button button = new()
                {
                    Content = content,
                    Margin = new Thickness(0, 0, 0, 7),
                    Padding = new Thickness(12, 9, 12, 9),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Style = (Style)FindResource("DrButton")
                };
                button.Click += (_, _) =>
                {
                    activeSlot = key;
                    RefreshAll();
                };
                slotButtons[slot.Key] = button;
                slotList.Children.Add(button);
            }
        }

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

        private Button CreateTile(DressingItem item)
        {
            bool selected = outfit.TryGetValue(item.Slot, out int current) && current == item.Id;

            Image image = new() { Width = 64, Height = 64, Source = LoadImage(item.Image), Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            TextBlock name = new()
            {
                Text = item.Name,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                MaxHeight = 30,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = Brushes.White
            };

            StackPanel content = new() { HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(image);
            content.Children.Add(name);

            Button tile = new()
            {
                Content = content,
                Width = 118,
                Height = 130,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(6),
                ToolTip = item.Owned ? item.Name : $"{item.Name} (locked)",
                IsEnabled = item.Owned,
                Style = (Style)FindResource("DrButton"),
                Background = selected ? new SolidColorBrush(Color.FromRgb(0x2A, 0x22, 0x10)) : PanelBrush,
                BorderBrush = selected ? GoldBrush : EdgeBrush
            };

            if (item.Owned)
            {
                tile.Click += (_, _) =>
                {
                    if (outfit.TryGetValue(item.Slot, out int id) && id == item.Id)
                        outfit.Remove(item.Slot);
                    else
                        outfit[item.Slot] = item.Id;
                    RefreshAll();
                };
            }

            return tile;
        }

        private void RefreshAll()
        {
            if (!ready)
                return;

            var meta = Slots.First(s => s.Key == activeSlot);
            string query = searchBox.Text?.Trim().ToLowerInvariant() ?? "";

            var all = items.Where(i => i.Slot == activeSlot).ToList();
            var filtered = all.Where(i => query.Length == 0 || i.Name.ToLowerInvariant().Contains(query)).ToList();
            var owned = filtered.Where(i => i.Owned).ToList();
            var locked = filtered.Where(i => !i.Owned).ToList();

            panelTitle.Text = meta.Title;
            panelCount.Text = $"OWNED {all.Count(i => i.Owned)} / {all.Count}";

            ownedPanel.Children.Clear();
            foreach (DressingItem item in owned)
                ownedPanel.Children.Add(CreateTile(item));

            lockedPanel.Children.Clear();
            bool showLockedItems = showLocked.IsChecked == true && locked.Count > 0;
            if (showLockedItems)
                foreach (DressingItem item in locked)
                    lockedPanel.Children.Add(CreateTile(item));
            lockedTitle.Visibility = showLockedItems ? Visibility.Visible : Visibility.Collapsed;
            lockedTitle.Text = $"LOCKED ({locked.Count})";
            lockedPanel.Visibility = lockedTitle.Visibility;

            emptyText.Visibility = owned.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            emptyText.Text = query.Length > 0 ? "No owned skins match your search." : "You don't own any skins in this category yet.";

            var byId = items.ToDictionary(i => i.Id);
            foreach (var slot in Slots)
            {
                bool hasPick = outfit.TryGetValue(slot.Key, out int id) && byId.ContainsKey(id);
                slotCurrent[slot.Key].Text = hasPick ? byId[id].Name : "Default";
                Button button = slotButtons[slot.Key];
                bool active = slot.Key == activeSlot;
                button.BorderBrush = active ? RedBrush : EdgeBrush;
                button.Background = active ? new SolidColorBrush(Color.FromRgb(0x2A, 0x10, 0x14)) : PanelBrush;
            }

            UpdateDirty();
        }

        private bool IsDirty()
        {
            if (saved.Count != outfit.Count)
                return true;
            return outfit.Any(kv => !saved.TryGetValue(kv.Key, out int v) || v != kv.Value);
        }

        private void UpdateDirty()
        {
            bool dirty = IsDirty();
            dirtyDot.Fill = new SolidColorBrush(dirty ? GoldBrush.Color : Color.FromRgb(0x45, 0xD3, 0x8A));
            if (!busy)
                statusText.Text = dirty ? "Unsaved changes" : "All changes saved - applies the next time you respawn in game.";
            saveButton.IsEnabled = ready && !busy && dirty;
            resetButton.IsEnabled = ready && !busy && dirty;
            unequipAllButton.IsEnabled = ready && !busy && outfit.Count > 0;
        }

        private void SearchChanged(object sender, TextChangedEventArgs e) => RefreshAll();

        private void ShowLockedChanged(object sender, RoutedEventArgs e) => RefreshAll();

        private void UnequipAllClick(object sender, RoutedEventArgs e)
        {
            outfit.Clear();
            RefreshAll();
        }

        private void ResetClick(object sender, RoutedEventArgs e)
        {
            outfit = new Dictionary<string, int>(saved);
            RefreshAll();
        }

        private async void SaveClick(object sender, RoutedEventArgs e)
        {
            busy = true;
            statusText.Text = "Saving...";
            UpdateDirty();

            var (ok, error) = await DressingRoomApi.SaveAsync(keyHash, outfit);
            busy = false;

            if (ok)
            {
                saved = new Dictionary<string, int>(outfit);
                UpdateDirty();
            }
            else
            {
                UpdateDirty();
                statusText.Text = error ?? "Failed to save outfit.";
            }
        }
    }
}
