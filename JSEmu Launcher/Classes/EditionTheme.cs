using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace H1Emu_Launcher.Classes
{
    // Accent colours of the two game editions.
    //
    //  2016  JSEmu red    #E11D2A  - the classic JSEmu accent.
    //  2018  ember orange #F28A2E  - the warm half of the same sunset palette: clearly a different
    //                                edition, still at home on the dark red/brown backdrops.
    //
    // The colours are published as Accent.* brushes (and the older RedAccent / AccentColour /
    // AccentHoverColour keys) in the application resources, for dialogs, settings and the Steam pages,
    // and in the launcher window's own resources. WPF freezes brushes that templates and styles pick up
    // from resources, so a brush cannot be animated in place: the switch instead publishes a new set of
    // (frozen) brushes on every rendered frame for TransitionDuration, and every DynamicResource follows.
    public static class EditionTheme
    {
        public sealed record Palette(Color Accent, Color Light, Color Dark, Color TextOn);

        public static readonly Palette Classic2016 = new(
            Color.FromRgb(0xE1, 0x1D, 0x2A), Color.FromRgb(0xFF, 0x4A, 0x55), Color.FromRgb(0x9E, 0x10, 0x18), Color.FromRgb(0xFF, 0xFF, 0xFF));

        public static readonly Palette Steam2018 = new(
            Color.FromRgb(0xF2, 0x8A, 0x2E), Color.FromRgb(0xFF, 0xB0, 0x61), Color.FromRgb(0xB4, 0x56, 0x1A), Color.FromRgb(0x1A, 0x0F, 0x05));

        public static Palette For(bool is2018) => is2018 ? Steam2018 : Classic2016;

        public static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(450);

        private static ResourceDictionary windowResources;
        private static Palette current;
        private static Palette from, target;
        private static Stopwatch clock;
        private static bool hooked;

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        private static void Publish(ResourceDictionary res, Palette p)
        {
            res["Accent.Brush"] = Frozen(new SolidColorBrush(p.Accent));
            res["Accent.LightBrush"] = Frozen(new SolidColorBrush(p.Light));
            res["Accent.DarkBrush"] = Frozen(new SolidColorBrush(p.Dark));
            res["Accent.TextOnBrush"] = Frozen(new SolidColorBrush(p.TextOn));
            res["Accent.SoftBrush"] = Frozen(new SolidColorBrush(WithAlpha(p.Accent, 0x26)));
            res["Accent.BorderBrush"] = Frozen(new SolidColorBrush(WithAlpha(p.Accent, 0x8C)));
            res["Accent.GradientBrush"] = Frozen(new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(p.Light, 0), new GradientStop(p.Accent, 0.45), new GradientStop(p.Dark, 1)
            }, new Point(0, 0), new Point(0, 1)));
            res["Accent.LineBrush"] = Frozen(new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(WithAlpha(p.Accent, 0), 0), new GradientStop(WithAlpha(p.Accent, 0xCC), 0.5), new GradientStop(WithAlpha(p.Accent, 0), 1)
            }, new Point(0, 0), new Point(1, 0)));
            res["Accent.GlowBrush"] = Frozen(new RadialGradientBrush(new GradientStopCollection
            {
                new GradientStop(WithAlpha(p.Accent, 0x55), 0.3), new GradientStop(WithAlpha(p.Accent, 0), 1)
            }) { RadiusX = 0.5, RadiusY = 0.5 });
        }

        /// <summary>Application-wide accent brushes for the edition (no animation).</summary>
        public static void ApplyToApplication(bool is2018)
        {
            Palette p = For(is2018);
            ResourceDictionary res = Application.Current.Resources;
            Publish(res, p);
            res["RedAccent"] = Frozen(new SolidColorBrush(p.Accent));
            res["AccentColour"] = Frozen(new SolidColorBrush(p.Accent));
            res["AccentHoverColour"] = Frozen(new SolidColorBrush(p.Light));
        }

        /// <summary>Gives a window its own accent brushes; <see cref="Apply"/> animates them.</summary>
        public static void AttachWindow(ResourceDictionary resources, bool is2018)
        {
            windowResources = resources;
            current = For(is2018);
            Publish(windowResources, current);
        }

        /// <summary>Switches the accent: the attached window glides to it, everything else follows at once.</summary>
        public static void Apply(bool is2018, bool animate = true)
        {
            ApplyToApplication(is2018);
            if (windowResources == null)
                return;

            target = For(is2018);
            if (!animate || current == null)
            {
                Stop();
                current = target;
                Publish(windowResources, current);
                return;
            }

            from = current;
            clock = Stopwatch.StartNew();
            if (!hooked)
            {
                CompositionTarget.Rendering += OnFrame;
                hooked = true;
            }
        }

        private static void Stop()
        {
            if (hooked)
            {
                CompositionTarget.Rendering -= OnFrame;
                hooked = false;
            }
        }

        private static void OnFrame(object sender, EventArgs e)
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / TransitionDuration.TotalMilliseconds);
            double eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; // cubic ease in-out
            current = new Palette(Mix(from.Accent, target.Accent, eased), Mix(from.Light, target.Light, eased),
                Mix(from.Dark, target.Dark, eased), Mix(from.TextOn, target.TextOn, eased));
            Publish(windowResources, current);
            if (t >= 1)
                Stop();
        }

        private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
            (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
    }
}
