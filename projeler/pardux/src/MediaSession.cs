using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Pardux
{
    /// <summary>Şu an çalan medyanın anlık durumu (Spotify, tarayıcı, her uygulama).</summary>
    public class NowPlaying
    {
        public string Title, Artist, App, Aumid;
        public bool IsPlaying;
        public TimeSpan Position, Duration;
        public DateTime PositionAt;
        public ImageSource Art;
        public Color ArtColor;
        /// <summary>Döngü: "none", "list" (tüm liste), "track" (bu şarkı); uygulama bildirmiyorsa null.</summary>
        public string Repeat;
        /// <summary>Döngü değiştirilebilir mi (Windows medya denetimiyle ya da Spotify kısayoluyla).</summary>
        public bool CanRepeat;

        public TimeSpan LivePosition
        {
            get
            {
                var p = IsPlaying ? Position + (DateTime.Now - PositionAt) : Position;
                return Duration > TimeSpan.Zero && p > Duration ? Duration : p;
            }
        }
    }

    /// <summary>Windows medya oturumlarını (SMTC) arka planda izler.</summary>
    internal static class MediaWatcher
    {
        public static NowPlaying Current { get; private set; }
        public static event Action Changed;

        static GlobalSystemMediaTransportControlsSessionManager _mgr;
        static string _artKey;
        static ImageSource _art;
        static Color _artColor = Color.FromRgb(139, 108, 255);
        static bool _started;

        static T Wait<T>(IAsyncOperation<T> op, int timeoutMs = 3000)
        {
            var end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (op.Status == AsyncStatus.Started && DateTime.Now < end) Thread.Sleep(10);
            if (op.Status != AsyncStatus.Completed) throw new TimeoutException("WinRT: " + op.Status);
            return op.GetResults();
        }

        public static void Start()
        {
            if (_started) return;
            _started = true;
            var t = new Thread(Loop) { IsBackground = true, Name = "MediaWatcher" };
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
        }

        static void Loop()
        {
            while (true)
            {
                NowPlaying np = null;
                try { np = Read(); }
                catch { _mgr = null; }
                if (!Same(np, Current))
                {
                    Current = np;
                    var app = Application.Current;
                    var h = Changed;
                    if (app != null && h != null) app.Dispatcher.BeginInvoke(h);
                }
                else if (np != null) Current = np; // konum güncellemesi (sessizce)
                // Pil: bir şey çalarken sık (0,7 sn), çalmıyorken seyrek (2 sn) bakılır; komut gelince Poke ile hemen uyanır
                _wake.WaitOne(Current != null && Current.IsPlaying ? 700 : 2000);
            }
        }

        static bool Same(NowPlaying a, NowPlaying b)
        {
            if (a == null || b == null) return a == b;
            return a.Title == b.Title && a.Artist == b.Artist && a.IsPlaying == b.IsPlaying && a.App == b.App && a.Art == b.Art &&
                   Math.Abs((a.LivePosition - b.LivePosition).TotalSeconds) < 2.5;
        }

        static GlobalSystemMediaTransportControlsSession Session()
        {
            if (_mgr == null) _mgr = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            var s = _mgr.GetCurrentSession();
            if (s == null)
            {
                foreach (var x in _mgr.GetSessions())
                    if (x.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) { s = x; break; }
            }
            return s;
        }

        static NowPlaying Read()
        {
            var s = Session();
            if (s == null) return null;
            var props = Wait(s.TryGetMediaPropertiesAsync());
            if (props == null || string.IsNullOrWhiteSpace(props.Title)) return null;
            var info = s.GetPlaybackInfo();
            var tl = s.GetTimelineProperties();

            string key = s.SourceAppUserModelId + "|" + props.Title + "|" + props.Artist;
            if (key != _artKey)
            {
                _artKey = key;
                _art = null;
                try { if (props.Thumbnail != null) LoadArt(props.Thumbnail); } catch { _art = null; }
            }

            return new NowPlaying
            {
                Title = props.Title,
                Artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist : props.Artist,
                App = AppName(s.SourceAppUserModelId),
                Aumid = s.SourceAppUserModelId,
                IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Position = tl.Position,
                Duration = tl.EndTime - tl.StartTime,
                PositionAt = tl.LastUpdatedTime.LocalDateTime > DateTime.Now.AddDays(-1) ? tl.LastUpdatedTime.LocalDateTime : DateTime.Now,
                Art = _art,
                ArtColor = _artColor,
                Repeat = info.Controls.IsRepeatEnabled && info.AutoRepeatMode != null ? RepeatName(info.AutoRepeatMode.Value) : (AppName(s.SourceAppUserModelId) == "Spotify" ? _spotifyRepeat : null),
                CanRepeat = info.Controls.IsRepeatEnabled || AppName(s.SourceAppUserModelId) == "Spotify"
            };
        }

        static void LoadArt(IRandomAccessStreamReference reference)
        {
            using (var stream = Wait(reference.OpenReadAsync()))
            {
                uint size = (uint)Math.Min(stream.Size, 8 * 1024 * 1024);
                var reader = new DataReader(stream.GetInputStreamAt(0));
                Wait(reader.LoadAsync(size));
                var bytes = new byte[size];
                reader.ReadBytes(bytes);
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.DecodePixelWidth = 160;
                img.StreamSource = new MemoryStream(bytes);
                img.EndInit();
                img.Freeze();
                _art = img;
                _artColor = Dominant(img);
            }
        }

        /// <summary>Kapak görselinin canlı baskın rengi (ada içindeki dalga çubukları için).</summary>
        static Color Dominant(BitmapSource img)
        {
            try
            {
                var small = new FormatConvertedBitmap(new TransformedBitmap(img, new ScaleTransform(16.0 / img.PixelWidth, 16.0 / img.PixelHeight)), PixelFormats.Bgra32, null, 0);
                int w = small.PixelWidth, h = small.PixelHeight;
                var px = new byte[w * h * 4];
                small.CopyPixels(px, w * 4, 0);
                double r = 0, g = 0, b = 0, wsum = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    double B = px[i], G = px[i + 1], R = px[i + 2];
                    double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
                    double weight = (max - min) / 255.0 + 0.05; // doygun pikseller daha ağır basar
                    r += R * weight; g += G * weight; b += B * weight; wsum += weight;
                }
                r /= wsum; g /= wsum; b /= wsum;
                // Siyah zemin üzerinde okunabilsin diye parlaklaştır
                double mx = Math.Max(r, Math.Max(g, b));
                double k = mx < 1 ? 1 : Math.Max(1, 210 / mx);
                return Color.FromRgb((byte)Math.Min(255, r * k), (byte)Math.Min(255, g * k), (byte)Math.Min(255, b * k));
            }
            catch { return Color.FromRgb(139, 108, 255); }
        }

        static string AppName(string aumid)
        {
            string a = (aumid ?? "").ToLowerInvariant();
            if (a.Contains("spotify")) return "Spotify";
            if (a.Contains("chrome")) return "Chrome";
            if (a.Contains("msedge") || a.Contains("edge")) return "Edge";
            if (a.Contains("firefox")) return "Firefox";
            if (a.Contains("opera")) return "Opera";
            if (a.Contains("brave")) return "Brave";
            if (a.Contains("zunemusic") || a.Contains("media player")) return "Medya Oynatıcı";
            if (a.Contains("vlc")) return "VLC";
            if (a.Contains("youtube")) return "YouTube";
            string n = aumid ?? "";
            int bang = n.IndexOf('!');
            if (bang > 0) n = n.Substring(0, bang);
            n = Path.GetFileNameWithoutExtension(n);
            return n.Length > 0 ? char.ToUpper(n[0]) + n.Substring(1) : "Medya";
        }

        // ---- Kontroller ----
        /// <summary>Komutun gideceği oturum: adada gösterilen uygulamanınki (Windows'un "geçerli" saydığı başka bir oturum olabilir).</summary>
        static GlobalSystemMediaTransportControlsSession Target()
        {
            if (_mgr == null) _mgr = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            var cur = Current;
            if (cur != null && cur.Aumid != null)
                foreach (var x in _mgr.GetSessions())
                    if (x.SourceAppUserModelId == cur.Aumid) return x;
            return Session();
        }

        /// <summary>Oturuma komut gönderir; oturum yoksa, komutu desteklemiyorsa ya da reddederse medya tuşuna düşer.</summary>
        static void Control(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> op,
                            Func<GlobalSystemMediaTransportControlsSessionPlaybackControls, bool> supported, Action fallback, int times = 1)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                try
                {
                    var s = Target();
                    if (s != null && supported(s.GetPlaybackInfo().Controls))
                    {
                        for (int i = 0; i < times; i++)
                        {
                            ok = Wait(op(s));
                            if (!ok) break;
                            if (i + 1 < times) Thread.Sleep(180);
                        }
                    }
                }
                catch { ok = false; }
                if (!ok) fallback();
                // Ada yeni şarkıyı beklemeden göstersin
                Thread.Sleep(350);
                _wake.Set();
            });
        }

        static readonly AutoResetEvent _wake = new AutoResetEvent(false);

        /// <summary>Beklemeden hemen yeniden oku (ör. müzik az önce başlatıldı).</summary>
        public static void Poke() { _wake.Set(); }

        /// <summary>Şarkıyı/videoyu verilen konuma sarar; uygulama desteklemiyorsa done(false).</summary>
        public static void Seek(TimeSpan pos, Action<bool> done)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                try
                {
                    var s = Target();
                    if (s != null && s.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled)
                    {
                        var tl = s.GetTimelineProperties();
                        ok = Wait(s.TryChangePlaybackPositionAsync((tl.StartTime + pos).Ticks));
                    }
                }
                catch { ok = false; }
                if (done != null) done(ok);
                Thread.Sleep(300);
                _wake.Set();
            });
        }

        static string RepeatName(Windows.Media.MediaPlaybackAutoRepeatMode m)
        {
            return m == Windows.Media.MediaPlaybackAutoRepeatMode.Track ? "track" : m == Windows.Media.MediaPlaybackAutoRepeatMode.List ? "list" : "none";
        }

        // Spotify döngü durumunu Windows'a bildirmezse kısayolla değiştirilir ve durum burada tutulur (Spotify'daki sıra: kapalı → liste → şarkı)
        static string _spotifyRepeat = "none";

        /// <summary>
        /// Döngü modunu sırayla değiştirir: kapalı → tüm liste → bu şarkı → kapalı. Uygulama Windows medya denetimiyle destekliyorsa
        /// oradan; Spotify desteklemiyorsa Spotify'ın kendi Ctrl+R kısayoluyla. done: yeni durum (başarısızsa null).
        /// </summary>
        public static void ToggleRepeat(Action<string> done)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string result = null;
                try
                {
                    var s = Target();
                    var info = s != null ? s.GetPlaybackInfo() : null;
                    if (info != null && info.Controls.IsRepeatEnabled)
                    {
                        var cur = info.AutoRepeatMode != null ? info.AutoRepeatMode.Value : Windows.Media.MediaPlaybackAutoRepeatMode.None;
                        var next = cur == Windows.Media.MediaPlaybackAutoRepeatMode.None ? Windows.Media.MediaPlaybackAutoRepeatMode.List
                                 : cur == Windows.Media.MediaPlaybackAutoRepeatMode.List ? Windows.Media.MediaPlaybackAutoRepeatMode.Track
                                 : Windows.Media.MediaPlaybackAutoRepeatMode.None;
                        if (Wait(s.TryChangeAutoRepeatModeAsync(next))) result = RepeatName(next);
                    }
                    else if (Current != null && Current.App == "Spotify" && Spotify.SendShortcut(0x52 /* R */))
                    {
                        _spotifyRepeat = _spotifyRepeat == "none" ? "list" : _spotifyRepeat == "list" ? "track" : "none";
                        result = _spotifyRepeat;
                    }
                }
                catch { result = null; }
                if (done != null) done(result);
                Thread.Sleep(300);
                _wake.Set();
            });
        }

        public static void PlayPause() { Control(s => s.TryTogglePlayPauseAsync(), c => c.IsPlayPauseToggleEnabled, Media.PlayPause); }
        public static void Next() { Control(s => s.TrySkipNextAsync(), c => c.IsNextEnabled, Media.Next); }

        public static void Prev()
        {
            // Spotify şarkının birkaç saniyesi geçtiyse "önceki" ile önce şarkıyı başa sarar; gerçekten önceki şarkıya geçmek için iki kez gönder
            var cur = Current;
            int times = cur != null && cur.App == "Spotify" && cur.LivePosition.TotalSeconds > 3 ? 2 : 1;
            Control(s => s.TrySkipPreviousAsync(), c => c.IsPreviousEnabled, Media.Prev, times);
        }
    }
}
