using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using Windows.Devices.Radios;
using Windows.Foundation;

namespace Pardux
{
    /// <summary>Bilgisayardaki Steam oyunları (kütüphane klasörlerindeki appmanifest dosyalarından) ve Steam mağazasından kurulum.</summary>
    internal static class SteamLibrary
    {
        public class Game { public string Name, Norm, AppId; }

        static List<Game> _cache;
        static DateTime _cacheAt;

        static string SteamPath()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var p = k != null ? k.GetValue("SteamPath") as string : null;
                    if (!string.IsNullOrEmpty(p)) return p.Replace('/', '\\');
                }
            }
            catch { }
            string def = @"C:\Program Files (x86)\Steam";
            return Directory.Exists(def) ? def : null;
        }

        /// <summary>Yüklü oyunlar (5 dakikada bir tazelenir; araçlar ve sunucular dışarıda).</summary>
        public static List<Game> Games()
        {
            if (_cache != null && (DateTime.Now - _cacheAt).TotalMinutes < 5) return _cache;
            var list = new List<Game>();
            try
            {
                string root = SteamPath();
                if (root != null)
                {
                    var libs = new List<string> { Path.Combine(root, "steamapps") };
                    string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            libs.Add(Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), "steamapps"));
                    foreach (string lib in libs.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
                        foreach (string f in Directory.GetFiles(lib, "appmanifest_*.acf"))
                        {
                            string t = File.ReadAllText(f);
                            string id = Field(t, "appid"), name = Field(t, "name");
                            if (id == null || name == null) continue;
                            if (Regex.IsMatch(name, @"Steamworks|Redistributable|Proton|Steam Linux|SteamVR|Dedicated Server|Wallpaper Engine", RegexOptions.IgnoreCase)) continue;
                            list.Add(new Game { Name = name, AppId = id, Norm = TextUtil.Norm(Regex.Replace(name, @"[®™:]", "")) });
                        }
                }
            }
            catch { }
            _cache = list.GroupBy(g => g.AppId).Select(g => g.First()).ToList();
            _cacheAt = DateTime.Now;
            return _cache;
        }

        static string Field(string acf, string key)
        {
            var m = Regex.Match(acf, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static void Run(Game g) { Shell.Open("steam://rungameid/" + g.AppId); }

        /// <summary>Mağazada arar, en uygun oyunun Steam kurulum penceresini açar.</summary>
        public static void Install(string query, Action<string> flash)
        {
            try
            {
                string json = WebTools.Get("https://store.steampowered.com/api/storesearch/?l=turkish&cc=TR&term=" + Uri.EscapeDataString(query), 8000);
                var items = Json.Arr(Json.Get(Json.Obj(Json.Parse(json)), "items")).Select(Json.Obj).Where(x => x != null).ToList();
                if (items.Count == 0) { if (flash != null) flash("Steam'de bulunamadı: " + query); return; }
                var best = items.OrderByDescending(x => TextUtil.Score(TextUtil.Norm(query), TextUtil.Norm(Json.Str(x, "name") ?? ""))).First();
                string id = Convert.ToString(Json.Get(best, "id"));
                if (flash != null) flash("Steam kurulumu açılıyor: " + Json.Str(best, "name"));
                Shell.Open("steam://install/" + id);
            }
            catch { if (flash != null) flash("Steam mağazasına ulaşılamadı"); Shell.Open("steam://store"); }
        }
    }

    /// <summary>Wi-Fi ve Bluetooth radyolarını açıp kapatır (Windows'un Radyo API'si; yönetici izni gerekmez).</summary>
    internal static class Radios
    {
        public enum Kind { WiFi, Bluetooth }

        static T Wait<T>(IAsyncOperation<T> op)
        {
            var end = DateTime.Now.AddSeconds(6);
            while (op.Status == AsyncStatus.Started && DateTime.Now < end) Thread.Sleep(15);
            if (op.Status != AsyncStatus.Completed) throw new TimeoutException();
            return op.GetResults();
        }

        public static void Set(Kind kind, bool on, Action<string> flash)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string name = kind == Kind.WiFi ? "Wi-Fi" : "Bluetooth";
                try
                {
                    var access = Wait(Radio.RequestAccessAsync());
                    if (access != RadioAccessStatus.Allowed)
                    {
                        if (flash != null) flash(name + " için izin yok · Ayarlar açılıyor");
                        Shell.Open(kind == Kind.WiFi ? "ms-settings:network-wifi" : "ms-settings:bluetooth");
                        return;
                    }
                    var radios = Wait(Radio.GetRadiosAsync()).Where(r => r.Kind == (kind == Kind.WiFi ? RadioKind.WiFi : RadioKind.Bluetooth)).ToList();
                    if (radios.Count == 0) { if (flash != null) flash("Bu bilgisayarda " + name + " bulunamadı"); return; }
                    bool ok = true;
                    foreach (var r in radios) ok &= Wait(r.SetStateAsync(on ? RadioState.On : RadioState.Off)) == RadioAccessStatus.Allowed;
                    if (flash != null) flash(ok ? name + (on ? " açıldı" : " kapatıldı") : name + " değiştirilemedi");
                }
                catch
                {
                    if (flash != null) flash(name + " değiştirilemedi · Ayarlar açılıyor");
                    Shell.Open(kind == Kind.WiFi ? "ms-settings:network-wifi" : "ms-settings:bluetooth");
                }
            });
        }
    }

    /// <summary>Dizüstü ekran parlaklığı (WMI) ve ekranı kapatma.</summary>
    internal static class Brightness
    {
        public static int? Get()
        {
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                    foreach (System.Management.ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return null;
        }

        public static void Set(int level, Action<string> flash)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                level = Math.Max(0, Math.Min(100, level));
                try
                {
                    using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                    {
                        bool any = false;
                        foreach (System.Management.ManagementObject o in s.Get()) { o.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)level }); any = true; }
                        if (flash != null) flash(any ? "Parlaklık %" + level : "Bu ekranın parlaklığı buradan değiştirilemiyor");
                    }
                }
                catch { if (flash != null) flash("Parlaklık değiştirilemedi"); }
            });
        }

        public static void Step(int delta, Action<string> flash)
        {
            ThreadPool.QueueUserWorkItem(_ => { var cur = Get(); Set((cur ?? 50) + delta, flash); });
        }

        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        public static void MonitorOff()
        {
            // Ekranı kapat (SC_MONITORPOWER = 2): fare/klavye ile geri açılır
            ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(600); SendMessage(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(2)); });
        }
    }

    /// <summary>Discord ve Gemini'ye klavye otomasyonuyla yazma (bu uygulamaların herkese açık bir mesaj API'si yok).</summary>
    internal static class AppAutomation
    {
        static IntPtr FindWindowOf(string process, string titleContains)
        {
            var pids = new HashSet<uint>(Process.GetProcessesByName(process).Select(p => (uint)p.Id));
            IntPtr found = IntPtr.Zero;
            if (pids.Count == 0) return found;
            Native.EnumWindows((h, l) =>
            {
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (!pids.Contains(pid)) return true;
                var sb = new StringBuilder(512);
                Native.GetWindowText(h, sb, 512);
                string t = sb.ToString();
                if (t.Length == 0 || (titleContains != null && t.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) < 0)) return true;
                var cls = new StringBuilder(128);
                Native.GetClassName(h, cls, 128);
                if (!cls.ToString().StartsWith("Chrome_WidgetWin")) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        static void Key(byte vk, bool ctrl = false)
        {
            if (ctrl) Native.keybd_event(0x11, 0, 0, UIntPtr.Zero);
            Native.keybd_event(vk, 0, 0, UIntPtr.Zero);
            Native.keybd_event(vk, 0, 2, UIntPtr.Zero);
            if (ctrl) Native.keybd_event(0x11, 0, 2, UIntPtr.Zero);
        }

        /// <summary>
        /// Discord'da kişiye yazar: Discord öne getirilir, Ctrl+K (hızlı geçiş) ile kişi aranıp açılır, mesaj yazılır ve
        /// (ayar açıksa) gönderilir. Başarılıysa null, değilse nedeni.
        /// </summary>
        public static string Discord(string who, string msg, bool send)
        {
            IntPtr h = FindWindowOf("Discord", "Discord");
            if (h == IntPtr.Zero)
            {
                Shell.Open("discord://");
                var end = DateTime.Now.AddSeconds(25);
                while (h == IntPtr.Zero && DateTime.Now < end) { Thread.Sleep(500); h = FindWindowOf("Discord", "Discord"); }
                if (h == IntPtr.Zero) return "Discord açılamadı";
                Thread.Sleep(3500); // Discord ilk açılışta arayüzü yüklesin
            }
            if (Native.IsIconic(h)) Native.ShowWindow(h, 9);
            Native.ForceForeground(h);
            Thread.Sleep(450);
            if (Native.GetForegroundWindow() != h) return "Discord öne getirilemedi";
            Key(0x1B);                       // açık bir pencere/menü varsa kapat
            Thread.Sleep(150);
            Key(0x4B, true);                 // Ctrl+K: hızlı geçiş
            Thread.Sleep(450);
            Native.TypeText(who);
            Thread.Sleep(1100);              // arama sonuçları gelsin
            if (Native.GetForegroundWindow() != h) return "Discord penceresi değişti";
            Native.PressEnter();
            Thread.Sleep(1000);              // sohbet açılsın, mesaj kutusu odaklansın
            if (Native.GetForegroundWindow() != h) return "Discord penceresi değişti";
            Native.TypeText(msg);
            Thread.Sleep(200);
            if (send) Native.PressEnter();
            return null;
        }

        /// <summary>Gemini'de yeni sohbet açar ve soruyu gönderir (tarayıcıda gemini.google.com).</summary>
        public static string Gemini(string question)
        {
            Shell.OpenUrl("https://gemini.google.com/app");
            // Tarayıcıda Gemini sekmesi öne gelip yüklenene kadar bekle
            var end = DateTime.Now.AddSeconds(20);
            IntPtr fg = IntPtr.Zero;
            while (DateTime.Now < end)
            {
                fg = Native.GetForegroundWindow();
                var sb = new StringBuilder(512);
                Native.GetWindowText(fg, sb, 512);
                if (sb.ToString().IndexOf("Gemini", StringComparison.OrdinalIgnoreCase) >= 0) break;
                Thread.Sleep(300);
                fg = IntPtr.Zero;
            }
            if (fg == IntPtr.Zero) return "Gemini sayfası açılmadı";
            Thread.Sleep(1800); // sayfa yazı kutusunu hazırlasın
            if (Native.GetForegroundWindow() != fg) return "Tarayıcı penceresi değişti";
            Native.TypeText(question);
            Thread.Sleep(250);
            Native.PressEnter();
            return null;
        }
    }
}
