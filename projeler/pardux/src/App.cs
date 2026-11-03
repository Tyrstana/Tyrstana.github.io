using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("Pardux")]
[assembly: AssemblyProduct("Pardux")]
[assembly: AssemblyDescription("Klavye kısayoluyla açılan kişisel masaüstü asistanı")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Pardux
{
    public class Controller
    {
        const int HotkeyId = 0x4E41;
        const string FallbackHotkey = "Ctrl+Shift+Space";

        public static System.Drawing.Icon AppIcon;
        public static Controller Instance { get; private set; }

        public Settings Settings { get; private set; }

        /// <summary>Açıkken odak kaybında pencere gizlenmesin (yalnızca --sorgu test modu için).</summary>
        public bool PinOpen { get; set; }

        public Controller() { Instance = this; }
        IslandWindow _island;
        Forms.NotifyIcon _tray;
        IntPtr _hwnd;

        public class Reminder
        {
            public string Text;
            public DateTime Due;
            public DispatcherTimer Timer;
            /// <summary>Zamanlayıcı (geri sayımı adada görünür); hatırlatıcılar geri sayım göstermez.</summary>
            public bool IsTimer;
        }

        /// <summary>Kronometre (adada ileri sayar).</summary>
        public readonly Stopwatch Watch = new Stopwatch();
        public bool WatchActive { get { return Watch.IsRunning || Watch.Elapsed > TimeSpan.Zero; } }

        public void StartWatch() { Watch.Start(); _island.RefreshRest(); }
        public void PauseWatch() { Watch.Stop(); _island.RefreshRest(); }
        public void ResetWatch() { Watch.Reset(); _island.RefreshRest(); }

        /// <summary>Geri sayan zamanlayıcı kurar (süre dolunca alarm sesi ve uyarı).</summary>
        public void AddTimer(TimeSpan delay, string label)
        {
            var r = new Reminder { Text = string.IsNullOrEmpty(label) ? "Zamanlayıcı" : label, Due = DateTime.Now.Add(delay), Timer = new DispatcherTimer { Interval = delay }, IsTimer = true };
            r.Timer.Tick += (s, e) =>
            {
                r.Timer.Stop();
                _reminders.Remove(r);
                _island.ShowReminder("Süre doldu · " + r.Text, () => AddTimer(TimeSpan.FromMinutes(1), r.Text), "1 dk ekle");
            };
            _reminders.Add(r);
            r.Timer.Start();
            Sounds.Play(Sound.Success);
            _island.RefreshRest();
        }

        readonly List<Reminder> _reminders = new List<Reminder>();
        public List<Reminder> Reminders { get { return _reminders; } }

        /// <summary>Pano geçmişi (yalnızca bellekte): en yeni başta.</summary>
        public readonly List<Tuple<DateTime, string>> Clips = new List<Tuple<DateTime, string>>();
        string _ignoreClip;

        public void Start(bool hidden, string query)
        {
            Settings = Settings.Load();
            Theme.Apply(Settings);
            bool firstRun = !Settings.FirstRunDone;
            // Başlangıç kaydı her açılışta tazelenir (uygulama klasörü taşındıysa Windows eski yolu açmaya çalışmasın)
            if (firstRun || Settings.StartWithWindows) Settings.ApplyStartup();
            Settings.Log("başlatıldı" + (hidden ? " (Windows ile)" : ""));

            AppIndex.LoadCache();
            AppIndex.RefreshAsync();
            IconLoader.Start(Dispatcher.CurrentDispatcher);

            _island = new IslandWindow(this);
            _hwnd = new WindowInteropHelper(_island).EnsureHandle();
            HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
            Native.AddClipboardFormatListener(_hwnd);

            CreateTray();

            if (!RegisterHotkey(Settings.Hotkey, false))
            {
                string old = Settings.Hotkey;
                if (RegisterHotkey(FallbackHotkey, false))
                {
                    Settings.Hotkey = FallbackHotkey;
                    Settings.Save();
                    Balloon(old + " başka bir uygulamada kullanılıyor. Kısayol " + FallbackHotkey + " olarak ayarlandı.");
                }
                else Balloon("Kısayol kaydedilemedi. Ayarlardan başka bir kısayol seç.");
            }

            if (firstRun)
            {
                Settings.FirstRunDone = true;
                Settings.Save();
                Balloon("Hazırım! İstediğin zaman " + Settings.Hotkey + " ile beni çağırabilirsin.");
            }
            MediaWatcher.Start();
            StartChargeWatch();
            Shell.Background(Shell.RefreshDesktopShortcut);
            Sounds.Warm();
            StartIdleWatch();
            StartTrim();
            ApplyVoice();
            Speech.Prepare(); // "Dinliyorum efendim" önceden hazırlanır
            var ui = Dispatcher.CurrentDispatcher;
            News.StartWatcher(() => Settings.NewsAlerts, () => _island.ForegroundIsFullscreen(),
                it => ui.BeginInvoke(new Action(() => NewsToast.Present(it))));
            // Kaçırılan bildirimler (bildirim merkezinde açılmadan bekleyenler) seyrek aralıklarla hatırlatılır
            Notifications.Enabled = () => Settings.MissedReminders;
            Notifications.Busy = () => { bool b = false; try { ui.Invoke(new Action(() => b = _island.ForegroundIsFullscreen())); } catch { } return b; };
            Notifications.Remind += items => ui.BeginInvoke(new Action(() => _island.ShowMissed(items)));
            Notifications.Start();
            if (hidden && query == null)
            {
                // Windows ile açılınca ada boşta gizli olabilir: çalıştığı görülsün diye kısa bir "hazırım" bildirimi
                var hello = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                hello.Tick += (s, e) => { hello.Stop(); _island.ShowNotice("Pardux hazır", Settings.Hotkey + " ile aç" + (Settings.WakeWord ? " ya da \"Hey Pardux\" de" : ""), G.Sparkle, Palette.Accent); };
                hello.Start();
            }
            if (query == "!ayarlar") OpenSettings();
            else if (query != null)
            {
                PinOpen = true;
                _island.OpenSearch();
                _island.SetQuery(query);
            }
            else if (!hidden) _island.OpenSearch();
            else _island.RefreshRest();
        }

        /// <summary>Adada kısa bir bildirim gösterir (herhangi bir iş parçacığından çağrılabilir).</summary>
        public void Notify(string text)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => _island.ShowNotice(text, null, null, null)));
        }

        public void Notify(string title, string sub, string glyph, System.Windows.Media.Brush color)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => _island.ShowNotice(title, sub, glyph, color)));
        }

        // ---------------- Haberler ----------------
        public void OpenNews(NewsItem it)
        {
            var w = new NewsWindow(it);
            w.Show();
            w.Activate();
        }

        /// <summary>Şu anki en önemli haberi hemen bildirim olarak gösterir (ayarlardaki "Şimdi göster").</summary>
        public void ShowTopNews()
        {
            var ui = Dispatcher.CurrentDispatcher;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var items = News.Fetch();
                    var it = News.Best(items) ?? items.FirstOrDefault();
                    if (it == null) throw new Exception("boş");
                    News.LoadDetails(it);
                    ui.BeginInvoke(new Action(() => NewsToast.Present(it)));
                }
                catch { Notify("Haberler alınamadı", "İnternet bağlantını kontrol et", G.Globe, Palette.Danger); }
            });
        }

        // ---------------- Şarj bildirimi ----------------
        Forms.PowerLineStatus _power = Forms.PowerLineStatus.Unknown;

        [System.Runtime.InteropServices.DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);

        /// <summary>
        /// Bellek: Pardux arka plandayken (ada kapalı ya da dinlenmede) 3 dakikada bir kullanılmayan sayfalar Windows'a geri verilir;
        /// Görev Yöneticisi'nde görünen bellek belirgin düşer, gerektiğinde sayfalar fark edilmeden geri gelir.
        /// </summary>
        void StartTrim()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
            t.Tick += (s, e) =>
            {
                if (_island == null || _island.IsActive) return;
                try { EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle); } catch { }
            };
            t.Start();
        }

        void StartChargeWatch()
        {
            _power = Forms.SystemInformation.PowerStatus.PowerLineStatus;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += (s, e) =>
            {
                var ps = Forms.SystemInformation.PowerStatus;
                if (ps.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery)) { t.Stop(); return; }
                var now = ps.PowerLineStatus;
                if (now == _power || now == Forms.PowerLineStatus.Unknown) return;
                _power = now;
                if (!Settings.IslandCharging) return;
                string pct = "%" + (int)Math.Round(ps.BatteryLifePercent * 100);
                if (now == Forms.PowerLineStatus.Online) _island.ShowNotice("Şarj oluyor", pct, G.Battery, Palette.Calc);
                else _island.ShowNotice("Şarj kablosu çıkarıldı", pct + " pil", G.Battery, Palette.System);
            };
            t.Start();
        }

        // ---------------- Kısayol ----------------
        public bool RegisterHotkey(string spec, bool showError)
        {
            Native.UnregisterHotKey(_hwnd, HotkeyId);
            uint mods, vk;
            if (!ParseHotkey(spec, out mods, out vk)) return false;
            bool ok = Native.RegisterHotKey(_hwnd, HotkeyId, mods | Native.MOD_NOREPEAT, vk);
            if (!ok && showError) Balloon(spec + " kaydedilemedi.");
            if (ok && _tray != null) _tray.Text = "Pardux (" + spec + ")";
            return ok;
        }

        public void SuspendHotkey()
        {
            Native.UnregisterHotKey(_hwnd, HotkeyId);
        }

        public static bool ParseHotkey(string spec, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(spec)) return false;
            foreach (string raw in spec.Split('+'))
            {
                string p = raw.Trim();
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                    case "alt": mods |= Native.MOD_ALT; break;
                    case "shift": mods |= Native.MOD_SHIFT; break;
                    case "win": case "windows": mods |= Native.MOD_WIN; break;
                    default:
                        if (p.Length == 1 && char.IsDigit(p[0])) p = "D" + p;
                        Key k;
                        if (!Enum.TryParse(p, true, out k)) return false;
                        vk = (uint)KeyInterop.VirtualKeyFromKey(k);
                        break;
                }
            }
            return vk != 0;
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                _island.Toggle();
                handled = true;
            }
            else if (msg == Native.WM_CLIPBOARDUPDATE && Settings.ClipboardHistory)
            {
                // Pano bazen başka bir uygulama tarafından kilitli olur; kısa bir gecikmeyle oku.
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(CaptureClipboard), DispatcherPriority.Background);
            }
            return IntPtr.Zero;
        }

        void CaptureClipboard()
        {
            string text = null;
            for (int i = 0; i < 3 && text == null; i++)
            {
                try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); else return; }
                catch { Thread.Sleep(40); }
            }
            if (string.IsNullOrWhiteSpace(text) || text.Length > 100000) return;
            if (text == _ignoreClip) { _ignoreClip = null; return; }
            Clips.RemoveAll(c => c.Item2 == text);
            Clips.Insert(0, Tuple.Create(DateTime.Now, text));
            if (Clips.Count > 30) Clips.RemoveRange(30, Clips.Count - 30);
        }

        /// <summary>Metni panoya koyar ve Pardux kapandıktan sonra önceki pencereye yapıştırır.</summary>
        public void PasteText(string text)
        {
            Clips.RemoveAll(c => c.Item2 == text);
            Clips.Insert(0, Tuple.Create(DateTime.Now, text));
            _ignoreClip = text;
            try { Clipboard.SetText(text); } catch { return; }
            _island.RestoreFocus();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            t.Tick += (s, e) => { t.Stop(); Native.SendCtrlV(); };
            t.Start();
        }

        // ---------------- Tepsi simgesi ----------------
        void CreateTray()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Pardux'u aç", null, (s, e) => _island.OpenSearch());
            menu.Items.Add("Ayarlar", null, (s, e) => OpenSettings());
            menu.Items.Add("Temayı değiştir (koyu/açık)", null, (s, e) =>
            {
                Settings.Theme = Theme.IsDark ? "acik" : "koyu";
                Settings.Save();
                Theme.Apply(Settings);
            });
            menu.Items.Add("Uygulama listesini yenile", null, (s, e) => AppIndex.RefreshAsync());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Çıkış", null, (s, e) => Exit());

            _tray = new Forms.NotifyIcon
            {
                Icon = AppIcon,
                Text = "Pardux (" + Settings.Hotkey + ")",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) _island.OpenSearch(); };
        }

        void Balloon(string text)
        {
            if (_tray == null) return;
            _tray.ShowBalloonTip(5000, "Pardux", text, Forms.ToolTipIcon.Info);
        }

        // ---------------- Diğer ----------------
        public void ShowLauncher()
        {
            _island.OpenSearch();
        }

        /// <summary>Ayarlar değişince adanın dinlenme halini (hap/şu an çalıyor) yeniden değerlendirir.</summary>
        public void RefreshIsland()
        {
            if (_island != null) _island.RefreshRest();
        }

        /// <summary>Ayarlar ayrı bir pencerede değil, adanın içinde açılır.</summary>
        public void OpenSettings()
        {
            _island.ShowSettings(null);
        }

        // ---------------- Sesli komut ----------------
        bool _voiceWired;

        /// <summary>"Hey Pardux" ayarına göre dinlemeyi başlatır ya da durdurur.</summary>
        public void ApplyVoice()
        {
            if (!_voiceWired)
            {
                _voiceWired = true;
                Voice.Woke += () =>
                {
                    Sounds.Play(Sound.Wake);
                    _island.ShowListening();
                    Piper.Warm(); // cevap verilecekse ses modeli hazır olsun
                    // Uyanma sesinin ardından: "Dinliyorum efendim" (komut zaten söylenmeye başladıysa araya girme)
                    if (!Voice.LastWakeHadCommand) Speech.Say("Dinliyorum efendim", 650);
                };
                Voice.Partial += t => _island.UpdateListening(t);
                Voice.Thinking += () => _island.ShowThinking();
                Voice.Command += t =>
                {
                    if (t.Length == 0 && Voice.LastFalseWake)
                    {
                        // Yanlış uyanma (TV, başka konuşma): sessizce kapan, hiçbir şey yapma
                        _island.CancelListening();
                        return;
                    }
                    if (t.Length == 0)
                    {
                        Sounds.Play(Sound.Error);
                        _island.EndListening("Duyamadım", "Tekrar “Hey Pardux” de ve isteğini söyle");
                        Speech.Say("Seni duyamadım");
                        return;
                    }
                    Sounds.Play(Sound.Listen);
                    _island.RunVoiceCommand(Voice.Normalize(t));
                };
            }
            if (Settings.WakeWord && Voice.Available) Voice.Start();
            else Voice.Stop();
        }

        // ---------------- "İyi misin?" ----------------
        bool _askedIdle;

        void StartIdleWatch()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            t.Tick += (s, e) =>
            {
                var idle = Native.IdleTime();
                if (_askedIdle) { if (idle.TotalSeconds < 15) _askedIdle = false; return; }
                if (!Settings.CheckIn || idle.TotalMinutes < Settings.CheckInMinutes) return;
                var np = MediaWatcher.Current;
                if (np != null && np.IsPlaying) return;           // film/dizi izlerken sorma
                if (_island.ForegroundIsFullscreen()) return;
                _askedIdle = true;
                AskCheckIn(false);
            };
            t.Start();
        }

        public void AskCheckIn(bool test)
        {
            string sub = test ? "Uzun süre hareketsiz kalınca böyle soracağım." : "Bir süredir bilgisayarın başında değilsin. Her şey yolunda mı?";
            _island.ShowQuestion("İyi misin?", sub,
                "İyiyim", () => _island.ShowNotice("Harika!", "Bir şeye ihtiyacın olursa buradayım", null, null),
                "Mola verdim", () => _island.ShowNotice("İyi dinlenmeler", "Su içmeyi ve biraz gerinmeyi unutma", G.Sun, Palette.Calc),
                TimeSpan.FromMinutes(test ? 1 : 30));
        }

        public void ReloadSettingsIfChanged()
        {
            try
            {
                if (File.Exists(Settings.FilePath) && File.GetLastWriteTimeUtc(Settings.FilePath) != Settings.Stamp)
                    Settings = Settings.Load();
            }
            catch { }
        }

        public void AddReminder(TimeSpan delay, string text)
        {
            var r = new Reminder { Text = text, Due = DateTime.Now.Add(delay), Timer = new DispatcherTimer { Interval = delay } };
            r.Timer.Tick += (s, e) =>
            {
                r.Timer.Stop();
                _reminders.Remove(r);
                _island.ShowReminder(text, () => AddReminder(TimeSpan.FromMinutes(5), text));
            };
            _reminders.Add(r);
            r.Timer.Start();
            _island.ShowNotice("Hatırlatıcı kuruldu", text + "  ·  " + r.Due.ToString("HH:mm"), G.Clock, Palette.Reminder);
        }

        public void CancelReminder(Reminder r)
        {
            r.Timer.Stop();
            _reminders.Remove(r);
            _island.RefreshRest();
        }

        /// <summary>Açık ayar panelindeki değişiklikler dahil her şeyi diske yazar (çıkışta, oturum kapanırken).</summary>
        public void Flush()
        {
            try { _island.FlushSettings(); } catch { }
            try { if (Settings != null) Settings.Save(); } catch { }
        }

        public void Exit()
        {
            Flush();
            Settings.Log("uygulama kapatıldı (çıkış komutu)");
            Native.UnregisterHotKey(_hwnd, HotkeyId);
            Piper.Stop();
            Whisper.StopServer();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            Application.Current.Shutdown();
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Pardux_TekKopya", out created))
            using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Pardux_Goster"))
            {
                if (!created)
                {
                    // Zaten çalışıyor: mevcut kopyaya "kendini göster" sinyali gönder.
                    Native.AllowSetForegroundWindow(-1);
                    showSignal.Set();
                    return;
                }

                bool hidden = args.Any(a => a == "--gizli" || a == "--hidden");
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    Log(e.Exception);
                    e.Handled = true;
                };

                try { Controller.AppIcon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
                catch { Controller.AppIcon = System.Drawing.SystemIcons.Application; }

                var controller = new Controller();
                string query = args.Where(a => a.StartsWith("--sorgu=")).Select(a => a.Substring(8)).FirstOrDefault();
                app.Startup += (s, e) => controller.Start(hidden, query);
                // Windows kapanırken / oturum kapanırken ayarlar (kişiler, komutlar) yarıda kalmasın
                app.SessionEnding += (s, e) => { Settings.Log("oturum kapanıyor, kaydediliyor"); controller.Flush(); };
                AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { controller.Flush(); } catch { } };

                var listener = new Thread(() =>
                {
                    while (showSignal.WaitOne())
                        app.Dispatcher.BeginInvoke(new Action(controller.ShowLauncher));
                }) { IsBackground = true };
                listener.Start();

                app.Run();
                GC.KeepAlive(mutex);
            }
        }

        static void Log(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Settings.Dir, "hata.log"), DateTime.Now + "  " + ex + Environment.NewLine + Environment.NewLine); }
            catch { }
            Debug.WriteLine(ex);
        }
    }
}
