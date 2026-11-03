using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Win32;

namespace Pardux
{
    internal static class Shell
    {
        public static void Open(string target, string args = null)
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
            Process.Start(psi);
        }

        public static void OpenUrl(string url)
        {
            if (!url.Contains("://")) url = "https://" + url;
            Open(url);
        }

        public static string Esc(string s)
        {
            return Uri.EscapeDataString(s ?? "");
        }

        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Pardux.lnk"); } }

        public static void CreateDesktopShortcut()
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string lnk = DesktopLink;
            // Eski kısayol silinir: Windows simgeyi yol üzerinden önbelleğe aldığı için üstüne yazmak eski simgeyi bırakabiliyor
            if (File.Exists(lnk)) File.Delete(lnk);
            dynamic wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic sc = wsh.CreateShortcut(lnk);
            sc.TargetPath = exe;
            sc.WorkingDirectory = Path.GetDirectoryName(exe);
            sc.IconLocation = IconFile() + ",0";
            sc.Description = "Pardux";
            sc.Save();
            RefreshShellIcons(lnk);
        }

        /// <summary>Masaüstünde kısayol varsa simgesini güncel simgeye çevirir (uygulama her açılışta çağırır).</summary>
        public static void RefreshDesktopShortcut()
        {
            try
            {
                // Eski adlı kısayol (Emo Asistan) varsa yerine "Pardux" kısayolu konur
                string old = Path.Combine(Path.GetDirectoryName(DesktopLink), "Emo Asistan.lnk");
                if (File.Exists(old)) { File.Delete(old); CreateDesktopShortcut(); return; }
                if (!File.Exists(DesktopLink)) return;
                dynamic wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic sc = wsh.CreateShortcut(DesktopLink);
                string icon = IconFile() + ",0";
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                // Simge ya da hedef değiştiyse (uygulama yeni konuma kuruldu) kısayol yeniden oluşturulur
                if ((string)sc.IconLocation == icon && string.Equals((string)sc.TargetPath, exe, StringComparison.OrdinalIgnoreCase)) return;
                CreateDesktopShortcut();
            }
            catch { }
        }

        /// <summary>
        /// Gömülü simgeyi içeriğine göre adlandırılmış bir dosyaya yazar (simge-xxxx.ico). Simge değişince dosya adı da değişir,
        /// böylece Windows simge önbelleği eski görüntüyü gösteremez.
        /// </summary>
        public static string IconFile()
        {
            using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("pardux.ico"))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                byte[] data = ms.ToArray();
                string hash;
                using (var md5 = System.Security.Cryptography.MD5.Create())
                    hash = BitConverter.ToString(md5.ComputeHash(data)).Replace("-", "").Substring(0, 8).ToLowerInvariant();
                string path = Path.Combine(Settings.Dir, "simge-" + hash + ".ico");
                if (!File.Exists(path))
                {
                    foreach (string old in Directory.GetFiles(Settings.Dir, "simge-*.ico")) try { File.Delete(old); } catch { }
                    File.WriteAllBytes(path, data);
                }
                return path;
            }
        }

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHChangeNotify")]
        static extern void SHChangeNotifyPath(int eventId, uint flags, string path1, IntPtr item2);

        /// <summary>
        /// Gezgin'e kısayolun değiştiğini bildirir ve simge önbelleğini tazeler; aksi halde masaüstü aynı addaki kısayol için
        /// önbellekteki eski simgeyi göstermeye devam edebiliyor.
        /// </summary>
        static void RefreshShellIcons(string lnk = null)
        {
            const int SHCNE_ASSOCCHANGED = 0x08000000, SHCNE_UPDATEITEM = 0x2000, SHCNE_CREATE = 0x2;
            const uint SHCNF_PATHW = 0x5, SHCNF_FLUSH = 0x1000;
            if (lnk != null)
            {
                SHChangeNotifyPath(SHCNE_CREATE, SHCNF_PATHW | SHCNF_FLUSH, lnk, IntPtr.Zero);
                SHChangeNotifyPath(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSH, lnk, IntPtr.Zero);
                SHChangeNotifyPath(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSH, Path.GetDirectoryName(lnk), IntPtr.Zero);
            }
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
            // Windows'un kendi simge önbelleği tazeleme aracı (kullanıcı simge önbelleğini yeniden okur)
            try
            {
                string tool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ie4uinit.exe");
                if (File.Exists(tool)) using (Process.Start(new ProcessStartInfo(tool, "-show") { UseShellExecute = false, CreateNoWindow = true })) { }
            }
            catch { }
        }

        /// <summary>Uzun süren bir işi arka planda çalıştırır.</summary>
        public static void Background(Action a)
        {
            var t = new Thread(() => { try { a(); } catch (Exception ex) { Debug.WriteLine(ex); } }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }

    internal static class Web
    {
        static readonly string[,] Sites =
        {
            { "youtube", "https://www.youtube.com" }, { "gmail", "https://mail.google.com" }, { "instagram", "https://www.instagram.com" },
            { "twitter", "https://x.com" }, { "x", "https://x.com" }, { "whatsapp", "https://web.whatsapp.com" },
            { "chatgpt", "https://chatgpt.com" }, { "claude", "https://claude.ai" }, { "github", "https://github.com" },
            { "netflix", "https://www.netflix.com" }, { "linkedin", "https://www.linkedin.com" }, { "google drive", "https://drive.google.com" },
            { "drive", "https://drive.google.com" }, { "google", "https://www.google.com" }, { "haritalar", "https://maps.google.com" },
            { "takvim", "https://calendar.google.com" }, { "outlook", "https://outlook.live.com" }, { "twitch", "https://www.twitch.tv" },
            { "reddit", "https://www.reddit.com" }, { "spotify web", "https://open.spotify.com" }, { "kick", "https://kick.com" },
            { "trendyol", "https://www.trendyol.com" }, { "hepsiburada", "https://www.hepsiburada.com" }, { "eksi sozluk", "https://eksisozluk.com" },
            { "e devlet", "https://www.turkiye.gov.tr" }, { "edevlet", "https://www.turkiye.gov.tr" }, { "steam", "https://store.steampowered.com" }
        };

        public static IEnumerable<KeyValuePair<string, string>> AllSites()
        {
            for (int i = 0; i < Sites.GetLength(0); i++) yield return new KeyValuePair<string, string>(Sites[i, 0], Sites[i, 1]);
        }

        public static string SiteFor(string norm)
        {
            foreach (var kv in AllSites()) if (kv.Key == norm) return kv.Value;
            return null;
        }

        public static void Search(string engine, string q)
        {
            switch (engine)
            {
                case "youtube": Shell.OpenUrl("https://www.youtube.com/results?search_query=" + Shell.Esc(q)); break;
                case "maps": Shell.OpenUrl("https://www.google.com/maps/search/" + Shell.Esc(q)); break;
                case "images": Shell.OpenUrl("https://www.google.com/search?tbm=isch&q=" + Shell.Esc(q)); break;
                case "translate": Shell.OpenUrl("https://translate.google.com/?sl=auto&tl=en&text=" + Shell.Esc(q) + "&op=translate"); break;
                default: Shell.OpenUrl("https://www.google.com/search?q=" + Shell.Esc(q)); break;
            }
        }

        public static string EngineName(string e)
        {
            switch (e)
            {
                case "youtube": return "YouTube";
                case "maps": return "Haritalar";
                case "images": return "Görseller";
                case "translate": return "Çeviri";
                default: return "Google";
            }
        }
    }

    internal static class Contacts
    {
        /// <summary>"annem, ali@x.com" gibi bir alıcı listesindeki kişi adlarını mail adresine çevirir.</summary>
        public static string Resolve(string to, Settings s)
        {
            if (string.IsNullOrWhiteSpace(to)) return "";
            var list = new List<string>();
            foreach (string raw in to.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string r = raw.Trim();
                if (r.Length == 0) continue;
                if (!r.Contains("@"))
                {
                    string key = TextUtil.Norm(TextUtil.StripSuffix(r));
                    var hit = s.Contacts.FirstOrDefault(kv => TextUtil.Norm(kv.Key) == key);
                    if (hit.Value != null) r = hit.Value;
                }
                list.Add(r);
            }
            return string.Join(", ", list);
        }
    }

    internal static class Media
    {
        public static void PlayPause() { Native.PressKey(Native.VK_MEDIA_PLAY_PAUSE); }
        public static void Next() { Native.PressKey(Native.VK_MEDIA_NEXT); }
        public static void Prev() { Native.PressKey(Native.VK_MEDIA_PREV); }
        public static void Mute() { Native.PressKey(Native.VK_VOLUME_MUTE); }

        public static void VolumeStep(int steps)
        {
            byte vk = steps > 0 ? Native.VK_VOLUME_UP : Native.VK_VOLUME_DOWN;
            for (int i = 0; i < Math.Abs(steps); i++) Native.PressKey(vk);
        }

        /// <summary>Sesi yaklaşık olarak verilen yüzdeye ayarlar (her tuş %2).</summary>
        public static void SetVolume(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            VolumeStep(-50);
            VolumeStep(percent / 2);
        }
    }

    internal static class Spotify
    {
        public class Outcome
        {
            public bool Ok;
            public string Message;
            public Outcome(bool ok, string m) { Ok = ok; Message = m; }
        }

        public static bool IsInstalled
        {
            get
            {
                try { using (var k = Registry.ClassesRoot.OpenSubKey("spotify")) return k != null; }
                catch { return false; }
            }
        }

        /// <summary>Spotify'ın ana penceresinin başlığı (gizli/tepside olsa bile). Çalışmıyorsa null.</summary>
        public static string WindowTitle(out IntPtr hwnd)
        {
            hwnd = IntPtr.Zero;
            var pids = new HashSet<uint>(Process.GetProcessesByName("Spotify").Select(p => (uint)p.Id));
            if (pids.Count == 0) return null;
            string best = null;
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((h, l) =>
            {
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (!pids.Contains(pid)) return true;
                var cls = new StringBuilder(256);
                Native.GetClassName(h, cls, 256);
                if (!cls.ToString().StartsWith("Chrome_WidgetWin")) return true;
                var sb = new StringBuilder(512);
                Native.GetWindowText(h, sb, 512);
                string t = sb.ToString();
                if (t.Length == 0) return true;
                best = t;
                found = h;
                return false;
            }, IntPtr.Zero);
            hwnd = found;
            return best;
        }

        public static bool IsRunning { get { IntPtr h; return WindowTitle(out h) != null; } }

        /// <summary>
        /// Spotify'a Ctrl+tuş kısayolu gönderir (ör. Ctrl+R döngü). Kısayollar yalnızca pencere öndeyken çalıştığı için Spotify
        /// bir an öne alınır, ardından önceki pencere (küçültülmüşse yine küçültülmüş olarak) geri gelir.
        /// </summary>
        public static bool SendShortcut(byte vk)
        {
            IntPtr h;
            if (WindowTitle(out h) == null || h == IntPtr.Zero) return false;
            IntPtr prev = Native.GetForegroundWindow();
            bool wasMin = Native.IsIconic(h);
            if (wasMin) Native.ShowWindow(h, 4 /* SW_SHOWNOACTIVATE */);
            Native.ForceForeground(h);
            for (int i = 0; i < 10 && Native.GetForegroundWindow() != h; i++) Thread.Sleep(20);
            bool ok = Native.GetForegroundWindow() == h;
            if (ok)
            {
                Thread.Sleep(60);
                const byte VK_CONTROL = 0x11;
                const uint KEYUP = 0x2;
                Native.keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                Native.keybd_event(vk, 0, 0, UIntPtr.Zero);
                Native.keybd_event(vk, 0, KEYUP, UIntPtr.Zero);
                Native.keybd_event(VK_CONTROL, 0, KEYUP, UIntPtr.Zero);
                Thread.Sleep(120);
            }
            if (wasMin) Native.ShowWindow(h, 7 /* SW_SHOWMINNOACTIVE */);
            if (prev != IntPtr.Zero && prev != h) Native.ForceForeground(prev);
            return ok;
        }

        /// <summary>Spotify penceresini simge durumuna küçültür (çalmaya devam eder).</summary>
        public static void Minimize()
        {
            IntPtr h;
            if (WindowTitle(out h) == null || h == IntPtr.Zero) return;
            Thread.Sleep(350); // Spotify'ın kendi pencere işlemleri bitsin
            Native.ShowWindow(h, 6 /* SW_MINIMIZE */);
        }

        /// <summary>Spotify bir şey çalarken pencere başlığı "Sanatçı - Şarkı" olur; duraklatılmışken "Spotify ...".</summary>
        public static string NowPlaying()
        {
            IntPtr h;
            string t = WindowTitle(out h);
            if (t == null || t.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase) || !t.Contains(" - ")) return null;
            return t;
        }

        static bool WaitFor(Func<bool> cond, int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end)
            {
                if (cond()) return true;
                Thread.Sleep(250);
            }
            return cond();
        }

        static bool EnsureRunning()
        {
            if (IsRunning) return true;
            Shell.Open("spotify:");
            if (!WaitFor(() => IsRunning, 20000)) return false;
            Thread.Sleep(2500); // arayüzün ve oynatıcının hazırlanması için
            return true;
        }

        public static Outcome OpenSync()
        {
            if (!IsInstalled) { Shell.OpenUrl("https://open.spotify.com"); return new Outcome(true, "Spotify Web açıldı"); }
            Shell.Open("spotify:");
            return new Outcome(true, "Spotify açıldı");
        }

        /// <summary>Spotify'ı açar ve kaldığı şarkıdan devam ettirir.</summary>
        public static Outcome ResumeSync()
        {
            if (!IsInstalled) { Shell.OpenUrl("https://open.spotify.com"); return new Outcome(true, "Spotify Web açıldı (masaüstü uygulaması yüklü değil)"); }
            if (!EnsureRunning()) return new Outcome(false, "Spotify açılamadı");
            string now = NowPlaying();
            if (now != null) return new Outcome(true, "Zaten çalıyor: " + now);
            Media.PlayPause();
            if (WaitFor(() => NowPlaying() != null, 3000)) return new Outcome(true, "Çalıyor: " + NowPlaying());
            if (ClickButton(n => n == "play" || n == "cal" || n == "oynat") && WaitFor(() => NowPlaying() != null, 3000))
                return new Outcome(true, "Çalıyor: " + NowPlaying());
            return new Outcome(false, "Spotify açıldı ama çalma başlatılamadı");
        }

        /// <summary>Spotify'da arar ve ilk sonucu çalmaya çalışır.</summary>
        public static Outcome PlaySync(string query, bool autoPlay)
        {
            if (!IsInstalled)
            {
                Shell.OpenUrl("https://open.spotify.com/search/" + Shell.Esc(query));
                return new Outcome(true, "Spotify Web'de arandı: " + query);
            }
            bool wasRunning = IsRunning;
            string before = NowPlaying();
            Shell.Open("spotify:search:" + Shell.Esc(query));
            if (!autoPlay) return new Outcome(true, "Spotify'da arandı: " + query);
            if (!wasRunning) { WaitFor(() => IsRunning, 20000); Thread.Sleep(2500); }

            string[] words = TextUtil.Norm(query).Split(' ').Where(w => w.Length > 1).ToArray();
            Func<string, bool> match = n =>
            {
                string[] tok = n.Split(' ');
                if (tok.Length < 2 || !(tok.Contains("play") || tok.Contains("cal") || tok.Contains("oynat"))) return false;
                return words.Length == 0 || words.Any(w => n.Contains(w));
            };
            var end = DateTime.Now.AddSeconds(12);
            Thread.Sleep(900);
            while (DateTime.Now < end)
            {
                if (ClickButton(match))
                {
                    if (WaitFor(() => { var np = NowPlaying(); return np != null && np != before; }, 4000))
                        return new Outcome(true, "Çalıyor: " + NowPlaying());
                }
                Thread.Sleep(700);
            }
            return new Outcome(false, "Spotify'da arama açıldı ama ilk sonuç otomatik başlatılamadı");
        }

        /// <summary>Spotify penceresinde adı koşula uyan ilk düğmeye UI Automation ile basar.</summary>
        static bool ClickButton(Func<string, bool> nameMatches)
        {
            try
            {
                IntPtr hwnd;
                if (WindowTitle(out hwnd) == null || hwnd == IntPtr.Zero) return false;
                var root = AutomationElement.FromHandle(hwnd);
                var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                foreach (AutomationElement b in root.FindAll(TreeScope.Descendants, cond))
                {
                    string n = TextUtil.Norm(b.Current.Name);
                    if (n.Length == 0 || !nameMatches(n)) continue;
                    object pat;
                    if (b.TryGetCurrentPattern(InvokePattern.Pattern, out pat))
                    {
                        ((InvokePattern)pat).Invoke();
                        return true;
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
            return false;
        }
    }

    internal static class WhatsApp
    {
        /// <summary>Numarayı uluslararası biçime çevirir: "0555 111 22 33" → "905551112233".</summary>
        public static string Phone(string raw)
        {
            string d = new string((raw ?? "").Where(char.IsDigit).ToArray());
            if (d.StartsWith("00")) d = d.Substring(2);
            if (d.Length == 11 && d.StartsWith("0")) d = "90" + d.Substring(1);
            else if (d.Length == 10 && d.StartsWith("5")) d = "90" + d;
            return d.Length >= 10 ? d : "";
        }

        /// <summary>Ad (kayıtlı kişi) ya da numarayı telefon numarasına çevirir; bulunamazsa boş döner.</summary>
        public static string Resolve(string to, Settings s)
        {
            if (string.IsNullOrWhiteSpace(to)) return "";
            if (to.Count(char.IsDigit) >= 10) return Phone(to);
            string key = TextUtil.Norm(TextUtil.StripSuffix(to.Trim()));
            foreach (var kv in s.Phones)
                if (TextUtil.Norm(kv.Key) == key || TextUtil.Norm(kv.Key) == TextUtil.Norm(to.Trim())) return Phone(kv.Value);
            return "";
        }

        static bool DesktopInstalled
        {
            get { try { using (var k = Registry.ClassesRoot.OpenSubKey("whatsapp")) return k != null; } catch { return false; } }
        }

        /// <summary>
        /// Sohbeti mesaj yazılı olarak açar. Numara biliniyorsa ve otomatik gönderme açıksa, WhatsApp öne gelince Enter'a basarak gönderir.
        /// Dönen değer: mesajın otomatik gönderilmeye çalışılıp çalışılmayacağı.
        /// </summary>
        public static bool Send(string phone, string text, bool autoSend, Action<bool> done)
        {
            string q = (phone.Length > 0 ? "phone=" + phone + "&" : "") + "text=" + Uri.EscapeDataString(text ?? "");
            bool desktop = DesktopInstalled;
            Shell.Open(desktop ? "whatsapp://send?" + q : "https://web.whatsapp.com/send?" + q);
            bool auto = autoSend && desktop && phone.Length > 0 && !string.IsNullOrWhiteSpace(text);
            if (!auto) return false;
            new Thread(() =>
            {
                // WhatsApp öne gelene kadar bekle (en fazla 12 sn); sohbet ve metin yerleşsin diye biraz daha bekle, sonra Enter
                bool sent = false;
                var end = DateTime.Now.AddSeconds(12);
                while (DateTime.Now < end && !IsForeground()) Thread.Sleep(200);
                if (IsForeground())
                {
                    Thread.Sleep(1400);
                    if (IsForeground())
                    {
                        Native.PressKey(0x0D);
                        sent = true;
                    }
                }
                if (done != null) done(sent);
            }) { IsBackground = true }.Start();
            return true;
        }

        static bool IsForeground()
        {
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                using (var p = Process.GetProcessById((int)pid))
                    return p.ProcessName.IndexOf("whatsapp", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }
    }

    internal static class Mail
    {
        public static void Compose(string provider, string to, string subject, string body)
        {
            string url;
            switch (provider)
            {
                case "outlook":
                    url = "https://outlook.live.com/mail/0/deeplink/compose?to=" + Shell.Esc(to) + "&subject=" + Shell.Esc(subject) + "&body=" + Shell.Esc(body);
                    break;
                case "varsayilan":
                    url = "mailto:" + (to ?? "").Replace(" ", "") + "?subject=" + Shell.Esc(subject) + "&body=" + Shell.Esc(body);
                    break;
                default:
                    url = "https://mail.google.com/mail/?view=cm&fs=1&to=" + Shell.Esc(to) + "&su=" + Shell.Esc(subject) + "&body=" + Shell.Esc(body);
                    break;
            }
            Shell.Open(url);
        }
    }

    internal static class SystemActions
    {
        public static void Lock() { Native.LockWorkStation(); }
        public static void Sleep() { Native.SetSuspendState(false, false, false); }
        public static void Shutdown() { Run("shutdown", "/s /t 0"); }
        public static void Restart() { Run("shutdown", "/r /t 0"); }
        public static void LogOff() { Run("shutdown", "/l"); }

        static void Run(string exe, string args)
        {
            Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false });
        }

        public static string KnownFolder(string key)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            switch (key)
            {
                case "indirilenler": case "downloads": return Path.Combine(home, "Downloads");
                case "belgeler": case "documents": return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                case "masaustu": case "desktop": return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                case "resimler": case "pictures": return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                case "muzik": case "music": return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                case "videolar": case "videos": return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                case "home": case "kullanici": return home;
                default: return null;
            }
        }

        public static string ResolveFolder(string f)
        {
            if (string.IsNullOrWhiteSpace(f)) return null;
            string k = KnownFolder(TextUtil.Norm(f));
            if (k != null) return k;
            string p = Environment.ExpandEnvironmentVariables(f.Trim().Trim('"'));
            return Directory.Exists(p) ? p : null;
        }

        static IEnumerable<Process> Windowed()
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                bool ok;
                try { ok = p.Id != me && p.MainWindowHandle != IntPtr.Zero; } catch { ok = false; }
                if (ok) yield return p;
            }
        }

        public static List<string> RunningApps()
        {
            return Windowed().GroupBy(p => p.ProcessName)
                .Select(g => g.Key + " — " + g.First().MainWindowTitle).OrderBy(x => x).ToList();
        }

        public static List<IGrouping<string, Process>> MatchRunning(string name)
        {
            string q = TextUtil.Norm(name);
            if (q.Length == 0) return new List<IGrouping<string, Process>>();
            return Windowed().GroupBy(p => p.ProcessName)
                .Select(g => new { g, s = Math.Max(TextUtil.Score(q, TextUtil.Norm(g.Key)), TextUtil.Score(q, TextUtil.Norm(g.First().MainWindowTitle)) - 200) })
                .Where(x => x.s >= 500).OrderByDescending(x => x.s).Select(x => x.g).ToList();
        }

        public static int CloseApp(string name)
        {
            var groups = MatchRunning(name);
            if (groups.Count == 0) return 0;
            int n = 0;
            foreach (var p in groups[0]) { try { if (p.CloseMainWindow()) n++; } catch { } }
            return n;
        }
    }

    internal class FileHit
    {
        public string Path;
        public string Name;
        public DateTime Modified;
        public bool IsFolder;
    }

    /// <summary>Windows arama dizini (Windows Search) üzerinden hızlı dosya araması.</summary>
    internal static class FileSearch
    {
        public static List<FileHit> Find(string query, string kind, int max)
        {
            var hits = new List<FileHit>();
            query = (query ?? "").Trim();
            if (query.Length < 2) return hits;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');
            var where = new StringBuilder("SCOPE='file:" + home.Replace("'", "''") + "'");
            // Türkçe ekler atılır ("raporu" → "rapor", "fotoğraflarını" → "fotoğraf"): dosya adında kök geçmesi yeter
            foreach (string w in query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(Stem).Where(x => x.Length > 0).Take(4))
                where.Append(" AND System.ItemNameDisplay LIKE '%" + w.Replace("'", "''").Replace("%", "").Replace("[", "") + "%'");
            switch (kind)
            {
                case "pdf": where.Append(" AND System.FileExtension = '.pdf'"); break;
                case "document": where.Append(" AND System.Kind = 'document'"); break;
                case "image": where.Append(" AND System.Kind = 'picture'"); break;
                case "video": where.Append(" AND System.Kind = 'video'"); break;
                case "music": where.Append(" AND System.Kind = 'music'"); break;
                case "folder": where.Append(" AND System.Kind = 'folder'"); break;
            }
            where.Append(" AND NOT System.ItemPathDisplay LIKE '%\\AppData\\%' AND NOT System.ItemPathDisplay LIKE '%\\node_modules\\%'");
            string sql = "SELECT TOP " + (max * 4) + " System.ItemPathDisplay, System.ItemNameDisplay, System.DateModified, System.Kind FROM SystemIndex WHERE " +
                         where + " ORDER BY System.DateModified DESC";
            try
            {
                using (var con = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';"))
                using (var cmd = new OleDbCommand(sql, con))
                {
                    con.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            string path = r[0] as string;
                            if (string.IsNullOrEmpty(path)) continue;
                            var kinds = r[3] as string[];
                            hits.Add(new FileHit
                            {
                                Path = path,
                                Name = r[1] as string ?? System.IO.Path.GetFileName(path),
                                Modified = r[2] is DateTime ? ((DateTime)r[2]).ToLocalTime() : DateTime.MinValue,
                                IsFolder = kinds != null && kinds.Contains("folder")
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
            // Dizinde yoksa (dizin dışı klasör, Türkçe harfsiz yazım "odev" ↔ "ödev") yaygın klasörler doğrudan taranır
            if (hits.Count(h => TextUtil.Norm(h.Name).Contains(TextUtil.Norm(query))) < Math.Min(3, max))
                foreach (var h in Scan(query, kind, max - hits.Count))
                    if (!hits.Any(x => string.Equals(x.Path, h.Path, StringComparison.OrdinalIgnoreCase))) hits.Add(h);
            // Sıralama: adı tam ya da baştan eşleşenler önce ("notlar" → "Notlar" klasörü, "Notifications.cs" değil), sonra en yeni
            string nq = TextUtil.Norm(query);
            Func<FileHit, int> rank = h =>
            {
                string n = TextUtil.Norm(System.IO.Path.GetFileNameWithoutExtension(h.Name));
                return n == nq ? 0 : n.StartsWith(nq) ? 1 : n.Contains(nq) ? 2 : 3;
            };
            return hits.OrderBy(rank).ThenByDescending(h => h.Modified).Take(max).ToList();
        }

        /// <summary>Sözcüğün kökü: sondaki yaygın Türkçe ekler atılır (en az 3 harf kalır).</summary>
        static string Stem(string w)
        {
            w = w.Trim('\'', '’', '"', ',', '.');
            int i = w.IndexOfAny(new[] { '\'', '’' });
            if (i > 0) return w.Substring(0, i);
            string s = Regex.Replace(w, @"(lar|ler)?(ımızı|imizi|ını|ini|unu|ünü|nı|ni|nu|nü|ı|i|u|ü|yı|yi|yu|yü|sını|sini|sunu|sünü|ları|leri)$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"(lar|ler)$", "", RegexOptions.IgnoreCase);
            return s.Length >= 3 ? s : w;
        }

        /// <summary>Masaüstü, Belgeler, İndirilenler, Resimler, Videolar, Müzik ve OneDrive'ı (en fazla ~1,5 sn) tarar; aksansız eşleşir.</summary>
        static List<FileHit> Scan(string query, string kind, int max)
        {
            var found = new List<FileHit>();
            var stems = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => TextUtil.Norm(Stem(w))).Where(x => x.Length > 0).ToList();
            if (stems.Count == 0 || max <= 0) return found;
            var roots = new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures,
                                Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.MyMusic }
                .Select(Environment.GetFolderPath).ToList();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.Add(System.IO.Path.Combine(home, "Downloads"));
            string od = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrEmpty(od)) roots.Add(od);
            var end = DateTime.Now.AddMilliseconds(1500);
            var queue = new Queue<Tuple<string, int>>(roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Select(r => Tuple.Create(r, 0)));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] exts = kind == "pdf" ? new[] { ".pdf" } : kind == "image" ? new[] { ".jpg", ".jpeg", ".png", ".heic", ".webp", ".gif", ".bmp" } :
                            kind == "video" ? new[] { ".mp4", ".mov", ".mkv", ".avi", ".webm" } :
                            kind == "document" ? new[] { ".doc", ".docx", ".pdf", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".odt" } : null;
            Func<string, bool> match = name => { string n = TextUtil.Norm(name); return stems.All(s => n.Contains(s)); };
            while (queue.Count > 0 && found.Count < max && DateTime.Now < end)
            {
                var cur = queue.Dequeue();
                if (!seen.Add(cur.Item1)) continue;
                try
                {
                    foreach (string d in Directory.EnumerateDirectories(cur.Item1))
                    {
                        string nm = System.IO.Path.GetFileName(d);
                        if (nm.StartsWith(".") || nm == "node_modules" || nm == "AppData" || nm == "bin" || nm == "obj") continue;
                        if ((kind == "any" || kind == "folder") && match(nm)) found.Add(new FileHit { Path = d, Name = nm, IsFolder = true, Modified = Directory.GetLastWriteTime(d) });
                        if (cur.Item2 < 5) queue.Enqueue(Tuple.Create(d, cur.Item2 + 1));
                    }
                    if (kind != "folder")
                        foreach (string f in Directory.EnumerateFiles(cur.Item1))
                        {
                            string nm = System.IO.Path.GetFileName(f);
                            if (exts != null && !exts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())) continue;
                            if (match(System.IO.Path.GetFileNameWithoutExtension(f))) found.Add(new FileHit { Path = f, Name = nm, Modified = File.GetLastWriteTime(f) });
                            if (found.Count >= max) break;
                        }
                }
                catch { }
            }
            return found.OrderByDescending(h => h.Modified).ToList();
        }
    }

    internal static class SystemInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength; public uint dwMemoryLoad; public ulong ullTotalPhys; public ulong ullAvailPhys;
            public ulong ullTotalPageFile; public ulong ullAvailPageFile; public ulong ullTotalVirtual; public ulong ullAvailVirtual; public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        public static int CpuPercent()
        {
            long i1, k1, u1, i2, k2, u2;
            GetSystemTimes(out i1, out k1, out u1);
            Thread.Sleep(350);
            GetSystemTimes(out i2, out k2, out u2);
            long total = (k2 - k1) + (u2 - u1);
            return total <= 0 ? 0 : (int)Math.Round(100.0 * (total - (i2 - i1)) / total);
        }

        public static string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Bilgisayar: " + Environment.MachineName + ", Windows " + (Fluent.Build >= 22000 ? "11" : "10") + " (derleme " + Fluent.Build + ")");
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery))
                sb.AppendLine("Pil: yok (masaüstü bilgisayar)");
            else
                sb.AppendLine("Pil: %" + (int)(ps.BatteryLifePercent * 100) + (ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? " (şarj oluyor/prizde)" : " (pilde)") +
                    (ps.BatteryLifeRemaining > 0 ? ", kalan yaklaşık " + TimeSpan.FromSeconds(ps.BatteryLifeRemaining).ToString(@"h\:mm") : ""));
            sb.AppendLine("İşlemci kullanımı: %" + CpuPercent());
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (GlobalMemoryStatusEx(ref m))
                sb.AppendLine("Bellek: %" + m.dwMemoryLoad + " dolu (" + Gb(m.ullTotalPhys - m.ullAvailPhys) + " / " + Gb(m.ullTotalPhys) + ")");
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                sb.AppendLine("Disk " + d.Name + ": " + Gb((ulong)d.AvailableFreeSpace) + " boş / " + Gb((ulong)d.TotalSize));
            var up = TimeSpan.FromMilliseconds(GetTickCount64());
            sb.AppendLine("Açık kalma süresi: " + (int)up.TotalDays + " gün " + up.Hours + " saat " + up.Minutes + " dakika");
            return sb.ToString().Trim();
        }

        static string Gb(ulong bytes) { return (bytes / 1073741824.0).ToString("0.0", TextUtil.Tr) + " GB"; }
    }

    internal static class Notes
    {
        public static string FilePath { get { return Path.Combine(Settings.Dir, "notlar.txt"); } }

        public static void Add(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            File.AppendAllText(FilePath, DateTime.Now.ToString("dd.MM.yyyy HH:mm") + "  " + text.Trim().Replace("\r", " ").Replace("\n", " ") + Environment.NewLine, new UTF8Encoding(false));
        }

        public static string Recent(int n)
        {
            if (!File.Exists(FilePath)) return "Henüz not yok.";
            var lines = File.ReadAllLines(FilePath, Encoding.UTF8).Where(l => l.Trim().Length > 0).ToList();
            return lines.Count == 0 ? "Henüz not yok." : string.Join("\n", lines.Skip(Math.Max(0, lines.Count - n)));
        }

        public static void Open()
        {
            if (!File.Exists(FilePath)) File.WriteAllText(FilePath, "", new UTF8Encoding(false));
            Shell.Open("notepad.exe", "\"" + FilePath + "\"");
        }
    }
}
