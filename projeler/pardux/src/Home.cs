using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Pardux
{
    /// <summary>
    /// Arama kutusu boşken görünen "bugün" paneli: uygulama listesi yerine takvim, pil, hava ve gündem (hatırlatıcılar, sayaçlar,
    /// çalan müzik, kaçırılan bildirimler). Yazmaya başlayınca kaybolur, yerini arama sonuçları alır.
    /// </summary>
    internal class HomeView : StackPanel
    {
        public event Action<string> QueryRequested;
        public event Action<string> OpenRequested;

        readonly TextBlock _dateBig = Big(), _dateSub = Sub(), _batBig = Big(), _batSub = Sub(), _wBig = Big(), _wSub = Sub(), _wSub2 = Sub();
        readonly StackPanel _week = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        readonly Border _batFill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Height = 4 };
        readonly Grid _batTrack = new Grid { Height = 4, Margin = new Thickness(0, 9, 0, 0) };
        readonly StackPanel _agenda = new StackPanel();
        readonly TextBlock _batGlyph;

        static readonly string[] Days = { "Pazar", "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi" };
        static readonly string[] Months = { "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };

        // Hava durumu az değişir: 20 dakikada bir tazelenir
        static string _wCity, _wTemp, _wDesc, _wRange;
        static DateTime _wAt = DateTime.MinValue;
        static bool _wLoading;

        public HomeView()
        {
            Margin = new Thickness(14, 10, 14, 6);
            var row = new Grid();
            for (int i = 0; i < 5; i++)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 1 ? new GridLength(10) : new GridLength(1, GridUnitType.Star) });

            // Takvim
            var date = new StackPanel();
            date.Children.Add(Header("", "Takvim", Color.FromRgb(0xFF, 0x6B, 0x6B)));
            date.Children.Add(_dateBig);
            date.Children.Add(_dateSub);
            date.Children.Add(_week);
            AddTile(row, 0, date, () => Open("outlookcal:", "https://calendar.google.com"));

            // Pil
            var bat = new StackPanel();
            var bh = Header("", "Pil", Color.FromRgb(0x34, 0xD3, 0x99));
            _batGlyph = (TextBlock)((StackPanel)bh).Children[0];
            bat.Children.Add(bh);
            bat.Children.Add(_batBig);
            _batTrack.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Res("Track") });
            _batTrack.Children.Add(_batFill);
            bat.Children.Add(_batTrack);
            bat.Children.Add(_batSub);
            _batSub.Margin = new Thickness(0, 7, 0, 0);
            AddTile(row, 2, bat, () => Open("ms-settings:batterysaver", null));

            // Hava
            var w = new StackPanel();
            w.Children.Add(Header("", "Hava", Color.FromRgb(0x60, 0xA5, 0xFA)));
            w.Children.Add(_wBig);
            w.Children.Add(_wSub);
            w.Children.Add(_wSub2);
            AddTile(row, 4, w, () => { if (QueryRequested != null) QueryRequested("hava durumu"); });
            Children.Add(row);

            // Gündem
            var agenda = new StackPanel();
            agenda.Children.Add(Header("", "Gündem", Color.FromRgb(0xC0, 0x84, 0xFC)));
            agenda.Children.Add(_agenda);
            var agendaTile = Tile(agenda, null);
            agendaTile.Margin = new Thickness(0, 10, 0, 0);
            agendaTile.Cursor = Cursors.Arrow;
            Children.Add(agendaTile);
        }

        void Open(string uri, string fallback)
        {
            if (OpenRequested != null) OpenRequested(uri + (fallback != null ? "|" + fallback : ""));
        }

        // ---------------- Yapı taşları ----------------
        static Brush Res(string key) { return (Brush)Application.Current.TryFindResource(key) ?? Brushes.Gray; }

        static TextBlock Big()
        {
            var t = new TextBlock { FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                                    FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            return t;
        }

        static TextBlock Sub()
        {
            var t = new TextBlock { FontSize = 12, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
            return t;
        }

        static FrameworkElement Header(string glyph, string label, Color color)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            var g = new TextBlock { Text = glyph, FontSize = 12, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
            g.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            var l = new TextBlock { Text = label, FontSize = 11.5, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            l.SetResourceReference(TextBlock.ForegroundProperty, "FgMuted");
            p.Children.Add(g);
            p.Children.Add(l);
            return p;
        }

        Border Tile(UIElement content, Action click)
        {
            var b = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(14, 11, 14, 12), Child = content };
            b.SetResourceReference(Border.BackgroundProperty, "CardBg");
            b.SetResourceReference(Border.BorderBrushProperty, "CardEdge");
            if (click != null)
            {
                b.Cursor = Cursors.Hand;
                b.MouseEnter += (s, e) => b.SetResourceReference(Border.BackgroundProperty, "Hover");
                b.MouseLeave += (s, e) => b.SetResourceReference(Border.BackgroundProperty, "CardBg");
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            }
            return b;
        }

        void AddTile(Grid row, int col, UIElement content, Action click)
        {
            var t = Tile(content, click);
            Grid.SetColumn(t, col);
            row.Children.Add(t);
        }

        // ---------------- İçerik ----------------
        /// <summary>Panel her açılışta güncel bilgilerle doldurulur (hava arka planda gelir).</summary>
        public void Refresh()
        {
            var now = DateTime.Now;
            _dateBig.Text = now.Day + " " + Months[now.Month - 1];
            _dateSub.Text = Days[(int)now.DayOfWeek] + "  ·  " + now.ToString("HH:mm");
            BuildWeek(now);
            UpdateBattery();
            UpdateWeather();
            UpdateAgenda();
        }

        void BuildWeek(DateTime now)
        {
            _week.Children.Clear();
            int offset = ((int)now.DayOfWeek + 6) % 7; // pazartesi başlangıç
            var monday = now.Date.AddDays(-offset);
            string[] letters = { "P", "S", "Ç", "P", "C", "C", "P" };
            for (int i = 0; i < 7; i++)
            {
                var d = monday.AddDays(i);
                bool today = d == now.Date;
                var cell = new Grid { Width = 26, Height = 30 };
                var l = new TextBlock { Text = letters[i], FontSize = 9.5, HorizontalAlignment = HorizontalAlignment.Center };
                l.SetResourceReference(TextBlock.ForegroundProperty, "FgMuted");
                var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
                if (today) dot.SetResourceReference(Border.BackgroundProperty, "Accent");
                var n = new TextBlock { Text = d.Day.ToString(), FontSize = 10.5, FontWeight = today ? FontWeights.SemiBold : FontWeights.Normal,
                                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                n.SetResourceReference(TextBlock.ForegroundProperty, today ? "OnAccent" : "FgDim");
                dot.Child = n;
                cell.Children.Add(l);
                cell.Children.Add(dot);
                _week.Children.Add(cell);
            }
        }

        void UpdateBattery()
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            bool noBattery = ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) || ps.BatteryLifePercent > 1.01f;
            bool plugged = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            if (noBattery)
            {
                _batBig.Text = "Prizde";
                _batSub.Text = "Bu bilgisayarda pil yok";
                _batTrack.Visibility = Visibility.Collapsed;
                return;
            }
            int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
            _batBig.Text = "%" + pct;
            _batTrack.Visibility = Visibility.Visible;
            _batTrack.UpdateLayout();
            Color c = plugged ? Color.FromRgb(0x34, 0xD3, 0x99) : pct <= 15 ? Color.FromRgb(0xFF, 0x5F, 0x57) : pct <= 30 ? Color.FromRgb(0xFF, 0xB0, 0x3A) : Color.FromRgb(0xE5, 0xE5, 0xEA);
            _batFill.Background = new SolidColorBrush(c);
            _batFill.Width = Math.Max(4, 196 * pct / 100.0);
            _batTrack.SizeChanged -= OnTrackSize;
            _batTrack.SizeChanged += OnTrackSize;
            _batGlyph.Text = plugged ? "" : "";
            int secs = ps.BatteryLifeRemaining;
            if (plugged) _batSub.Text = pct >= 99 ? "Dolu, prize takılı" : "Şarj oluyor";
            else if (secs > 0) _batSub.Text = "Yaklaşık " + Duration(secs) + " kaldı";
            else _batSub.Text = "Pille çalışıyor";
        }

        void OnTrackSize(object s, SizeChangedEventArgs e)
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            _batFill.Width = Math.Max(4, _batTrack.ActualWidth * Math.Min(1, ps.BatteryLifePercent));
        }

        static string Duration(int secs)
        {
            int h = secs / 3600, m = secs % 3600 / 60;
            return h > 0 ? h + " sa " + m + " dk" : m + " dk";
        }

        void UpdateWeather()
        {
            if (_wTemp != null) ShowWeather();
            else { _wBig.Text = "—"; _wSub.Text = "Hava durumu alınıyor…"; _wSub2.Text = ""; }
            if ((DateTime.Now - _wAt).TotalMinutes < 20 || _wLoading) return;
            _wLoading = true;
            var ui = Dispatcher;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string text = WebTools.Weather("");
                    var city = Regex.Match(text, @"Konum: ([^,\n]+)");
                    var cur = Regex.Match(text, @"Şu an: (-?\d+)°C[^,]*\)?, ([^,\n]+)");
                    var today = Regex.Match(text, @"Bugün \([^)]*\): (-?\d+)–(-?\d+)°C");
                    if (cur.Success)
                    {
                        _wCity = city.Success ? city.Groups[1].Value.Trim() : "";
                        _wTemp = cur.Groups[1].Value + "°";
                        _wDesc = cur.Groups[2].Value.Trim();
                        _wRange = today.Success ? "En yüksek " + today.Groups[2].Value + "°  ·  en düşük " + today.Groups[1].Value + "°" : "";
                        _wAt = DateTime.Now;
                    }
                }
                catch { }
                _wLoading = false;
                ui.BeginInvoke(new Action(() => { if (_wTemp != null) ShowWeather(); else _wSub.Text = "Hava durumu alınamadı"; }));
            });
        }

        void ShowWeather()
        {
            _wBig.Text = _wTemp;
            _wSub.Text = (_wCity.Length > 0 ? _wCity + "  ·  " : "") + _wDesc;
            _wSub2.Text = _wRange;
        }

        void UpdateAgenda()
        {
            _agenda.Children.Clear();
            var c = Controller.Instance;
            int lines = 0;
            var np = MediaWatcher.Current;
            if (np != null)
            {
                AddLine(np.IsPlaying ? "" : "", Color.FromRgb(0x1E, 0xD7, 0x60), np.Title, string.IsNullOrEmpty(np.Artist) ? np.App : np.Artist,
                        np.IsPlaying ? "Duraklat" : "Oynat", () => { MediaWatcher.PlayPause(); });
                lines++;
            }
            if (c != null)
            {
                foreach (var r in c.Reminders.OrderBy(x => x.Due).Take(3))
                {
                    var left = r.Due - DateTime.Now;
                    string when = r.IsTimer ? (left.TotalHours >= 1 ? (int)left.TotalHours + " sa " + left.Minutes + " dk" : Math.Max(0, left.Minutes) + " dk " + Math.Max(0, left.Seconds) + " sn") + " kaldı"
                                            : (r.Due.Date == DateTime.Today ? "Bugün " : r.Due.ToString("d MMM ", TextUtil.Tr)) + r.Due.ToString("HH:mm");
                    AddLine(r.IsTimer ? "" : "", Color.FromRgb(0xFF, 0x86, 0xCF), r.Text, when, null, () => { if (QueryRequested != null) QueryRequested("hatırlatıcılar"); });
                    lines++;
                }
                if (c.WatchActive)
                {
                    var t = c.Watch.Elapsed;
                    AddLine("", Color.FromRgb(0xFF, 0xB0, 0x4A), "Kronometre", (c.Watch.IsRunning ? "Çalışıyor  ·  " : "Duraklatıldı  ·  ") + ((int)t.TotalMinutes) + " dk " + t.Seconds + " sn", null, null);
                    lines++;
                }
            }
            var missed = Notifications.Missed;
            if (missed.Count > 0)
            {
                var last = missed[missed.Count - 1];
                AddLine("", Color.FromRgb(0xFF, 0xD6, 0x0A), missed.Count + " kaçırılan bildirim", last.App + ": " + last.Title, "Göster",
                        () => { if (QueryRequested != null) QueryRequested("bildirimler"); });
                lines++;
            }
            if (lines == 0)
                AddLine("", Color.FromRgb(0xA1, 0xA1, 0xAA), "Bugün için kurulu bir şey yok", "“10 dk sonra çayı hatırlat” ya da “5 dakikalık zamanlayıcı” yazabilirsin", null, null);
        }

        void AddLine(string glyph, Color color, string title, string sub, string action, Action click)
        {
            var g = new Grid { Margin = new Thickness(0, _agenda.Children.Count == 0 ? 8 : 4, 0, 0), Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var gl = new TextBlock { Text = glyph, FontSize = 13, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
            gl.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
            var t1 = new System.Windows.Documents.Run(title) { FontWeight = FontWeights.SemiBold };
            t1.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Fg");
            var t2 = new System.Windows.Documents.Run("   " + sub) { FontSize = 12 };
            t2.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "FgDim");
            text.Inlines.Add(t1);
            text.Inlines.Add(t2);
            Grid.SetColumn(text, 1);
            g.Children.Add(gl);
            g.Children.Add(text);
            if (action != null)
            {
                var chip = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                chip.SetResourceReference(Border.BackgroundProperty, "Chip");
                var ct = new TextBlock { Text = action, FontSize = 11 };
                ct.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
                chip.Child = ct;
                Grid.SetColumn(chip, 2);
                g.Children.Add(chip);
            }
            if (click != null)
            {
                g.Cursor = Cursors.Hand;
                g.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            }
            _agenda.Children.Add(g);
        }
    }
}
