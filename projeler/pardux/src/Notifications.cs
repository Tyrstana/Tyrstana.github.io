using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Windows.Foundation;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Pardux
{
    /// <summary>
    /// Kaçırılan bildirimler: Windows'un bildirim merkezinde duran ve bir süredir açılmamış (tıklanmamış, kapatılmamış) bildirimler.
    /// Windows bir bildirimi kullanıcı açınca ya da kapatınca merkezden kaldırır; uzun süre orada kalan bildirim büyük olasılıkla görülmemiştir.
    /// Pardux bunları seyrek aralıklarla hatırlatır (gece ve tam ekran oyun sırasında hatırlatmaz).
    /// </summary>
    internal static class Notifications
    {
        public class Item
        {
            public uint Id;
            public string App, Title, Body, Aumid;
            public DateTime Time;
        }

        /// <summary>Şu an kaçırılmış sayılan bildirimler (eskiden yeniye).</summary>
        public static List<Item> Missed { get { lock (Gate) return _missed.ToList(); } }

        static readonly object Gate = new object();
        static List<Item> _missed = new List<Item>();
        static readonly HashSet<uint> _reminded = new HashSet<uint>();
        static DateTime _lastReminder = DateTime.MinValue;
        static Timer _timer;
        static bool _denied;

        /// <summary>Hatırlatma gerektiğinde çağrılır: (bildirimler) — UI iş parçacığına aktarmak çağıranın işi.</summary>
        public static event Action<List<Item>> Remind;

        public static Func<bool> Enabled = () => true;
        public static Func<bool> Busy = () => false; // tam ekran oyun / video
        public static int AfterMinutes = 10;

        public static void Start()
        {
            if (_timer != null) return;
            // Seyrek yoklama (30 sn): pil dostu, bildirim merkezini okumak birkaç milisaniye sürer
            _timer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        }

        static T Wait<T>(IAsyncOperation<T> op)
        {
            var end = DateTime.Now.AddSeconds(5);
            while (op.Status == AsyncStatus.Started && DateTime.Now < end) Thread.Sleep(10);
            if (op.Status != AsyncStatus.Completed) throw new TimeoutException();
            return op.GetResults();
        }

        public static void Poll()
        {
            if (_denied || !Enabled()) return;
            try
            {
                var listener = UserNotificationListener.Current;
                if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
                {
                    var st = Wait(listener.RequestAccessAsync());
                    if (st != UserNotificationListenerAccessStatus.Allowed) { _denied = true; return; }
                }
                var list = Wait(listener.GetNotificationsAsync(NotificationKinds.Toast));
                var now = DateTime.Now;
                var items = new List<Item>();
                foreach (var n in list)
                {
                    try
                    {
                        string app = "", aumid = "";
                        try { app = n.AppInfo.DisplayInfo.DisplayName; aumid = n.AppInfo.AppUserModelId; } catch { }
                        if (app.IndexOf("Pardux", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        var b = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                        var texts = b != null ? b.GetTextElements().Select(t => (t.Text ?? "").Trim()).Where(t => t.Length > 0).ToList() : new List<string>();
                        var time = n.CreationTime.LocalDateTime;
                        // Bir süredir duran bildirimler kaçırılmış sayılır; çok eskiler (2 günden eski) sayılmaz
                        if ((now - time).TotalMinutes < AfterMinutes || (now - time).TotalDays > 2) continue;
                        items.Add(new Item
                        {
                            Id = n.Id, App = app.Length > 0 ? app : "Bildirim", Aumid = aumid, Time = time,
                            Title = texts.Count > 0 ? texts[0] : "", Body = texts.Count > 1 ? string.Join(" ", texts.Skip(1)) : ""
                        });
                    }
                    catch { }
                }
                items = items.OrderBy(x => x.Time).ToList();
                List<Item> fresh;
                lock (Gate)
                {
                    _missed = items;
                    _reminded.IntersectWith(items.Select(x => x.Id));
                    fresh = items.Where(x => !_reminded.Contains(x.Id)).ToList();
                }
                // Hatırlatma: yeni kaçırılan varsa, en az 30 dakikada bir; gece (00–08) ve tam ekran sırasında değil
                bool night = now.Hour < 8;
                if (fresh.Count > 0 && !night && !Busy() && (now - _lastReminder).TotalMinutes >= 30 && Remind != null)
                {
                    _lastReminder = now;
                    lock (Gate) foreach (var x in fresh) _reminded.Add(x.Id);
                    Remind(items);
                }
            }
            catch { }
        }

        /// <summary>Bildirimin geldiği uygulamayı açar (yoksa Windows bildirim merkezini).</summary>
        public static void OpenApp(Item it)
        {
            try
            {
                if (it != null && !string.IsNullOrEmpty(it.Aumid))
                {
                    System.Diagnostics.Process.Start("explorer.exe", "shell:AppsFolder\\" + it.Aumid);
                    return;
                }
            }
            catch { }
            OpenCenter();
        }

        /// <summary>Windows bildirim merkezini açar (Win+N).</summary>
        public static void OpenCenter()
        {
            try { System.Diagnostics.Process.Start("ms-actioncenter:"); }
            catch
            {
                Native.keybd_event(0x5B, 0, 0, UIntPtr.Zero); Native.keybd_event(0x4E, 0, 0, UIntPtr.Zero);
                Native.keybd_event(0x4E, 0, 2, UIntPtr.Zero); Native.keybd_event(0x5B, 0, 2, UIntPtr.Zero);
            }
        }

        static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            return d.TotalMinutes < 60 ? (int)d.TotalMinutes + " dk önce" : d.TotalHours < 24 ? (int)d.TotalHours + " sa önce" : "dün";
        }

        public static string Describe(Item it) { return it.App + "  ·  " + Ago(it.Time); }
    }
}
