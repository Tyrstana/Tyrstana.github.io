using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;

namespace Pardux
{
    public class NewsLink
    {
        public string Title, Source, Link;
    }

    public class NewsItem
    {
        public string Id, Title, Source, Link;
        public DateTime Published; // UTC
        public int Rank;
        public List<NewsLink> Related = new List<NewsLink>();

        // Ayrıntılar (News.LoadDetails doldurur)
        public string Url, Summary, Body, ImageUrl;
        public BitmapSource Image;
        public bool DetailsLoaded;

        public string Age
        {
            get
            {
                if (Published == DateTime.MinValue) return "";
                var d = DateTime.UtcNow - Published;
                if (d.TotalMinutes < 2) return "şimdi";
                if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " dk önce";
                if (d.TotalHours < 24) return (int)d.TotalHours + " sa önce";
                return Published.ToLocalTime().ToString("d MMMM HH:mm", TextUtil.Tr);
            }
        }
    }

    /// <summary>
    /// Gündem haberleri: Google Haberler Türkiye "öne çıkanlar" akışı. Akıştaki sıra önemi gösterir; üst sıradaki taze haberler
    /// ve "son dakika" türü sözcükler içerenler önemli sayılır. Bildirimler seyrek tutulur (aralık, günlük sınır, gece sessizliği).
    /// </summary>
    internal static class News
    {
        const string Feed = "https://news.google.com/rss?hl=tr&gl=TR&ceid=TR:tr";
        const int MinGapMinutes = 45, MaxPerDay = 6, QuietUntilHour = 8;

        static readonly string[] Breaking =
        {
            "son dakika", "deprem", "patlama", "saldiri", "hayatini kaybetti", "can kaybi", "yarali", "istifa", "secim", "faiz karari",
            "asgari ucret", "savas", "tsunami", "yangin", "tutuklandi", "gozalti", "olaganustu", "suikast", "darbe", "teror", "firtina",
            "sel felaketi", "tahliye", "acil durum", "vefat etti", "hayatini kaybeden"
        };

        static readonly object Gate = new object();
        static List<NewsItem> _latest = new List<NewsItem>();

        public static List<NewsItem> Latest { get { lock (Gate) return _latest.ToList(); } }

        // ---------------- Akış ----------------
        public static List<NewsItem> Fetch()
        {
            string xml = WebTools.Get(Feed, 12000);
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var list = new List<NewsItem>();
            int rank = 0;
            foreach (XmlNode n in doc.SelectNodes("//item"))
            {
                string title = Text(n, "title"), src = Text(n, "source");
                if (src.Length > 0 && title.EndsWith(" - " + src)) title = title.Substring(0, title.Length - src.Length - 3).Trim();
                DateTime pub;
                if (!DateTime.TryParse(Text(n, "pubDate"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out pub))
                    pub = DateTime.MinValue;
                var it = new NewsItem { Id = Text(n, "guid"), Title = title, Source = src, Link = Text(n, "link"), Published = pub, Rank = rank++ };
                if (string.IsNullOrEmpty(it.Id)) it.Id = it.Link;
                foreach (Match m in Regex.Matches(Text(n, "description"), "<a href=\"([^\"]+)\"[^>]*>(.*?)</a>.*?<font[^>]*>(.*?)</font>", RegexOptions.Singleline))
                {
                    string link = WebUtility.HtmlDecode(m.Groups[1].Value);
                    if (link == it.Link) continue;
                    it.Related.Add(new NewsLink { Link = link, Title = Clean(m.Groups[2].Value), Source = Clean(m.Groups[3].Value) });
                }
                list.Add(it);
            }
            lock (Gate) _latest = list;
            return list;
        }

        static string Text(XmlNode n, string name)
        {
            var c = n.SelectSingleNode(name);
            return c == null ? "" : c.InnerText.Trim();
        }

        static string Clean(string html)
        {
            string s = WebUtility.HtmlDecode(Regex.Replace(html ?? "", "<[^>]+>", " "));
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        /// <summary>Önem puanı: akıştaki sıra + tazelik + "son dakika" türü sözcükler. 50 ve üstü bildirilmeye değer.</summary>
        public static int Score(NewsItem it)
        {
            double ageH = (DateTime.UtcNow - it.Published).TotalHours;
            if (it.Published == DateTime.MinValue || ageH > 4) return 0;
            int s = it.Rank < 6 ? 60 - it.Rank * 5 : it.Rank < 12 ? 20 : 0;
            string n = TextUtil.Norm(it.Title);
            if (Breaking.Any(k => n.Contains(k))) s += 40;
            if (ageH < 1) s += 15;
            return s;
        }

        public static NewsItem Best(IEnumerable<NewsItem> items)
        {
            return items.Select(i => Tuple.Create(i, Score(i))).Where(t => t.Item2 >= 50)
                        .OrderByDescending(t => t.Item2).Select(t => t.Item1).FirstOrDefault();
        }

        // ---------------- Ayrıntı ----------------
        /// <summary>Google Haberler bağlantısını asıl habere çevirir, sayfadan görsel, özet ve metni çıkarır (arka planda çağrılmalı).</summary>
        public static void LoadDetails(NewsItem it)
        {
            if (it.DetailsLoaded) return;
            try { it.Url = Resolve(it.Id); } catch { }
            if (string.IsNullOrEmpty(it.Url)) it.Url = it.Link;
            if (it.Url != it.Link)
            {
                try
                {
                    string html = WebTools.Get(it.Url, 12000);
                    it.ImageUrl = Meta(html, "og:image") ?? Meta(html, "twitter:image");
                    string d = Meta(html, "og:description") ?? Meta(html, "description");
                    // Bazı siteler özetin başına tarih ekler ("29.10.2026 - ..."): at
                    if (d != null) it.Summary = Regex.Replace(Clean(d), @"^\d{1,2}[./]\d{1,2}[./]\d{2,4}\s*[-–]\s*", "");
                    it.Body = ArticleBody(html, it.Summary);
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(it.ImageUrl))
            {
                try
                {
                    if (it.ImageUrl.StartsWith("//")) it.ImageUrl = "https:" + it.ImageUrl;
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = new MemoryStream(WebTools.GetBytes(it.ImageUrl));
                    bi.DecodePixelWidth = 960;
                    bi.EndInit();
                    bi.Freeze();
                    it.Image = bi;
                }
                catch { }
            }
            it.DetailsLoaded = true;
        }

        static string Resolve(string id)
        {
            if (string.IsNullOrEmpty(id) || id.StartsWith("http")) return null;
            string page = WebTools.Get("https://news.google.com/rss/articles/" + id + "?hl=tr&gl=TR&ceid=TR:tr", 10000);
            string sg = Regex.Match(page, "data-n-a-sg=\"([^\"]+)\"").Groups[1].Value;
            string ts = Regex.Match(page, "data-n-a-ts=\"([^\"]+)\"").Groups[1].Value;
            if (sg.Length == 0 || ts.Length == 0) return null;
            string inner = "[\"garturlreq\",[[\"X\",\"X\",[\"X\",\"X\"],null,null,1,1,\"US:en\",null,1,null,null,null,null,null,0,1],\"X\",\"X\",1,[1,1,1],1,1,null,0,0,null,0],\"" +
                           id + "\"," + ts + ",\"" + sg + "\"]";
            var js = new JavaScriptSerializer();
            string req = "[[[\"Fbv4je\"," + js.Serialize(inner) + ",null,\"generic\"]]]";
            string resp = WebTools.Post("https://news.google.com/_/DotsSplashUi/data/batchexecute", "f.req=" + Uri.EscapeDataString(req), 10000);
            var m = Regex.Match(resp, @"garturlres\\"",\\""(.+?)\\""");
            if (!m.Success) return null;
            string url = js.Deserialize<string>("\"" + m.Groups[1].Value + "\"");
            return url.StartsWith("http") ? url : null;
        }

        static string Meta(string html, string prop)
        {
            foreach (Match m in Regex.Matches(html, "<meta\\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var p = Regex.Match(m.Value, "(?:property|name)\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
                if (!p.Success || !p.Groups[1].Value.Equals(prop, StringComparison.OrdinalIgnoreCase)) continue;
                var c = Regex.Match(m.Value, "content\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')", RegexOptions.IgnoreCase);
                if (!c.Success) continue;
                string v = WebUtility.HtmlDecode(c.Groups[1].Success ? c.Groups[1].Value : c.Groups[2].Value).Trim();
                if (v.Length > 0) return v;
            }
            return null;
        }

        static readonly string[] Junk = { "çerez", "cookie", "©", "abone ol", "tıklayın", "kvkk", "aydınlatma metni", "reklam", "uygulamamızı", "whatsapp", "telegram" };

        /// <summary>Haberin metni: önce yapısal veri (articleBody), yoksa sayfadaki uzun paragraflar.</summary>
        static string ArticleBody(string html, string summary)
        {
            string body = null;
            var m = Regex.Match(html, "\"articleBody\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (m.Success)
            {
                try { body = Clean(new JavaScriptSerializer().Deserialize<string>("\"" + m.Groups[1].Value + "\"")); } catch { }
            }
            if (string.IsNullOrEmpty(body) || body.Length < 120)
            {
                var sb = new StringBuilder();
                foreach (Match p in Regex.Matches(html, "<p\\b[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    string t = Clean(p.Groups[1].Value);
                    if (t.Length < 70) continue;
                    string low = t.ToLower(TextUtil.Tr);
                    if (Junk.Any(j => low.Contains(j))) continue;
                    if (sb.Length > 0) sb.Append("\n\n");
                    sb.Append(t);
                    if (sb.Length > 1800) break;
                }
                body = sb.ToString();
            }
            if (string.IsNullOrEmpty(body)) return null;
            // Özetle aynı başlıyorsa tekrar etme
            if (!string.IsNullOrEmpty(summary) && body.StartsWith(summary.TrimEnd('.', '…'))) body = body.Substring(Math.Min(body.Length, summary.TrimEnd('.', '…').Length)).TrimStart('.', ' ', '…');
            if (body.Length > 2000)
            {
                int cut = body.LastIndexOf(". ", 2000, StringComparison.Ordinal);
                body = body.Substring(0, cut > 600 ? cut + 1 : 2000) + " …";
            }
            return body.Trim().Length < 40 ? null : body.Trim();
        }

        // ---------------- Bildirim izleyicisi ----------------
        class State
        {
            public DateTime LastShown = DateTime.MinValue, Day = DateTime.MinValue;
            public int Count;
            public List<Tuple<string, string>> Shown = new List<Tuple<string, string>>(); // id, başlık
        }

        static string StatePath { get { return Path.Combine(Settings.Dir, "haberler.txt"); } }

        static State LoadState()
        {
            var s = new State();
            try
            {
                if (!File.Exists(StatePath)) return s;
                var lines = File.ReadAllLines(StatePath, Encoding.UTF8);
                if (lines.Length > 0)
                {
                    var head = lines[0].Split('|');
                    s.LastShown = new DateTime(long.Parse(head[0]));
                    s.Day = new DateTime(long.Parse(head[1]));
                    s.Count = int.Parse(head[2]);
                }
                foreach (string l in lines.Skip(1))
                {
                    int tab = l.IndexOf('\t');
                    if (tab > 0) s.Shown.Add(Tuple.Create(l.Substring(0, tab), l.Substring(tab + 1)));
                }
            }
            catch { }
            return s;
        }

        static void SaveState(State s)
        {
            try
            {
                var lines = new List<string> { s.LastShown.Ticks + "|" + s.Day.Ticks + "|" + s.Count };
                lines.AddRange(s.Shown.Skip(Math.Max(0, s.Shown.Count - 200)).Select(t => t.Item1 + "\t" + t.Item2.Replace('\t', ' ').Replace('\n', ' ')));
                File.WriteAllLines(StatePath, lines, new UTF8Encoding(false));
            }
            catch { }
        }

        static HashSet<string> Words(string title)
        {
            return new HashSet<string>(TextUtil.Norm(title).Split(new[] { ' ', ',', '.', ':', '!', '?', '\'', '"' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3));
        }

        /// <summary>Aynı olay başka bir kaynaktan tekrar gelmesin: başlık sözcükleri büyük ölçüde örtüşüyorsa aynı haber sayılır.</summary>
        static bool AlreadyShown(State s, NewsItem it)
        {
            if (s.Shown.Any(t => t.Item1 == it.Id)) return true;
            var w = Words(it.Title);
            if (w.Count == 0) return false;
            return s.Shown.Skip(Math.Max(0, s.Shown.Count - 30)).Any(t =>
            {
                var o = Words(t.Item2);
                int common = o.Count(x => w.Contains(x));
                return common >= 3 && common >= Math.Min(w.Count, o.Count) * 0.5;
            });
        }

        /// <summary>Arka planda 10 dakikada bir akışı yoklar; önemli ve yeni bir haber varsa show ile bildirir.</summary>
        public static void StartWatcher(Func<bool> enabled, Func<bool> busy, Action<NewsItem> show)
        {
            var t = new Thread(() =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(75));
                while (true)
                {
                    try { Check(enabled, busy, show); }
                    catch { }
                    Thread.Sleep(TimeSpan.FromMinutes(10));
                }
            }) { IsBackground = true, Name = "Haberler", Priority = ThreadPriority.BelowNormal };
            t.Start();
        }

        static void Check(Func<bool> enabled, Func<bool> busy, Action<NewsItem> show)
        {
            if (!enabled()) return;
            var now = DateTime.Now;
            if (now.Hour < QuietUntilHour) return;
            var s = LoadState();
            if (s.Day != now.Date) { s.Day = now.Date; s.Count = 0; }
            if ((now - s.LastShown).TotalMinutes < MinGapMinutes || s.Count >= MaxPerDay) return;
            var best = Best(Fetch().Where(i => !AlreadyShown(s, i)));
            if (best == null || busy()) return;
            LoadDetails(best);
            s.Shown.Add(Tuple.Create(best.Id, best.Title));
            s.LastShown = now;
            s.Count++;
            SaveState(s);
            show(best);
        }
    }

    /// <summary>Sağ üstten kayarak gelen macOS tarzı haber bildirimi. Tıklayınca ayrıntı penceresi açılır.</summary>
    public class NewsToast : Window
    {
        static NewsToast _current;

        /// <summary>Açık bildirimin pencere tutamacı (adanın buzlu cam yakalamasından hariç tutmak için).</summary>
        public static IntPtr Handle { get { return _current == null ? IntPtr.Zero : new WindowInteropHelper(_current).Handle; } }

        public static void Present(NewsItem it)
        {
            if (_current != null) _current.Dismiss();
            _current = new NewsToast(it);
            Sounds.Play(Sound.News);
            _current.Show();
        }

        const string Xaml = @"
<Grid x:Name='Root' Margin='14,10,14,24' " + Theme.Ns + @">
  <Grid.RenderTransform><TranslateTransform x:Name='Slide'/></Grid.RenderTransform>
  <Border x:Name='Card' CornerRadius='16' Background='{DynamicResource ToastBg}' BorderBrush='{DynamicResource ToastEdge}' BorderThickness='1' Cursor='Hand'>
    <Border.Effect><DropShadowEffect BlurRadius='22' ShadowDepth='5' Direction='270' Opacity='0.3' Color='Black'/></Border.Effect>
  </Border>
  <Grid Margin='14,11,14,13' IsHitTestVisible='False'>
    <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
    <Grid>
      <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
      LOGO
      <TextBlock Grid.Column='1' Text='PARDUX  ·  GÜNDEM' Foreground='{DynamicResource FgDim}' FontSize='10.5' FontWeight='SemiBold' Margin='7,0,0,0' VerticalAlignment='Center'/>
      <TextBlock x:Name='Time' Grid.Column='2' Foreground='{DynamicResource FgMuted}' FontSize='11' VerticalAlignment='Center'/>
    </Grid>
    <Grid Grid.Row='1' Margin='0,8,0,0'>
      <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
      <StackPanel VerticalAlignment='Center'>
        <TextBlock x:Name='Head' Foreground='{DynamicResource Fg}' FontSize='13.5' FontWeight='SemiBold' TextWrapping='Wrap' LineHeight='18'
                   MaxHeight='54' TextTrimming='CharacterEllipsis'/>
        <TextBlock x:Name='Src' Foreground='{DynamicResource FgDim}' FontSize='12' Margin='0,3,0,0' TextTrimming='CharacterEllipsis'/>
      </StackPanel>
      <Border x:Name='Thumb' Grid.Column='1' Width='54' Height='54' CornerRadius='9' Margin='12,0,0,0' VerticalAlignment='Center' Visibility='Collapsed'/>
    </Grid>
  </Grid>
  <Border x:Name='X' Width='26' Height='24' CornerRadius='5' HorizontalAlignment='Right' VerticalAlignment='Top' Margin='0,7,8,0' Opacity='0' ToolTip='Kapat'>
    <Border.Style>
      <Style TargetType='Border'>
        <Setter Property='Background' Value='Transparent'/>
        <Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='{DynamicResource BtnHover}'/></Trigger></Style.Triggers>
      </Style>
    </Border.Style>
    <TextBlock Text='&#xE8BB;' FontFamily='{DynamicResource IconFont}' FontSize='9' Foreground='{DynamicResource Fg}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
  </Border>
</Grid>";

        readonly NewsItem _item;
        readonly Grid _root;
        readonly TranslateTransform _slide;
        readonly Border _x;
        readonly DispatcherTimer _life;
        bool _closing;

        bool Animated { get { return Controller.Instance == null || Controller.Instance.Settings == null || Controller.Instance.Settings.Animations; } }

        NewsToast(NewsItem it)
        {
            _item = it;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Width = 392;
            SizeToContent = SizeToContent.Height;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            Resources.MergedDictionaries.Add(Theme.NewDict());

            _root = (Grid)XamlReader.Parse(Xaml.Replace("LOGO", Theme.Logo(18, 5, "T")));
            Content = _root;
            _slide = (TranslateTransform)_root.FindName("Slide");
            _x = (Border)_root.FindName("X");
            ((TextBlock)_root.FindName("Head")).Text = it.Title;
            ((TextBlock)_root.FindName("Src")).Text = it.Source;
            ((TextBlock)_root.FindName("Time")).Text = it.Age;
            if (it.Image != null)
            {
                var th = (Border)_root.FindName("Thumb");
                th.Background = new ImageBrush(it.Image) { Stretch = Stretch.UniformToFill };
                th.Visibility = Visibility.Visible;
            }

            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 2;
            Top = wa.Top + 4;

            _life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
            _life.Tick += (s, e) => Dismiss();
            ((Border)_root.FindName("Card")).MouseLeftButtonUp += (s, e) => { var c = Controller.Instance; Dismiss(); if (c != null) c.OpenNews(_item); };
            Ui.Click(_x, Dismiss);
            // Üzerine gelince sağ üstteki saat yerine kapatma düğmesi görünür (Windows bildirimleri gibi)
            var time = (TextBlock)_root.FindName("Time");
            _root.MouseEnter += (s, e) => { _life.Stop(); _x.BeginAnimation(OpacityProperty, Anim.To(1, 140)); time.BeginAnimation(OpacityProperty, Anim.To(0, 100)); };
            _root.MouseLeave += (s, e) =>
            {
                _life.Interval = TimeSpan.FromSeconds(4); _life.Start();
                _x.BeginAnimation(OpacityProperty, Anim.To(0, 160)); time.BeginAnimation(OpacityProperty, Anim.To(1, 160));
            };

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
                Native.SetWindowLong(h, GWL_EXSTYLE, Native.GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            };
            Loaded += (s, e) => Enter();
        }

        void Enter()
        {
            _life.Start();
            if (!Animated) return;
            _root.CacheMode = new BitmapCache(); // kayarken gölge her karede yeniden hesaplanmasın
            var a = new DoubleAnimation(ActualWidth, 0, TimeSpan.FromMilliseconds(680)) { EasingFunction = Anim.SpringOpen };
            a.Completed += (s, e) => _root.CacheMode = null; // yazılar keskin kalsın
            _slide.BeginAnimation(TranslateTransform.XProperty, a);
            _root.BeginAnimation(OpacityProperty, Anim.FromTo(0, 1, 200));
        }

        public void Dismiss()
        {
            if (_closing) return;
            _closing = true;
            _life.Stop();
            if (_current == this) _current = null;
            if (!Animated) { Close(); return; }
            _root.CacheMode = new BitmapCache();
            var a = new DoubleAnimation(ActualWidth + 20, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (s, e) => Close();
            _slide.BeginAnimation(TranslateTransform.XProperty, a);
            _root.BeginAnimation(OpacityProperty, Anim.To(0, 300));
        }
    }

    /// <summary>Haber ayrıntısı: görsel, başlık, özet, metin ve diğer kaynaklar.</summary>
    public class NewsWindow : DarkWindow
    {
        static string Xaml()
        {
            return @"
<Grid " + Theme.Ns + @">
  <Border Background='{DynamicResource WinTint}'/>
  <Grid>
    <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
    <Grid x:Name='Top' Height='44' Background='Transparent'>
      <StackPanel Orientation='Horizontal' VerticalAlignment='Center' Margin='20,0,0,0'>
        " + Theme.Logo(18, 5, "N") + @"
        <TextBlock Text='Gündem' Foreground='{DynamicResource Fg}' FontSize='12.5' Margin='9,0,0,0' VerticalAlignment='Center'/>
      </StackPanel>
      " + Ui.CaptionButtons + @"
    </Grid>
    <ScrollViewer x:Name='Scroll' Grid.Row='1' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>
      <StackPanel Margin='26,4,26,16'>
        <Border x:Name='Hero' Height='260' CornerRadius='12' Margin='0,0,0,18' Visibility='Collapsed'/>
        <StackPanel Orientation='Horizontal'>
          <Border CornerRadius='5' Background='{DynamicResource AccentSoft}' Padding='7,2'>
            <TextBlock x:Name='Src' Foreground='{DynamicResource AccentText}' FontSize='11.5' FontWeight='SemiBold'/>
          </Border>
          <TextBlock x:Name='When' Foreground='{DynamicResource FgMuted}' FontSize='12' Margin='9,0,0,0' VerticalAlignment='Center'/>
        </StackPanel>
        <TextBlock x:Name='Head' Foreground='{DynamicResource Fg}' FontSize='22' FontWeight='SemiBold' TextWrapping='Wrap' LineHeight='29' Margin='0,10,0,0'
                   FontFamily='Segoe UI Variable Display, Segoe UI'/>
        <TextBlock x:Name='Lead' Foreground='{DynamicResource Fg}' FontSize='14.5' TextWrapping='Wrap' LineHeight='22' Margin='0,12,0,0' Visibility='Collapsed'/>
        <TextBlock x:Name='Body' Foreground='{DynamicResource FgDim}' FontSize='13.5' TextWrapping='Wrap' LineHeight='22' Margin='0,12,0,0' Visibility='Collapsed'/>
        <TextBlock x:Name='Loading' Text='Ayrıntılar yükleniyor…' Foreground='{DynamicResource FgMuted}' FontSize='12.5' Margin='0,14,0,0'/>
        <TextBlock x:Name='RelTitle' Text='Diğer kaynaklarda' Style='{DynamicResource GroupTitle}' Margin='2,22,0,7' Visibility='Collapsed'/>
        <Border x:Name='RelCard' Style='{DynamicResource Card}' Visibility='Collapsed'>
          <StackPanel x:Name='Rel'/>
        </Border>
      </StackPanel>
    </ScrollViewer>
    <Border Grid.Row='2' BorderBrush='{DynamicResource Line}' BorderThickness='0,1,0,0'>
      <Grid Margin='26,12,24,14'>
        <TextBlock Text='Kaynak: Google Haberler' Foreground='{DynamicResource FgMuted}' FontSize='11.5' VerticalAlignment='Center'/>
        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
          <Button x:Name='CloseBtn' Content='Kapat' Style='{DynamicResource Btn}' Margin='0,0,8,0' MinWidth='76'/>
          <Button x:Name='Read' Content='Haberin tamamını oku' Style='{DynamicResource PrimaryBtn}'/>
        </StackPanel>
      </Grid>
    </Border>
  </Grid>
</Grid>";
        }

        readonly NewsItem _it;

        public NewsWindow(NewsItem it)
        {
            _it = it;
            Init("Gündem — " + it.Title, Xaml(), 600, true);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Find<ScrollViewer>("Scroll").MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 220);
            Find<TextBlock>("Src").Text = it.Source;
            Find<TextBlock>("When").Text = it.Age;
            Find<TextBlock>("Head").Text = it.Title;

            int n = 0;
            foreach (var r in it.Related.Take(5))
            {
                var link = r;
                if (n++ > 0) Find<StackPanel>("Rel").Children.Add(new Border { Style = (Style)FindResource("Sep") });
                Find<StackPanel>("Rel").Children.Add(RelatedRow(link));
            }
            if (n > 0) { Find<FrameworkElement>("RelTitle").Visibility = Visibility.Visible; Find<FrameworkElement>("RelCard").Visibility = Visibility.Visible; }

            MouseButtonEventHandler drag = (o, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            Find<Grid>("Top").MouseLeftButtonDown += drag;
            Ui.Click(Find<Border>("CloseX"), Close);
            Ui.Click(Find<Border>("MinX"), () => WindowState = WindowState.Minimized);
            Find<Button>("CloseBtn").Click += (o, e) => Close();
            Find<Button>("Read").Click += (o, e) => { Shell.OpenUrl(_it.Url ?? _it.Link); Close(); };
            KeyDown += (o, e) => { if (e.Key == Key.Escape) Close(); };

            if (it.DetailsLoaded) Fill();
            else ThreadPool.QueueUserWorkItem(_ =>
            {
                News.LoadDetails(it);
                Dispatcher.BeginInvoke(new Action(Fill));
            });
            bool anim = Controller.Instance == null || Controller.Instance.Settings.Animations;
            Loaded += (o, e) => { if (anim) Anim.Enter(Body, 0, 10, 300); };
        }

        FrameworkElement RelatedRow(NewsLink l)
        {
            var g = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var sp = new StackPanel { Margin = new Thickness(14, 9, 14, 9) };
            var t = new TextBlock { Text = l.Title, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            var s = new TextBlock { Text = l.Source, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
            s.SetResourceReference(TextBlock.ForegroundProperty, "FgMuted");
            sp.Children.Add(t);
            sp.Children.Add(s);
            g.Children.Add(sp);
            g.MouseEnter += (o, e) => g.SetResourceReference(Panel.BackgroundProperty, "Hover");
            g.MouseLeave += (o, e) => g.Background = Brushes.Transparent;
            Ui.Click(g, () => Shell.OpenUrl(l.Link));
            return g;
        }

        void Fill()
        {
            Find<FrameworkElement>("Loading").Visibility = Visibility.Collapsed;
            bool anim = Controller.Instance == null || Controller.Instance.Settings.Animations;
            if (_it.Image != null)
            {
                var hero = Find<Border>("Hero");
                hero.Background = new ImageBrush(_it.Image) { Stretch = Stretch.UniformToFill };
                hero.Visibility = Visibility.Visible;
                if (anim) Anim.Enter(hero, 0, 6, 320);
            }
            Reveal(Find<TextBlock>("Lead"), _it.Summary, anim, 40);
            Reveal(Find<TextBlock>("Body"), _it.Body, anim, 90);
            if (string.IsNullOrEmpty(_it.Summary) && string.IsNullOrEmpty(_it.Body))
            {
                var l = Find<TextBlock>("Loading");
                l.Text = "Bu haberin metni alınamadı. \"Haberin tamamını oku\" ile kaynağında açabilirsin.";
                l.Visibility = Visibility.Visible;
            }
        }

        static void Reveal(TextBlock t, string text, bool anim, int delay)
        {
            if (string.IsNullOrEmpty(text)) return;
            t.Text = text;
            t.Visibility = Visibility.Visible;
            if (anim) Anim.Enter(t, delay, 6, 280);
        }
    }
}
