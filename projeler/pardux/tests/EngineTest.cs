using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Pardux
{
    class FakeHost : IHost
    {
        public Settings Settings { get; set; }
        public void ShowCompose(string to, string subject, string body) { Console.WriteLine("   [compose] to=" + to + " | konu=" + subject + " | mesaj=" + body); }
        public void ShowWhatsApp(string to, string text) { Console.WriteLine("   [whatsapp panel] to=" + to + " | mesaj=" + text); }
        public void SendInstagram(string user, string text, string name) { Console.WriteLine("   [instagram] user=" + user + " | mesaj=" + text); }
        public void SendWhatsApp(string to, string text) { Console.WriteLine("   [whatsapp gönder] to=" + to + " | mesaj=" + text); }
        public void SetQuery(string text) { }
        public void OpenSettings() { }
        public void ExitApp() { }
        public void AddReminder(TimeSpan delay, string text) { Console.WriteLine("   [reminder] " + delay + " -> " + text); }
        public void Flash(string message) { Console.WriteLine("   [flash] " + message); }
        public void SendDiscord(string who, string text, bool send) { Console.WriteLine("   [discord] " + who + " | " + text + " | gönder=" + send); }
        public void AskGemini(string q) { Console.WriteLine("   [gemini] " + q); }
        public Action<List<ResultItem>> OnAppend;
        public void AppendResults(int version, List<ResultItem> items, ResultItem placeholder) { if (OnAppend != null) OnAppend(items); }
    }

    static class EngineTest
    {
        static void Section(string name, Func<string> f)
        {
            Console.WriteLine("\n--- " + name + " ---");
            try { Console.WriteLine(f()); }
            catch (Exception ex) { Console.WriteLine("HATA: " + ex.Message); }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // Testler gerçek ayar dosyasına (kişiler!) asla yazmasın
            Settings.DirOverride = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ParduxTest");
            new Controller(); // Controller.Instance (pano/hatırlatıcı listeleri) için
            AppIndex.RefreshAsync();
            for (int i = 0; i < 100 && AppIndex.Apps.Count == 0; i++) Thread.Sleep(100);
            var host = new FakeHost { Settings = new Settings() };
            typeof(Controller).GetProperty("Settings").SetValue(Controller.Instance, host.Settings);
            host.Settings.Contacts["annem"] = "anne@ornek.com"; host.Settings.Phones["annem"] = "0555 111 22 33"; host.Settings.Phones["Ahmet Yılmaz"] = "05321234567"; host.Settings.Phones["ahmet"] = "05329998877"; host.Settings.Instagram["ayşe"] = "ayse.kaya";
            var engine = new CommandEngine(host);
            foreach (string q in File.ReadAllLines(args[0], Encoding.UTF8))
            {
                if (q.Trim().Length == 0) continue;
                Console.WriteLine("> " + q);
                var res = engine.Query(q, 1);
                if (res.Count > 0 && res[0].Title.StartsWith("İnternette")) { var done = new System.Threading.ManualResetEvent(false); host.OnAppend = items => { foreach (var x in items.Take(3)) Console.WriteLine("   ↳ " + x.Title + "  —  " + (x.Subtitle.Length > 150 ? x.Subtitle.Substring(0, 150) + "…" : x.Subtitle) + (x.Speak != null ? "   🔊 " + x.Speak : "")); done.Set(); }; for (int k = 0; k < 120 && !done.WaitOne(0); k++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(100); } }
                for (int i = 0; i < Math.Min(2, res.Count); i++)
                    Console.WriteLine("   " + (i + 1) + ". [" + res[i].Score + "] " + res[i].Title + "  —  " + res[i].Subtitle + (i == 0 ? "   🔊 " + Speech.Confirm(res[i]) : ""));
            }
            if (args.Length > 1 && args[1] == "persist")
            {
                string dir = Settings.DirOverride;
                Action wipe = () => { foreach (var f in System.IO.Directory.GetFiles(dir, "ayarlar*")) System.IO.File.Delete(f); };
                foreach (var f in System.IO.Directory.GetFiles(dir, "*.json", System.IO.SearchOption.AllDirectories)) System.IO.File.Delete(f);
                var a = Settings.Load();
                a.Contacts.Clear(); a.Phones["kuzey"] = "+90 555 000 00 00"; a.Instagram["kuzey"] = "https://www.instagram.com/direct/t/1"; a.Save();
                Func<string> check = () => { var s = Settings.Load(); string t; return s.Phones.TryGetValue("kuzey", out t) ? "VAR" : "YOK"; };
                Console.WriteLine("1) normal yeniden açılış: " + check());
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "ayarlar.json"), "{bozuk");
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "ayarlar.yedek.json"), "");
                Console.WriteLine("2) ayar + yedek bozuk: " + check());
                wipe();
                Console.WriteLine("3) ayar dosyaları silinmiş: " + check());
                var d = Settings.Load(); d.Phones.Clear(); d.Instagram.Clear(); d.Contacts.Clear(); d.Save(); // eski sürüm gibi boş kaydetti
                Console.WriteLine("4) boş liste kaydedildikten sonra: " + check());
                using (var lk = new System.IO.FileStream(System.IO.Path.Combine(dir, "ayarlar.json"), System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
                {
                    var e = Settings.Load(); // dosya kilitli: varsayılanlarla açılır
                    lk.Close();
                    e.Save();
                }
                Console.WriteLine("5) açılışta dosya kilitliydi, sonra kaydedildi: " + check());
                var g = Settings.Load(); g.Phones.Clear(); g.Instagram.Clear(); g.Contacts.Clear(); g.DeletedContacts.Add("kuzey"); g.Save();
                Console.WriteLine("6) kullanıcı hepsini bilerek sildi (YOK beklenir): " + check());
                // 7) Senin bilgisayarında görülen durum: ana dosyada 3 kişi + 2 komut, eski bir kopyada yalnızca 1 kişi.
                //    Ardından ayar dosyası açılışta okunamıyor ve eski panel yalnızca 1 kişiyle kaydediyor.
                foreach (var f in System.IO.Directory.GetFiles(dir, "*.json", System.IO.SearchOption.AllDirectories)) System.IO.File.Delete(f);
                var h = Settings.Load(); h.CustomCommands.Clear();
                h.Phones["kuzey"] = "1"; h.Phones["annem"] = "2"; h.Phones["babam"] = "3";
                h.CustomCommands.Add(new CustomCommand { Name = "sitemi aç", Target = "https://a.com" }); h.CustomCommands.Add(new CustomCommand { Name = "kanalımı aç", Target = "https://b.com" });
                h.Save();
                var stale = Settings.Load(); stale.Phones.Remove("annem"); stale.Phones.Remove("babam"); stale.CustomCommands.Clear(); stale.Save(); // eski kopya üstüne yazdı
                using (var lk = new System.IO.FileStream(System.IO.Path.Combine(dir, "ayarlar.json"), System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
                { var k = Settings.Load(); lk.Close(); }
                var final = Settings.Load();
                Console.WriteLine("7) eski kopya + kilitli açılış: kişiler=" + string.Join(",", final.Phones.Keys.OrderBy(x => x)) + " komutlar=" + string.Join(",", final.CustomCommands.Select(x => x.Name)));
                final.DeletedContacts.Add("babam"); final.Phones.Remove("babam"); final.Save();
                Console.WriteLine("8) babam silindi (geri gelmemeli): " + string.Join(",", Settings.Load().Phones.Keys.OrderBy(x => x)));
                return;
            }
            if (args.Length > 1 && args[1] == "sounds")
            {
                foreach (Sound snd in new[] { Sound.Notify, Sound.News, Sound.Success, Sound.Alarm })
                {
                    string p = System.IO.Path.Combine(args[2], "pardux_" + snd.ToString().ToLowerInvariant() + ".wav");
                    Sounds.Export(snd, p);
                    var b = System.IO.File.ReadAllBytes(p);
                    int n = (b.Length - 44) / 2; double peak = 0, sum = 0; bool bad = false;
                    for (int i = 0; i < n; i++) { double v = BitConverter.ToInt16(b, 44 + i * 2) / 32768.0; peak = Math.Max(peak, Math.Abs(v)); sum += v * v; if (double.IsNaN(v)) bad = true; }
                    Console.WriteLine(snd + ": " + (n / 2 / 44100.0).ToString("0.00") + " sn, tepe " + peak.ToString("0.00") + ", RMS " + Math.Sqrt(sum / n).ToString("0.000") + (bad ? " HATA" : ""));
                }
                return;
            }
            if (args.Length > 1 && args[1] == "files")
            {
                foreach (string q in (args.Length > 2 ? args[2] : "pardux;projeler;uygulamalar;notlar;slaytlar").Split(';'))
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var hits = FileSearch.Find(q, "any", 7);
                    Console.WriteLine(q + " → " + hits.Count + " sonuç, " + sw.ElapsedMilliseconds + " ms" + (hits.Count > 0 ? "  ilk: " + hits[0].Name + (hits[0].IsFolder ? " (klasör)" : "") : ""));
                }
                return;
            }
            if (args.Length > 1 && args[1] == "facts")
            {
                foreach (string q in (args.Length > 2 ? args[2] : "türkiye nüfusu;fenerbahçe ne zaman kuruldu;everest'in yüksekliği;messi kaç yaşında;fransanın başkenti").Split(';'))
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try { var a = Facts.Answer(q); Console.WriteLine(q + " → " + (a == null ? "(yok)" : a[1]) + "  " + sw.ElapsedMilliseconds + " ms"); }
                    catch (Exception ex) { Console.WriteLine(q + " → HATA " + ex.Message); }
                }
                return;
            }
            if (args.Length > 1 && args[1] == "probe")
            {
                foreach (string q in (args.Length > 2 ? args[2] : "istanbulun nüfusu kaç;suyun kaynama noktası kaç derece;nikola tesla kimdir;dünyanın en uzun nehri hangisi").Split(';'))
                {
                    foreach (var f in new Func<string, List<string[]>>[] { WebTools.SearchBing, WebTools.SearchDdg })
                    {
                        List<string[]> res;
                        try { res = f(q); } catch (Exception ex) { Console.WriteLine("  hata " + ex.Message); continue; }
                        Console.WriteLine("> " + q + " [" + f.Method.Name + "] " + string.Join(" || ", res.Take(3).Select(x => x[0])));
                    }
                }
                return;
            }
            if (args.Length > 1 && args[1] == "research")
            {
                var qs = args.Length > 2 ? args[2].Split(';') : new[] { "atatürk ne zaman doğdu", "türkiyenin başkenti neresi", "everest dağı kaç metre", "fotosentez nedir",
                                               "google'da dünyanın en uzun nehri hangisi ara", "istanbulun nüfusu kaç", "ay ile dünya arası kaç km",
                                               "suyun kaynama noktası kaç derece", "nikola tesla kimdir", "karadeniz nerede" };
                if (args.Length > 3) WebTools.Debug = true;
                foreach (string raw in qs)
                {
                    string q = CommandEngine.ResearchQuery(raw);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string data = WebTools.Research(q);
                    Console.WriteLine("> " + raw + (q != raw ? "  [" + q + "]" : "") + "  (" + sw.ElapsedMilliseconds + " ms)");
                    foreach (string line in data.Split('\n').Where(l => l.Trim().Length > 0).Take(2))
                    {
                        var p = line.Split('\t');
                        string body = p.Length > 2 ? p[2] : "";
                        Console.WriteLine("   " + p[0] + " | " + p[1] + " | " + (body.Length > 300 ? body.Substring(0, 300) + "…" : body) + (p[0] == "A" ? " | kaynak: " + p[3] : ""));
                    }
                }
                return;
            }
            if (args.Length > 1) return;
            Section("Hava", () => WebTools.Weather("Ankara"));
            Section("Kur", () => WebTools.Exchange("USD", "TRY", 100));
        }
    }
}
