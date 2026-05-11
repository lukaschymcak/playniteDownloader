using System.Windows.Media;

namespace BlankPlugin
{
    internal static class Theme
    {
        public static readonly SolidColorBrush Accent = Brush(0xFF, 0x4F, 0xD1, 0xC5);
        public static readonly SolidColorBrush AccentSoft = Brush(0x24, 0x4F, 0xD1, 0xC5);

        public static readonly SolidColorBrush Bg0 = Hex("#0B0C0F");
        public static readonly SolidColorBrush Bg1 = Hex("#131519");
        public static readonly SolidColorBrush Bg2 = Hex("#181B21");
        public static readonly SolidColorBrush Bg3 = Hex("#1F232A");
        public static readonly SolidColorBrush Bg4 = Hex("#262B33");

        public static readonly SolidColorBrush Line = Hex("#252A32");
        public static readonly SolidColorBrush LineSoft = Hex("#1E222A");

        public static readonly SolidColorBrush Fg0 = Hex("#F2F4F8");
        public static readonly SolidColorBrush Fg1 = Hex("#C7CCD4");
        public static readonly SolidColorBrush Fg2 = Hex("#8C93A0");
        public static readonly SolidColorBrush Fg3 = Hex("#5C6470");

        public static readonly SolidColorBrush Good = Hex("#5BD68F");
        public static readonly SolidColorBrush Warn = Hex("#F5B454");
        public static readonly SolidColorBrush Danger = Hex("#F26B6B");
        public static readonly SolidColorBrush Info = Hex("#6FA8FF");

        public static SolidColorBrush GoodSoft => Brush(0x1F, 0x5B, 0xD6, 0x8F);
        public static SolidColorBrush GoodLine => Brush(0x40, 0x5B, 0xD6, 0x8F);
        public static SolidColorBrush WarnSoft => Brush(0x1F, 0xF5, 0xB4, 0x54);
        public static SolidColorBrush WarnLine => Brush(0x47, 0xF5, 0xB4, 0x54);
        public static SolidColorBrush InfoSoft => Brush(0x1F, 0x6F, 0xA8, 0xFF);
        public static SolidColorBrush InfoLine => Brush(0x40, 0x6F, 0xA8, 0xFF);
        public static SolidColorBrush DangerSoft => Brush(0x1F, 0xF2, 0x6B, 0x6B);

        private static SolidColorBrush Hex(string value)
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush Brush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
