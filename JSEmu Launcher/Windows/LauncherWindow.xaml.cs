using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.Json.Serialization;
using System.Net;
using System.Threading.Tasks;
using System.Net.Http;
using System.Windows.Media.Animation;
using System.Linq;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Data;
using Microsoft.Win32;
using Microsoft.Toolkit.Uwp.Notifications;
using H1Emu_Launcher.Classes;

namespace H1Emu_Launcher
{
    public partial class LauncherWindow : Window
    {
        private readonly System.Windows.Forms.NotifyIcon launcherNotifyIcon = new();
        private readonly ProcessStartInfo cmdShell = new()
        {
            FileName = "cmd.exe",
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        public static JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = true };
        public static LauncherWindow launcherInstance;
        public static ContextMenu notifyIconContextMenu = new();
        public static string customServersJsonFile = $"{Info.APPLICATION_DATA_PATH}\\JSEmu Launcher\\servers.json";
        public static string recentServersJsonFile = $"{Info.APPLICATION_DATA_PATH}\\JSEmu Launcher\\recentServers.json";
        public static string assetPacksJsonFile = $"{Info.APPLICATION_DATA_PATH}\\JSEmu Launcher\\assetPacks.json";

        public Storyboard CarouselNextAnimation;
        public Storyboard CarouselNextAnimationFollow;
        public Storyboard CarouselPreviousAnimation;
        public Storyboard CarouselPreviousAnimationFollow;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SetForegroundWindow(IntPtr hwnd);

        public LauncherWindow()
        {
            InitializeComponent();
            launcherInstance = this;

            // Adds the correct language file to the resource dictionary and then loads it
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(SetLanguageFile.LoadFile());

            CarouselNextAnimation = FindResource("CarouselNextImageAnimation") as Storyboard;
            CarouselNextAnimationFollow = FindResource("CarouselNextImageAnimationFollow") as Storyboard;
            CarouselPreviousAnimation = FindResource("CarouselPrevImageAnimation") as Storyboard;
            CarouselPreviousAnimationFollow = FindResource("CarouselPrevImageAnimationFollow") as Storyboard;

            launcherNotifyIcon.Icon = Properties.Resources.Icon;
            launcherNotifyIcon.Text = "JSEmu Launcher";
            launcherNotifyIcon.MouseDown += (o, s) =>
            {
                if (s.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    Show();
                    Activate();
                }
                else if (s.Button == System.Windows.Forms.MouseButtons.Right)
                {
                    notifyIconContextMenu.IsOpen = true;
                    // Get context menu handle and bring it to the foreground
                    if (PresentationSource.FromVisual(notifyIconContextMenu) is HwndSource hwndSource)
                        SetForegroundWindow(hwndSource.Handle);
                }
            };

            ulong bytesReadLastUpdated = 0;
            ContentDownloader.downloadSpeedTimer.Elapsed += (s, e) =>
            {
                ulong difference = ContentDownloader.sizeDownloadedPublic - bytesReadLastUpdated;
                bytesReadLastUpdated = ContentDownloader.sizeDownloadedPublic;
                ContentDownloader.downloadSpeed = (float)difference / (1024 * 1024);
            };
        }

        public class ServerList
        {
            [JsonPropertyName("Server Name")]
            public string CustomServerName { get; set; }

            [JsonPropertyName("Server Address")]
            public string CustomServerIp { get; set; }
        }

        public class ServerListRecent
        {
            [JsonPropertyName("Recent Server Name")]
            public string CustomServerNameRecent { get; set; }
        }

        public class AssetPackList
        {
            [JsonPropertyName("Asset Pack Name")]
            public string AssetPackName { get; set; }

            [JsonPropertyName("Asset Pack URL")]
            public string AssetPackURL { get; set; }
        }

        public async Task ExecuteArguments(string[] rawArgs)
        {
            if (WindowState != WindowState.Normal)
                WindowState = WindowState.Normal;

            Show();
            Activate();

            // If there are no args then return after the launcher was brought into focus
            if (rawArgs.Length == 0)
                return;

            // If the new server and ip arguments exist, open the Add Server window and tell it to fill in the name and ip fields with the specified argument values
            if (!string.IsNullOrEmpty(SteamFramePages.Login.GetParameter(rawArgs, "-servername", "")) || !string.IsNullOrEmpty(SteamFramePages.Login.GetParameter(rawArgs, "-serverip", "")))
            {
                // Close every other window apart from the Launcher and Add Server window
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is not LauncherWindow && window is not AddItemWindow)
                        window.Close();
                }

                string newServerName = SteamFramePages.Login.GetParameter(rawArgs, "-servername", "");
                string newServerIp = SteamFramePages.Login.GetParameter(rawArgs, "-serverip", "");

                if (AddItemWindow.addItemWindowInstance == null)
                {
                    AddItemWindow addServer = new()
                    {
                        Owner = this,
                        itemType = 1
                    };
                    addServer.primaryTextBox.Text = newServerName;
                    addServer.secondaryTextBox.Text = newServerIp;
                    addServer.primaryTextBoxHint.Visibility = Visibility.Hidden;
                    addServer.secondaryTextBoxHint.Visibility = Visibility.Hidden;
                    await Task.Run(() =>
                    {
                        Dispatcher.Invoke(new Action(delegate
                        {
                            addServer.ShowDialog();
                        }));
                    });
                }
                else
                    AddItemWindow.addItemWindowInstance.FillInFields(newServerName, newServerIp);

                newServerName = null;
                newServerIp = null;
                return;
            }

            // If the new asset pack name and URL arguments exist, open the Add Asset Pack window and tell it to fill in the name and URL fields with the specified argument values
            if (!string.IsNullOrEmpty(SteamFramePages.Login.GetParameter(rawArgs, "-assetpackname", "")) || !string.IsNullOrEmpty(SteamFramePages.Login.GetParameter(rawArgs, "-assetpackurl", "")))
            {
                // Close every other window apart from the Launcher and Settings windows
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is not LauncherWindow && window is not SettingsWindow && window is not AddItemWindow)
                        window.Close();
                }

                string newAssetPackName = SteamFramePages.Login.GetParameter(rawArgs, "-assetpackname", "");
                string newAssetPackURL = SteamFramePages.Login.GetParameter(rawArgs, "-assetpackurl", "");

                if (SettingsWindow.settingsInstance == null)
                {
                    SettingsWindow.newAssetPackName = newAssetPackName;
                    SettingsWindow.newAssetPackURL = newAssetPackURL;
                    SettingsWindow sw = new();
                    sw.settingsTabControl.SelectedIndex = 1;
                    await Task.Run(() =>
                    {
                        Dispatcher.Invoke(new Action(delegate
                        {
                            sw.ShowDialog();
                        }));
                    });
                }
                else
                    SettingsWindow.SwitchToAssetPacksTab(newAssetPackName, newAssetPackURL);

                newAssetPackName = null;
                newAssetPackURL = null;
                SettingsWindow.newAssetPackName = null;
                SettingsWindow.newAssetPackURL = null;
                return;
            }

            // If the account key argument exists, open the Settings window and tell it to open the Account Key tab with accountkey argument value
            if (!string.IsNullOrEmpty(SteamFramePages.Login.GetParameter(rawArgs, "-accountkey", "")))
            {
                // Close every other window apart from the Launcher and Settings windows
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is not LauncherWindow && window is not SettingsWindow)
                        window.Close();
                }

                string newAccountKey = SteamFramePages.Login.GetParameter(rawArgs, "-accountkey", "");

                if (SettingsWindow.settingsInstance == null)
                {
                    SettingsWindow.newAccountKey = newAccountKey;
                    SettingsWindow sw = new();
                    sw.settingsTabControl.SelectedIndex = 2;
                    await Task.Run(() =>
                    {
                        Dispatcher.Invoke(new Action(delegate
                        {
                            sw.ShowDialog();
                        }));
                    });
                }
                else
                    SettingsWindow.SwitchToAccountKeyTab(newAccountKey);

                newAccountKey = null;
                SettingsWindow.newAccountKey = null;
                return;
            }

            // If the launch game argument exists, then launch the game straight away
            if (string.Join(' ', rawArgs).Contains("-launchgame"))
            {
                playButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                return;
            }
        }

        private void ServerSelectorChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!serverSelector.IsLoaded)
                return;

            if (serverSelector.SelectedIndex == serverSelector.Items.Count - 1 ||
                serverSelector.SelectedIndex == serverSelector.Items.Count - 2 ||
                serverSelector.SelectedIndex == 3 || serverSelector.SelectedIndex == -1)
            {
                serverSelector.SelectedIndex = 0;
            }

            Properties.Settings.Default.lastServer = serverSelector.SelectedIndex;
            Properties.Settings.Default.Save();
        }

        private void LoadServers()
        {
            if (!File.Exists(customServersJsonFile) || string.IsNullOrEmpty(File.ReadAllText(customServersJsonFile)))
                File.WriteAllText(customServersJsonFile, "[]");

            if (!File.Exists(recentServersJsonFile) || string.IsNullOrEmpty(File.ReadAllText(recentServersJsonFile)))
                File.WriteAllText(recentServersJsonFile, "[]");

            try
            {
                // Load options for context menu on the applications' NotifyIcon
                notifyIconContextMenu.Style = (Style)FindResource("ContextMenuStyle");
                notifyIconContextMenu.PlacementTarget = this;

                MenuItem notifyIconMenuItemH1EmuServersPlay = new()
                {
                    Style = (Style)FindResource("CustomMenuItem"),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                System.Windows.Shapes.Path pathH1EmuServersPlay = new()
                {
                    Data = (PathGeometry)FindResource("PlayIcon"),
                    Stretch = Stretch.Uniform,
                    Width = 14,
                    Height = 14
                };
                Binding bindingH1EmuServersPlay = new("Foreground")
                {
                    Source = notifyIconMenuItemH1EmuServersPlay,
                    Mode = BindingMode.OneWay
                };
                BindingOperations.SetBinding(pathH1EmuServersPlay, System.Windows.Shapes.Path.StrokeProperty, bindingH1EmuServersPlay);

                notifyIconMenuItemH1EmuServersPlay.Icon = pathH1EmuServersPlay;
                notifyIconMenuItemH1EmuServersPlay.SetResourceReference(HeaderedItemsControl.HeaderProperty, "item139");
                notifyIconMenuItemH1EmuServersPlay.Click += (o, s) =>
                {
                    serverSelector.SelectedIndex = 0;
                    playButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                };

                MenuItem notifyIconMenuItemSingleplayerPlay = new()
                {
                    Style = (Style)FindResource("CustomMenuItem"),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                System.Windows.Shapes.Path pathSinglePlayerPlay = new()
                {
                    Data = (PathGeometry)FindResource("PlayIcon"),
                    Stretch = Stretch.Uniform,
                    Width = 14,
                    Height = 14
                };
                Binding bindingSinglePlayerPlay = new("Foreground")
                {
                    Source = notifyIconMenuItemSingleplayerPlay,
                    Mode = BindingMode.OneWay
                };
                BindingOperations.SetBinding(pathSinglePlayerPlay, System.Windows.Shapes.Path.StrokeProperty, bindingSinglePlayerPlay);

                notifyIconMenuItemSingleplayerPlay.Icon = pathSinglePlayerPlay;
                notifyIconMenuItemSingleplayerPlay.SetResourceReference(HeaderedItemsControl.HeaderProperty, "item140");
                notifyIconMenuItemSingleplayerPlay.Click += (o, s) =>
                {
                    serverSelector.SelectedIndex = 2;

                playButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                };

                Separator notifyIconItemSeparator = new()
                {
                    Style = (Style)FindResource("SeparatorMenuItem"),
                    Background = new SolidColorBrush(Color.FromRgb(66, 66, 66)),
                    Margin = new Thickness(10, 0, 10, 8)
                };

                MenuItem notifyIconMenuItemExit = new()
                {
                    Style = (Style)FindResource("CustomMenuItem"),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 71, 76))
                };
                System.Windows.Shapes.Path pathExitNotifyIcon = new()
                {
                    Data = (PathGeometry)FindResource("ExitIcon"),
                    Stretch = Stretch.Uniform,
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(1, 0, 0, 0)
                };
                Binding bindingExitNotifyIcon = new("Foreground")
                {
                    Source = notifyIconMenuItemExit,
                    Mode = BindingMode.OneWay
                };
                BindingOperations.SetBinding(pathExitNotifyIcon, System.Windows.Shapes.Path.FillProperty, bindingExitNotifyIcon);

                notifyIconMenuItemExit.Icon = pathExitNotifyIcon;
                notifyIconMenuItemExit.SetResourceReference(HeaderedItemsControl.HeaderProperty, "item194");
                notifyIconMenuItemExit.Click += (o, s) => { Close(); };

                notifyIconContextMenu.Items.Add(notifyIconMenuItemH1EmuServersPlay);
                notifyIconContextMenu.Items.Add(notifyIconMenuItemSingleplayerPlay);
                notifyIconContextMenu.Items.Add(notifyIconItemSeparator);
                notifyIconContextMenu.Items.Add(notifyIconMenuItemExit);

                List<ServerListRecent> currentJsonRecent = JsonSerializer.Deserialize<List<ServerListRecent>>(File.ReadAllText(recentServersJsonFile));
                foreach (ServerListRecent server in currentJsonRecent)
                {
                    MenuItem playCustomServer = new()
                    {
                        Style = (Style)FindResource("CustomMenuItem"),
                        Margin = new Thickness(0, 0, 0, 6),
                        Header = server.CustomServerNameRecent
                    };
                    System.Windows.Shapes.Path pathCustomServerDelete = new()
                    {
                        Data = (PathGeometry)FindResource("PlayIcon"),
                        Stretch = Stretch.Uniform,
                        Width = 14,
                        Height = 14
                    };
                    Binding bindingCustomServerDelete = new("Foreground")
                    {
                        Source = playCustomServer,
                        Mode = BindingMode.OneWay
                    };
                    BindingOperations.SetBinding(pathCustomServerDelete, System.Windows.Shapes.Path.StrokeProperty, bindingCustomServerDelete);

                    playCustomServer.Icon = pathCustomServerDelete;
                    playCustomServer.Click += LaunchToCustomServerFromNotifyIcon;
                    notifyIconContextMenu.Items.Insert(notifyIconContextMenu.Items.Count - 1, playCustomServer);
                }

                if (notifyIconContextMenu.Items.Count > 4)
                {
                    Separator separator = new()
                    {
                        Style = (Style)FindResource("SeparatorMenuItem"),
                        Background = new SolidColorBrush(Color.FromRgb(66, 66, 66)),
                        Margin = new Thickness(10, 2, 10, 10)
                    };
                    notifyIconContextMenu.Items.Insert(notifyIconContextMenu.Items.Count - 1, separator);
                }

                // Load all of the servers into the server selector
                List<ServerList> currentJson = JsonSerializer.Deserialize<List<ServerList>>(File.ReadAllText(customServersJsonFile));
                foreach (ServerList server in currentJson)
                {
                    ComboBoxItem newItem = new()
                    {
                        Content = server.CustomServerName,
                        Style = (Style)FindResource("ComboBoxItemStyle")
                    };
                    serverSelector.Items.Insert(serverSelector.Items.Count - 1, newItem);
                }

                if (serverSelector.Items.Count > 4)
                {
                    Separator separator = new()
                    {
                        Style = (Style)FindResource("SeparatorMenuItem"),
                        Background = new SolidColorBrush(Color.FromRgb(66, 66, 66))
                    };
                    serverSelector.Items.Insert(serverSelector.Items.Count - 1, separator);
                }

                // Add an event for only user added servers in the list to delete on right click
                for (int i = 0; i <= serverSelector.Items.Count - 1; i++)
                {
                    if (serverSelector.Items[i] is ComboBoxItem serverItem && i > 2 && i < serverSelector.Items.Count - 2)
                    {
                        ContextMenu itemContextMenu = new()
                        {
                            Style = (Style)FindResource("ContextMenuStyle")
                        };
                        serverItem.ContextMenu = itemContextMenu;

                        MenuItem editOptionCustom = new()
                        {
                            Style = (Style)FindResource("CustomMenuItem"),
                            Margin = new Thickness(0, 0, 0, 6)
                        };
                        System.Windows.Shapes.Path pathCustom = new()
                        {
                            Data = (PathGeometry)FindResource("EditIcon"),
                            Stretch = Stretch.Uniform
                        };
                        Binding bindingCustom = new("Foreground")
                        {
                            Source = editOptionCustom,
                            Mode = BindingMode.OneWay
                        };
                        BindingOperations.SetBinding(pathCustom, System.Windows.Shapes.Path.FillProperty, bindingCustom);

                        editOptionCustom.Icon = pathCustom;
                        editOptionCustom.SetResourceReference(HeaderedItemsControl.HeaderProperty, "item212");
                        editOptionCustom.Click += (s, e) => { EditServer(serverItem); };

                        Separator separator = new()
                        {
                            Style = (Style)FindResource("SeparatorMenuItem"),
                            Background = new SolidColorBrush(Color.FromRgb(66, 66, 66)),
                            Margin = new Thickness(10, 2, 10, 10)
                        };

                        MenuItem deleteOptionCustom = new()
                        {
                            Style = (Style)FindResource("CustomMenuItem"),
                            Foreground = new SolidColorBrush(Color.FromRgb(255, 71, 76))
                        };
                        System.Windows.Shapes.Path pathDeleteCustom = new()
                        {
                            Data = (PathGeometry)FindResource("BinIcon"),
                            Stretch = Stretch.Uniform
                        };
                        Binding bindingDeleteCustom = new("Foreground")
                        {
                            Source = deleteOptionCustom,
                            Mode = BindingMode.OneWay
                        };
                        BindingOperations.SetBinding(pathDeleteCustom, System.Windows.Shapes.Path.FillProperty, bindingDeleteCustom);

                        deleteOptionCustom.Icon = pathDeleteCustom;
                        deleteOptionCustom.SetResourceReference(HeaderedItemsControl.HeaderProperty, "item192");
                        deleteOptionCustom.Click += (s, e) => { DeleteServer(serverItem); };

                        itemContextMenu.Items.Add(editOptionCustom);
                        itemContextMenu.Items.Add(separator);
                        itemContextMenu.Items.Add(deleteOptionCustom);
                    }
                }
            }
            catch (Exception e)
            {
                CustomMessageBox.Show($"{FindResource("item184")} \"{e.Message}\".", this);
            }

            serverSelector.SelectedIndex = Properties.Settings.Default.lastServer;
        }

        private void AddNewServerClick(object sender, MouseButtonEventArgs e)
        {
            AddItemWindow addServer = new()
            {
                Owner = this,
                itemType = 1
            };
            addServer.ShowDialog();
        }

        public async void EditServer(ComboBoxItem serverItem)
        {
            List<ServerList> currentJson = JsonSerializer.Deserialize<List<ServerList>>(File.ReadAllText(customServersJsonFile));
            for (int i = currentJson.Count - 1; i >= 0; i--)
            {
                if (currentJson[i].CustomServerName == (string)serverItem.Content)
                {
                    AddItemWindow editServer = new()
                    {
                        Owner = this,
                        itemType = 1
                    };
                    editServer.primaryTextBox.Text = currentJson[i].CustomServerName;
                    editServer.secondaryTextBox.Text = currentJson[i].CustomServerIp;
                    editServer.primaryTextBoxHint.Visibility = Visibility.Hidden;
                    editServer.secondaryTextBoxHint.Visibility = Visibility.Hidden;
                    editServer.editItem = true;
                    editServer.editIndex = i;

                    await Task.Run(() =>
                    {
                        Dispatcher.Invoke(new Action(delegate
                        {
                            editServer.ShowDialog();
                        }));
                    });

                    serverSelector.IsDropDownOpen = false;
                    break;
                }
            }
        }

        public void DeleteServer(ComboBoxItem serverItem)
        {
            MessageBoxResult mbr = CustomMessageBox.Show(FindResource("item147").ToString(), this, false, true, true);
            if (mbr != MessageBoxResult.Yes)
                return;

            // Delete the server from the custom servers file list
            List<ServerList> currentJson = JsonSerializer.Deserialize<List<ServerList>>(File.ReadAllText(customServersJsonFile));
            for (int i = currentJson.Count - 1; i >= 0; i--)
            {
                if (currentJson[i].CustomServerName == (string)serverItem.Content) 
                {
                    currentJson.Remove(currentJson[i]);
                    if (serverSelector.SelectedItem == serverItem)
                    {
                        if (i + 5 < serverSelector.Items.Count - 2)
                    serverSelector.SelectedIndex = i + 5;
                else
                    serverSelector.SelectedIndex = i + 3;
                    }
                    break;
                }
            }

            string newJson = JsonSerializer.Serialize(currentJson, jsonSerializerOptions);
            File.WriteAllText(customServersJsonFile, newJson);

            // Delete the server from the recent servers file list, used for the system tray icon context menu
            List<ServerListRecent> currentJsonRecent = JsonSerializer.Deserialize<List<ServerListRecent>>(File.ReadAllText(recentServersJsonFile));
            for (int i = currentJsonRecent.Count - 1; i >= 0; i--)
            {
                if (currentJsonRecent[i].CustomServerNameRecent == (string)serverItem.Content)
                {
                    currentJsonRecent.Remove(currentJsonRecent[i]);
                    for (int j = notifyIconContextMenu.Items.Count - 1; j >= 0; j--)
                    {
                        if (notifyIconContextMenu.Items[j] is not MenuItem)
                            continue;

                        MenuItem item = (MenuItem)notifyIconContextMenu.Items[j];
                        if ((string)item.Header == (string)serverItem.Content)
                        {
                            notifyIconContextMenu.Items.Remove(item);
                            break;
                        }
                    }
                    break;
                }
            }

            string newJsonRecent = JsonSerializer.Serialize(currentJsonRecent, jsonSerializerOptions);
            File.WriteAllText(recentServersJsonFile, newJsonRecent);

            serverSelector.Items.Remove(serverItem);

            if (notifyIconContextMenu.Items.Count == 5)
                notifyIconContextMenu.Items.RemoveAt(notifyIconContextMenu.Items.Count - 2);

            if (serverSelector.Items.Count == 5)
                serverSelector.Items.RemoveAt(serverSelector.Items.Count - 2);
        }

        private void AddServerToRecentList(string name)
        {
            Dispatcher.Invoke(new Action(delegate
            {
                try
                {
                    // Remove the item and add it back again so that the most recently played server is at the top of the list
                    List<ServerListRecent> currentJsonRecent = JsonSerializer.Deserialize<List<ServerListRecent>>(File.ReadAllText(recentServersJsonFile));
                    for (int i = currentJsonRecent.Count - 1; i >= 0; i--)
                    {
                        if (currentJsonRecent[i].CustomServerNameRecent == serverSelector.Text)
                        {
                            currentJsonRecent.Remove(currentJsonRecent[i]);
                            for (int j = notifyIconContextMenu.Items.Count - 1; j >= 0; j--)
                            {
                                if (notifyIconContextMenu.Items[j] is not MenuItem)
                                    continue;

                                MenuItem item = (MenuItem)notifyIconContextMenu.Items[j];
                                if ((string)item.Header == name)
                                {
                                    notifyIconContextMenu.Items.Remove(item);
                                    break;
                                }
                            }
                            break;
                        }
                    }

                    currentJsonRecent.Add(new ServerListRecent()
                    {
                        CustomServerNameRecent = name
                    });

                    string newJsonRecentServers = JsonSerializer.Serialize(currentJsonRecent, jsonSerializerOptions);
                    File.WriteAllText(recentServersJsonFile, newJsonRecentServers);

                    MenuItem playCustomServer = new()
                    {
                        Style = (Style)FindResource("CustomMenuItem"),
                        Margin = new Thickness(0, 0, 0, 6),
                        Header = name
                    };
                    System.Windows.Shapes.Path pathCustomServerPlay = new()
                    {
                        Data = (PathGeometry)FindResource("PlayIcon"),
                        Stretch = Stretch.Uniform,
                        Width = 14,
                        Height = 14
                    };
                    Binding bindingCustomServerPlay = new("Foreground")
                    {
                        Source = playCustomServer,
                        Mode = BindingMode.OneWay
                    };
                    BindingOperations.SetBinding(pathCustomServerPlay, System.Windows.Shapes.Path.StrokeProperty, bindingCustomServerPlay);

                    playCustomServer.Icon = pathCustomServerPlay;
                    playCustomServer.Click += LaunchToCustomServerFromNotifyIcon;
                    notifyIconContextMenu.Items.Insert(3, playCustomServer);

                    if (notifyIconContextMenu.Items.Count == 5)
                    {
                        Separator separator = new()
                        {
                            Style = (Style)FindResource("SeparatorMenuItem"),
                            Background = new SolidColorBrush(Color.FromRgb(66, 66, 66)),
                            Margin = new Thickness(10, 2, 10, 10)
                        };
                        notifyIconContextMenu.Items.Insert(notifyIconContextMenu.Items.Count - 1, separator);
                    }

                    if (notifyIconContextMenu.Items.Count > 10)
                    {
                        MenuItem item = (MenuItem)notifyIconContextMenu.Items[notifyIconContextMenu.Items.Count - 3];
                        for (int i = currentJsonRecent.Count - 1; i >= 0; i--)
                        {
                            if (currentJsonRecent[i].CustomServerNameRecent == (string)item.Header)
                                currentJsonRecent.Remove(currentJsonRecent[i]);
                        }

                        notifyIconContextMenu.Items.RemoveAt(notifyIconContextMenu.Items.Count - 3);
                        newJsonRecentServers = JsonSerializer.Serialize(currentJsonRecent, jsonSerializerOptions);
                        File.WriteAllText(recentServersJsonFile, newJsonRecentServers);
                    }
                }
                catch (Exception e)
                {
                    CustomMessageBox.Show($"{FindResource("item142")} {e.Message}", this);
                }
            }));
        }

        Process startSingleplayerServerProcess;
        public bool LaunchLocalServer()
        {
            try
            {
                if (!Directory.Exists($"{Properties.Settings.Default.activeDirectory}\\H1EmuServerFiles\\h1z1-server-QuickStart-master\\node_modules"))
                {
                    MessageBoxResult mbr = MessageBoxResult.None;
                    Dispatcher.Invoke(new Action(delegate
                    {
                        mbr = CustomMessageBox.InstallServerInline(FindResource("item52").ToString().Replace("\\n\\n", $"{Environment.NewLine}{Environment.NewLine}"), this);
                    }));

                    if (mbr != MessageBoxResult.Yes)
                        return false;
                }

                startSingleplayerServerProcess = new Process { StartInfo = cmdShell };
                startSingleplayerServerProcess.Start();
                using (StreamWriter sw = startSingleplayerServerProcess.StandardInput)
                {
                    if (sw.BaseStream.CanWrite)
                    {
                        sw.WriteLine($"SET PATH={Properties.Settings.Default.activeDirectory}\\H1EmuServerFiles\\h1z1-server-QuickStart-master\\node-v{Info.NODEJS_VERSION}-win-x64");
                        sw.WriteLine($"cd /d {Properties.Settings.Default.activeDirectory}\\H1EmuServerFiles\\h1z1-server-QuickStart-master");
                        sw.WriteLine("npm run start-2016");
                    }
                }
                startSingleplayerServerProcess.WaitForExit(5000);

                if (startSingleplayerServerProcess.HasExited)
                {
                    Dispatcher.Invoke(new Action(delegate
                    {
                        CustomMessageBox.Show(FindResource("item168").ToString().Replace("\\n\\n", $"{Environment.NewLine}{Environment.NewLine}"), this);
                    }));
                    return false;
                }
            }
            catch (Exception e)
            {
                Dispatcher.Invoke(new Action(delegate
                {
                    CustomMessageBox.Show($"{FindResource("item53")} \"{e.Message}\"", this);
                }));
                return false;
            }

            return true;
        }

        private async void LaunchClient(object sender, RoutedEventArgs e)
        {
            if (!Properties.Settings.Default.developerMode)
            {
                playButton.IsEnabled = false;
                playButton.SetResourceReference(ContentProperty, "item217");
            }

            // KOTK mode: own game files from the KOTK server, played through the launcher's tunnel.
            if (EditionKotK.Selected)
            {
                await LaunchKotK();
                return;
            }

            // 2018 mode: separate game folder, own LaunchPad, always the JSEmu 2018 server.
            if (Edition2018.Selected)
            {
                Launch2018();
                playButton.IsEnabled = true;
                playButton.SetResourceReference(ContentProperty, "item8");
                return;
            }

            if (!CheckGameVersionAndPath(this, false, true))
            {
                playButton.IsEnabled = true;
                playButton.SetResourceReference(ContentProperty, "item8");
                return;
            }

            string serverIp = string.Empty;
            string sessionId = string.Empty;
            int serverIndex = serverSelector.SelectedIndex;
            Properties.Settings.Default.sessionIdKey = Properties.Settings.Default.sessionIdKey?.Trim() ?? string.Empty;

            try
            {
                switch (serverIndex)
            {
                case 0:

                    
                    // JSEMU_AUTH_ENDPOINT_CASE_0
                    Info.ACCOUNT_KEY_CHECK_API = Info.JSEMU_ACCOUNT_KEY_CHECK_API;
                    // END_JSEMU_AUTH_ENDPOINT_CASE_0
// JSEmu Servers
                    if (string.IsNullOrEmpty(Properties.Settings.Default.sessionIdKey))
                    {
                        MessageBoxResult mbr = CustomMessageBox.Show(FindResource("item153").ToString(), this, false, true, true);

                        if (mbr != MessageBoxResult.Yes)
                            throw new Exception("emptyAccountKey");
                        else
                            throw new Exception("createAccountKey");
                    }
                    sessionId = $"{{\"sessionId\":\"{AccountKeyUtil.EncryptStringSHA256(Properties.Settings.Default.sessionIdKey)}\",\"gameVersion\":2}}";
                    serverIp = Info.H1EMU_SERVER_IP;

                    break;

                case 1:

                    // Official H1Emu Servers
                    // IMPORTANT: H1Emu must receive the RAW account key, not hashed JSEmu key.
                    string h1emuRawAuthKey = Properties.Settings.Default.sessionIdKey?.Trim() ?? string.Empty;

                    if (string.IsNullOrEmpty(h1emuRawAuthKey))
                    {
                        MessageBoxResult mbr = CustomMessageBox.Show(FindResource("item153").ToString(), this, false, true, true);

                        if (mbr != MessageBoxResult.Yes)
                            throw new Exception("emptyAccountKey");
                        else
                            throw new Exception("createAccountKey");
                    }

                    HttpResponseMessage h1emuAuthResponse = await SplashWindow.httpClient.GetAsync(
                        Info.H1EMU_ACCOUNT_KEY_CHECK_API + Uri.EscapeDataString(h1emuRawAuthKey)
                    );

                    if ((int)h1emuAuthResponse.StatusCode != 200)
                    {
                        throw new Exception("H1Emu Account Key is not verified. Paste your H1Emu key in Account Key tab.");
                    }

                    // Debug without exposing full key
                    try
                    {
                        string debugPath = $"{Info.APPLICATION_DATA_PATH}\\JSEmu Launcher\\jsemu-h1emu-auth-debug.txt";
                        string keyPreview = h1emuRawAuthKey.Length > 12
                            ? h1emuRawAuthKey.Substring(0, 6) + "..." + h1emuRawAuthKey.Substring(h1emuRawAuthKey.Length - 6)
                            : h1emuRawAuthKey;

                        File.AppendAllText(
                            debugPath,
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] H1Emu launch serverIp={Info.H1EMU_OFFICIAL_SERVER_IP}, keyLength={h1emuRawAuthKey.Length}, keyPreview={keyPreview}{Environment.NewLine}"
                        );
                    }
                    catch { }

                    sessionId = $"{{\"sessionId\":\"{h1emuRawAuthKey}\",\"gameVersion\":2}}";
                    serverIp = Info.H1EMU_OFFICIAL_SERVER_IP;

                    break;

                case 2:

                    // Singleplayer
                    if (!LaunchLocalServer())
                        throw new Exception("launchLocalServerFailed");

                    sessionId = $"{{\"sessionId\":\"0\",\"gameVersion\":2}}";
                    serverIp = "localhost:1115";

                    break;

                default:
                        List<ServerList> currentJson = JsonSerializer.Deserialize<List<ServerList>>(File.ReadAllText(customServersJsonFile));
                        foreach (ServerList item in currentJson)
                        {
                            if (item.CustomServerName == serverSelector.Text)
                            {
                                sessionId = $"{{\"sessionId\":\"{AccountKeyUtil.EncryptStringSHA256(Properties.Settings.Default.sessionIdKey)}\",\"gameVersion\":2}}";
                                serverIp = item.CustomServerIp;
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                switch (ex.Message)
                {
                    case "emptyAccountKey":
                    case "launchLocalServerFailed":
                        break;

                    case "createAccountKey":
                        SettingsWindow sw = new();
                        sw.settingsTabControl.SelectedIndex = 2;
                        sw.ShowDialog();
                        break;

                    default:
                        CustomMessageBox.Show($"{FindResource("item142")} \"{ex.Message}\"", this);
                        break;
                }

                playButton.IsEnabled = true;
                playButton.SetResourceReference(ContentProperty, "item8");
                return;
            }

            try
            {
                string arguments = $"sessionid={sessionId} gamecrashurl={Info.GAME_CRASH_URL} server={serverIp}";

                // Check that the patch is the latest version and prevent playing if trying to connect to H1Emu Servers
                if (!Properties.Settings.Default.developerMode && !await InstallPatchClass.InstallPatch() && serverIndex != 2)
                    return;

                // Check that the launcher is the latest version and prevent playing if trying to connect to H1Emu Servers
                if (SplashWindow.checkForUpdates && !await SplashWindow.CheckVersion(this) && serverIndex != 2)
                    return;

                // If user has Steam enabled then add it to the launch arguments
                if (Properties.Settings.Default.steamEnabled)
                    arguments += " STEAM_ENABLED=1";

                // Kill any lingering processes to prevent bugs
                if (!Properties.Settings.Default.developerMode)
                    KillProcesses();

                // Launch game
                Process h1Process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = $"{Properties.Settings.Default.activeDirectory}\\H1Z1.exe",
                        Arguments = arguments,
                        WindowStyle = ProcessWindowStyle.Normal,
                        WorkingDirectory = Properties.Settings.Default.activeDirectory,
                        UseShellExecute = true,
                        Verb = "runas"
                    },
                    EnableRaisingEvents = true
                };
                h1Process.Exited += (o, s) =>
                {
                    Dispatcher.Invoke(new Action(delegate
                    {
                        if (Visibility == Visibility.Hidden)
                        {
                            Show();
                            Activate();
                        }

                        playButton.IsEnabled = true;
                        playButton.SetResourceReference(ContentProperty, "item8");
                    }));

                    if (!Properties.Settings.Default.developerMode && startSingleplayerServerProcess != null)
                        startSingleplayerServerProcess.Kill(true);
                };
                h1Process.Start();

                if (serverSelector.SelectedIndex != 0 && serverSelector.SelectedIndex != 2 && serverSelector.SelectedIndex != 2 && serverSelector.SelectedIndex != serverSelector.Items.Count - 1 && serverSelector.SelectedItem is ComboBoxItem)
                    AddServerToRecentList(serverSelector.Text);

                if (Properties.Settings.Default.autoMinimise && Visibility == Visibility.Visible)
                {
                    Hide();

                    // Windows 11 may keep the icon in the overflow flyout, so say where the launcher went.
                    launcherNotifyIcon.Visible = true;
                    launcherNotifyIcon.ShowBalloonTip(5000, "JSEmu Launcher", "Launcher is running in the system tray. Click its icon to bring it back.", System.Windows.Forms.ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                playButton.IsEnabled = true;
                playButton.SetResourceReference(ContentProperty, "item8");
                CustomMessageBox.Show($"{FindResource("item13")}\n\n{e.GetType().Name}: \"{ex.Message}\".", this);
            }
        }

        public static void KillProcesses()
        {
            string[] processNames = { "H1EmuVoiceClient", "H1Z1", "H1Z1_FP", "H1Z1_BE" };
            foreach (string name in processNames)
            {
                foreach (Process p in Process.GetProcessesByName(name))
                    p.Kill(true);
            }
        }

        private void LaunchToCustomServerFromNotifyIcon(object sender, RoutedEventArgs e)
        {
            try
            {
                MenuItem clickedMenuItem = (MenuItem)sender;
                for (int i = 0; i <= serverSelector.Items.Count - 1; i++)
                {
                    if (serverSelector.Items[i] is not ComboBoxItem)
                        continue;

                    ComboBoxItem serverSelectorItem = (ComboBoxItem)serverSelector.Items[i];
                    if ((string)serverSelectorItem.Content == (string)clickedMenuItem.Header)
                    {
                        serverSelector.SelectedIndex = i;
                        playButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"{FindResource("item142")} {ex.Message}", this);
            }
        }

        // ---- Game edition switch (2016 / 2018) --------------------------------------------
        private void EditionButtonClick(object sender, RoutedEventArgs e)
        {
            string edition = (sender as Button)?.Tag as string ?? "2016";
            if (Properties.Settings.Default.gameEdition == edition)
                return;

            Properties.Settings.Default.gameEdition = edition;
            Properties.Settings.Default.Save();
            ShowEdition();
            if (edition == "2016")
                CheckGameVersionAndPath(this, false, false);
        }

        // Highlights the active edition and shows its game folder.
        public void ShowEdition()
        {
            bool is2018 = Edition2018.Selected;
            bool isKotK = EditionKotK.Selected;
            bool is2016 = !is2018 && !isKotK;

            // KOTK replaces everything under the top chrome with its own layout.
            classicLayout.Visibility = isKotK ? Visibility.Collapsed : Visibility.Visible;
            kotkLayout.Visibility = isKotK ? Visibility.Visible : Visibility.Collapsed;

            // Accent: JSEmu red for 2016, amber for 2018, KOTK crown gold.
            Color accent = is2018 ? Color.FromRgb(0xE8, 0xA3, 0x3D) : isKotK ? Color.FromRgb(0xF2, 0xB2, 0x33) : Color.FromRgb(0xE1, 0x1D, 0x27);
            Color accentDark = is2018 ? Color.FromRgb(0xB8, 0x6A, 0x1A) : isKotK ? Color.FromRgb(0xC9, 0x8A, 0x12) : Color.FromRgb(0x9E, 0x10, 0x18);
            SolidColorBrush accentBrush = new(accent);
            SolidColorBrush idleText = new(Color.FromRgb(0x9A, 0x9F, 0xA6));
            SolidColorBrush idleSub = new(Color.FromRgb(0x6B, 0x70, 0x78));
            edition2016Button.Background = is2016 ? accentBrush : Brushes.Transparent;
            edition2018Button.Background = is2018 ? accentBrush : Brushes.Transparent;
            editionKotKButton.Background = isKotK ? accentBrush : Brushes.Transparent;
            edition2016Button.Foreground = is2016 ? Brushes.White : idleText;
            edition2018Button.Foreground = is2018 ? new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x08)) : idleText;
            editionKotKButton.Foreground = isKotK ? new SolidColorBrush(Color.FromRgb(0x1A, 0x13, 0x05)) : idleText;
            edition2016Button.BorderBrush = is2016 ? new SolidColorBrush(Color.FromRgb(0xFF, 0x4A, 0x52)) : Brushes.Transparent;
            edition2018Button.BorderBrush = is2018 ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x80)) : Brushes.Transparent;
            editionKotKButton.BorderBrush = isKotK ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD3, 0x6A)) : Brushes.Transparent;
            edition2016Sub.Foreground = is2016 ? new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)) : idleSub;
            edition2018Sub.Foreground = is2018 ? new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x12, 0x08)) : idleSub;
            editionKotKSub.Foreground = isKotK ? new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x13, 0x05)) : idleSub;

            playButton.BorderBrush = accentBrush;
            if (!is2016)
            {
                playButton.Background = new LinearGradientBrush(accent, accentDark, 90);
                playButton.Foreground = is2018 ? new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x08)) : Brushes.White;
            }
            else
            {
                playButton.ClearValue(Button.BackgroundProperty);   // back to the PrimaryButton style
                playButton.ClearValue(Button.ForegroundProperty);
            }
            directoryBox.BorderBrush = new SolidColorBrush(is2018 ? Color.FromRgb(0x8A, 0x5A, 0x1E)
                : isKotK ? Color.FromRgb(0x1E, 0x5A, 0x8A) : Color.FromRgb(0x7F, 0x25, 0x2A));

            // 2018 gets a tinted banner, an edition badge and a fixed server card instead of the list.
            tint2018.Visibility = is2018 ? Visibility.Visible : Visibility.Collapsed;
            badge2018.Visibility = is2018 ? Visibility.Visible : Visibility.Collapsed;
            server2018Info.Visibility = is2018 ? Visibility.Visible : Visibility.Collapsed;
            serverSelector.Visibility = is2016 ? Visibility.Visible : Visibility.Hidden;
            serverSelectorIcon.Visibility = is2016 ? Visibility.Visible : Visibility.Hidden;
            serverSelector.IsEnabled = is2016;
            directoryButton.IsEnabled = !is2018;
            ShowKotKPanel(isKotK);

            if (isKotK)
            {
                ShowKotKInstall();
                return;
            }

            if (is2016)
            {
                directoryBox.Text = string.IsNullOrEmpty(Properties.Settings.Default.activeDirectory)
                    ? FindResource("item75").ToString() : Properties.Settings.Default.activeDirectory;
                return;
            }

            // 2018: prefer an install Steam knows about, then a folder saved after a download.
            string dir = Edition2018.FindInSteam();
            if (dir == null && Edition2018.IsValidInstall(Properties.Settings.Default.activeDirectory2018))
                dir = Properties.Settings.Default.activeDirectory2018;
            if (dir != null && dir != Properties.Settings.Default.activeDirectory2018)
            {
                Properties.Settings.Default.activeDirectory2018 = dir;
                Properties.Settings.Default.Save();
            }
            directoryBox.Text = dir ?? "Just Survive 2018: not installed - log in with Steam below to download it";
            currentGame.Text = dir == null ? "Game Version: 2018 not installed"
                : Edition2018.IsValidInstall(dir) ? "Current Game Version: 2018" : "Game Version: not the 2018 build";

            // Put our LaunchPad and its settings in place right away - Steam's own Play button
            // never goes through this launcher.
            Task.Run(() => Edition2018.EnsureGameDirReady(dir));
        }

        // Play in 2018 mode. Returns when the game was started (or an error was shown).
        private void Launch2018()
        {
            string dir = Edition2018.FindInSteam();
            if (dir == null && Edition2018.IsValidInstall(Properties.Settings.Default.activeDirectory2018))
                dir = Properties.Settings.Default.activeDirectory2018;

            if (dir == null)
            {
                CustomMessageBox.Show("Just Survive 2018 is not installed.\n\nLog in with your Steam account in the panel below - " +
                    "the launcher will download the 2018 version into your Steam library.", this);
                return;
            }

            if (!Edition2018.IsValidInstall(dir))
            {
                CustomMessageBox.Show($"The game in\n{dir}\nis not the 2018 build (2.1.886508). Update it in Steam or remove it and download again.", this);
                return;
            }

            if (string.IsNullOrEmpty(Properties.Settings.Default.sessionIdKey?.Trim()))
            {
                CustomMessageBox.Show(FindResource("item153").ToString(), this);
                return;
            }

            try
            {
                Edition2018.PrepareGameDir(dir);
                Edition2018.Launch(dir);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"{FindResource("item142")} \"{ex.Message}\"", this);
            }
        }

        private void LauncherWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Delete old setup file
            if (File.Exists($"{Info.APPLICATION_DATA_PATH}\\JSEmu Launcher\\{UpdateWindow.installerFileName}"))
                File.Delete($"{Info.APPLICATION_DATA_PATH}\\JSEmu Launcher\\{UpdateWindow.installerFileName}");

            if (Properties.Settings.Default.language == 1)
                chineseLink.Visibility = Visibility.Visible;

            DisplayVersionInformation();
            LoadServers();
            CheckGameVersionAndPath(this, false, false);
            ShowEdition();
            // Also when the 2016 tab is the active one: a Steam "verify files" puts the Daybreak
            // LaunchPad back, and players start the game from Steam without opening us.
            Task.Run(() => Edition2018.EnsureGameDirReady());
            Carousel.BeginImageCarousel();
            if (!Properties.Settings.Default.imageCarouselVisibility)
            {
                // Show image carousel
                Carousel.playCarousel.Stop();
                imageCarousel.Visibility = Visibility.Visible;
            }
        }

        public static string[] rawArgs;
        private async void LauncherWindowContentRendered(object sender, EventArgs e)
        {
            _ = UpdateHubCoins();

            if (rawArgs != null)
                await ExecuteArguments(rawArgs);
        }

        public async void DisplayVersionInformation()
        {
            try
            {
                                // JSEmu custom update panel
                await Task.CompletedTask;

                string latestVersion = "Updates";
                string latestPatchNotes =
                    "# Latest Updates\n\n" +
                    "All server updates, fixes, events and patch notes are now posted in the official JSEmu Discord update channel.\n\n" +
                    "Click the link below to open the update channel.";

                datePublished.Text = string.Empty;
                updateVersion.Text = $" {latestVersion}";
                patchNotesBox.Text = latestPatchNotes;

                // Cache custom JSEmu update text
                Properties.Settings.Default.latestServerVersion = latestVersion;
                Properties.Settings.Default.patchNotes = latestPatchNotes;
                Properties.Settings.Default.publishDate = System.DateTime.Now;
                Properties.Settings.Default.Save();
            }
            catch
            {
                updateVersion.Text = $" {Properties.Settings.Default.latestServerVersion}";
                datePublished.Text = $" ({Properties.Settings.Default.publishDate:dd MMMM yyyy})";
                patchNotesBox.Text = Properties.Settings.Default.patchNotes;
            }
        }

        public bool doContinue = true;
        private void StoryboardCompleted(object sender, EventArgs e)
        {
            Carousel.playCarousel.Stop();
            Carousel.playCarousel.Begin();
            doContinue = true;
        }

        private void PrevImageClick(object sender, RoutedEventArgs e)
        {
            if (!doContinue)
                return;

            Carousel.PreviousImage();
            doContinue = false;
        }

        private void NextImageClick(object sender, RoutedEventArgs e)
        {
            if (!doContinue)
                return;

            Carousel.NextImage();
            doContinue = false;
        }

        readonly double carouselButtonsAnimationDurationMilliseconds = 100;

        private void CarouselMouseEnter(object sender, MouseEventArgs e)
        {
            if (Carousel.playCarousel == null)
                return;

            Carousel.playCarousel.Pause();

            // Animation for fade in visibility next image button
            DoubleAnimation showImageControls = new(0.2, new Duration(TimeSpan.FromMilliseconds(carouselButtonsAnimationDurationMilliseconds)), FillBehavior.Stop)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            showImageControls.Completed += (o, s) => { nextImage.Opacity = 0.2; prevImage.Opacity = 0.2; };
            nextImage.BeginAnimation(Window.OpacityProperty, showImageControls);
            prevImage.BeginAnimation(Window.OpacityProperty, showImageControls);
        }

        private void CarouselMouseLeave(object sender, MouseEventArgs e)
        {
            if (Carousel.playCarousel == null)
                return;

            Carousel.playCarousel.Resume();

            // Animation for fade out visibility next image button
            DoubleAnimation hideImageControls = new(0, new Duration(TimeSpan.FromMilliseconds(carouselButtonsAnimationDurationMilliseconds)), FillBehavior.Stop)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            hideImageControls.Completed += (o, s) => { nextImage.Opacity = 0; prevImage.Opacity = 0; };
            nextImage.BeginAnimation(Window.OpacityProperty, hideImageControls);
            prevImage.BeginAnimation(Window.OpacityProperty, hideImageControls);
        }

        public void SelectDirectory(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog selectDirectory = new();
            if (!(bool)selectDirectory.ShowDialog())
                return;

            if (EditionKotK.Selected)
            {
                SelectKotKDirectory(selectDirectory.FolderName);
                return;
            }

            Properties.Settings.Default.activeDirectory = selectDirectory.FolderName;
            Properties.Settings.Default.Save();

            new Thread(() =>
            {
                CheckGameVersionAndPath(this, true, true);
            }).Start();
        }

        public void OpenDirectory(object sender, RoutedEventArgs e)
        {
            string openDir = EditionKotK.Selected ? EditionKotK.GameDirectory : Properties.Settings.Default.activeDirectory;
            if (!Directory.Exists(openDir))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = openDir,
                UseShellExecute = true
            });
        }

        public bool CheckGameVersionAndPath(Window callingWindow, bool showSuccess = true, bool showErrors = true)
        {
            Dispatcher.Invoke(new Action(delegate
            {
                currentGame.SetResourceReference(TextBlock.TextProperty, "item70");
                taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Indeterminate;
                directoryButton.IsEnabled = false;
            }));

            Properties.Settings.Default.gameVersionString = string.Empty;
            Properties.Settings.Default.Save();

            if (!Directory.Exists(Properties.Settings.Default.activeDirectory))
            {
                Dispatcher.Invoke(new Action(delegate
                {
                    Properties.Settings.Default.activeDirectory = "Directory";
                    Properties.Settings.Default.Save();

                    directoryBox.Text = Properties.Settings.Default.activeDirectory;
                    currentGame.SetResourceReference(TextBlock.TextProperty, "item69");
                    taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
                    directoryButton.IsEnabled = true;

                    if (showErrors)
                    {
                        if (SettingsWindow.settingsInstance != null && SettingsWindow.settingsInstance.Visibility == Visibility.Visible)
                            CustomMessageBox.Show(FindResource("item14").ToString(), callingWindow);
                        else
                            CustomMessageBox.Show($"{FindResource("item14")}\n\n{FindResource("item9")}", callingWindow);
                    }
                }));
                return false;
            }
            else if (!File.Exists($"{Properties.Settings.Default.activeDirectory}\\h1z1.exe"))
            {
                Dispatcher.Invoke(new Action(delegate
                {
                    directoryBox.Text = Properties.Settings.Default.activeDirectory;
                    currentGame.SetResourceReference(TextBlock.TextProperty, "item69");
                    taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
                    directoryButton.IsEnabled = true;

                    if (showErrors)
                    {
                        if (SettingsWindow.settingsInstance != null && SettingsWindow.settingsInstance.Visibility == Visibility.Visible)
                            CustomMessageBox.Show(FindResource("item14").ToString(), callingWindow);
                        else
                            CustomMessageBox.Show($"{FindResource("item14")}\n\n{FindResource("item9")}", callingWindow);
                    }
                }));
                return false;
            }

            Crc32 crc32 = new();
            string hash = string.Empty;

            try
            {
                using FileStream fs = File.Open($"{Properties.Settings.Default.activeDirectory}\\h1z1.exe", FileMode.Open);
                foreach (byte b in crc32.ComputeHash(fs)) hash += b.ToString("x2").ToLower();
            }
            catch (IOException)
            {
                Properties.Settings.Default.gameVersionString = "processBeingUsed";
                Properties.Settings.Default.Save();

                Dispatcher.Invoke(new Action(delegate
                {
                    directoryBox.Text = Properties.Settings.Default.activeDirectory;
                    currentGame.SetResourceReference(TextBlock.TextProperty, "item120");
                    taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
                    directoryButton.IsEnabled = true;
                    
                    if (showErrors)
                        CustomMessageBox.Show(FindResource("item121").ToString().Replace("\\n\\n", $"{Environment.NewLine}{Environment.NewLine}"), callingWindow, true, false, false, true);
                }));
                return false;
            }
            catch (Exception e)
            {
                Properties.Settings.Default.gameVersionString = string.Empty;
                Properties.Settings.Default.Save();

                Dispatcher.Invoke(new Action(delegate
                {
                    directoryBox.Text = Properties.Settings.Default.activeDirectory;
                    currentGame.SetResourceReference(TextBlock.TextProperty, "item148");
                    taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
                    directoryButton.IsEnabled = true;

                    if (showErrors)
                        CustomMessageBox.Show($"{FindResource("item142")} \"{e.Message}\".", callingWindow);
                }));
                return false;
            }

            switch (hash)
            {
                case "bc5b3ab6": // Just Survive: 22nd December 2016
                    Properties.Settings.Default.gameVersionString = "22dec2016";
                    Properties.Settings.Default.Save();

                    Dispatcher.Invoke(new Action(delegate
                    {
                        directoryBox.Text = Properties.Settings.Default.activeDirectory;
                        currentGame.SetResourceReference(TextBlock.TextProperty, "item122");
                        taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
                        directoryButton.IsEnabled = true;

                        if (showSuccess)
                            CustomMessageBox.Show(FindResource("item74").ToString(), callingWindow);
                    }));
                    return true;

                default:
                    Properties.Settings.Default.gameVersionString = hash;
                    Properties.Settings.Default.Save();

                    Dispatcher.Invoke(new Action(delegate
                    {
                        directoryBox.Text = Properties.Settings.Default.activeDirectory;
                        currentGame.SetResourceReference(TextBlock.TextProperty, "item72");
                        taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
                        directoryButton.IsEnabled = true;

                        if (showErrors)
                            CustomMessageBox.Show(FindResource("item58").ToString().Replace("\\n\\n", $"{Environment.NewLine}{Environment.NewLine}"), callingWindow);
                    }));
                    return false;
            }
        }

        private void SettingsButtonClick(object sender, RoutedEventArgs e)
        {
            SettingsWindow sw = new();
            sw.ShowDialog();
        }

        private void OpenDressingRoom(object sender, RoutedEventArgs e)
        {
            string accountKey = Properties.Settings.Default.sessionIdKey?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(accountKey))
            {
                CustomMessageBox.Show("Enter your Account Key in Settings first - the Dressing Room uses it to find your skins.", this);
                return;
            }

            DressingRoomWindow window = new(AccountKeyUtil.EncryptStringSHA256(accountKey)) { Owner = this };
            window.ShowDialog();
        }

        private void OpenHub(object sender, RoutedEventArgs e)
        {
            if (HubApi.KeyHash() == null)
            {
                CustomMessageBox.Show("Enter your Account Key in Settings first - the Hub uses it to find your account.", this);
                return;
            }

            HubWindow window = new() { Owner = this };
            window.Closed += async (_, _) => await UpdateHubCoins();
            window.Show();
        }

        private async System.Threading.Tasks.Task UpdateHubCoins()
        {
            try
            {
                long? coins = await HubApi.GetCoinsAsync();
                if (coins.HasValue)
                    hubButton.Tag = $"{coins.Value:N0} coins";
            }
            catch
            {
                // Keep the default subtitle.
            }
        }

        private async Task PromoteTrayIconAsync()
        {
            if (!await TrayIconUtil.PromoteOwnIconAsync())
                return;

            // The shell only reads the promotion when the icon is registered, so re-add it.
            if (!IsVisible)
            {
                launcherNotifyIcon.Visible = false;
                launcherNotifyIcon.Visible = true;
            }
        }

        private void LauncherWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible)
            {
                launcherNotifyIcon.Visible = true;
                _ = PromoteTrayIconAsync();
            }
            else
                launcherNotifyIcon.Visible = false;
        }

        private void FullUpdatesHyperlink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri.ToString(),
                UseShellExecute = true
            });
        }

        private void H1EmuChineseLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri.ToString(),
                UseShellExecute = true
            });
        }

        private void OpenDiscord(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Info.DISCORD_LINK,
                UseShellExecute = true
            });
        }

        private void PatchNotesCopyClick(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(Info.CHANGELOG);
        }

        private void DiscordLinkCopyClick(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(Info.DISCORD_LINK);
        }

        private void ChineseLinkCopyClick(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(Info.CHANGELOG);
        }

        private void MinimiseToSystemTrayButtonClick(object sender, RoutedEventArgs e)
        {
            Hide();
            new ToastContentBuilder().AddText(FindResource("item191").ToString()).Show();
        }

        private void MinimiseButtonClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void LauncherWindowClosed(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }

        private void MoveWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            try
            {
                DragMove();
                e.Handled = true;
            }
            catch (InvalidOperationException)
            {
                // The mouse button can be released before Windows begins the move.
            }
        }

        private void CloseLauncher(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
