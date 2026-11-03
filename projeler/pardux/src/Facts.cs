using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pardux
{
    /// <summary>
    /// Kesin bilgi soruları için Wikidata (Vikipedi'nin yapılandırılmış veri tabanı): "messi kaç yaşında", "türkiye nüfusu",
    /// "everest kaç metre", "fenerbahçe ne zaman kuruldu", "fransanın başkenti". Sorudaki konu ve istenen özellik ayrılır,
    /// konu Wikidata'da bulunur, özelliğin değeri okunup Türkçe bir cümleye çevrilir. Sayfalardan cümle seçmekten çok daha güvenilir.
    /// </summary>
    internal static class Facts
    {
        class Attr
        {
            public string Pattern, Prop, Kind; // Kind: age, date, place, quantity, entity, born, died
            public Attr(string pattern, string prop, string kind) { Pattern = pattern; Prop = prop; Kind = kind; }
        }

        // Normalize edilmiş (aksansız) sorudaki özellik kalıpları → Wikidata özelliği
        static readonly Attr[] Attrs =
        {
            new Attr(@"\b(kac yasinda|yasi kac|kac yasina girdi|yasi nedir|kac yasindadir)\b", "P569", "age"),
            new Attr(@"\b(ne zaman dogdu|dogum tarihi|hangi yil dogdu|kac yilinda dogdu|dogum gunu)\b", "P569", "date"),
            new Attr(@"\b(nerede dogdu|nereli|dogum yeri|nerelidir)\b", "P19", "entity"),
            new Attr(@"\b(ne zaman (oldu|vefat etti|hayatini kaybetti)|olum tarihi|hangi yil oldu)\b", "P570", "date"),
            new Attr(@"\b(nufusu?|kac kisi yasiyor|nufusu kac)\b", "P1082", "quantity"),
            new Attr(@"\b(boyu kac|kac boyunda|boyu)\b", "P2048", "quantity"),
            new Attr(@"\b(yuksekligi|kac metre|rakimi|yukseklik|ne kadar yuksek)\b", "P2044", "quantity"),
            new Attr(@"\b(uzunlugu|ne kadar uzun|kac km uzunlugunda)\b", "P2043", "quantity"),
            new Attr(@"\b(yuzolcumu|yuz olcumu|alani|kac kilometrekare|kac km2)\b", "P2046", "quantity"),
            new Attr(@"\b(ne zaman kuruldu|kurulus tarihi|kurulus yili|hangi yil kuruldu|kac yilinda kuruldu|ne zaman kurulmus)\b", "P571", "date"),
            new Attr(@"\b(baskenti|baskent)\b", "P36", "entity"),
            new Attr(@"\b(kurucusu|kim kurdu|kim tarafindan kuruldu)\b", "P112", "entity"),
            new Attr(@"\b(yazari|kim yazdi|kimin eseri)\b", "P50", "entity"),
            new Attr(@"\b(yonetmeni|kim yonetti)\b", "P57", "entity"),
            new Attr(@"\b(ne zaman cikti|cikis tarihi|yayin tarihi|ne zaman yayinlandi|hangi yil cikti)\b", "P577", "date"),
            new Attr(@"\b(para birimi)\b", "P38", "entity"),
            new Attr(@"\b(cumhurbaskani|devlet baskani)\b", "P35", "entity"),
            new Attr(@"\b(basbakani|hukumet baskani)\b", "P6", "entity"),
            new Attr(@"\b(resmi dili|hangi dil konusuluyor)\b", "P37", "entity"),
            new Attr(@"\b(hangi ulkede|hangi ulkenin|nerededir|ulkesi)\b", "P17", "entity"),
            new Attr(@"\b(derinligi|ne kadar derin)\b", "P4511", "quantity"),
        };

        static readonly HashSet<string> Filler = new HashSet<string>("ne nedir kac kactir hangi mi mu midir mudur acaba bana soyle google da de ara arastir en bir ve ile su bu o".Split(' '));

        static readonly string[] Months = { "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };

        /// <summary>Soru bir özellik soruyorsa cevabı döndürür: {başlık, cevap cümlesi, adres}; değilse ya da bulunamazsa null.</summary>
        public static string[] Answer(string question)
        {
            string norm = TextUtil.Norm(question);
            Attr attr = null;
            Match am = null;
            foreach (var a in Attrs)
            {
                var m = Regex.Match(norm, a.Pattern);
                if (m.Success) { attr = a; am = m; break; }
            }
            if (attr == null) return null;
            // Konu: özellik kalıbı ve dolgu sözcükleri atılınca kalan (Türkçe ek ve kesme işaretiyle birlikte)
            string subjectNorm = (norm.Substring(0, am.Index) + " " + norm.Substring(am.Index + am.Length)).Trim();
            var words = new List<string>();
            foreach (string w in Regex.Split(subjectNorm, @"\s+"))
            {
                string x = Regex.Replace(w, @"['’].*$", "");
                if (x.Length == 0 || Filler.Contains(x)) continue;
                words.Add(x);
            }
            if (words.Count == 0) return null;
            // Orijinal yazımı (Türkçe harfler) koru: aynı sözcükleri sorudan al
            var orig = Regex.Split(question.Trim(), @"\s+").Select(w => Regex.Replace(w, @"['’].*$", "")).Where(w => words.Contains(TextUtil.Norm(w))).ToList();
            if (orig.Count == 0) return null;
            var candidates = new List<string> { string.Join(" ", orig) };
            // Ek almış son sözcük: "türkiyenin" → "türkiye", "fenerbahçenin" → "fenerbahçe" (kesme işaretsiz yazımlar)
            string last = orig[orig.Count - 1];
            string stripped = Regex.Replace(last, @"(n?[ıiuü]n|n?[ıiuü]|[dt][ae]|[dt][ae]n|y?[ae]|l[ae]r[ıi]?n?[ıi]?n?)$", "", RegexOptions.IgnoreCase);
            if (stripped.Length >= 3 && stripped != last) { orig[orig.Count - 1] = stripped; candidates.Add(string.Join(" ", orig)); }

            foreach (string subject in candidates)
            {
                string id, label, desc;
                if (!Find(subject, attr.Prop, out id, out label, out desc)) continue;
                string ans = Format(attr, id, label);
                if (ans == null) continue;
                return new[] { label, ans, "https://www.wikidata.org/wiki/" + id };
            }
            return null;
        }

        static object J(string url)
        {
            // Wikidata kısa süreli yoğunlukta 429 döndürebilir: biraz bekleyip bir kez daha dene
            for (int i = 0; ; i++)
            {
                try { return Json.Parse(WebTools.Get(url, 6000)); }
                catch (System.Net.WebException ex)
                {
                    var r = ex.Response as System.Net.HttpWebResponse;
                    if (i >= 1 || r == null || (int)r.StatusCode != 429) throw;
                    System.Threading.Thread.Sleep(900);
                }
            }
        }

        /// <summary>Konuyu Wikidata'da arar; istenen özelliği taşıyan ilk sonucu seçer ("messi" → Lionel Messi).</summary>
        static bool Find(string subject, string prop, out string id, out string label, out string desc)
        {
            id = label = desc = null;
            foreach (string lang in new[] { "tr", "en" })
            {
                var o = Json.Obj(J("https://www.wikidata.org/w/api.php?action=wbsearchentities&format=json&limit=5&type=item&language=" + lang +
                                   "&uselang=tr&search=" + Uri.EscapeDataString(subject)));
                var hits = Json.Arr(Json.Get(o, "search")).Select(Json.Obj).Where(h => h != null).ToList();
                foreach (var h in hits.Take(3))
                {
                    string qid = Json.Str(h, "id");
                    var claims = Claims(qid, prop);
                    if (claims.Count == 0) continue;
                    id = qid;
                    label = Json.Str(h, "label");
                    desc = Json.Str(h, "description");
                    // Türkçe aramada etiket zaten Türkçe gelir; İngilizce aramada Türkçe adı ayrıca sorulur
                    if (lang != "tr") { string tr = Label(qid); if (!string.IsNullOrEmpty(tr)) label = tr; }
                    return true;
                }
            }
            return false;
        }

        static readonly Dictionary<string, List<Dictionary<string, object>>> ClaimCache = new Dictionary<string, List<Dictionary<string, object>>>();

        static List<Dictionary<string, object>> Claims(string qid, string prop)
        {
            string key = qid + "|" + prop;
            lock (ClaimCache) { List<Dictionary<string, object>> c; if (ClaimCache.TryGetValue(key, out c)) return c; }
            var o = Json.Obj(J("https://www.wikidata.org/w/api.php?action=wbgetclaims&format=json&entity=" + qid + "&property=" + prop));
            var list = Json.Arr(Json.Get(Json.Obj(Json.Get(o, "claims")), prop)).Select(Json.Obj).Where(x => x != null && Json.Str(x, "rank") != "deprecated").ToList();
            lock (ClaimCache) ClaimCache[key] = list;
            return list;
        }

        static string Label(string qid)
        {
            try
            {
                var o = Json.Obj(J("https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=labels&languages=tr|en&ids=" + qid));
                var labels = Json.Obj(Json.Get(Json.Obj(Json.Get(Json.Obj(Json.Get(o, "entities")), qid)), "labels"));
                foreach (string l in new[] { "tr", "en" })
                {
                    string v = Json.Str(Json.Obj(Json.Get(labels, l)), "value");
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { }
            return null;
        }

        /// <summary>En güncel / tercih edilen değer (nüfus gibi yıllara göre birden çok değer olabilir).</summary>
        static Dictionary<string, object> Best(List<Dictionary<string, object>> claims, out string when)
        {
            when = null;
            var preferred = claims.Where(c => Json.Str(c, "rank") == "preferred").ToList();
            var pool = preferred.Count > 0 ? preferred : claims;
            Dictionary<string, object> best = null;
            string bestTime = "";
            foreach (var c in pool)
            {
                string t = "";
                var q = Json.Obj(Json.Get(c, "qualifiers"));
                var pit = q != null ? Json.Arr(Json.Get(q, "P585")).Select(Json.Obj).FirstOrDefault() : null; // tarih itibarıyla
                if (pit != null) t = Json.Str(Json.Obj(Json.Get(Json.Obj(Json.Get(pit, "datavalue")), "value")), "time") ?? "";
                if (best == null || string.CompareOrdinal(t, bestTime) > 0) { best = c; bestTime = t; }
            }
            if (bestTime.Length >= 5) when = bestTime.Substring(1, 4);
            return best;
        }

        static Dictionary<string, object> Value(Dictionary<string, object> claim)
        {
            var snak = Json.Obj(Json.Get(claim, "mainsnak"));
            return Json.Obj(Json.Get(Json.Obj(Json.Get(snak, "datavalue")), "value"));
        }

        static DateTime? Date(Dictionary<string, object> v, out int precision)
        {
            precision = (int)(Json.Num(v, "precision") ?? 0);
            var m = Regex.Match(Json.Str(v, "time") ?? "", @"^\+?(-?\d+)-(\d\d)-(\d\d)");
            if (!m.Success) return null;
            int y = int.Parse(m.Groups[1].Value), mo = Math.Max(1, int.Parse(m.Groups[2].Value)), d = Math.Max(1, int.Parse(m.Groups[3].Value));
            if (y < 1 || y > 9999) return null;
            return new DateTime(y, mo, d);
        }

        static string DateText(DateTime d, int precision)
        {
            if (precision >= 11) return d.Day + " " + Months[d.Month - 1] + " " + d.Year;
            if (precision == 10) return Months[d.Month - 1] + " " + d.Year;
            return d.Year.ToString();
        }

        static string Format(Attr attr, string qid, string label)
        {
            var claims = Claims(qid, attr.Prop);
            if (claims.Count == 0) return null;
            string when;
            var claim = Best(claims, out when);
            var v = Value(claim);
            if (v == null) return null;
            string gen = Genitive(label);
            switch (attr.Kind)
            {
                case "age":
                {
                    int p; var born = Date(v, out p);
                    if (born == null) return null;
                    var deathClaims = Claims(qid, "P570");
                    int dp; DateTime? died = deathClaims.Count > 0 ? Date(Value(deathClaims[0]) ?? new Dictionary<string, object>(), out dp) : null;
                    DateTime end = died ?? DateTime.Today;
                    int age = end.Year - born.Value.Year - (p >= 11 && end < born.Value.AddYears(end.Year - born.Value.Year) ? 1 : 0);
                    return died != null
                        ? label + " " + DateText(died.Value, 11) + " tarihinde " + age + " yaşında hayatını kaybetti (doğum: " + DateText(born.Value, p) + ")."
                        : label + " " + age + " yaşında (doğum tarihi: " + DateText(born.Value, p) + ").";
                }
                case "date":
                {
                    int p; var d = Date(v, out p);
                    if (d == null) return null;
                    string what = attr.Prop == "P569" ? "doğum tarihi" : attr.Prop == "P570" ? "ölüm tarihi" : attr.Prop == "P571" ? "kuruluş tarihi" : "çıkış tarihi";
                    return gen + " " + what + ": " + DateText(d.Value, p) + ".";
                }
                case "quantity":
                {
                    double amount;
                    if (!double.TryParse((Json.Str(v, "amount") ?? "").TrimStart('+'), NumberStyles.Float, CultureInfo.InvariantCulture, out amount)) return null;
                    string unit = Unit(Json.Str(v, "unit"));
                    string num = amount.ToString(Math.Abs(amount - Math.Round(amount)) < 1e-9 ? "#,0" : "#,0.##", TextUtil.Tr);
                    string what = attr.Prop == "P1082" ? "nüfusu" : attr.Prop == "P2048" ? "boyu" : attr.Prop == "P2044" ? "yüksekliği" :
                                  attr.Prop == "P2043" ? "uzunluğu" : attr.Prop == "P2046" ? "yüzölçümü" : "derinliği";
                    if (attr.Prop == "P2048" && unit == "metre" && amount < 3) { num = (amount * 100).ToString("0", TextUtil.Tr); unit = "santimetre"; }
                    return gen + " " + what + " " + num + (unit.Length > 0 ? " " + unit : "") + (when != null ? " (" + when + ")" : "") + ".";
                }
                case "entity":
                {
                    var names = claims.Where(c => Json.Str(c, "rank") != "deprecated").Take(3)
                                      .Select(c => Json.Str(Value(c) ?? new Dictionary<string, object>(), "id")).Where(x => x != null)
                                      .Select(Label).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                    if (attr.Prop == "P35" || attr.Prop == "P6" || attr.Prop == "P36" || attr.Prop == "P38" || attr.Prop == "P17") names = names.Take(1).ToList();
                    if (names.Count == 0) return null;
                    string what = attr.Prop == "P19" ? "doğum yeri" : attr.Prop == "P36" ? "başkenti" : attr.Prop == "P112" ? "kurucusu" :
                                  attr.Prop == "P50" ? "yazarı" : attr.Prop == "P57" ? "yönetmeni" : attr.Prop == "P38" ? "para birimi" :
                                  attr.Prop == "P35" ? "devlet başkanı" : attr.Prop == "P6" ? "hükümet başkanı" : attr.Prop == "P37" ? "resmî dili" : "bulunduğu ülke";
                    return gen + " " + what + ": " + string.Join(", ", names) + ".";
                }
            }
            return null;
        }

        static string Unit(string unitUrl)
        {
            if (string.IsNullOrEmpty(unitUrl) || unitUrl == "1") return "";
            string q = unitUrl.Substring(unitUrl.LastIndexOf('/') + 1);
            switch (q)
            {
                case "Q11573": return "metre";
                case "Q828224": return "kilometre";
                case "Q712226": return "kilometrekare";
                case "Q25343": return "metrekare";
                case "Q174728": return "santimetre";
                case "Q11570": return "kilogram";
                case "Q3710": return "fit";
                default: return Label(q) ?? "";
            }
        }

        /// <summary>Türkçe tamlayan eki: "Türkiye" → "Türkiye'nin", "Everest Dağı" → "Everest Dağı'nın", "Messi" → "Messi'nin".</summary>
        public static string Genitive(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            string low = name.ToLower(TextUtil.Tr);
            char lastVowel = 'e';
            for (int i = low.Length - 1; i >= 0; i--) if ("aeıioöuü".IndexOf(low[i]) >= 0) { lastVowel = low[i]; break; }
            string suf = "aı".IndexOf(lastVowel) >= 0 ? "ın" : "ei".IndexOf(lastVowel) >= 0 ? "in" : "ou".IndexOf(lastVowel) >= 0 ? "un" : "ün";
            bool endsVowel = "aeıioöuü".IndexOf(low[low.Length - 1]) >= 0;
            return name + "'" + (endsVowel ? "n" : "") + suf;
        }
    }
}
