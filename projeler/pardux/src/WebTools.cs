using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Pardux
{
    /// <summary>Anahtar gerektirmeyen ücretsiz internet kaynakları: Open-Meteo hava durumu, open.er-api.com kurlar.</summary>
    internal static class WebTools
    {
        const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

        static WebTools()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 12288);
        }

        /// <summary>Ham bayt indirir (haber görselleri için).</summary>
        internal static byte[] GetBytes(string url, int timeoutMs = 12000)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.Accept = "image/avif,image/png,image/jpeg,image/*;q=0.8,*/*;q=0.5";
            req.Timeout = timeoutMs;
            using (var resp = req.GetResponse())
            using (var ms = new MemoryStream())
            {
                resp.GetResponseStream().CopyTo(ms);
                return ms.ToArray();
            }
        }

        internal static string Post(string url, string form, int timeoutMs = 12000)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.UserAgent = UserAgent;
            req.ContentType = "application/x-www-form-urlencoded;charset=UTF-8";
            req.Timeout = timeoutMs;
            req.AutomaticDecompression = DecompressionMethods.GZip;
            byte[] body = Encoding.UTF8.GetBytes(form);
            using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
        }

        internal static string Get(string url, int timeoutMs = 15000) { return Get(url, timeoutMs, null); }

        internal static string Get(string url, int timeoutMs, string cookie)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            if (cookie != null) req.Headers["Cookie"] = cookie;
            // Vikipedi API'si uygulamaların kendini tanıtmasını ister; tarayıcı kılığındaki isteklere sık sınır koyar
            req.UserAgent = (url.Contains("wikipedia.org/") || url.Contains("wikidata.org/")) ? "Pardux/1.0 (Windows masaustu asistani; kisisel kullanim)" : UserAgent;
            req.Accept = "text/html,application/json;q=0.9,*/*;q=0.8";
            req.Headers["Accept-Language"] = "tr-TR,tr;q=0.9,en;q=0.7";
            req.Timeout = timeoutMs;
            // Yalnızca gzip: .NET Framework'ün deflate çözücüsü bazı sunucularla bozuk çalışıyor.
            req.AutomaticDecompression = DecompressionMethods.GZip;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                var enc = Encoding.UTF8;
                try { if (!string.IsNullOrEmpty(resp.CharacterSet)) enc = Encoding.GetEncoding(resp.CharacterSet); } catch { }
                using (var sr = new StreamReader(resp.GetResponseStream(), enc)) return sr.ReadToEnd();
            }
        }

        static string Clean(string html)
        {
            return WebUtility.HtmlDecode(Regex.Replace(html ?? "", "<[^>]+>", "")).Trim();
        }

        /// <summary>WMO hava kodunun Türkçe karşılığı (Open-Meteo).</summary>
        static string Wmo(int c)
        {
            if (c == 0) return "açık";
            if (c <= 2) return "parçalı bulutlu";
            if (c == 3) return "kapalı";
            if (c == 45 || c == 48) return "sisli";
            if (c >= 51 && c <= 57) return "çiseleme";
            if (c >= 61 && c <= 67) return c >= 65 ? "kuvvetli yağmur" : "yağmurlu";
            if (c >= 71 && c <= 77) return "karlı";
            if (c >= 80 && c <= 82) return "sağanak yağış";
            if (c == 85 || c == 86) return "kar sağanağı";
            if (c >= 95) return "gök gürültülü fırtına";
            return "değişken";
        }

        public static string Weather(string city)
        {
            city = (city ?? "").Trim();
            if (city.Length == 0)
            {
                try { city = Json.Str(Json.Obj(Json.Parse(Get("http://ip-api.com/json/?fields=city", 8000))), "city"); } catch { }
                if (string.IsNullOrEmpty(city)) city = "İstanbul";
            }
            string name = city.Split(',')[0].Trim();
            var geo = Json.Obj(Json.Parse(Get("https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(name) + "&count=10&language=tr")));
            var results = Json.Arr(Json.Get(geo, "results")).Select(Json.Obj).Where(r => r != null).ToList();
            if (results.Count == 0) return "'" + city + "' adında bir yer bulunamadı. İlçe yerine il adıyla tekrar dene.";
            string norm = TextUtil.Norm(name);
            var place = results.Where(r => TextUtil.Norm(Json.Str(r, "name")) == norm)
                .OrderByDescending(r => Json.Num(r, "population") ?? 0).FirstOrDefault() ?? results[0];

            string lat = Convert.ToString(Json.Num(place, "latitude"), CultureInfo.InvariantCulture);
            string lon = Convert.ToString(Json.Num(place, "longitude"), CultureInfo.InvariantCulture);
            var f = Json.Obj(Json.Parse(Get("https://api.open-meteo.com/v1/forecast?latitude=" + lat + "&longitude=" + lon +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m" +
                "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,weather_code&timezone=auto&forecast_days=3")));

            var sb = new StringBuilder();
            sb.AppendLine("Konum: " + Json.Str(place, "name") + ", " + Json.Str(place, "admin1") + ", " + Json.Str(place, "country"));
            var cur = Json.Obj(Json.Get(f, "current"));
            if (cur != null)
                sb.AppendLine("Şu an: " + Math.Round(Json.Num(cur, "temperature_2m") ?? 0) + "°C (hissedilen " + Math.Round(Json.Num(cur, "apparent_temperature") ?? 0) +
                              "°C), " + Wmo((int)(Json.Num(cur, "weather_code") ?? 0)) + ", nem %" + Json.Str(cur, "relative_humidity_2m") +
                              ", rüzgâr " + Math.Round(Json.Num(cur, "wind_speed_10m") ?? 0) + " km/s");
            var daily = Json.Obj(Json.Get(f, "daily"));
            var dates = Json.Arr(Json.Get(daily, "time")).ToList();
            Func<string, int, double> at = (k, i) => { var a = Json.Arr(Json.Get(daily, k)).ToList(); double v; return i < a.Count && a[i] != null && double.TryParse(Convert.ToString(a[i], CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0; };
            string[] names = { "Bugün", "Yarın", "Öbür gün" };
            for (int i = 0; i < dates.Count && i < 3; i++)
                sb.AppendLine(names[i] + " (" + dates[i] + "): " + Math.Round(at("temperature_2m_min", i)) + "–" + Math.Round(at("temperature_2m_max", i)) + "°C, " +
                              Wmo((int)at("weather_code", i)) + ", yağış ihtimali %" + at("precipitation_probability_max", i));
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Uygulamada karşılığı olmayan sorular için internette araştırır (DuckDuckGo; ilk sonuç Vikipedi ise oradan özet).
        /// Satırlar: "A\tcevap\tkaynak\tadres" (en iyi cevap) ve "R\tbaşlık\tözet\tadres" (diğer sonuçlar).
        /// </summary>
        /// <summary>Bing sonuçları (sunucuda hazırlanmış sayfa): başlık, özet, gerçek adres.</summary>
        internal static List<string[]> SearchBing(string q) { return SearchBing(q, false); }

        /// <summary>retry: alakasız sonuç gelince farklı pazar parametreleriyle yeniden sorulur.</summary>
        internal static List<string[]> SearchBing(string q, bool retry)
        {
            var results = new List<string[]>();
            // Dil/pazar çerezi olmadan Bing, çerezsiz istemcilere çoğu Türkçe soruda alakasız sonuç döndürüyor
            // ("kuantum bilgisayar nedir" → e-Devlet, "aya ilk kim gitti" → bir şarkı)
            // Bing ayrıca tarayıcı başlıklarını da denetliyor: eksik sürümlü tarayıcı kimliği ("Chrome/130.0") bot sayılıp çöp sonuç alıyordu
            var req = (HttpWebRequest)WebRequest.Create("https://www.bing.com/search?q=" + Uri.EscapeDataString(q) + "&cc=tr&setlang=tr-tr&mkt=tr-TR" + (retry ? "&first=1" : ""));
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0";
            req.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            req.Headers["Accept-Language"] = "tr-TR,tr;q=0.9,en;q=0.7";
            req.Headers["Cookie"] = "SRCHHPGUSR=SRCHLANG=tr; _EDGE_S=mkt=tr-tr";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.Timeout = 8000;
            string html;
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) html = sr.ReadToEnd();
            foreach (string block in html.Split(new[] { "<li class=\"b_algo\"" }, StringSplitOptions.None).Skip(1))
            {
                var h = Regex.Match(block, "<h2[^>]*>\\s*<a[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
                if (!h.Success) continue;
                string href = WebUtility.HtmlDecode(h.Groups[1].Value), url = href;
                // Bing yönlendirme adresi: gerçek adres "u=a1" sonrasında base64url olarak durur
                var u = Regex.Match(href, "[?&]u=a1([^&]+)");
                if (u.Success)
                {
                    try
                    {
                        string b = u.Groups[1].Value.Replace('-', '+').Replace('_', '/');
                        while (b.Length % 4 != 0) b += "=";
                        url = Encoding.UTF8.GetString(Convert.FromBase64String(b));
                    }
                    catch { }
                }
                var p = Regex.Match(Regex.Replace(block, "<span class=\"news_dt\">.*?</span>", ""), "<p[^>]*>(.*?)</p>", RegexOptions.Singleline);
                string snip = p.Success ? Clean(p.Groups[1].Value).TrimStart('·', ' ', ' ') : "";
                results.Add(new[] { Clean(h.Groups[2].Value), snip, url });
                if (results.Count >= 5) break;
            }
            return results;
        }

        /// <summary>DuckDuckGo (yedek kaynak; sık istekte bir süre sonuç vermeyebilir).</summary>
        internal static List<string[]> SearchDdg(string q)
        {
            var results = new List<string[]>();
            string html = Get("https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(q) + "&kl=tr-tr", 10000);
            var links = Regex.Matches(html, "class=\"result__a\" href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
            var snippets = Regex.Matches(html, "class=\"result__snippet\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
            for (int i = 0; i < links.Count && results.Count < 5; i++)
            {
                string href = WebUtility.HtmlDecode(links[i].Groups[1].Value);
                if (href.Contains("duckduckgo.com/y.js")) continue; // reklam
                var u = Regex.Match(href, "[?&]uddg=([^&]+)");
                string url = u.Success ? Uri.UnescapeDataString(u.Groups[1].Value) : (href.StartsWith("//") ? "https:" + href : href);
                results.Add(new[] { Clean(links[i].Groups[2].Value), i < snippets.Count ? Clean(snippets[i].Groups[1].Value) : "", url });
            }
            return results;
        }

        // Cevap çıkarmak için açılmayan siteler (video, sosyal medya, arama motorları)
        static readonly string[] NoAnswerHosts = { "youtube.", "youtu.be", "tiktok.", "instagram.", "facebook.", "twitter.", "x.com", "pinterest.",
                                                   "bing.com", "google.", "msn.com", "linkedin.", "reddit.", "spotify.", "apps.apple.", "play.google." };

        // Sorudaki anahtar sözcükleri bulurken atılan soru ve dolgu sözcükleri (normalize, aksansız)
        static readonly HashSet<string> Stop = new HashSet<string>(("nedir kimdir ne neler zaman nasil kac kactir nerede neresi neden niye nicin hangi hangisi " +
            "kim kimin mi mu midir mudur misin musun dir dur tir tur bir ve ile icin bu su o da de en ki demek anlami anlamina ara arastir google googleda " +
            "googledan internetten bul bana soyle soyler anlat hakkinda bilgi ver lutfen olarak olan kadar pardux acaba nedeni sence var yok").Split(' '));

        /// <summary>
        /// Sorunun cevabını internetten bulur: Bing sonuçlarındaki en iyi sayfalar (ve varsa Türkçe Vikipedi) açılır, metinleri
        /// cümlelere bölünür ve soruyu en iyi cevaplayan cümle seçilir ("everest kaç metre" → yüksekliğin geçtiği cümle).
        /// Satırlar: "A\tbaşlık\tcevap\tkaynak\tadres" (cevap) ve "R\tbaşlık\tözet\tadres" (diğer sonuçlar).
        /// </summary>
        /// <summary>Testte cevap seçiminin ayrıntılarını yazdırır.</summary>
        internal static bool Debug;

        public static string Research(string q)
        {
            var keys = Keywords(q);
            bool numeric = Regex.IsMatch(TextUtil.Norm(q), @"\b(kac|kactir|ne zaman|hangi yil|kacinci|yasinda|yuksekligi|nufusu|uzunlugu|derinligi|fiyati|ne kadar|tarihi|yilinda)\b");

            // Vikipedi'nin kendi araması arama motorundan bağımsız, aynı anda başlar (arama motoru alakasız sonuç verirse yedek)
            string[] wikiHit = null;
            var wikiSearch = System.Threading.Tasks.Task.Run(() => { try { wikiHit = WikiSearch(q, keys); } catch { } });
            // Kesin bilgi soruları ("kaç yaşında", "nüfusu", "ne zaman kuruldu"): Wikidata'dan doğrudan değer
            string[] fact = null;
            var factTask = System.Threading.Tasks.Task.Run(() => { try { fact = Facts.Answer(q); } catch { } });

            // Bing art arda isteklerde bazen alakasız sonuç döndürür: sonuçlar sorunun sözcükleriyle süzülür, gerekirse bir kez daha sorulur
            List<string[]> results = new List<string[]>();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                List<string[]> got;
                try { got = attempt == 0 ? SearchBing(q) : SearchBing(q, true); } catch { got = new List<string[]>(); }
                if (got.Count == 0) { try { got = SearchDdg(q); } catch { } }
                foreach (var r in got) r[1] = CleanSnippet(r[1]);
                results = got.Where(r => r[0].Length > 0 && Relevance(r[0] + " " + r[1], keys) >= 0.5).ToList();
                if (results.Count >= 2) break;
                System.Threading.Thread.Sleep(250);
            }
            try { wikiSearch.Wait(4000); } catch { }

            // Vikipedi ve en iyi birkaç sayfa aynı anda açılır (toplam bekleme ~5 sn ile sınırlı)
            var sources = new List<Tuple<string, string, string, double>>(); // metin, başlık, adres, güven
            var tasks = new List<System.Threading.Tasks.Task>();
            // Vikipedi maddeleri (arama motorundan ve Vikipedi aramasından, en çok iki): konusu soruya en uygun olan önce
            var wikis = results.Take(5).Where(r => r[2].Contains("tr.wikipedia.org/wiki/")).Concat(wikiHit != null ? new[] { wikiHit } : new string[0][])
                               .Where(r => Relevance(ShortTitle(r[0]) + " " + r[1], keys) >= 0.5)
                               .GroupBy(r => Uri.UnescapeDataString(r[2])).Select(g => g.First())
                               .OrderByDescending(r => Relevance(ShortTitle(r[0]), keys)).Take(2).ToList();
            var wiki = wikis.FirstOrDefault();
            foreach (var w in wikis) if (!results.Contains(w)) results.Add(w);
            if (Debug) Console.WriteLine("   [sonuçlar] " + string.Join(" || ", results.Select(r => r[0])) + "\n   [vikipedi] " + string.Join(" || ", wikis.Select(r => r[0])));
            if (results.Count == 0) return "";
            for (int wi = 0; wi < wikis.Count; wi++)
            {
                var w = wikis[wi];
                double trust = wi == 0 ? 1.5 : 1.3;
                tasks.Add(System.Threading.Tasks.Task.Run(() =>
                {
                    string t = WikiText(w[2]);
                    if (t != null) lock (sources) sources.Add(Tuple.Create(t, w[0], w[2], trust));
                }));
            }
            int rank = 0;
            foreach (var r in results.Where(r => !wikis.Contains(r) && !NoAnswerHosts.Any(h => r[2].Contains(h))).Take(3))
            {
                var res = r;
                double trust = 1.0 - 0.15 * rank++;
                tasks.Add(System.Threading.Tasks.Task.Run(() =>
                {
                    string t = PageText(res[2]);
                    if (t != null) lock (sources) sources.Add(Tuple.Create(t, res[0], res[2], trust));
                }));
            }
            try { System.Threading.Tasks.Task.WaitAll(tasks.ToArray(), 5500); } catch { }

            // Sonuç özetleri de aday: sayfa açılamazsa en azından onlardan seçilir
            List<Tuple<string, string, string, double>> all;
            lock (sources) all = sources.ToList();
            for (int i = 0; i < results.Count; i++) all.Add(Tuple.Create(results[i][1], results[i][0], results[i][2], 0.7 - 0.1 * i));

            string answer = null, title = results[0][0], url = results[0][2];
            double bestScore = 0;
            try { factTask.Wait(5000); } catch { }
            if (fact != null)
            {
                if (Debug) Console.WriteLine("   [wikidata] " + fact[1]);
                answer = fact[1]; title = fact[0]; url = fact[2]; bestScore = 1000;
            }
            // "X nedir / kimdir / ne demek": en iyi cevap Vikipedi maddesinin giriş cümleleridir
            bool definition = Regex.IsMatch(TextUtil.Norm(q), @"\b(nedir|kimdir|ne demek|anlami|anlamina)\b") || (!numeric && keys.Count <= 2 && !Regex.IsMatch(TextUtil.Norm(q), @"\b(nerede|nasil|neden|niye|hangi)\b"));
            if (fact == null && definition && wiki != null && Relevance(ShortTitle(wiki[0]), keys) >= 0.99)
            {
                string w = WikiSummary(wiki[2]);
                if (w != null)
                {
                    var lead = Sentences(Regex.Replace(w, @"\s*\([^()]*\)", "")); // parantez içindeki yazılış/telaffuz notları atılır
                    if (lead.Count > 0) { answer = string.Join(" ", lead.Take(lead[0].Length < 140 ? 2 : 1)); title = wiki[0]; url = wiki[2]; bestScore = 100; }
                }
            }
            foreach (var src in all)
            {
                var sents = Sentences(src.Item1);
                string srcTitle = TextUtil.Norm(src.Item2);
                for (int i = 0; i < sents.Count; i++)
                {
                    double s = ScoreSentence(sents[i], keys, numeric, srcTitle) * src.Item4 + (i < 3 ? 0.6 : 0);
                    if (s <= bestScore) continue;
                    if (Debug) Console.WriteLine("   [" + s.ToString("0.0") + "] " + src.Item2 + ": " + (sents[i].Length > 140 ? sents[i].Substring(0, 140) : sents[i]));
                    bestScore = s;
                    string a = sents[i];
                    // Kısa cümleyi, aynı konudan devam eden sonraki cümleyle tamamla
                    if (a.Length < 110 && i + 1 < sents.Count && sents[i + 1].Length < 220 && ScoreSentence(sents[i + 1], keys, false, srcTitle) > 0 && sents[i + 1] != a) a += " " + sents[i + 1];
                    answer = a; title = src.Item2; url = src.Item3;
                }
            }
            // Hiçbir cümle soruyla yeterince örtüşmediyse ilk sonucun özeti
            if (answer == null || bestScore < Math.Max(2, keys.Count * 0.9))
            {
                answer = null;
                // Önce konusu soruya uyan Türkçe Vikipedi maddesinin girişi, yoksa Türkçe ilk sonuç özeti
                if (wiki != null && Relevance(ShortTitle(wiki[0]), keys) >= 0.5)
                {
                    string w = WikiSummary(wiki[2]);
                    if (w != null && IsTurkish(w)) { answer = w; title = wiki[0]; url = wiki[2]; }
                }
                if (answer == null)
                {
                    var r0 = results.FirstOrDefault(r => r[1].Length > 40 && IsTurkish(r[1]) && !NoAnswerHosts.Any(h => r[2].Contains(h)));
                    if (r0 != null) { answer = r0[1]; title = r0[0]; url = r0[2]; }
                }
            }
            // Cevap çıkarılamadıysa yalnızca bulunan sayfalar listelenir (yanlış bir cümleyi cevapmış gibi göstermektense)
            if (string.IsNullOrWhiteSpace(answer))
            {
                var sbr = new StringBuilder();
                foreach (var r in results.Where(r => !NoAnswerHosts.Any(h => r[2].Contains(h))).Take(4)) sbr.AppendLine("R	" + Flat(r[0]) + "	" + Flat(r[1]) + "	" + r[2]);
                return sbr.ToString();
            }
            if (answer.Length > 420) { int cut = answer.LastIndexOf(". ", 420, StringComparison.Ordinal); answer = answer.Substring(0, cut > 120 ? cut + 1 : 420) + (cut > 120 ? "" : " …"); }
            string source = url.Contains("wikipedia.org") ? "Vikipedi" : url.Contains("wikidata.org") ? "Wikidata" : Host(url);
            var sb = new StringBuilder();
            sb.AppendLine("A\t" + Flat(ShortTitle(title)) + "\t" + Flat(answer) + "\t" + source + "\t" + url);
            foreach (var r in results.Where(r => r[2] != url).Take(3)) sb.AppendLine("R\t" + Flat(r[0]) + "\t" + Flat(r[1]) + "\t" + r[2]);
            return sb.ToString();
        }

        /// <summary>Sorudaki anlamlı sözcükler (normalize, en çok 5 harflik kök: "yüksekliği" → "yukse").</summary>
        internal static List<string> Keywords(string q)
        {
            var list = new List<string>();
            foreach (string w in Regex.Split(TextUtil.Norm(Regex.Replace(q, @"['’]\p{L}+", "")), @"[^\p{L}\p{N}]+"))
            {
                if (w.Length < 2 || Stop.Contains(w)) continue;
                string k = w.Length > 5 ? w.Substring(0, 5) : w;
                // Ünlü düşmesi: "nehri" ↔ "nehir", "şehri" ↔ "şehir", "burnu" ↔ "burun" (ikisi de kabul edilir, "|" ile ayrılır)
                int L = w.Length;
                if (L >= 4 && L <= 6 && IsVowel(w[L - 1]) && !IsVowel(w[L - 2]) && !IsVowel(w[L - 3]))
                    k = k + "|" + w.Substring(0, L - 2) + w[L - 1] + w[L - 2];
                if (!list.Contains(k)) list.Add(k);
            }
            return list;
        }

        static bool IsVowel(char c) { return "aeiou".IndexOf(c) >= 0; }

        /// <summary>Metin sorunun anahtar sözcüklerinin ne kadarını içeriyor (0–1).</summary>
        static double Relevance(string text, List<string> keys)
        {
            if (keys.Count == 0) return 1;
            var words = Regex.Split(TextUtil.Norm(text), @"[^\p{L}\p{N}]+");
            return keys.Count(k => Has(words, k)) / (double)keys.Count;
        }

        static bool Has(string[] words, string key)
        {
            foreach (string alt in key.Split('|'))
                foreach (string w in words)
                    if (alt.Length <= 2 ? w == alt : w.StartsWith(alt, StringComparison.Ordinal)) return true; // "ay" yalnızca "ay" (Aydan değil)
            return false;
        }

        /// <summary>
        /// Cümlenin soruya cevap olma puanı: anahtar sözcük örtüşmesi, sayı bekleyen sorularda sayı, eksik sözcüklere ceza
        /// (sözcük sayfanın başlığında geçiyorsa eksik sayılmaz: "Everest Dağı" maddesindeki cümle "Everest" demeyebilir).
        /// </summary>
        static double ScoreSentence(string s, List<string> keys, bool numeric, string normTitle = "")
        {
            string n = TextUtil.Norm(s);
            // Kısaltılmış birimler de sorudaki sözcüğü karşılar ("8.848 m" → metre, "385.000 km" → kilometre)
            n = Regex.Replace(n, @"(\d)\s*km\b", "$1 km kilometre");
            n = Regex.Replace(n, @"(\d)\s*m\b", "$1 m metre");
            var words = Regex.Split(n, @"[^\p{L}\p{N}]+");
            var titleWords = Regex.Split(normTitle ?? "", @"[^\p{L}\p{N}]+");
            double score = 0;
            int found = 0;
            foreach (string k in keys)
            {
                if (Has(words, k)) { score += k.Length >= 3 ? 2 : 1; found++; }
                else if (Has(titleWords, k)) score += 1.5; // maddenin konusu: cümlede tekrar edilmesi gerekmez
                else score -= 1.5;
            }
            if (found == 0) return 0;
            if (numeric && Regex.IsMatch(s, @"\d")) score += 3;
            // "kaç metre" yükseklik sorusudur: yükseklikten söz eden cümle öne çıkar
            if (numeric && Has(keys.ToArray(), "metre") && Regex.IsMatch(n, @"\b(yuksekligi|yuksekliginde|yuksekliktedir|rakimi)")) score += 3;
            // Gürültü: çerez uyarıları, menüler, reklam ve "tıklayın" cümleleri
            if (Regex.IsMatch(n, @"cerez|cookie|abone ol|tiklayin|reklam|giris yap|uye ol|yorum yap|paylas|telif|copyright|©|tum haklari")) score -= 8;
            if (s.Length > 320) score -= 1.5;
            if (s.Length < 30) score -= 1;
            if (s.EndsWith("?")) score -= 2; // soru cümlesi cevap değildir
            // Sayı soran sorularda cümle, konunun yanında sorulan özelliği de içermeli ("messi kaç yaşında" → "yaş" geçmeli)
            if (numeric)
            {
                var attrKeys = keys.Where(k => !Has(titleWords, k)).ToList();
                if (attrKeys.Count > 0 && !attrKeys.Any(k => Has(words, k))) score -= 4;
            }
            // Soruyu tekrarlayan giriş cümleleri ("Ay Dünya arası kaç km sorusu yeniden gündeme geldi") cevap değildir
            if (Regex.IsMatch(n, @"\b(kac|nedir|kimdir|kim|nerede|ne zaman|sorusu|sorusunun|konusunu|merak edilen|merak ediliyor|bu makalede|bu yazida|bu icerikte|hazirladigimiz|anlatiyoruz|detaylari|haberimizde)\b")) score -= 3;
            return score;
        }

        static readonly HashSet<string> TrWords = new HashSet<string>("ve bir bu ile için olarak olan da de çok daha en gibi kadar ise olan veya ancak sonra yılında tarafından".Split(' '));
        static readonly HashSet<string> EnWords = new HashSet<string>("the of and is was in to that for with as by are from which an it on at his".Split(' '));

        /// <summary>Cümle Türkçe mi (İngilizce Vikipedi, yabancı site cümleleri cevap olarak gösterilip okunmasın).</summary>
        static bool IsTurkish(string s)
        {
            var w = Regex.Split(s.ToLower(TextUtil.Tr), @"[^\p{L}]+");
            int tr = w.Count(x => TrWords.Contains(x)) + 2 * s.Count(ch => "çğıöşüÇĞİÖŞÜ".IndexOf(ch) >= 0) / 3;
            int en = w.Count(x => EnWords.Contains(x));
            return tr >= en && !(en >= 2 && tr == 0);
        }

        static List<string> Sentences(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            // Kaynak işaretleri ("[c]", "[12]") ve noktalama öncesi boşluklar ("Sistemi 'nin") temizlenir
            text = Regex.Replace(text, @"\s*\[[^\]\n]{1,6}\]", "");
            text = Regex.Replace(text, @"\s+(['’,.;:])", "$1").Replace(" ... ", " ");
            foreach (string para in text.Split('\n'))
                foreach (string s in Regex.Split(para.Trim(), @"(?<=[.!?])\s+(?=[\p{Lu}0-9""“(])"))
                {
                    string t = s.Trim();
                    if (t.Length >= 25 && t.Length <= 450 && t.Count(char.IsLetter) > t.Length / 2 && IsTurkish(t)) list.Add(t);
                }
            return list;
        }

        /// <summary>Bir sayfanın okunabilir metni (yalnızca paragraflar). Açılamazsa null.</summary>
        static string PageText(string url)
        {
            try
            {
                string html = GetPage(url, 4500);
                if (html == null) return null;
                html = Regex.Replace(html, @"<(script|style|noscript|nav|header|footer|aside|form|svg|figure)\b.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var sb = new StringBuilder();
                foreach (Match m in Regex.Matches(html, @"<p\b[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    string t = Regex.Replace(Clean(m.Groups[1].Value), @"\s+", " ").Trim();
                    t = Regex.Replace(t, @"\[\d+\]", "");
                    if (t.Length >= 40) sb.Append(t).Append('\n');
                    if (sb.Length > 30000) break;
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch { return null; }
        }

        /// <summary>Sayfayı indirir; karakter kümesi başlıkta yoksa sayfanın içindeki meta etiketinden okunur.</summary>
        static string GetPage(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.Accept = "text/html,*/*;q=0.8";
            req.Headers["Accept-Language"] = "tr-TR,tr;q=0.9,en;q=0.7";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AutomaticDecompression = DecompressionMethods.GZip;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if (resp.ContentType != null && !resp.ContentType.Contains("html")) return null;
                byte[] data;
                using (var ms = new MemoryStream())
                {
                    var s = resp.GetResponseStream();
                    var buf = new byte[32768];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0 && ms.Length < 2000000) ms.Write(buf, 0, n);
                    data = ms.ToArray();
                }
                Encoding enc = null;
                try { if (!string.IsNullOrEmpty(resp.CharacterSet) && resp.ContentType.ToLowerInvariant().Contains("charset")) enc = Encoding.GetEncoding(resp.CharacterSet); } catch { }
                if (enc == null)
                {
                    var m = Regex.Match(Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 4096)), @"charset=[""']?([\w-]+)", RegexOptions.IgnoreCase);
                    try { enc = m.Success ? Encoding.GetEncoding(m.Groups[1].Value) : Encoding.UTF8; } catch { enc = Encoding.UTF8; }
                }
                return enc.GetString(data);
            }
        }

        /// <summary>Türkçe Vikipedi'de soruyla en ilgili madde: {başlık, özet, adres} ya da null.</summary>
        static string[] WikiSearch(string q, List<string> keys)
        {
            string terms = string.Join(" ", Regex.Split(q, @"\s+").Where(w => !Stop.Contains(TextUtil.Norm(Regex.Replace(w, @"['’]\p{L}+", "")))));
            if (terms.Trim().Length < 2) return null;
            var o = Json.Obj(Json.Parse(Get("https://tr.wikipedia.org/w/api.php?action=query&list=search&srlimit=3&format=json&utf8=1&srsearch=" + Uri.EscapeDataString(terms), 5000)));
            foreach (var hit in Json.Arr(Json.Get(Json.Obj(Json.Get(o, "query")), "search")).Select(Json.Obj).Where(h => h != null))
            {
                string title = Json.Str(hit, "title"), snip = Clean(Json.Str(hit, "snippet"));
                if (Relevance(title + " " + snip, keys) < 0.6) continue;
                return new[] { title + " - Vikipedi", snip, "https://tr.wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_')) };
            }
            return null;
        }

        /// <summary>Türkçe Vikipedi maddesinin düz metni (girişten başlayarak, ilk ~15 bin karakter).</summary>
        static string WikiText(string url)
        {
            try
            {
                var m = Regex.Match(url, @"https?://tr\.wikipedia\.org/wiki/([^#?]+)");
                if (!m.Success) return null;
                var o = Json.Obj(Json.Parse(Get("https://tr.wikipedia.org/w/api.php?action=query&prop=extracts&explaintext=1&exsectionformat=plain&redirects=1&format=json&titles=" + m.Groups[1].Value, 6000)));
                var pages = Json.Obj(Json.Get(Json.Obj(Json.Get(o, "query")), "pages"));
                if (pages == null) return null;
                foreach (var kv in pages)
                {
                    string ex = Json.Str(Json.Obj(kv.Value), "extract");
                    if (!string.IsNullOrEmpty(ex)) return ex.Length > 15000 ? ex.Substring(0, 15000) : ex;
                }
            }
            catch { }
            return null;
        }

        static string WikiSummary(string url)
        {
            try
            {
                var m = Regex.Match(url, @"https?://tr\.wikipedia\.org/wiki/([^#?]+)");
                if (!m.Success) return null;
                string ex = Json.Str(Json.Obj(Json.Parse(Get("https://tr.wikipedia.org/api/rest_v1/page/summary/" + m.Groups[1].Value, 6000))), "extract");
                return string.IsNullOrEmpty(ex) || ex.Length < 40 ? null : ex;
            }
            catch { return null; }
        }

        /// <summary>Bing özetinin başındaki tarih ("18 Eki 2024 · ") ve sonundaki "…" atılır.</summary>
        static string CleanSnippet(string s)
        {
            s = Regex.Replace(s ?? "", @"^\s*(\d{1,2}\s+\p{L}{3,}\s+\d{4}|\d+\s+\p{L}+\s+önce)\s*[·\-–]\s*", "");
            return s.Replace(" ", " ").TrimEnd(' ', '…').Trim();
        }

        /// <summary>Sayfa başlığından site adı eklerini atar ("Everest Dağı - Vikipedi" → "Everest Dağı").</summary>
        static string ShortTitle(string t)
        {
            t = Regex.Replace(t ?? "", @"\s+[-|–—:]\s+[^-|–—:]{2,40}$", "");
            return t.Length > 90 ? t.Substring(0, 90) + "…" : t;
        }

        static string Flat(string s) { return (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim(); }

        static string Host(string url)
        {
            try { return new Uri(url).Host.Replace("www.", ""); } catch { return ""; }
        }

        public static string Exchange(string from, string to, double amount)
        {
            from = (string.IsNullOrWhiteSpace(from) ? "USD" : from.Trim()).ToUpperInvariant();
            to = (string.IsNullOrWhiteSpace(to) ? "TRY" : to.Trim()).ToUpperInvariant();
            var o = Json.Obj(Json.Parse(Get("https://open.er-api.com/v6/latest/" + Uri.EscapeDataString(from))));
            if (Json.Str(o, "result") != "success") return "Kur bilgisi alınamadı (" + from + ").";
            var rates = Json.Obj(Json.Get(o, "rates"));
            double? rate = Json.Num(rates, to);
            if (!rate.HasValue) return "Bilinmeyen para birimi: " + to;
            double total = amount * rate.Value;
            return amount.ToString("0.##", TextUtil.Tr) + " " + from + " = " + total.ToString("#,0.####", TextUtil.Tr) + " " + to +
                   " (1 " + from + " = " + rate.Value.ToString("0.####", TextUtil.Tr) + " " + to + "; güncelleme: " + Json.Str(o, "time_last_update_utc") + ")";
        }
    }
}
