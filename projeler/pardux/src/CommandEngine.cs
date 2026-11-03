using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace Pardux
{
    public interface IHost
    {
        Settings Settings { get; }
        void ShowCompose(string to, string subject, string body);
        /// <summary>WhatsApp mesaj panelini açar (kime: kişi adı ya da numara).</summary>
        void ShowWhatsApp(string to, string text);
        /// <summary>WhatsApp mesajını gönderir (sohbeti açar; ayara göre otomatik gönderir).</summary>
        void SendWhatsApp(string to, string text);
        /// <summary>Instagram sohbetini açar, mesajı panoya koyup yazar (göndermek için Enter kullanıcıda).</summary>
        void SendInstagram(string user, string text, string displayName);
        void SetQuery(string text);
        void OpenSettings();
        void ExitApp();
        void AddReminder(TimeSpan delay, string text);
        /// <summary>Discord'da kişiye yazar (send: Enter'a da basılır).</summary>
        void SendDiscord(string who, string text, bool send);
        /// <summary>Gemini'de yeni sohbet açıp soruyu gönderir.</summary>
        void AskGemini(string question);
        void Flash(string message);
        /// <summary>Arka planda gelen sonuçları listeye ekler; placeholder verilirse onun yerine koyar.</summary>
        void AppendResults(int version, List<ResultItem> items, ResultItem placeholder);
    }

    public class ResultItem : INotifyPropertyChanged
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Glyph { get; set; }
        public Brush GlyphBrush { get; set; }
        public Brush GlyphBackground { get; set; }
        public string Hint { get; set; }
        public string UsageKey { get; set; }
        public string Complete { get; set; }
        public Action Execute { get; set; }
        public bool KeepOpen { get; set; }
        /// <summary>Bilgi satırı (hava, internet cevabı): sesli komutta çalıştırılmaz, yalnızca okunur.</summary>
        public bool Info { get; set; }
        public bool NeedsConfirm { get; set; }
        public bool Confirming { get; set; }
        public int Score { get; set; }
        /// <summary>Sesle sorulduysa Pardux'un söyleyeceği kısa yanıt (ör. "Kronometreyi başlatıyorum").</summary>
        public string Speak { get; set; }
        /// <summary>Alt satır birkaç satıra yayılsın (internetten gelen cevaplar).</summary>
        public bool Wrap { get; set; }
        /// <summary>Sesli komutta sonuç gelince kendiliğinden çalıştırılır (ör. "rapor dosyasını aç").</summary>
        public bool AutoRun { get; set; }

        ImageSource _icon;
        public ImageSource Icon
        {
            get { return _icon; }
            set
            {
                _icon = value;
                Raise("Icon"); Raise("IconVisibility"); Raise("GlyphVisibility");
            }
        }

        public Visibility IconVisibility { get { return _icon != null ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility GlyphVisibility { get { return _icon == null ? Visibility.Visible : Visibility.Collapsed; } }

        public void BeginConfirm()
        {
            Confirming = true;
            Title = "Emin misin? " + Title;
            Subtitle = "Onaylamak için tekrar Enter'a bas · Vazgeçmek için Esc";
            Hint = "Onayla";
            Raise("Title"); Raise("Subtitle"); Raise("Hint");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Raise(string p)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(p));
        }
    }

    internal static class Palette
    {
        public static readonly Brush Accent = Make("#A28BFF");
        public static readonly Brush Spotify = Make("#1ED760");
        public static readonly Brush Mail = Make("#FF7A7A");
        public static readonly Brush Web = Make("#5DB1FF");
        public static readonly Brush System = Make("#FFBE5C");
        public static readonly Brush Calc = Make("#3DD6C6");
        public static readonly Brush Reminder = Make("#FF86CF");
        public static readonly Brush Folder = Make("#FFD25A");
        public static readonly Brush Danger = Make("#FF6161");
        public static readonly Brush Ai = Make("#C9A7FF");
        public static readonly Brush Steam = Make("#66C0F4");

        public static Brush Make(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public static Brush Soft(Brush b)
        {
            Color c = ((SolidColorBrush)b).Color;
            var s = new SolidColorBrush(Color.FromArgb(0x2A, c.R, c.G, c.B));
            s.Freeze();
            return s;
        }
    }

    internal static class G
    {
        public const string Search = "", Music = "", Play = "", Pause = "", Next = "", Prev = "";
        public const string Mail = "", Globe = "", Calc = "", Clock = "", Lock = "", Power = "";
        public const string Sleep = "", Restart = "", Volume = "", Mute = "", Settings = "", Folder = "";
        public const string Camera = "", Close = "", App = "", Video = "", Map = "", Cloud = "";
        public const string Edit = "", Recycle = "", Translate = "", Diag = "", Refresh = "", Link = "";
        public const string Sparkle = "", Note = "", File = "", Battery = "", Chat = "", Check = "";
        public const string Copy = "", Send = "", Sun = "";
        public const string Download = "", Wifi = "", Bluetooth = "", Plane = "", Display = "", Bell = "";
    }

    public partial class CommandEngine
    {
        readonly IHost _host;
        Settings S { get { return _host.Settings; } }

        class SysCmd
        {
            public string[] Keys;
            public string Title, Sub, Glyph, Usage;
            public Brush Color;
            public Action Run;
            public bool Confirm, KeepOpen;
        }

        readonly List<SysCmd> _sys = new List<SysCmd>();

        static readonly HashSet<string> OpenVerbs = new HashSet<string> { "ac", "baslat", "calistir", "open", "run", "lutfen", "misin", "acar", "acsana", "aciver", "acabilir", "acarmisin", "oyna", "oynayalim", "oyunu", "oyununu", "gir" };

        public CommandEngine(IHost host)
        {
            _host = host;
            BuildSystemCommands();
            AddAliases();
        }

        void Sys(string keys, string title, string sub, string glyph, Brush color, Action run, bool confirm = false, bool keepOpen = false)
        {
            _sys.Add(new SysCmd
            {
                Keys = keys.Split('|').Select(TextUtil.Norm).ToArray(),
                Title = title, Sub = sub, Glyph = glyph, Color = color, Run = run, Confirm = confirm, KeepOpen = keepOpen,
                Usage = "sys:" + title
            });
        }

        static void SpotifyAsync(Func<Spotify.Outcome> f)
        {
            Shell.Background(() =>
            {
                var o = f();
                if (!o.Ok) Controller.Instance.Notify(o.Message);
                // Müzik başladıysa Spotify'ı simge durumuna küçült: müzik adada hemen görünsün, ekran boş kalsın
                else if (o.Message.StartsWith("Çalıyor")) Spotify.Minimize();
                MediaWatcher.Poke();
            });
        }

        void BuildSystemCommands()
        {
            Sys("durdur|duraklat|dur|pause|muzigi durdur|sarkiyi durdur|muzigi duraklat", "Müziği duraklat", "Çalan müziği duraklatır / sürdürür", G.Pause, Palette.Spotify, MediaWatcher.PlayPause);
            Sys("devam|devam et|oynat|play|cal|muzige devam", "Müziği devam ettir", "Spotify kapalıysa açılır ve kaldığı yerden çalar", G.Play, Palette.Spotify, () => SpotifyAsync(Spotify.ResumeSync));
            Sys("sonraki|sonraki sarki|gec|atla|next|siradaki|sarkiyi gec", "Sonraki şarkı", "Medya: sonraki parça", G.Next, Palette.Spotify, MediaWatcher.Next);
            Sys("onceki|onceki sarki|previous|bir onceki|geri sar", "Önceki şarkı", "Medya: önceki parça", G.Prev, Palette.Spotify, MediaWatcher.Prev);
            Sys("ses ac|sesi ac|ses yukselt|sesi yukselt|sesi arttir|ses arttir|volume up|sesi artir", "Sesi yükselt", "+%10", G.Volume, Palette.System, () => Media.VolumeStep(5), false, true);
            Sys("ses kis|sesi kis|sesi azalt|ses azalt|volume down|sesi dusur", "Sesi kıs", "-%10", G.Volume, Palette.System, () => Media.VolumeStep(-5), false, true);
            Sys("sessiz|sesi kapat|ses kapat|mute|sessize al", "Sessize al / sesi geri aç", "Ses kapat-aç", G.Mute, Palette.System, Media.Mute);
            Sys("kilitle|bilgisayari kilitle|ekrani kilitle|lock", "Bilgisayarı kilitle", "Windows oturumunu kilitler", G.Lock, Palette.System, SystemActions.Lock);
            Sys("uyku|uyut|uyku modu|bilgisayari uyut|sleep", "Uyku moduna al", "Bilgisayarı uyutur", G.Sleep, Palette.System, SystemActions.Sleep, true);
            Sys("kapat|bilgisayari kapat|bilgisayar kapat|shutdown|sistemi kapat", "Bilgisayarı kapat", "Tüm uygulamalar kapanır", G.Power, Palette.Danger, SystemActions.Shutdown, true);
            Sys("yeniden baslat|restart|bilgisayari yeniden baslat", "Yeniden başlat", "Bilgisayarı yeniden başlatır", G.Restart, Palette.Danger, SystemActions.Restart, true);
            Sys("oturumu kapat|cikis yap|log off|oturum kapat", "Oturumu kapat", "Windows oturumundan çıkar", G.Power, Palette.Danger, SystemActions.LogOff, true);
            Sys("ekran goruntusu|ss al|ss|ekran alintisi|screenshot|ekran resmi", "Ekran görüntüsü al", "Ekran alıntısı aracı", G.Camera, Palette.Accent, () => Shell.Open("ms-screenclip:"));
            Sys("ayarlar|windows ayarlari|settings", "Windows Ayarları", "ms-settings", G.Settings, Palette.System, () => Shell.Open("ms-settings:"));
            Sys("wifi|wi fi|internet ayarlari|ag ayarlari", "Wi-Fi ayarları", "Ağ bağlantıları", G.Settings, Palette.System, () => Shell.Open("ms-settings:network-wifi"));
            Sys("bluetooth|bluetooth ayarlari", "Bluetooth ayarları", "Cihaz eşleştirme", G.Settings, Palette.System, () => Shell.Open("ms-settings:bluetooth"));
            Sys("ses ayarlari|hoparlor|mikrofon ayarlari", "Ses ayarları", "Çıkış/giriş aygıtları", G.Volume, Palette.System, () => Shell.Open("ms-settings:sound"));
            Sys("ekran ayarlari|parlaklik|cozunurluk", "Ekran ayarları", "Parlaklık, çözünürlük", G.Settings, Palette.System, () => Shell.Open("ms-settings:display"));
            Sys("gorev yoneticisi|task manager", "Görev Yöneticisi", "taskmgr", G.Diag, Palette.System, () => Shell.Open("taskmgr.exe"));
            Sys("denetim masasi|control panel", "Denetim Masası", "control", G.Settings, Palette.System, () => Shell.Open("control.exe"));
            Sys("cop kutusu|geri donusum kutusu|recycle bin", "Geri Dönüşüm Kutusu", "Klasörü açar", G.Recycle, Palette.Folder, () => Shell.Open("explorer.exe", "shell:RecycleBinFolder"));
            Sys("dosya gezgini|bu bilgisayar|explorer|dosyalar", "Dosya Gezgini", "Bu bilgisayar", G.Folder, Palette.Folder, () => Shell.Open("explorer.exe"));
            Sys("notlar|notlarim|notlari ac|not defterim", "Notlarım", "Pardux ile aldığın notlar", G.Note, Palette.Accent, Notes.Open);
            foreach (var f in new[] { "Indirilenler", "Belgeler", "Masaustu", "Resimler", "Muzik", "Videolar" })
            {
                string key = f.ToLowerInvariant();
                string disp = f.Replace("Masaustu", "Masaüstü").Replace("Indirilenler", "İndirilenler").Replace("Muzik", "Müzik");
                Sys(key + "|" + key + " klasoru", disp + " klasörü", SystemActions.KnownFolder(key), G.Folder, Palette.Folder, () => Shell.Open(SystemActions.KnownFolder(key)));
            }
            Sys("pardux ayarlari|parduks ayarlari|asistan ayarlari|pardux ayar|asistan ayar|kisayol degistir|api anahtari", "Pardux ayarları", "Kısayol, yapay zekâ, mail, Spotify", G.Settings, Palette.Accent, () => _host.OpenSettings());
            Sys("uygulamalari yenile|listeyi yenile|yenile", "Uygulama listesini yenile", "Yeni kurulan uygulamaları bulur", G.Refresh, Palette.Accent, () => { AppIndex.RefreshAsync(); _host.Flash("Uygulama listesi yenileniyor…"); }, false, true);
            Sys("asistani kapat|pardux kapat|parduks kapat|parduxu kapat|parduksu kapat|parduxtan cik|parduksdan cik|asistandan cik", "Pardux'u kapat", "Uygulamadan tamamen çıkar", G.Close, Palette.Danger, () => _host.ExitApp());
        }

        int _latest; // en son sorgunun sürümü (yazmaya devam edilince eski aramalar iptal edilir)

        static readonly Regex QuestionRx = new Regex(@"\b(nedir|kimdir|ne zaman|nasil|nasıl|kac|kaç|nerede|neresi|neden|niye|niçin|nicin|hangi|hangisi|kim|kimin|ne demek|anlami|anlamı|tarifi|kaçtır|kactir|mi|mı|mu|mü|midir|mıdır|ne kadar|anlat|açıkla|acikla|bilgi ver|hakkında|hakkinda|araştır|arastir)\b", RegexOptions.Compiled);

        /// <summary>
        /// İnternette aranacak asıl soru: "google'da ara", "araştır", "bana söyle" gibi komut sözcükleri atılır
        /// ("google'da everest kaç metre ara" → "everest kaç metre").
        /// </summary>
        internal static string ResearchQuery(string text)
        {
            string q = Regex.Replace(" " + text + " ", @"(?i)\s(google['’]?(da|de|dan|den)?|internet(te|ten)|ara(ştır|stir)?|araştırır mısın|bul|bana|söyle|soyle|söyler misin|lütfen|lutfen|emo)(?=\s)", " ");
            q = Regex.Replace(q, @"\s{2,}", " ").Trim();
            return q.Length >= 3 ? q : text;
        }

        /// <summary>
        /// Uygulamada karşılığı olmayan sorularda internette araştırır ve cevabı listede gösterir.
        /// Yalnızca soru gibi görünen ya da hiçbir komuta uymayan çok sözcüklü yazılarda; site/YouTube aramalarında devreye girmez.
        /// </summary>
        void TryResearch(string text, string n, List<ResultItem> r, int version)
        {
            if (!S.WebAnswers || text.Length < 4 || !text.Any(char.IsLetter)) return;
            // YouTube / site açma isteği: tarayıcı açılır, araştırılmaz
            if (r.Any(x => x.Title.StartsWith("YouTube'da ara") || x.Title.StartsWith("Siteyi aç"))) return;
            // Google araması ve harita önerisi cevabı engellemez: cevap onların üstünde gösterilir, bağlantılar altta kalır
            Func<ResultItem, bool> webFallback = x => x.Title.StartsWith("Google'da ara") || x.Title.StartsWith("Haritada göster");
            bool explicitSearch = r.Any(x => x.Title.StartsWith("Google'da ara"));
            int best = r.Where(x => !webFallback(x)).Select(x => x.Score).DefaultIfEmpty(0).Max();
            bool question = explicitSearch || text.EndsWith("?") || QuestionRx.IsMatch(text.ToLower(TextUtil.Tr)) || QuestionRx.IsMatch(n);
            bool multiWord = text.Trim().Contains(" ");
            bool unknownWord = !multiWord && text.Length >= 5 && best < 300; // tek sözcük, hiçbir komut ya da uygulama değil ("fotosentez")
            if (!(question && best < 1700) && !(multiWord && best < 600) && !unknownWord) return;
            var loading = Make("İnternette araştırılıyor…", text, G.Search, Palette.Web, question ? 1760 : 900, null);
            loading.KeepOpen = true;
            string q = ResearchQuery(text);
            Fetch("ara:" + TextUtil.Norm(text), () =>
            {
                // Yazarken her harfte arama yapılmasın: kısa bir duraklamayı bekle
                Thread.Sleep(550);
                if (version != _latest) return "!!eski";
                return WebTools.Research(q);
            }, data =>
            {
                var list = new List<ResultItem>();
                int sc = loading.Score;
                foreach (string line in data.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0))
                {
                    var p = line.Split('\t');
                    string url = p[p.Length - 1];
                    if (p[0] == "A" && p.Length >= 5)
                    {
                        var a = Make(p[1], p[2], G.Sparkle, Palette.Accent, sc--, () => Shell.OpenUrl(url));
                        a.Wrap = true;
                        a.Hint = p[3];
                        a.Speak = BestSentence(p[2], q);
                        a.Info = true;
                        list.Add(a);
                    }
                    else if (p[0] == "R" && p.Length >= 4)
                    {
                        var it = Make(p[1], p[2], G.Globe, Palette.Web, sc--, () => Shell.OpenUrl(url));
                        it.Hint = "Aç";
                        list.Add(it);
                    }
                }
                if (list.Count == 0)
                {
                    var none = Make("İnternette bir şey bulunamadı", "Google'da aramak için aşağıdaki satırı seç", G.Search, Palette.Web, loading.Score, null);
                    none.KeepOpen = true;
                    list.Add(none);
                }
                return list;
            }, version, r, loading);
        }

        /// <summary>
        /// Sesli okunacak cümle: "kaç / ne zaman / hangi yıl" gibi sayı bekleyen sorularda sayı içeren ilk cümle,
        /// diğerlerinde ilk cümle.
        /// </summary>
        static string BestSentence(string s, string question)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string q = TextUtil.Norm(question);
            if (Regex.IsMatch(q, @"\b(kac|ne zaman|hangi yil|kacinci|yasinda|yuksekligi|nufusu|uzunlugu|kilometre|metre)\b"))
            {
                foreach (Match m in Regex.Matches(s, @"[^.!?]*\d[^.!?]*[.!?]"))
                {
                    string sent = m.Value.Trim();
                    if (sent.Length >= 15) return sent;
                }
            }
            return FirstSentence(s);
        }

        static string FirstSentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var m = Regex.Match(s, @"^(.{20,220}?[.!?])(\s|$)");
            return m.Success ? m.Groups[1].Value : (s.Length > 220 ? s.Substring(0, 220) : s);
        }

        public List<ResultItem> Query(string input, int version)
        {
            _latest = version;
            string text = (input ?? "").Trim();
            var r = new List<ResultItem>();
            // Boş arama: uygulama listesi gösterilmez, yerine "bugün" paneli görünür (IslandWindow)
            if (text.Length == 0) return new List<ResultItem>();
            string raw = text;
            text = Simplify(text);
            if (text.Length == 0) text = raw;

            string n = TextUtil.Norm(TextUtil.StripAllSuffixes(text));

            try
            {
                TryCalc(text, r);
                TryReminder(text, n, r);
                TryVolume(n, r);
                TryNote(text, n, r);
                TryInfo(n, r);
                TrySpotify(text, r);
                TryDiscord(text, r);
                TryGemini(text, r);
                TrySteam(text, n, r);
                TryToggles(n, r);
                TryMissed(n, r);
                if (!r.Any(x => x.Score >= 2400) && !TryWhatsApp(text, r) && !TryInstagram(text, r)) TryMail(text, n, r);
                TryStopwatch(n, r);
                TryWeb(text, n, r);
                TryCloseApp(n, r);
                TryFiles(text, n, r, version);
                TryWeather(text, n, r, version);
                TryNews(n, r, version);
                TryCurrency(n, r, version);
                TryClipboard(n, r);
                TryReminderList(n, r);
                TryTheme(n, r);
                AddSystem(n, r);
                AddCustom(n, r);
                AddApps(n, r);
                TryOpenAsFile(text, r, version);
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
            try { TryResearch(text, n, r, version); } catch (Exception ex) { Debug.WriteLine(ex); }

            var ordered = r.OrderByDescending(x => x.Score).Take(8).ToList();
            if (!ordered.Any(x => x.Title.StartsWith("Google'da ara")))
            {
                string g = raw;
                ordered.Add(Make("Google'da ara: " + raw, "Web araması", G.Search, Palette.Web, 0,
                    () => Shell.Open("https://www.google.com/search?q=" + Shell.Esc(g))));
            }
            return ordered;
        }

        static ResultItem Make(string title, string sub, string glyph, Brush color, int score, Action exec)
        {
            return new ResultItem
            {
                Title = title, Subtitle = sub, Glyph = glyph, GlyphBrush = color, GlyphBackground = Palette.Soft(color),
                Score = score, Execute = exec, Hint = "Enter"
            };
        }

        List<ResultItem> Suggestions()
        {
            var list = new List<ResultItem>();
            var np = MediaWatcher.Current;
            if (np != null)
            {
                var media = Make((np.IsPlaying ? "Çalıyor: " : "Duraklatıldı: ") + np.Title,
                    (string.IsNullOrEmpty(np.Artist) ? np.App : np.Artist + "  ·  " + np.App) + "  ·  Enter " + (np.IsPlaying ? "duraklatır" : "sürdürür"),
                    np.IsPlaying ? G.Pause : G.Play, Palette.Spotify, 0, MediaWatcher.PlayPause);
                media.KeepOpen = true;
                media.Hint = np.IsPlaying ? "Duraklat" : "Oynat";
                if (np.Art != null) media.Icon = np.Art;
                list.Add(media);
            }
            var apps = AppIndex.Apps;
            foreach (var u in S.Usage.Where(k => k.Key.StartsWith("app:")).OrderByDescending(k => k.Value).Take(S.ShowFrequent ? 3 : 0))
            {
                var app = apps.FirstOrDefault(a => "app:" + a.Name == u.Key);
                if (app != null)
                {
                    var it = AppItem(app, 0);
                    it.Subtitle = "Sık kullandığın";
                    list.Add(it);
                }
            }
            Action<string, string, string, Brush, string> hint = (t, s, g, c, q) =>
            {
                var it = Make(t, s, g, c, 0, () => _host.SetQuery(q));
                it.KeepOpen = true;
                it.Hint = "Tab";
                it.Complete = q;
                list.Add(it);
            };
            hint("Müzik aç", "Spotify açılır, kaldığın şarkı devam eder", G.Music, Palette.Spotify, "müzik aç");
            hint("Hava durumu", "Örn: \"hava\", \"ankara hava durumu\"", G.Cloud, Palette.Web, "hava ");
            if (S.ClipboardHistory && Controller.Instance != null && Controller.Instance.Clips.Count > 0)
                hint("Pano geçmişi", "Son kopyaladıkların · seçince yapıştırır", G.Copy, Palette.Calc, "pano");
            hint("Hatırlatıcı kur", "Örn: \"10 dk sonra çayı hatırlat\"", G.Clock, Palette.Reminder, "10 dk sonra hatırlat ");
            hint("Dosya bul", "Örn: \"bul rapor\"", G.File, Palette.Folder, "bul ");
            return list;
        }

        // ---------- Hava durumu (anahtarsız, Open-Meteo) ----------
        static readonly Dictionary<string, Tuple<DateTime, string>> NetCache = new Dictionary<string, Tuple<DateTime, string>>();

        /// <summary>İnternetten gelen cevabı 10 dk önbellekle arka planda alır, sonucu listeye ekler.</summary>
        void Fetch(string cacheKey, Func<string> fetch, Func<string, List<ResultItem>> build, int version, List<ResultItem> r, ResultItem loading)
        {
            Tuple<DateTime, string> c;
            lock (NetCache)
            {
                if (NetCache.TryGetValue(cacheKey, out c) && (DateTime.Now - c.Item1).TotalMinutes < 10)
                {
                    r.AddRange(build(c.Item2));
                    return;
                }
            }
            r.Add(loading);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string data;
                try
                {
                    data = fetch();
                    // "!!eski": kullanıcı bu arada yazmaya devam etti, sonuç artık gereksiz (önbelleğe de alınmaz)
                    if (data == "!!eski") return;
                    if (!data.StartsWith("!")) lock (NetCache) NetCache[cacheKey] = Tuple.Create(DateTime.Now, data);
                }
                catch (Exception ex) { data = "!" + ex.Message; }
                var app = Application.Current;
                if (app == null) return;
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    List<ResultItem> items;
                    if (data.StartsWith("!"))
                    {
                        var err = Make("Bilgi alınamadı", "İnternet bağlantını kontrol et", G.Cloud, Palette.Danger, loading.Score, null);
                        err.KeepOpen = true;
                        items = new List<ResultItem> { err };
                    }
                    else items = build(data);
                    _host.AppendResults(version, items, loading);
                }));
            });
        }

        void TryWeather(string text, string n, List<ResultItem> r, int version)
        {
            var words = n.Split(' ').ToList();
            if (!words.Contains("hava") && !words.Contains("havadurumu")) return;
            var drop = new HashSet<string> { "hava", "durumu", "havadurumu", "nasil", "ne", "bugun", "yarin", "simdi", "da", "de", "ta", "te", "icin", "olacak", "kac", "derece", "sicaklik" };
            string city = string.Join(" ", text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(TextUtil.StripSuffix).Where(w => !drop.Contains(TextUtil.Norm(w)))).Trim();
            if (city.Length > 0) city = city.Substring(0, 1).ToUpper(TextUtil.Tr) + city.Substring(1);
            var loading = Make("Hava durumuna bakılıyor…", city.Length > 0 ? city : "Konumuna göre", G.Cloud, Palette.Web, 2700, null);
            loading.KeepOpen = true;
            string q = city;
            Fetch("hava:" + TextUtil.Norm(city), () => WebTools.Weather(q), data =>
            {
                var lines = data.Split('\n').Select(l => l.Trim()).ToList();
                var list = new List<ResultItem>();
                if (lines.Count < 2 || !lines[0].StartsWith("Konum"))
                {
                    var none = Make(lines.FirstOrDefault() ?? "Bulunamadı", "İl adıyla dene, ör. \"ankara hava\"", G.Cloud, Palette.Web, 2700, null);
                    none.KeepOpen = true;
                    list.Add(none);
                    return list;
                }
                string place = lines[0].Substring(7).Split(',')[0].Trim();
                string now = lines[1].Replace("Şu an: ", "");
                string search = "https://www.google.com/search?q=" + Shell.Esc(place + " hava durumu");
                var main = Make(place + " · " + now.Split(',')[0] + ", " + (now.Split(',').Length > 1 ? now.Split(',')[1].Trim() : ""),
                    string.Join(", ", now.Split(',').Skip(2).Select(x => x.Trim())), G.Cloud, Palette.Web, 2700, () => Shell.OpenUrl(search));
                main.Hint = "Ayrıntı";
                main.Info = true;
                main.Speak = place + " için hava " + now.Split(',')[0].Replace("°C", " derece").Trim() + (now.Split(',').Length > 1 ? ", " + now.Split(',')[1].Trim() : "");
                list.Add(main);
                int sc = 2699;
                foreach (string l in lines.Skip(2))
                {
                    int colon = l.IndexOf(": ", StringComparison.Ordinal);
                    if (colon < 0) continue;
                    var it = Make(l.Substring(0, l.IndexOf(" (", StringComparison.Ordinal) > 0 ? l.IndexOf(" (", StringComparison.Ordinal) : colon) + ": " + l.Substring(colon + 2).Split(',')[0],
                        string.Join(",", l.Substring(colon + 2).Split(',').Skip(1)).Trim(), G.Cloud, Palette.Web, sc--, () => Shell.OpenUrl(search));
                    it.Hint = "Ayrıntı";
                    list.Add(it);
                }
                return list;
            }, version, r, loading);
        }

        // ---------- Gündem haberleri ----------
        void TryNews(string n, List<ResultItem> r, int version)
        {
            var words = n.Split(' ');
            if (!words.Any(w => w.StartsWith("haber") || w == "gundem" || w == "manset") && !n.Contains("son dakika")) return;
            var loading = Make("Gündem getiriliyor…", "Google Haberler · öne çıkanlar", G.Globe, Palette.Danger, 2650, null);
            loading.KeepOpen = true;
            Fetch("haberler", () => { News.Fetch(); return "ok"; }, data =>
            {
                var list = new List<ResultItem>();
                int sc = 2650;
                foreach (var it in News.Latest.Take(7))
                {
                    var item = it;
                    var ri = Make(it.Title, it.Source + "  ·  " + it.Age, G.Globe, Palette.Danger, sc--, () => Controller.Instance.OpenNews(item));
                    ri.Hint = "Oku";
                    list.Add(ri);
                }
                return list;
            }, version, r, loading);
        }

        // ---------- Döviz (anahtarsız, open.er-api.com) ----------
        static readonly Dictionary<string, string> CurrencyWords = new Dictionary<string, string>
        {
            { "dolar", "USD" }, { "usd", "USD" }, { "euro", "EUR" }, { "avro", "EUR" }, { "eur", "EUR" }, { "sterlin", "GBP" }, { "pound", "GBP" },
            { "gbp", "GBP" }, { "frank", "CHF" }, { "yen", "JPY" }, { "ruble", "RUB" }, { "riyal", "SAR" }, { "manat", "AZN" }, { "lira", "TRY" }, { "tl", "TRY" }
        };

        void TryCurrency(string n, List<ResultItem> r, int version)
        {
            var words = n.Split(' ');
            var codes = words.Select(w => { string c; return CurrencyWords.TryGetValue(w, out c) ? c : null; }).Where(c => c != null).ToList();
            bool kw = words.Contains("kur") || words.Contains("doviz") || words.Contains("kac");
            string from = codes.FirstOrDefault(c => c != "TRY");
            if (from == null) { if (words.Length == 1 && (n == "kur" || n == "doviz")) from = "USD"; else return; }
            if (words.Length > 4 && !kw) return;
            string to = codes.Count > 1 && codes[1] != from ? codes[1] : "TRY";
            if (to == from) to = "TRY";
            double amount = 1;
            var num = Regex.Match(n, @"\b(\d+(?:\s\d+)?)\b");
            if (num.Success) double.TryParse(num.Groups[1].Value.Replace(" ", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out amount);
            if (amount <= 0) amount = 1;
            var loading = Make("Kur bilgisi alınıyor…", from + " → " + to, G.Calc, Palette.Calc, 2700, null);
            loading.KeepOpen = true;
            double amt = amount;
            string f = from, t = to;
            Fetch("kur:" + f + t, () => WebTools.Exchange(f, t, 1), data =>
            {
                var list = new List<ResultItem>();
                var m = Regex.Match(data, @"1 " + f + " = ([\\d.,]+) " + t);
                double rate;
                if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(".", "").Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out rate))
                {
                    var none = Make("Kur bulunamadı", data, G.Calc, Palette.Calc, 2700, null);
                    none.KeepOpen = true;
                    list.Add(none);
                    return list;
                }
                string total = (amt * rate).ToString("#,0.##", TextUtil.Tr);
                var it = Make(amt.ToString("#,0.##", TextUtil.Tr) + " " + f + " = " + total + " " + t, "1 " + f + " = " + rate.ToString("#,0.####", TextUtil.Tr) + " " + t + " · Enter sonucu kopyalar",
                    G.Calc, Palette.Calc, 2700, null);
                it.Execute = () => { Clipboard.SetText(total); _host.Flash("Kopyalandı: " + total); };
                it.KeepOpen = true;
                it.Hint = "Kopyala";
                list.Add(it);
                return list;
            }, version, r, loading);
        }

        // ---------- Pano geçmişi ----------
        void TryClipboard(string n, List<ResultItem> r)
        {
            if (!(n == "pano" || n.StartsWith("pano gecmisi") || n.StartsWith("kopyaladiklarim") || n == "clipboard" || n.StartsWith("panodakiler"))) return;
            if (!S.ClipboardHistory)
            {
                var off = Make("Pano geçmişi kapalı", "Ayarlar → Genel bölümünden açabilirsin", G.Copy, Palette.Calc, 2800, () => _host.OpenSettings());
                r.Add(off);
                return;
            }
            var clips = Controller.Instance.Clips.ToList();
            if (clips.Count == 0)
            {
                var none = Make("Pano geçmişi boş", "Bir şey kopyaladığında burada görünür", G.Copy, Palette.Calc, 2800, null);
                none.KeepOpen = true;
                r.Add(none);
                return;
            }
            int sc = 2800;
            foreach (var c in clips.Take(8))
            {
                string clip = c.Item2;
                string one = Regex.Replace(clip, @"\s+", " ").Trim();
                var it = Make(one.Length > 90 ? one.Substring(0, 90) + "…" : one, Ago(c.Item1) + " · " + clip.Length + " karakter · Enter yapıştırır",
                    G.Copy, Palette.Calc, sc--, () => Controller.Instance.PasteText(clip));
                it.Hint = "Yapıştır";
                r.Add(it);
            }
        }

        static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "az önce";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + " dk önce";
            if (d.TotalDays < 1) return (int)d.TotalHours + " saat önce";
            return t.ToString("dd.MM HH:mm");
        }

        // ---------- Hatırlatıcı listesi ----------
        void TryReminderList(string n, List<ResultItem> r)
        {
            if (!(n.StartsWith("hatirlaticilar") || n.StartsWith("hatirlatmalar") || n == "alarmlar" || n.StartsWith("zamanlayicilar"))) return;
            var list = Controller.Instance.Reminders;
            if (list.Count == 0)
            {
                var none = Make("Kurulu hatırlatıcı yok", "Örn: \"10 dk sonra çayı hatırlat\"", G.Clock, Palette.Reminder, 2800, () => _host.SetQuery("10 dk sonra hatırlat "));
                none.KeepOpen = true;
                r.Add(none);
                return;
            }
            int sc = 2800;
            foreach (var rem in list.OrderBy(x => x.Due))
            {
                var item = rem;
                var it = Make(rem.Text, rem.Due.ToString("HH:mm") + " · " + Describe(rem.Due - DateTime.Now) + " kaldı · Enter iptal eder", G.Clock, Palette.Reminder, sc--, null);
                it.Execute = () => { Controller.Instance.CancelReminder(item); _host.Flash("Hatırlatıcı iptal edildi"); _host.SetQuery("hatırlatıcılar"); };
                it.KeepOpen = true;
                it.Hint = "İptal et";
                r.Add(it);
            }
        }

        // ---------- Tema ----------
        void TryTheme(string n, List<ResultItem> r)
        {
            string target = null;
            if (n.Contains("karanlik mod") || n.Contains("koyu tema") || n == "tema koyu" || n == "dark mode") target = "koyu";
            else if (n.Contains("aydinlik mod") || n.Contains("acik tema") || n == "tema acik" || n == "light mode") target = "acik";
            else if (n == "tema" || n == "temayi degistir" || n == "tema degistir") target = Theme.IsDark ? "acik" : "koyu";
            if (target == null) return;
            string t = target;
            var it = Make(t == "koyu" ? "Koyu temaya geç" : "Açık temaya geç", "Pardux'un görünümünü değiştirir", t == "koyu" ? G.Sleep : G.Sun, Palette.Accent, 2400,
                () => { S.Theme = t; S.Save(); Theme.Apply(S); _host.Flash(t == "koyu" ? "Koyu tema" : "Açık tema"); });
            it.KeepOpen = true;
            r.Add(it);
        }

        // ---------- Hesap makinesi ----------
        void TryCalc(string text, List<ResultItem> r)
        {
            string e = text.Replace("×", "*").Replace("÷", "/").Replace(" x ", "*").Replace(",", ".");
            if (e.StartsWith("=")) e = e.Substring(1);
            if (!Regex.IsMatch(e, @"^[\d\s\.\+\-\*/\(\)]+$") || !Regex.IsMatch(e, @"\d") || !Regex.IsMatch(e, @"\d\s*[\+\-\*/]\s*[\d\(]|\)\s*[\+\-\*/]"))
                return;
            e = Regex.Replace(e, @"(?<![\d.])(\d+)(?![\d.])", "$1.0");
            double d;
            try { d = Convert.ToDouble(new DataTable().Compute(e, null), CultureInfo.InvariantCulture); }
            catch { return; }
            if (double.IsNaN(d) || double.IsInfinity(d)) return;
            string res = Math.Round(d, 10).ToString("#,0.##########", TextUtil.Tr);
            string plain = Math.Round(d, 10).ToString("0.##########", TextUtil.Tr);
            var item = Make("= " + res, "Sonucu kopyalamak için Enter", G.Calc, Palette.Calc, 3000, null);
            item.Execute = () => { Clipboard.SetText(plain); _host.Flash("Kopyalandı: " + plain); };
            item.KeepOpen = true;
            item.Hint = "Kopyala";
            r.Add(item);
        }

        // ---------- Hatırlatıcı ----------
        void TryReminder(string text, string n, List<ResultItem> r)
        {
            bool isTimer = n.Contains("zamanlayici") || n.Contains("geri say") || n.Contains("sayac");
            if (!(n.Contains("hatirlat") || isTimer || n.Contains("alarm") || n.Contains("sonra"))) return;
            string low = text.ToLower(TextUtil.Tr);
            TimeSpan delay;
            string matched;

            var m = Regex.Match(low, @"(\d+(?:[.,]\d+)?)\s*(saniye|sn|dakika|dak|dk|saat|sa)(?:l[ıiuü]k)?\b");
            if (m.Success)
            {
                double v = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                string u = m.Groups[2].Value;
                delay = u.StartsWith("sa") && u != "saniye" ? TimeSpan.FromHours(v) : (u == "saniye" || u == "sn" ? TimeSpan.FromSeconds(v) : TimeSpan.FromMinutes(v));
                matched = m.Value;
            }
            else
            {
                m = Regex.Match(low, @"(?:saat\s*)?(\d{1,2})[:.](\d{2})");
                if (!m.Success) return;
                int hh = int.Parse(m.Groups[1].Value), mm = int.Parse(m.Groups[2].Value);
                if (hh > 23 || mm > 59) return;
                DateTime at = DateTime.Today.AddHours(hh).AddMinutes(mm);
                if (at <= DateTime.Now) at = at.AddDays(1);
                delay = at - DateTime.Now;
                matched = m.Value;
            }
            if (delay <= TimeSpan.Zero) return;

            int pos = low.IndexOf(matched, StringComparison.Ordinal);
            string msg = text.Substring(0, pos) + " " + text.Substring(pos + matched.Length);
            var drop = new HashSet<string> { "sonra", "hatirlat", "hatirla", "bana", "zamanlayici", "kur", "alarm", "icinde", "lutfen", "de", "da", "te", "ta", "geri", "sayim", "sayac", "baslat", "luk", "lik", "dakikalik", "saniyelik", "saatlik" };
            msg = string.Join(" ", msg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !drop.Contains(TextUtil.Norm(TextUtil.StripSuffix(w)))).Select(TextUtil.StripSuffix)).Trim();
            if (msg.Length == 0) msg = "Süre doldu!";

            string when = DateTime.Now.Add(delay).ToString("HH:mm", TextUtil.Tr);
            var span = delay;
            var text2 = msg;
            if (isTimer)
            {
                string label = msg == "Süre doldu!" ? "Zamanlayıcı" : msg;
                r.Add(Make("Zamanlayıcı kur: " + Describe(delay), "Adada geri sayar · süre dolunca alarm çalar (" + when + ")", "", Palette.Make("#FF9F0A"), 2610,
                    () => Controller.Instance.AddTimer(span, label)));
                r[r.Count - 1].Speak = "Zamanlayıcıyı " + Describe(delay) + " olarak kuruyorum";
                return;
            }
            r.Add(Make("Hatırlatıcı kur: " + msg, Describe(delay) + " sonra (" + when + ")", G.Clock, Palette.Reminder, 2600,
                () => _host.AddReminder(span, text2)));
            r[r.Count - 1].Speak = "Tamam, " + Describe(delay) + " sonra hatırlatacağım";
        }

        static string Describe(TimeSpan t)
        {
            if (t.TotalMinutes < 1) return (int)t.TotalSeconds + " saniye";
            if (t.TotalHours < 1) return (int)Math.Round(t.TotalMinutes) + " dakika";
            int h = (int)t.TotalHours, m = t.Minutes;
            return h + " saat" + (m > 0 ? " " + m + " dakika" : "");
        }

        // ---------- Ses seviyesi ----------
        void TryVolume(string n, List<ResultItem> r)
        {
            var m = Regex.Match(n, @"^ses(?:i|ini)?\s+(?:seviyesi(?:ni)?\s+)?(\d{1,3})(?:\s+(?:yap|ayarla|olsun))?$");
            if (!m.Success) return;
            int v = Math.Min(100, int.Parse(m.Groups[1].Value));
            var it = Make("Sesi %" + v + " yap", "Ses seviyesini ayarlar", G.Volume, Palette.System, 2500, () => Media.SetVolume(v));
            it.KeepOpen = true;
            r.Add(it);
        }

        // ---------- Notlar ----------
        void TryNote(string text, string n, List<ResultItem> r)
        {
            var m = Regex.Match(text, @"^\s*(?:not\s*(?:al|et|ekle|yaz)?\s*[:\-]?|note:?)\s+(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success || n.StartsWith("notlar") || n.StartsWith("not defteri")) return;
            string note = m.Groups[1].Value.Trim();
            r.Add(Make("Not al: " + note, "Notlarına kaydedilir · \"notlarım\" ile görebilirsin", G.Note, Palette.Accent, 2600,
                () => { Notes.Add(note); Controller.Instance.Notify("Not kaydedildi"); }));
        }

        // ---------- Sistem bilgisi ----------
        void TryInfo(string n, List<ResultItem> r)
        {
            if (!(n == "pil" || n == "batarya" || n == "sarj" || n.StartsWith("pil durumu") || n.StartsWith("sistem durumu") || n.StartsWith("sistem bilgisi") || n == "ram" || n == "bellek")) return;
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            bool noBattery = ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery);
            string battery = noBattery ? "Pil yok (prize bağlı)" : "Pil %" + (int)(ps.BatteryLifePercent * 100) + (ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? " · şarjda" : " · pilde");
            var drive = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory));
            string disk = "C: " + (drive.AvailableFreeSpace / 1073741824.0).ToString("0", TextUtil.Tr) + " GB boş";
            var it = Make(battery, disk + " · ayrıntı için Enter", G.Battery, Palette.Calc, 2300, null);
            it.KeepOpen = true;
            it.Execute = () => Shell.Open("taskmgr.exe");
            it.Subtitle = disk + " · Enter Görev Yöneticisi'ni açar";
            r.Add(it);
        }

        // ---------- Spotify ----------
        static readonly HashSet<string> SpotifyFiller = new HashSet<string>
        {
            "spotify", "cal", "dinle", "oynat", "sarki", "sarkisi", "sarkisini", "sarkiyi", "sarkilari", "lutfen", "misin", "calar", "acar", "ac", "bana", "bir", "uzerinden"
        };
        static readonly HashSet<string> MusicWords = new HashSet<string> { "sarki", "sarkiyi", "sarkilar", "sarkilari", "muzik", "muzigi", "muzikler", "biraz", "bir", "baslat", "devam", "et", "ac", "cal", "dinle", "oynat", "spotify", "lutfen", "misin", "acar", "calar" };

        void TrySpotify(string text, List<ResultItem> r)
        {
            string[] tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] norm = tokens.Select(t => TextUtil.Norm(TextUtil.StripSuffix(t))).ToArray();
            bool verb = norm.Any(t => t == "cal" || t == "dinle" || t == "calar");
            bool spot = norm.Contains("spotify");
            bool musicWord = norm.Any(t => t.StartsWith("sarki") || t.StartsWith("muzi"));

            // "şarkı aç", "müzik çal", "biraz müzik" → Spotify'ı aç ve kaldığı yerden devam et
            if (musicWord && norm.All(t => MusicWords.Contains(t) || t.StartsWith("sarki") || t.StartsWith("muzi")))
            {
                r.Add(Make("Müziği başlat", "Spotify açılır ve kaldığın şarkı devam eder", G.Music, Palette.Spotify, 2100,
                    () => SpotifyAsync(Spotify.ResumeSync)));
                return;
            }
            if (!verb && !spot) return;

            var words = new List<string>();
            for (int i = 0; i < tokens.Length; i++)
                if (!SpotifyFiller.Contains(norm[i])) words.Add(TextUtil.StripSuffix(tokens[i]));
            string q = string.Join(" ", words).Trim();

            if (q.Length == 0)
            {
                if (spot)
                {
                    var it = Make("Spotify'ı aç", "Müzik uygulaması", G.Music, Palette.Spotify, 1500, () => SpotifyAsync(Spotify.OpenSync));
                    it.UsageKey = "sys:spotify";
                    r.Add(it);
                    r.Add(Make("Spotify'ı aç ve çal", "Kaldığın şarkıdan devam eder", G.Play, Palette.Spotify, 1490, () => SpotifyAsync(Spotify.ResumeSync)));
                }
                return;
            }
            if (spot && !verb && norm.Any(t => t == "ac")) return;
            bool auto = S.SpotifyAutoPlay;
            string query = q;
            r.Add(Make("Spotify'da çal: " + q, auto ? "Arar ve ilk sonucu otomatik başlatır" : "Spotify'da arar", G.Music, Palette.Spotify, 2000,
                () => SpotifyAsync(() => Spotify.PlaySync(query, auto))));
        }

        // ---------- Mail ----------
        // ---------- WhatsApp ----------
        static readonly Brush WaGreen = Palette.Make("#25D366");
        static readonly HashSet<string> WaWords = new HashSet<string> { "whatsapp", "whatsap", "watsap", "watsapp", "vatsap", "wp", "wapp", "whatsup" };
        static readonly HashSet<string> WaFill = new HashSet<string>
        {
            "mesaj", "mesaji", "mesajla", "yaz", "yazsana", "at", "atsana", "gonder", "gondersene", "ilet", "de", "da", "diye", "ki", "uzerinden",
            "bir", "bi", "ona", "lutfen", "kisa", "bunu", "soyle", "sunu", "ile", "ac", "acsana", "acar", "misin"
        };

        /// <summary>"ahmet'e whatsapp'tan selam yaz", "whatsapp 0555 111 22 33 geliyorum", "wp annem: akşam geliyorum"</summary>
        bool TryWhatsApp(string text, List<ResultItem> r)
        {
            var tok = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            Func<string, string> key = t => TextUtil.Norm(TextUtil.StripSuffix(t.Trim(':', ',', '.', '!', '?', '\'', '’')));
            Func<string, bool> isWa = t => { string k = key(t); return WaWords.Contains(k) || WaWords.Any(w => w.Length > 3 && k.StartsWith(w)); };
            if (!tok.Any(isWa)) return false;

            // Alıcı: telefon numarası ya da kayıtlı kişi adı
            string to = "", phone = "";
            var num = Regex.Match(text, @"\+?\d[\d\s\-()]{8,}\d");
            var used = new HashSet<int>();
            if (num.Success && WhatsApp.Phone(num.Value).Length > 0)
            {
                phone = WhatsApp.Phone(num.Value);
                to = num.Value.Trim();
                text = text.Replace(num.Value, " ");
                tok = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            else
            {
                foreach (var kv in S.Phones.Concat(S.Contacts.Where(c => !S.Phones.ContainsKey(c.Key))))
                {
                    var parts = kv.Key.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(p => TextUtil.Norm(p)).ToList();
                    // "anneme", "ahmet'e" gibi ekli halleri de tanı
                    var idx = parts.Select(p => tok.FindIndex(t => { string k = key(t), raw = TextUtil.Norm(t.Split('\'', '’')[0]);
                        return k == p || raw == p || (k.StartsWith(p) && k.Length - p.Length <= 3); })).ToList();
                    if (parts.Count == 0 || idx.Any(i => i < 0)) continue;
                    to = kv.Key;
                    phone = S.Phones.ContainsKey(kv.Key) ? WhatsApp.Phone(S.Phones[kv.Key]) : "";
                    foreach (int i in idx) used.Add(i);
                    break;
                }
                if (to.Length == 0)
                {
                    // Kayıtlı olmayan ama "mehmet'e" diye yazılmış alıcı: adı al, kişiyi WhatsApp'ta seçer
                    int ri = tok.FindIndex(t => Regex.IsMatch(t, @"^\p{L}+['’](y?[ae])$") && !isWa(t));
                    if (ri >= 0)
                    {
                        string nm = tok[ri].Split('\'', '’')[0];
                        to = char.ToUpper(nm[0], TextUtil.Tr) + nm.Substring(1);
                        used.Add(ri);
                    }
                }
            }

            // Mesaj: iki nokta varsa sonrası; yoksa baştaki ve sondaki komut sözcükleri atılır
            string msg;
            int colon = text.IndexOf(':');
            if (colon >= 0 && colon < text.Length - 1) msg = text.Substring(colon + 1).Trim();
            else
            {
                Func<int, bool> drop = i => used.Contains(i) || isWa(tok[i]) || WaFill.Contains(key(tok[i]));
                int a = 0, b = tok.Count - 1;
                while (a <= b && drop(a)) a++;
                while (b >= a && drop(b)) b--;
                msg = a <= b ? string.Join(" ", tok.Skip(a).Take(b - a + 1).Where((t, j) => !used.Contains(a + j))) : "";
            }

            string who = to.Length > 0 ? to : "kişi seç";
            if (msg.Length > 0)
            {
                string sub = "\"" + msg + "\"";
                if (to.Length > 0 && phone.Length == 0) sub += "  ·  " + to + " için kayıtlı numara yok, WhatsApp'ta seçersin";
                else if (to.Length == 0) sub += "  ·  kişiyi WhatsApp'ta seçersin";
                string t = to, m = msg;
                var send = Make("WhatsApp → " + who, sub, G.Chat, WaGreen, 1950, () => _host.SendWhatsApp(t, m));
                send.Speak = "WhatsApp mesajını gönderiyorum";
                send.Hint = phone.Length > 0 && S.WhatsAppAutoSend ? "Gönder" : "Aç";
                r.Add(send);
                var edit = Make("WhatsApp mesajını düzenle", "Göndermeden önce panelde değiştir", G.Edit, WaGreen, 1940, null);
                edit.Execute = () => _host.ShowWhatsApp(t, m);
                edit.KeepOpen = true;
                r.Add(edit);
            }
            else
            {
                string t = to;
                var it = Make(to.Length > 0 ? "WhatsApp mesajı yaz → " + to : "WhatsApp mesajı yaz",
                    to.Length > 0 && phone.Length == 0 ? to + " için kayıtlı numara yok · Ayarlar → Kişiler" : "Kime ve mesajı yazacağın panel açılır",
                    G.Chat, WaGreen, to.Length > 0 ? 1950 : 950, null);
                it.Execute = () => _host.ShowWhatsApp(t, "");
                it.KeepOpen = true;
                r.Add(it);
            }
            return true;
        }

        // ---------- Instagram ----------
        static readonly Brush IgPink = Palette.Make("#E1306C");
        static readonly HashSet<string> IgWords = new HashSet<string> { "instagram", "insta", "ig", "instagramdan", "instadan", "dm" };

        /// <summary>"ayşe'ye instagramdan selam yaz", "insta @kullanici naber", "instagram ayşe: yarın görüşürüz"</summary>
        bool TryInstagram(string text, List<ResultItem> r)
        {
            var tok = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            Func<string, string> key = t => TextUtil.Norm(TextUtil.StripSuffix(t.Trim(':', ',', '.', '!', '?', '\'', '’')));
            Func<string, bool> isIg = t => { string k = key(t); return IgWords.Contains(k) || k.StartsWith("instagram") || k.StartsWith("insta"); };
            if (!tok.Any(isIg)) return false;

            string to = "", user = "";
            var used = new HashSet<int>();
            int at = tok.FindIndex(t => t.StartsWith("@") && t.Length > 1);
            if (at >= 0)
            {
                user = tok[at].TrimStart('@').Trim(':', ',', '.');
                to = "@" + user;
                used.Add(at);
            }
            else
            {
                foreach (var kv in S.Instagram.Concat(S.Contacts.Where(c => !S.Instagram.ContainsKey(c.Key))))
                {
                    var parts = kv.Key.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(p => TextUtil.Norm(p)).ToList();
                    var idx = parts.Select(p => tok.FindIndex(t => { string k = key(t), raw = TextUtil.Norm(t.Split('\'', '’')[0]);
                        return k == p || raw == p || (k.StartsWith(p) && k.Length - p.Length <= 3); })).ToList();
                    if (parts.Count == 0 || idx.Any(i => i < 0)) continue;
                    to = kv.Key;
                    user = S.Instagram.ContainsKey(kv.Key) ? S.Instagram[kv.Key] : "";
                    foreach (int i in idx) used.Add(i);
                    break;
                }
                if (to.Length == 0)
                {
                    int ri = tok.FindIndex(t => Regex.IsMatch(t, @"^\p{L}+['’](y?[ae])$") && !isIg(t));
                    if (ri >= 0) { string nm = tok[ri].Split('\'', '’')[0]; to = char.ToUpper(nm[0], TextUtil.Tr) + nm.Substring(1); used.Add(ri); }
                }
            }

            string msg;
            int colon = text.IndexOf(':');
            if (colon >= 0 && colon < text.Length - 1) msg = text.Substring(colon + 1).Trim();
            else
            {
                Func<int, bool> drop = i => used.Contains(i) || isIg(tok[i]) || WaFill.Contains(key(tok[i]));
                int a = 0, b = tok.Count - 1;
                while (a <= b && drop(a)) a++;
                while (b >= a && drop(b)) b--;
                msg = a <= b ? string.Join(" ", tok.Skip(a).Take(b - a + 1).Where((t, j) => !used.Contains(a + j))) : "";
            }

            string who = to.Length > 0 ? to : "Mesajlar";
            string sub = msg.Length > 0 ? "\"" + msg + "\"  ·  " : "";
            sub += user.Length > 0 ? "sohbet açılır, mesaj yazılıp gönderilir"
                 : to.Length > 0 ? to + " için sohbet linki yok · Ayarlar → Kişiler"
                 : "Instagram mesajları açılır";
            string u = user, m2 = msg, t2 = to;
            var it = Make("Instagram → " + who, sub, G.Chat, IgPink, msg.Length > 0 || user.Length > 0 ? 1950 : 950, () => _host.SendInstagram(u, m2, t2));
            it.Hint = u.Length > 0 && m2.Length > 0 ? "Gönder" : "Aç";
            it.Speak = m2.Length > 0 ? "Instagram mesajını gönderiyorum" : "Instagram açılıyor";
            r.Add(it);
            return true;
        }

        // ---------- Kronometre ve zamanlayıcı ----------
        void TryStopwatch(string n, List<ResultItem> r)
        {
            if (!(n.Contains("kronometre") || n.Contains("stopwatch") || n.StartsWith("sure tut"))) return;
            var c = Controller.Instance;
            var color = Palette.Make("#FF9F0A");
            if (!c.WatchActive)
            {
                r.Add(Make("Kronometreyi başlat", "Adada ileri sayar · tıklayınca durdur / sıfırla", "", color, 2700, () => c.StartWatch()));
                return;
            }
            string now = c.Watch.Elapsed.ToString(c.Watch.Elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
            if (c.Watch.IsRunning) r.Add(Make("Kronometreyi duraklat · " + now, "Adadaki sayaç durur", G.Pause, color, 2700, () => c.PauseWatch()));
            else r.Add(Make("Kronometreye devam et · " + now, "Kaldığı yerden sayar", G.Play, color, 2700, () => c.StartWatch()));
            r.Add(Make("Kronometreyi sıfırla", "Adadan kaldırılır", "", color, 2690, () => c.ResetWatch()));
        }

        void TryMail(string text, string n, List<ResultItem> r)
        {
            var email = Regex.Match(text, @"[\w\.\-+]+@[\w\-]+(\.[\w\-]+)+");
            bool kw = Regex.IsMatch(n, @"\b(mail|eposta|email|e posta|e mail|mektup|ileti)\b");
            if (!kw && !email.Success) return;

            string to = email.Success ? email.Value : "";
            if (to.Length == 0)
            {
                foreach (var c in S.Contacts)
                {
                    string cn = TextUtil.Norm(c.Key);
                    if (cn.Length > 0 && (" " + n + " ").Contains(" " + cn + " ")) { to = c.Value; break; }
                }
            }
            string subject = "", body = "";
            var sm = Regex.Match(text, @"\bkonu(?:su)?\s*[:=]?\s*(.+?)(?=\s+(?:mesaj|metin|içerik|icerik|içeriği|yazı)\s*[:=]?|$)", RegexOptions.IgnoreCase);
            if (sm.Success) subject = sm.Groups[1].Value.Trim();
            var bm = Regex.Match(text, @"\b(?:mesaj|metin|içerik|icerik|içeriği|yazı)\s*[:=]?\s*(.+)$", RegexOptions.IgnoreCase);
            if (bm.Success) body = bm.Groups[1].Value.Trim();

            string sub = to.Length > 0 ? "Kime: " + to : "Alıcı, konu ve mesajı yazacağın panel açılır";
            if (subject.Length > 0) sub += " · Konu: " + subject;
            var it = Make(to.Length > 0 ? "Mail yaz → " + to : "Mail yaz", sub, G.Mail, Palette.Mail, 1900, null);
            it.Execute = () => _host.ShowCompose(to, subject, body);
            it.KeepOpen = true;
            r.Add(it);
        }

        // ---------- Web ----------
        void TryWeb(string text, string n, List<ResultItem> r)
        {
            if (!text.Contains(" ") && Regex.IsMatch(text, @"^(https?://)?([\w\-]+\.)+[a-zA-Z]{2,}(:\d+)?(/\S*)?$"))
            {
                r.Add(Make("Siteyi aç: " + text, "Tarayıcıda açar", G.Link, Palette.Web, 1800, () => Shell.OpenUrl(text)));
                return;
            }

            string[] tok = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] nt = tok.Select(t => TextUtil.Norm(TextUtil.StripSuffix(t))).ToArray();
            Func<HashSet<string>, string> rest = drop =>
                string.Join(" ", tok.Where((t, i) => !drop.Contains(nt[i])).Select(TextUtil.StripSuffix)).Trim();

            if (nt.Length > 1 && nt.Contains("youtube"))
            {
                string q = rest(new HashSet<string> { "youtube", "ac", "ara", "izle", "da", "de", "uzerinden", "videosu", "videosunu", "video" });
                if (q.Length > 0)
                    r.Add(Make("YouTube'da ara: " + q, "Video sonuçlarını açar", G.Video, Palette.Danger, 1850, () => Web.Search("youtube", q)));
            }
            if (nt.Length > 1 && (nt[0] == "google" || nt[0] == "ara" || nt[nt.Length - 1] == "ara" || nt[0] == "arastir" || nt[nt.Length - 1] == "arastir"))
            {
                string q = rest(new HashSet<string> { "google", "ara", "arastir", "da", "de" });
                if (q.Length > 0)
                    r.Add(Make("Google'da ara: " + q, "Web araması", G.Search, Palette.Web, 1700, () => Web.Search("google", q)));
            }
            if (nt.Length > 1 && (nt.Contains("harita") || nt.Contains("haritada") || nt.Contains("nerede") || nt.Contains("yol")))
            {
                string q = rest(new HashSet<string> { "harita", "haritada", "nerede", "yol", "tarifi", "goster", "ac", "bul" });
                if (q.Length > 0)
                    r.Add(Make("Haritada göster: " + q, "Google Haritalar", G.Map, Palette.Web, 1700, () => Web.Search("maps", q)));
            }
            if (nt.Length > 1 && (nt[0] == "cevir" || nt[nt.Length - 1] == "cevir" || nt[0] == "ceviri" || nt[0] == "translate"))
            {
                string q = rest(new HashSet<string> { "cevir", "ceviri", "translate", "ingilizceye", "turkceye", "ingilizce", "turkce" });
                bool toTr = n.Contains("turkce");
                if (q.Length > 0)
                    r.Add(Make("Çevir: " + q, toTr ? "→ Türkçe" : "→ İngilizce", G.Translate, Palette.Web, 1700,
                        () => Shell.OpenUrl("https://translate.google.com/?sl=auto&tl=" + (toTr ? "tr" : "en") + "&text=" + Shell.Esc(q) + "&op=translate")));
            }
            string sq = StripVerbs(n);
            if (sq.Length < 2) return;
            foreach (var kv in Web.AllSites())
            {
                int s = TextUtil.Score(sq, kv.Key);
                if (s < 500 || (kv.Key.Length == 1 && s < 1000)) continue;
                string url = kv.Value;
                r.Add(Make(kv.Key.Substring(0, 1).ToUpper(TextUtil.Tr) + kv.Key.Substring(1) + " (web)", url, G.Globe, Palette.Web, s - 20,
                    () => Shell.OpenUrl(url)));
            }
        }

        // ---------- Uygulama kapat ----------
        void TryCloseApp(string n, List<ResultItem> r)
        {
            string[] w = n.Split(' ');
            if (w.Length < 2 || w[w.Length - 1] != "kapat") return;
            string name = string.Join(" ", w.Take(w.Length - 1));
            if (name == "bilgisayari" || name == "bilgisayar" || name == "sesi" || name == "ses" || name == "oturumu" || name == "asistani" || name == "pardux" || name == "parduks") return;
            foreach (var g in SystemActions.MatchRunning(name).Take(3))
            {
                var procs = g.ToList();
                string title = g.First().MainWindowTitle;
                r.Add(Make(g.Key + " uygulamasını kapat", string.IsNullOrEmpty(title) ? "Pencereyi kapatır" : title, G.Close, Palette.Danger, 1650,
                    () => { foreach (var p in procs) { try { p.CloseMainWindow(); } catch { } } }));
            }
        }

        // ---------- Dosya arama ----------
        void TryFiles(string text, string n, List<ResultItem> r, int version)
        {
            var m = Regex.Match(text, @"^\s*(?:bul|dosya|dosyayı|ara dosya|find)\s+(.+)$", RegexOptions.IgnoreCase);
            string q, kind = "any";
            bool open = false;
            int score = 2600;
            if (m.Success) q = m.Groups[1].Value.Trim();
            else
            {
                // "rapor dosyasını aç", "ödev pdf'ini aç", "tatil fotoğraflarını göster", "projeler klasörünü aç"
                q = FileQuery(text, out kind);
                open = q != null && Regex.IsMatch(text, @"\b(aç|ac|getir|göster)\s*$", RegexOptions.IgnoreCase);
                if (q == null) return;
            }
            if (q.Length < 2) return;
            RunFileSearch(q, kind, open, score, r, version);
        }

        /// <summary>"X aç" denip hiçbir uygulama/komut eşleşmediyse dosyalarda da arar (ör. "bitirme projesi aç").</summary>
        void TryOpenAsFile(string text, List<ResultItem> r, int version)
        {
            if (r.Any(x => x.Score >= 900)) return;
            var m = Regex.Match(text, @"^(.{3,}?)['’]?\s*(?:y?[ıiuü]|n[ıiuü])?\s+(aç|ac|göster)$", RegexOptions.IgnoreCase);
            if (!m.Success) return;
            RunFileSearch(m.Groups[1].Value.Trim(), "any", true, 1500, r, version);
        }

        void RunFileSearch(string q, string kind, bool open, int score, List<ResultItem> r, int version)
        {
            var searching = Make("Aranıyor: " + q, "Bilgisayarındaki dosyalarda", G.Search, Palette.Folder, score, null);
            searching.KeepOpen = true;
            r.Add(searching);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var hits = FileSearch.Find(q, kind, 7);
                var app = Application.Current;
                if (app == null) return;
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var items = new List<ResultItem>();
                    int sc = score - 1;
                    foreach (var h in hits)
                    {
                        var hit = h;
                        var it = Make(h.Name, h.Path, h.IsFolder ? G.Folder : G.File, Palette.Folder, sc--, () => Shell.Open(hit.Path));
                        it.Speak = h.Name + " açılıyor";
                        RequestIcon(it, h.Path);
                        items.Add(it);
                    }
                    // "… aç" denmişse en iyi eşleşme sesli komutta kendiliğinden açılır
                    if (open && items.Count > 0) items[0].AutoRun = true;
                    if (items.Count == 0)
                    {
                        var none = Make("Dosya bulunamadı: " + q, "Masaüstü, Belgeler, İndirilenler ve diğer klasörlerde eşleşme yok", G.File, Palette.Folder, score - 1, null);
                        none.Speak = "Bu isimde bir dosya bulamadım";
                        none.KeepOpen = true;
                        items.Add(none);
                    }
                    _host.AppendResults(version, items, searching);
                }));
            });
        }

        // ---------- Sistem komutları ----------
        void AddSystem(string n, List<ResultItem> r)
        {
            foreach (var c in _sys)
            {
                int best = 0;
                foreach (string k in c.Keys)
                {
                    int s = TextUtil.Score(n, k);
                    if (n.Length < 3 && s < 900) s = 0;
                    if (s > best) best = s;
                }
                if (best < 500) continue;
                var it = Make(c.Title, c.Sub, c.Glyph, c.Color, best + 100 + Math.Min(S.GetUsage(c.Usage) * 10, 100), c.Run);
                it.NeedsConfirm = c.Confirm;
                it.KeepOpen = c.KeepOpen;
                it.UsageKey = c.Usage;
                r.Add(it);
            }
        }

        // ---------- Özel komutlar ----------
        void AddCustom(string n, List<ResultItem> r)
        {
            string q = StripVerbs(n);
            foreach (var c in S.CustomCommands)
            {
                if (c == null || string.IsNullOrEmpty(c.Name) || string.IsNullOrEmpty(c.Target)) continue;
                int s = TextUtil.Score(q, TextUtil.Norm(c.Name));
                if (s < 500) continue;
                var cmd = c;
                var it = Make(c.Name, c.Target, G.Link, Palette.Accent, s + 150, () => Shell.Open(cmd.Target, cmd.Args));
                it.UsageKey = "cmd:" + c.Name;
                if (!c.Target.StartsWith("http")) RequestIcon(it, c.Target);
                r.Add(it);
            }
        }

        // ---------- Uygulamalar ----------
        static string StripVerbs(string n)
        {
            var w = n.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            while (w.Count > 1 && OpenVerbs.Contains(w[w.Count - 1])) w.RemoveAt(w.Count - 1);
            while (w.Count > 1 && OpenVerbs.Contains(w[0])) w.RemoveAt(0);
            return string.Join(" ", w);
        }

        void AddApps(string n, List<ResultItem> r)
        {
            string q = StripVerbs(n);
            if (q.Length == 0 || OpenVerbs.Contains(q)) return;
            bool explicitOpen = q != n;
            var scored = new List<Tuple<AppEntry, int>>();
            foreach (var a in AppIndex.Apps)
            {
                int s = TextUtil.Score(q, a.NormName);
                if (s <= 0) continue;
                if (q.Length < 3 && s < 800) continue;
                s += Math.Min(S.GetUsage("app:" + a.Name) * 15, 150);
                // "discord aç" gibi net istekler yapay zekâya gitmeden anında açılsın
                if (explicitOpen) s += s >= 750 ? 1000 : 50;
                scored.Add(Tuple.Create(a, s));
            }
            foreach (var t in scored.OrderByDescending(x => x.Item2).Take(6))
                r.Add(AppItem(t.Item1, t.Item2));
        }

        ResultItem AppItem(AppEntry a, int score)
        {
            var app = a;
            var it = Make(a.Name, "Uygulama", G.App, Palette.Accent, score, () => AppIndex.Launch(app));
            it.UsageKey = "app:" + a.Name;
            it.Complete = a.Name;
            RequestIcon(it, AppIndex.IconPath(a));
            return it;
        }

        static void RequestIcon(ResultItem it, string path)
        {
            IconLoader.Request(path, img => it.Icon = img);
        }
    }
}
