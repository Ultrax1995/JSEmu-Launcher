using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Cranberry.Launcher.Core.Voice;

namespace H1Emu_Launcher.Classes
{
    // Who is talking, for the 2016 proximity voice: a small click-through list in the top-left
    // corner of the game window. The 2016 client has no native talking indicator the server can
    // drive (the KOTK one is a patched UI window), so the launcher draws it. It shows only while
    // the game has focus and someone is audible; exclusive fullscreen hides it (use borderless or
    // windowed mode to see it).
    public sealed class VoiceTalkingOverlay : Window
    {
        private readonly StackPanel rows = new();
        private int _gamePid;

        public VoiceTalkingOverlay()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            IsHitTestVisible = false;
            Title = "JSEmu voice";
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x99, 0x06, 0x09, 0x0D)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 12, 6),
                Child = rows
            };
            SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                // Transparent to the mouse, never activated, not in Alt+Tab.
                SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE)
                    | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            };
        }

        public void Show(VoiceTalker[] talkers, ulong self, int gamePid)
        {
            _gamePid = gamePid;
            rows.Children.Clear();
            foreach (var talker in talkers)
            {
                bool me = talker.CharacterId == self;
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                line.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 8, Height = 8, Margin = new Thickness(0, 1, 8, 0),
                    Fill = me ? new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x2A)) : new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99))
                });
                line.Children.Add(new TextBlock
                {
                    Text = me ? "You" : (string.IsNullOrWhiteSpace(talker.Name) ? "Player" : talker.Name),
                    Foreground = Brushes.White,
                    FontSize = 15,
                    FontWeight = me ? FontWeights.SemiBold : FontWeights.Normal,
                    FontFamily = new FontFamily("Segoe UI")
                });
                rows.Children.Add(line);
            }
            Follow(gamePid);
        }

        /// <summary>Keeps the list on the game window, and hidden while the game is not in front.</summary>
        public void Follow(int gamePid)
        {
            _gamePid = gamePid;
            if (rows.Children.Count == 0) { Hide(); return; }
            IntPtr foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out uint pid);
            if (pid != (uint)_gamePid || !GetWindowRect(foreground, out RECT rect)) { Hide(); return; }
            // Window rectangles are in device pixels; WPF positions in DIPs.
            var source = PresentationSource.FromVisual(this);
            double scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            Left = rect.Left / scale + 24;
            Top = rect.Top / scale + 140;
            if (!IsVisible) base.Show();
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    }
}
