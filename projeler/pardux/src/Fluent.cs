using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Pardux
{
    /// <summary>Windows 11 akrilik/mika arka planı, yuvarlak köşeler ve sistem gölgesi.</summary>
    internal static class Fluent
    {
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_BORDER_COLOR = 34;
        const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        public const int None = 1, Mica = 2, Acrylic = 3;

        /// <summary>Sistem arka planı (backdrop) desteği: Windows 11 22H2 ve sonrası.</summary>
        public static bool Supported { get { return Build >= 22621; } }

        static int _build = -1;

        /// <summary>Windows derleme numarası (OSVersion manifest'e bağlı olduğu için kayıt defterinden okunur).</summary>
        public static int Build
        {
            get
            {
                if (_build >= 0) return _build;
                _build = 0;
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                        int.TryParse(Convert.ToString(k.GetValue("CurrentBuildNumber")), out _build);
                }
                catch { }
                return _build;
            }
        }

        /// <summary>Ayarlardaki pencere arka planı: akrilik (bulanık), mika ya da düz.</summary>
        static string Backdrop
        {
            get
            {
                var c = Controller.Instance;
                string b = c == null || c.Settings == null ? "akrilik" : c.Settings.Backdrop ?? "akrilik";
                return Supported ? b : "duz";
            }
        }

        /// <summary>Pencere oluşturulmadan önce çağrılmalı (kenarlıksız + cam çerçeve). Tema değişince kendini günceller.</summary>
        public static void Prepare(Window w)
        {
            w.WindowStyle = WindowStyle.SingleBorderWindow;
            w.ResizeMode = ResizeMode.NoResize;
            w.AllowsTransparency = false;
            WindowChrome.SetWindowChrome(w, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
            w.Background = Brushes.Transparent;
            Action refresh = () => Apply(w);
            w.SourceInitialized += (s, e) => Apply(w);
            Theme.Changed += refresh;
            w.Closed += (s, e) => Theme.Changed -= refresh;
        }

        public static void Apply(Window w)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;
                // Cam çerçeve içerinin tamamına uzatılınca Windows kendi (çalışmayan) kapat/küçült düğmelerini
                // sağ üst köşeye çiziyor; sistem menüsü stillerini kaldırarak bunları tamamen gizliyoruz.
                const int GWL_STYLE = -16;
                const int WS_SYSMENU = 0x80000, WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000;
                int style = Native.GetWindowLong(hwnd, GWL_STYLE);
                Native.SetWindowLong(hwnd, GWL_STYLE, style & ~(WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX));
                int dark = Theme.IsDark ? 1 : 0, round = 2;
                int border = Theme.IsDark ? unchecked((int)0x00403A3A) : unchecked((int)0x00D8D4D4);
                Native.DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
                Native.DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
                Native.DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, 4);
                if (!Supported) return;
                var src = HwndSource.FromHwnd(hwnd);
                if (src != null && src.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                Native.DwmExtendFrameIntoClientArea(hwnd, ref m);
                string b = Backdrop;
                int backdrop = b == "mika" ? Mica : b == "akrilik" ? Acrylic : None;
                Native.DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4);
            }
            catch { }
        }
    }
}
