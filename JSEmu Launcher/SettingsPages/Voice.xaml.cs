using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Cranberry.Launcher.Core.Voice;
using H1Emu_Launcher.Classes;
using NAudio.Wave;

namespace H1Emu_Launcher.SettingsPages
{
    // Settings of the 2016 proximity voice (Classes/Voice2016.cs).
    public partial class Voice : Page
    {
        private sealed record AudioDevice(int Id, string Name) { public override string ToString() => Name; }

        public Voice()
        {
            InitializeComponent();
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(SetLanguageFile.LoadFile());
        }

        private void VoiceLoaded(object sender, RoutedEventArgs e)
        {
            var settings = Properties.Settings.Default;
            enabledToggle.IsChecked = settings.voice2016Enabled;
            keyBox.Text = settings.voice2016Key ?? "";
            volumeSlider.Value = Math.Clamp(settings.voice2016Volume, 0, 100);
            volumeLabel.Text = $"Voice volume  {(int)volumeSlider.Value}%";
            inputBox.Items.Clear();
            outputBox.Items.Clear();
            inputBox.Items.Add(new AudioDevice(GameAudioDevices.WindowsDefault, "Windows default microphone"));
            outputBox.Items.Add(new AudioDevice(GameAudioDevices.WindowsDefault, "Windows default speakers / headphones"));
            try
            {
                for (int i = 0; i < WaveIn.DeviceCount; i++) inputBox.Items.Add(new AudioDevice(i, WaveIn.GetCapabilities(i).ProductName));
                for (int i = 0; i < WaveOut.DeviceCount; i++) outputBox.Items.Add(new AudioDevice(i, WaveOut.GetCapabilities(i).ProductName));
            }
            catch (NAudio.MmException) { statusText.Text = "Audio devices unavailable. Check Windows sound settings."; }
            AddGameDevice(inputBox, GameAudioDevices.GameInput());
            AddGameDevice(outputBox, GameAudioDevices.GameOutput());
            Select(inputBox, settings.voice2016Input);
            Select(outputBox, settings.voice2016Output);

            Voice2016.StatusChanged += ShowStatus;
            Voice2016.TalkersChanged += ShowTalkers;
            ShowStatus();
        }

        // First on the list: the device from the game's own options, used until the player picks another.
        private static void AddGameDevice(ComboBox box, int gameDevice)
        {
            AudioDevice match = box.Items.Cast<AudioDevice>().FirstOrDefault(d => d.Id == gameDevice);
            box.Items.Insert(0, new AudioDevice(GameAudioDevices.FollowGame, match == null ? "Same as in game (Windows default)" : $"Same as in game ({match})"));
        }

        private static void Select(ComboBox box, int id) =>
            box.SelectedItem = box.Items.Cast<AudioDevice>().FirstOrDefault(d => d.Id == id) ?? box.Items[0];

        private void VoiceUnloaded(object sender, RoutedEventArgs e)
        {
            Voice2016.StatusChanged -= ShowStatus;
            Voice2016.TalkersChanged -= ShowTalkers;
        }

        private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (volumeLabel != null) volumeLabel.Text = $"Voice volume  {(int)e.NewValue}%";
        }

        private void SaveClick(object sender, RoutedEventArgs e)
        {
            string key = keyBox.Text.Trim();
            if (key.Length > 0 && VoiceBindings.Parse(key).Length == 0)
            {
                statusText.Text = $"Unknown key \"{key}\". Use names like V, F5, KP_4, Mouse_3, Mouse_4 or Alt+V.";
                return;
            }
            var settings = Properties.Settings.Default;
            settings.voice2016Enabled = enabledToggle.IsChecked == true;
            settings.voice2016Input = (inputBox.SelectedItem as AudioDevice)?.Id ?? -1;
            settings.voice2016Output = (outputBox.SelectedItem as AudioDevice)?.Id ?? -1;
            settings.voice2016Volume = (int)volumeSlider.Value;
            settings.voice2016Key = key;
            settings.Save();
            if (!settings.voice2016Enabled) Voice2016.Stop();
            else Voice2016.Restart();
            statusText.Text = "Saved. " + Voice2016.Status;
        }

        private void ShowStatus() => Dispatcher.BeginInvoke(() => statusText.Text = Voice2016.Status);

        private void ShowTalkers(VoiceTalker[] talkers, ulong self) => Dispatcher.BeginInvoke(() =>
            talkingText.Text = talkers.Length == 0 ? "" :
                "Talking: " + string.Join(", ", talkers.Select(t => t.CharacterId == self ? "you" : t.Name)));
    }
}
