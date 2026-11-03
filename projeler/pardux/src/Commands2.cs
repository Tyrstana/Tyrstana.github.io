using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;

namespace Pardux
{
    /// <summary>
    /// Ek komutlar: Steam oyunları (aç / indir), Discord mesajı, Gemini'ye soru, Wi-Fi / Bluetooth / parlaklık, kaçırılan bildirimler,
    /// dosya açma ve aynı iş için çok sayıda alternatif söyleyiş ("açar mısın", "açsana", "başlatabilir misin"…).
    /// </summary>
    public partial class CommandEngine
    {
        // ---------------- Söyleyişi sadeleştirme ----------------
        // Komut fiilleri: kibar / soru / çoğul biçimleri yalın emre çevrilir ("açar mısın" → "aç", "kapatabilir misin" → "kapat")
        static readonly string[] Verbs =
        {
            "aç", "kapat", "kapa", "çal", "başlat", "durdur", "duraklat", "kur", "yaz", "göster", "söyle", "ara", "bul", "oku", "gönder", "at",
            "yükle", "indir", "oyna", "sor", "hatırlat", "geç", "artır", "arttır", "azalt", "kıs", "yükselt", "sil", "kaydet", "al", "ver",
            "değiştir", "yap", "sus", "dinle", "çalıştır", "aktar", "anlat", "bak", "getir", "ayarla", "sıfırla", "kilitle", "uyut"
        };

        static readonly Regex Fillers = new Regex(@"^(pardux|parduks|hey pardux|hey parduks|lütfen|lutfen|hadi|haydi|bana|acaba|bi|şimdi|simdi)\s+|\s+(lütfen|lutfen|bakalım|bakalim|bi zahmet|hemen|acaba)$",
                                                  RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Komutu sadeleştirir: dolgu sözcükleri atılır, fiillerin kibar biçimleri yalın emre çevrilir.</summary>
        internal static string Simplify(string text)
        {
            string t = (text ?? "").Trim().TrimEnd('?', '.', '!');
            for (int i = 0; i < 3; i++)
            {
                string before = t;
                t = Fillers.Replace(t, "").Trim();
                if (t == before) break;
            }
            foreach (string v in Verbs)
            {
                string e = Regex.Escape(v);
                // açar mısın / kapatır mısınız / söyler misin
                t = Regex.Replace(t, @"\b" + e + @"(?:ar|er|ır|ir|ur|ür|r)\s*m[ıiuü]s[ıiuü]n(?:[ıiuü]z)?\b", v, RegexOptions.IgnoreCase);
                // açabilir misin / kapatabilir misiniz / söyleyebilir misin
                t = Regex.Replace(t, @"\b" + e + @"(?:y?abilir|y?ebilir)\s*m[ıi](?:s[ıi]n(?:[ıi]z)?)?\b", v, RegexOptions.IgnoreCase);
                // açsana / kapatsana / açıver / açın / açınız
                t = Regex.Replace(t, @"\b" + e + @"(?:sana|sene|ıver|iver|uver|üver|yıver|yiver|ın|in|un|ün|ınız|iniz|unuz|ünüz)\b(?!\s*\p{L}*['’])", v, RegexOptions.IgnoreCase);
                // açmanı istiyorum / açman lazım
                t = Regex.Replace(t, @"\b" + e + @"(?:ma|me)(?:n[ıi])?\s+(?:istiyorum|lazım|gerek)\b", v, RegexOptions.IgnoreCase);
            }
            // Eş anlamlılar: "çalıştır" uygulama için "aç" demektir
            t = Regex.Replace(t, @"\bçalıştır\b", "aç", RegexOptions.IgnoreCase);
            return Regex.Replace(t, @"\s{2,}", " ").Trim();
        }

        /// <summary>Var olan sistem komutlarına daha fazla söyleyiş eklenir (aynı iş için farklı cümleler).</summary>
        void AddAliases()
        {
            var more = new Dictionary<string, string>
            {
                { "Müziği duraklat", "muzigi kes|sarkiyi kes|muzigi kapat|sarkiyi durdur|sus|biraz sus|durdur muzigi|muzik dursun|bekle" },
                { "Müziği devam ettir", "muzigi ac|muzik ac|sarki ac|sarkiyi ac|muzik cal|sarki cal|muzigi baslat|muzige devam et|devam ettir|kaldigi yerden" },
                { "Sonraki şarkı", "sonraki sarkiya gec|baska sarki|diger sarki|sonrakine gec|sarkiyi degistir|bunu gec|ileri" },
                { "Önceki şarkı", "onceki sarkiya don|onceki sarkiyi ac|geri don|basa sar" },
                { "Sesi yükselt", "sesi ac biraz|daha yuksek|sesi yukari|sesi fazlalastir|sesi kok|ses yukari" },
                { "Sesi kıs", "daha alcak|sesi asagi|sesi biraz kis|ses asagi|sesi alcalt" },
                { "Sessize al / sesi geri aç", "sesi tamamen kapat|sesi kes|sessiz mod" },
                { "Bilgisayarı kilitle", "kilitle ekrani|ekrani kilitle bilgisayari|pc kilitle|bilgisayarimi kilitle" },
                { "Ekran görüntüsü al", "ekrani kaydet|ekranin fotografini cek|ss at|goruntu al" },
                { "Görev Yöneticisi", "gorev yoneticisini ac|islemler|calisan uygulamalar" },
                { "Dosya Gezgini", "klasorleri ac|dosyalarimi ac|dosya gezginini ac" },
                { "Notlarım", "notlarimi goster|notlarima bak|notlari goster" },
            };
            foreach (var c in _sys)
            {
                string extra;
                if (more.TryGetValue(c.Title, out extra))
                    c.Keys = c.Keys.Concat(extra.Split('|').Select(TextUtil.Norm)).Distinct().ToArray();
            }
        }

        // ---------------- Steam ----------------
        void TrySteam(string text, string n, List<ResultItem> r)
        {
            var games = SteamLibrary.Games();
            var install = Regex.Match(n, @"^(?:steam(?:den|ten|dan|tan)?\s+)?(.+?)\s+(?:oyununu\s+|oyunu\s+)?(?:indir|yukle|kur|satin al)(?:\s+steam(?:den|ten|dan|tan)?)?$");
            bool steamWord = Regex.IsMatch(n, @"\bsteam");
            if (install.Success && (steamWord || Regex.IsMatch(n, @"\boyun")))
            {
                string q = Regex.Replace(install.Groups[1].Value, @"\b(steam(?:den|ten|dan|tan)?|oyununu|oyunu|oyun)\b", "").Trim();
                if (q.Length < 2) return;
                var have = games.FirstOrDefault(g => TextUtil.Score(TextUtil.Norm(q), g.Norm) >= 850);
                if (have != null)
                {
                    var h = have;
                    var it = Make(have.Name + " zaten yüklü", "Oynamak için Enter", G.Play, Palette.Steam, 2550, () => SteamLibrary.Run(h));
                    it.Speak = h.Name + " zaten yüklü, açıyorum";
                    r.Add(it);
                    return;
                }
                string query = q;
                var it2 = Make("Steam'den indir: " + q, "Mağazada arar ve kurulum penceresini açar", G.Download, Palette.Steam, 2500,
                    () => Shell.Background(() => SteamLibrary.Install(query, msg => _host.Flash(msg))));
                it2.Speak = q + " için Steam kurulumunu açıyorum";
                r.Add(it2);
                return;
            }
            // "x oyna / x'i aç / x başlat": yüklü Steam oyunları
            if (games.Count == 0) return;
            string target = Regex.Replace(n, @"\b(oyna|oynayalim|ac|baslat|calistir|oyununu|oyunu|oyun|steamde|steamden|steam)\b", "").Trim();
            if (target.Length < 2) return;
            bool playWord = Regex.IsMatch(n, @"\b(oyna|oynayalim|oyun)");
            foreach (var g in games.Select(g => Tuple.Create(g, TextUtil.Score(target, g.Norm))).Where(x => x.Item2 >= (playWord ? 600 : 800)).OrderByDescending(x => x.Item2).Take(3))
            {
                var game = g.Item1;
                var it = Make(game.Name, "Steam oyunu  ·  oynamak için Enter", G.Play, Palette.Steam, (playWord ? 1950 : 1700) + g.Item2 / 10, () => SteamLibrary.Run(game));
                it.Speak = game.Name + " açılıyor";
                r.Add(it);
            }
        }

        // ---------------- Discord ----------------
        void TryDiscord(string text, List<ResultItem> r)
        {
            if (!Regex.IsMatch(text, @"\bdisc?ord", RegexOptions.IgnoreCase)) return;
            string to = null, msg = null;
            var m = Regex.Match(text, @"^(.+?)['’]?(?:y?[ae]|n[ae])?\s+disc?ord(?:['’]?(?:dan|tan|den|ten|da|de))?(?:\s+üzerinden)?\s+(.+?)\s+(?:yaz|gönder|de|söyle|mesaj at|at)$", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(text, @"^disc?ord(?:['’]?(?:dan|tan|den|ten|da|de))?\s+(.+?)['’]?(?:y?[ae]|n[ae])?\s+(.+?)\s+(?:yaz|gönder|de|söyle|mesaj at|at)$", RegexOptions.IgnoreCase);
            if (m.Success) { to = m.Groups[1].Value.Trim(); msg = m.Groups[2].Value.Trim(); }
            else
            {
                var c = Regex.Match(text, @"^disc?ord(?:['’]?(?:dan|tan|den|ten))?\s+([^:]+):\s*(.+)$", RegexOptions.IgnoreCase);
                if (c.Success) { to = c.Groups[1].Value.Trim(); msg = c.Groups[2].Value.Trim(); }
            }
            if (to == null || msg == null || to.Length == 0 || msg.Length == 0) return;
            to = Regex.Replace(to, @"['’].*$", "");
            // Kayıtlı kişi adıyla eşleşirse yazımı oradan al
            var known = S.Phones.Keys.Concat(S.Contacts.Keys).Concat(S.Instagram.Keys).FirstOrDefault(k => TextUtil.Norm(k) == TextUtil.Norm(to));
            if (known != null) to = known;
            string who = to, body = msg;
            bool send = S.WhatsAppAutoSend;
            var it = Make("Discord'dan yaz → " + who, "“" + body + "”" + (send ? "  ·  otomatik gönderilir" : "  ·  yazılır, Enter'a sen basarsın"), G.Chat, Palette.Make("#5865F2"), 2400,
                () => _host.SendDiscord(who, body, send));
            it.Speak = "Discord'dan " + who + " kişisine yazıyorum";
            r.Add(it);
        }

        // ---------------- Gemini ----------------
        void TryGemini(string text, List<ResultItem> r)
        {
            var m = Regex.Match(text, @"^(?:gemini(?:['’]?(?:ye|a|ya))?\s+(?:sor|danış|sorar mısın)?:?\s*(.+)|(.+?)\s+(?:diye\s+)?gemini(?:['’]?(?:ye|a|ya))?\s+(?:sor|danış))$", RegexOptions.IgnoreCase);
            if (!m.Success) return;
            string q = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim().Trim('"', '“', '”');
            if (q.Length < 2 || Regex.IsMatch(q, @"^(aç|ac|sor)$", RegexOptions.IgnoreCase)) return;
            string question = q;
            var it = Make("Gemini'ye sor: " + q, "Yeni sohbet açılır ve soru gönderilir", G.Sparkle, Palette.Make("#8E75FF"), 2450, () => _host.AskGemini(question));
            it.Speak = "Gemini'ye soruyorum";
            r.Add(it);
        }

        // ---------------- Wi-Fi, Bluetooth, parlaklık ----------------
        void TryToggles(string n, List<ResultItem> r)
        {
            bool off = Regex.IsMatch(n, @"\b(kapat|kapa|kes|devre disi|off)\b"), on = Regex.IsMatch(n, @"\b(ac|baslat|etkinlestir|on|bagla)\b");
            if (on == off) { /* ikisi de yok ya da ikisi de var: belirsiz */ }
            else
            {
                if (Regex.IsMatch(n, @"\b(wifi|wi fi|kablosuz|internet|wlan)"))
                    r.Add(Toggle("Wi-Fi'ı " + (on ? "aç" : "kapat"), "Kablosuz ağ bağlantısı", G.Wifi, on, () => Radios.Set(Radios.Kind.WiFi, on, msg => _host.Flash(msg)),
                                 "Wi-Fi'ı " + (on ? "açıyorum" : "kapatıyorum")));
                if (Regex.IsMatch(n, @"\bbluetooth|\bblutut|\bbluetut"))
                    r.Add(Toggle("Bluetooth'u " + (on ? "aç" : "kapat"), "Bluetooth bağlantısı", G.Bluetooth, on, () => Radios.Set(Radios.Kind.Bluetooth, on, msg => _host.Flash(msg)),
                                 "Bluetooth'u " + (on ? "açıyorum" : "kapatıyorum")));
                if (Regex.IsMatch(n, @"\bucak mod"))
                    r.Add(Toggle("Uçak modu " + (on ? "aç (tüm kablosuzları kapat)" : "kapat (kablosuzları aç)"), "Wi-Fi ve Bluetooth", G.Plane, !on,
                                 () => { Radios.Set(Radios.Kind.WiFi, !on, null); Radios.Set(Radios.Kind.Bluetooth, !on, msg => _host.Flash(msg)); },
                                 on ? "Kablosuz bağlantıları kapatıyorum" : "Kablosuz bağlantıları açıyorum"));
                if (Regex.IsMatch(n, @"\bekran") && off && !Regex.IsMatch(n, @"goruntu|kilit"))
                {
                    var it = Make("Ekranı kapat", "Fareyi oynatınca geri açılır", G.Display, Palette.System, 2300, Brightness.MonitorOff);
                    it.Speak = "Ekranı kapatıyorum";
                    r.Add(it);
                }
            }
            // Parlaklık: "parlaklık 40", "parlaklığı artır / azalt", "ekranı karart / parlat"
            var p = Regex.Match(n, @"parlakl\w*\s+(?:yuzde\s*)?(\d{1,3})|(?:yuzde\s*)?(\d{1,3})\s*parlakl");
            if (p.Success)
            {
                int v = Math.Min(100, int.Parse(p.Groups[1].Success ? p.Groups[1].Value : p.Groups[2].Value));
                r.Add(Toggle("Parlaklığı %" + v + " yap", "Ekran parlaklığı", G.Display, true, () => Brightness.Set(v, msg => _host.Flash(msg)), "Parlaklığı yüzde " + v + " yapıyorum"));
            }
            else if (Regex.IsMatch(n, @"parlakl\w*\s+(artir|arttir|yukselt|ac)|ekrani\s+(parlat|ac biraz)|daha parlak"))
                r.Add(Toggle("Parlaklığı artır", "+%15", G.Display, true, () => Brightness.Step(15, msg => _host.Flash(msg)), "Parlaklığı artırıyorum"));
            else if (Regex.IsMatch(n, @"parlakl\w*\s+(azalt|dusur|kis)|ekrani\s+karart|daha karanlik|ekran cok parlak"))
                r.Add(Toggle("Parlaklığı azalt", "-%15", G.Display, true, () => Brightness.Step(-15, msg => _host.Flash(msg)), "Parlaklığı azaltıyorum"));
            if (Regex.IsMatch(n, @"\b(gece isigi|gece modu)"))
                r.Add(Make("Gece ışığı ayarları", "Mavi ışığı azaltır", G.Settings, Palette.System, 1900, () => Shell.Open("ms-settings:nightlight")));
            if (Regex.IsMatch(n, @"\b(rahatsiz etme|odak modu|bildirimleri sustur)"))
                r.Add(Make("Rahatsız etme ayarları", "Bildirimleri susturur", G.Settings, Palette.System, 1900, () => Shell.Open("ms-settings:notifications")));
            if (Regex.IsMatch(n, @"\b(pil tasarruf|enerji tasarruf)"))
                r.Add(Make("Pil tasarrufu ayarları", "Pil ömrünü uzatır", G.Settings, Palette.System, 1900, () => Shell.Open("ms-settings:batterysaver")));
        }

        ResultItem Toggle(string title, string sub, string glyph, bool on, Action run, string speak)
        {
            var it = Make(title, sub, glyph, Palette.System, 2450, run);
            it.KeepOpen = true;
            it.Speak = speak;
            return it;
        }

        // ---------------- Kaçırılan bildirimler ----------------
        void TryMissed(string n, List<ResultItem> r)
        {
            if (!Regex.IsMatch(n, @"^(bildirimler|bildirimlerim|kacirdigim bildirimler|kacan bildirimler|kacirilan bildirimler|bildirim merkezi|ne kacirdim|yeni bildirim var mi|bildirimleri goster)$")) return;
            var list = Notifications.Missed;
            int sc = 2700;
            foreach (var x in list.AsEnumerable().Reverse().Take(7))
            {
                var item = x;
                var it = Make((x.Title.Length > 0 ? x.Title : x.App), (x.Body.Length > 0 ? x.Body + "  ·  " : "") + Notifications.Describe(x), G.Bell, Palette.Make("#FFD60A"), sc--,
                              () => Notifications.OpenApp(item));
                it.Hint = "Aç";
                r.Add(it);
            }
            var center = Make(list.Count == 0 ? "Kaçırılan bildirim yok" : "Windows bildirim merkezini aç", list.Count == 0 ? "Hepsi görülmüş" : list.Count + " bildirim bekliyor",
                              G.Bell, Palette.System, list.Count == 0 ? 2700 : sc--, Notifications.OpenCenter);
            center.Speak = list.Count == 0 ? "Kaçırdığın bildirim yok" : list.Count + " kaçırılan bildirim var";
            r.Add(center);
        }

        // ---------------- Dosya açma (doğal söyleyiş) ----------------
        static readonly Regex FileAsk = new Regex(@"^(.+?)\s+(dosyasını|dosyası|dosyayı|klasörünü|klasörü|belgesini|pdf['’]?ini|pdf['’]?sini|pdf|fotoğrafını|resmini|videosunu|sunumunu|excelini|wordünü)\s*(aç|bul|göster|getir)?$",
                                                  RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>"rapor dosyasını aç", "ödev pdf'ini aç", "tatil fotoğrafını göster", "projeler klasörünü aç".</summary>
        string FileQuery(string text, out string kind)
        {
            kind = "any";
            var m = FileAsk.Match(text);
            if (!m.Success) return null;
            string w = TextUtil.Norm(m.Groups[2].Value);
            kind = w.StartsWith("klasor") ? "folder" : w.StartsWith("pdf") ? "pdf" : w.StartsWith("fotograf") || w.StartsWith("resm") ? "image" :
                   w.StartsWith("video") ? "video" : w.StartsWith("belge") || w.StartsWith("sunum") || w.StartsWith("excel") || w.StartsWith("word") ? "document" : "any";
            return m.Groups[1].Value.Trim();
        }
    }
}
