using System.Windows;
using System.Windows.Media;

namespace H1Emu_Launcher.Classes
{
    // Accent colour of the launcher: JSEmu red #E11D2A.
    //
    // The colours are published as Accent.* brushes (and the older RedAccent / AccentColour /
    // AccentHoverColour keys) in the application resources, for dialogs, settings and the Steam pages,
    // and in the launcher window's own resources.
    public static class EditionTheme
    {
        private static readonly Color Accent = Color.FromRgb(0xE1, 0x1D, 0x2A);
        private static readonly Color Light = Color.FromRgb(0xFF, 0x4A, 0x55);
        private static readonly Color Dark = Color.FromRgb(0x9E, 0x10, 0x18);
        private static readonly Color TextOn = Color.FromRgb(0xFF, 0xFF, 0xFF);

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        private static void Publish(ResourceDictionary res)
        {
            res["Accent.Brush"] = Frozen(new SolidColorBrush(Accent));
            res["Accent.LightBrush"] = Frozen(new SolidColorBrush(Light));
            res["Accent.DarkBrush"] = Frozen(new SolidColorBrush(Dark));
            res["Accent.TextOnBrush"] = Frozen(new SolidColorBrush(TextOn));
            res["Accent.SoftBrush"] = Frozen(new SolidColorBrush(WithAlpha(Accent, 0x26)));
            res["Accent.BorderBrush"] = Frozen(new SolidColorBrush(WithAlpha(Accent, 0x8C)));
            res["Accent.GradientBrush"] = Frozen(new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Light, 0), new GradientStop(Accent, 0.45), new GradientStop(Dark, 1)
            }, new Point(0, 0), new Point(0, 1)));
            res["Accent.LineBrush"] = Frozen(new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(WithAlpha(Accent, 0), 0), new GradientStop(WithAlpha(Accent, 0xCC), 0.5), new GradientStop(WithAlpha(Accent, 0), 1)
            }, new Point(0, 0), new Point(1, 0)));
            res["Accent.GlowBrush"] = Frozen(new RadialGradientBrush(new GradientStopCollection
            {
                new GradientStop(WithAlpha(Accent, 0x55), 0.3), new GradientStop(WithAlpha(Accent, 0), 1)
            }) { RadiusX = 0.5, RadiusY = 0.5 });
        }

        /// <summary>Application-wide accent brushes.</summary>
        public static void ApplyToApplication()
        {
            ResourceDictionary res = Application.Current.Resources;
            Publish(res);
            res["RedAccent"] = Frozen(new SolidColorBrush(Accent));
            res["AccentColour"] = Frozen(new SolidColorBrush(Accent));
            res["AccentHoverColour"] = Frozen(new SolidColorBrush(Light));
        }

        /// <summary>Gives a window its own accent brushes.</summary>
        public static void AttachWindow(ResourceDictionary resources) => Publish(resources);
    }
}
