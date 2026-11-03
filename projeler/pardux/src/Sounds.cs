using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Threading;

namespace Pardux
{
    public enum Sound { Wake, Listen, Notify, News, Alarm, Success, Error, Open }

    /// <summary>
    /// Pardux'un kendi sesleri. Dosya kullanılmaz: her ses ilk çalınışta küçük bir sentezleyiciyle üretilir
    /// (cam çan / marimba tınısı, yumuşak atak, hafif stereo genişlik ve oda yankısı), 44.1 kHz 16 bit stereo WAV olarak bellekte tutulur.
    /// </summary>
    internal static class Sounds
    {
        const int Rate = 44100;
        static readonly Dictionary<Sound, byte[]> Cache = new Dictionary<Sound, byte[]>();
        static int _cacheVolume = -1;
        static SoundPlayer _player;
        static readonly object Gate = new object();

        // ---- Kullanıcının kendi sesleri (exe yanındaki sounds klasörü): uyanış ve tüm bildirimler ----
        static readonly Dictionary<string, System.Windows.Media.MediaPlayer> Players = new Dictionary<string, System.Windows.Media.MediaPlayer>();

        static string FileFor(Sound s)
        {
            switch (s)
            {
                case Sound.Wake: return "uyanis.mp3";
                // Bildirim, haber, alarm ve başarı sesleri artık Pardux'un kendi "çan" tınısıyla üretilir (bildirim.mp3 kullanılmaz)
                default: return null;
            }
        }

        static string SoundPath(string file)
        {
            if (file == null) return null;
            string p = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "sounds", file);
            return File.Exists(p) ? p : null;
        }

        /// <summary>MP3'ü hazırda tutulan oynatıcıyla çalar (UI iş parçacığında; ilk çalışta gecikme olmasın diye önceden açılır).</summary>
        static bool PlayFile(Sound s, int vol)
        {
            string path = SoundPath(FileFor(s));
            var app = System.Windows.Application.Current;
            if (path == null || app == null) return false;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                System.Windows.Media.MediaPlayer mp;
                if (!Players.TryGetValue(path, out mp))
                {
                    mp = new System.Windows.Media.MediaPlayer();
                    mp.Open(new Uri(path));
                    Players[path] = mp;
                }
                mp.Volume = vol / 100.0;
                mp.Stop();
                mp.Position = TimeSpan.Zero;
                mp.Play();
            }));
            return true;
        }

        public static void Play(Sound s)
        {
            if (IslandWindow.TestMode) return;
            var c = Controller.Instance;
            if (c == null || c.Settings == null || !c.Settings.Sounds) return;
            int vol = Math.Max(0, Math.Min(100, c.Settings.SoundVolume));
            if (vol == 0) return;
            if (PlayFile(s, vol)) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    byte[] wav = Get(s, vol);
                    lock (Gate)
                    {
                        // Player alanda tutulur: tampon çalarken çöp toplayıcı onu silmesin
                        _player = new SoundPlayer(new MemoryStream(wav));
                        _player.Play();
                    }
                }
                catch { }
            });
        }

        /// <summary>Açılışta sesleri arka planda hazırlar (ilk çalışta gecikme olmasın).</summary>
        public static void Warm()
        {
            var c = Controller.Instance;
            int vol = c != null && c.Settings != null ? c.Settings.SoundVolume : 70;
            ThreadPool.QueueUserWorkItem(_ => { foreach (Sound s in Enum.GetValues(typeof(Sound))) try { Get(s, vol); } catch { } });
            // Kendi ses dosyalarını önceden aç
            foreach (string f in new[] { "uyanis.mp3" })
            {
                string p = SoundPath(f);
                if (p == null || Players.ContainsKey(p)) continue;
                var mp = new System.Windows.Media.MediaPlayer();
                mp.Open(new Uri(p));
                Players[p] = mp;
            }
        }

        static byte[] Get(Sound s, int vol)
        {
            lock (Cache)
            {
                if (vol != _cacheVolume) { Cache.Clear(); _cacheVolume = vol; }
                byte[] b;
                if (Cache.TryGetValue(s, out b)) return b;
                b = Render(s, vol / 100.0);
                Cache[s] = b;
                return b;
            }
        }

        // ---------------- Sentez ----------------
        enum Timbre { Glass, Marimba, Soft, Bell }

        static byte[] Render(Sound s, double volume)
        {
            double len;
            var notes = Score(s, out len);
            int n = (int)((len + 0.9) * Rate); // yankı kuyruğu için pay
            var L = new float[n];
            var R = new float[n];
            foreach (var x in notes) Tone(L, R, x.Item1, x.Item2, x.Item3, x.Item4, x.Item5);
            Reverb(L, R, s == Sound.Alarm ? 0.22 : 0.30);
            Soften(L); Soften(R);
            return Wav(L, R, volume * (s == Sound.Open || s == Sound.Listen ? 0.45 : 0.62));
        }

        static double Note(string name)
        {
            // "A5", "C#6" → frekans (A4 = 440 Hz)
            string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            string pitch = name.Substring(0, name.Length - 1);
            int octave = name[name.Length - 1] - '0';
            int semis = Array.IndexOf(names, pitch) + (octave - 4) * 12 - 9;
            return 440.0 * Math.Pow(2, semis / 12.0);
        }

        /// <summary>Her ses için notalar: (başlangıç sn, frekans, genlik, süre sn, tını).</summary>
        static List<Tuple<double, double, double, double, Timbre>> Score(Sound s, out double length)
        {
            var l = new List<Tuple<double, double, double, double, Timbre>>();
            Action<double, string, double, double, Timbre> add = (t, note, amp, dur, tb) => l.Add(Tuple.Create(t, Note(note), amp, dur, tb));
            switch (s)
            {
                case Sound.Wake:      // yükselen iki cam nota: "dinliyorum"
                    add(0.00, "E5", 0.80, 0.9, Timbre.Glass);
                    add(0.10, "B5", 0.75, 1.1, Timbre.Glass);
                    add(0.10, "E6", 0.18, 0.8, Timbre.Glass);
                    length = 1.2; break;
                case Sound.Listen:    // komut alındı: tek yumuşak damla
                    add(0.00, "G5", 0.9, 0.35, Timbre.Soft);
                    length = 0.4; break;
                case Sound.Notify:    // iki notalı yumuşak çan (beşli aralık) + üstte ince bir parıltı: kısa, temiz, dikkat çekici ama rahatsız etmeyen
                    add(0.00, "F#5", 0.70, 1.1, Timbre.Bell);
                    add(0.105, "C#6", 0.80, 1.4, Timbre.Bell);
                    add(0.105, "C#7", 0.07, 0.6, Timbre.Glass);
                    length = 1.45; break;
                case Sound.News:      // aynı çan, inen: "haber var"
                    add(0.00, "C#6", 0.75, 1.1, Timbre.Bell);
                    add(0.12, "F#5", 0.80, 1.4, Timbre.Bell);
                    length = 1.5; break;
                case Sound.Alarm:     // çan arpeji üç tur (zamanlayıcı bitti): belirgin ama yumuşak
                    for (int r = 0; r < 3; r++)
                    {
                        double o = r * 0.78;
                        add(o + 0.00, "F#5", 0.75, 0.7, Timbre.Bell);
                        add(o + 0.11, "A#5", 0.75, 0.7, Timbre.Bell);
                        add(o + 0.22, "C#6", 0.85, 0.9, Timbre.Bell);
                    }
                    length = 2.6; break;
                case Sound.Success:   // hızlı yükselen üçlü çan: "tamam"
                    add(0.00, "C#6", 0.55, 0.8, Timbre.Bell);
                    add(0.07, "F#6", 0.60, 0.9, Timbre.Bell);
                    add(0.07, "F#7", 0.05, 0.5, Timbre.Glass);
                    length = 1.1; break;
                case Sound.Error:     // alçak, inen iki nota
                    add(0.00, "E4", 0.8, 0.5, Timbre.Soft);
                    add(0.13, "C4", 0.8, 0.7, Timbre.Soft);
                    length = 0.9; break;
                default:              // Open: çok hafif "tık"
                    add(0.00, "E6", 0.5, 0.18, Timbre.Soft);
                    length = 0.2; break;
            }
            return l;
        }

        static void Tone(float[] L, float[] R, double t0, double freq, double amp, double dur, Timbre tb)
        {
            if (tb == Timbre.Bell) { Bell(L, R, t0, freq, amp, dur); return; }
            // (oran, genlik, sönüm hızı)
            double[,] partials = tb == Timbre.Glass
                ? new double[,] { { 1, 1.0, 3.0 }, { 2.0, 0.32, 5.0 }, { 3.01, 0.16, 8.0 }, { 4.17, 0.07, 11.0 }, { 5.43, 0.035, 15.0 } }
                : tb == Timbre.Marimba
                ? new double[,] { { 1, 1.0, 6.0 }, { 3.98, 0.30, 18.0 }, { 9.1, 0.08, 34.0 } }
                : new double[,] { { 1, 1.0, 7.0 }, { 2.0, 0.12, 12.0 } };
            int start = (int)(t0 * Rate), count = (int)(dur * Rate);
            double scale = 1.0 / Math.Max(0.35, dur); // kısa notalar daha hızlı söner
            for (int i = 0; i < count && start + i < L.Length; i++)
            {
                double t = (double)i / Rate;
                double attack = 1 - Math.Exp(-t / 0.0035);
                double release = i > count - 900 ? (count - i) / 900.0 : 1; // son ~20 ms yumuşak bitiş
                double l = 0, r = 0;
                for (int p = 0; p < partials.GetLength(0); p++)
                {
                    double f = freq * partials[p, 0];
                    if (f > 16000) continue;
                    double env = partials[p, 1] * Math.Exp(-partials[p, 2] * scale * t * 0.6);
                    // hafif ters akort: sol ve sağ kanal arası genişlik
                    l += env * Math.Sin(2 * Math.PI * f * 0.9985 * t);
                    r += env * Math.Sin(2 * Math.PI * f * 1.0015 * t + 0.3);
                }
                double a = amp * attack * release;
                L[start + i] += (float)(l * a);
                R[start + i] += (float)(r * a);
            }
        }

        /// <summary>
        /// Premium çan tınısı (FM sentezi): taşıyıcı + 3,5 oranında, hızla sönen bir modülatör. Vuruş anında parlak, hemen ardından
        /// saf ve sıcak bir tona yerleşir; altta oktav aşağısında hafif bir gövde ve vuruşta çok kısa yumuşak bir "tık" vardır.
        /// </summary>
        static void Bell(float[] L, float[] R, double t0, double freq, double amp, double dur)
        {
            int start = (int)(t0 * Rate), count = (int)(dur * Rate);
            var rnd = new Random((int)freq);
            float lp = 0;
            for (int i = 0; i < count && start + i < L.Length; i++)
            {
                double t = (double)i / Rate;
                double attack = 1 - Math.Exp(-t / 0.0022);
                double release = i > count - 1500 ? (count - i) / 1500.0 : 1;
                double env = Math.Exp(-t * 3.2 / Math.Max(0.5, dur));            // ana ses
                double index = 1.6 * Math.Exp(-t / 0.075);                        // parlaklık: ilk ~75 ms
                double body = 0.16 * Math.Exp(-t * 9);                             // oktav altı gövde
                double l = 0, r = 0;
                for (int side = 0; side < 2; side++)
                {
                    double det = side == 0 ? 0.9992 : 1.0008, ph = side == 0 ? 0 : 0.35;
                    double f = freq * det;
                    double mod = index * Math.Sin(2 * Math.PI * f * 3.5 * t);
                    double v = env * Math.Sin(2 * Math.PI * f * t + mod + ph)
                             + 0.22 * env * Math.Exp(-t * 2.5) * Math.Sin(2 * Math.PI * f * 2.0 * t + ph)   // ikinci harmonik
                             + body * Math.Sin(2 * Math.PI * f * 0.5 * t);
                    if (side == 0) l = v; else r = v;
                }
                // Vuruş anındaki çok kısa, süzülmüş gürültü ("tokmak" hissi)
                if (t < 0.006)
                {
                    lp += (float)(((rnd.NextDouble() * 2 - 1) - lp) * 0.35);
                    double n = lp * 0.35 * (1 - t / 0.006);
                    l += n; r += n;
                }
                double a = amp * attack * release;
                L[start + i] += (float)(l * a);
                R[start + i] += (float)(r * a);
            }
        }

        /// <summary>Hafif tiz yumuşatma (tek kutuplu alçak geçiren, ~9 kHz): dijital sertliği alır.</summary>
        static void Soften(float[] x)
        {
            double a = 1 - Math.Exp(-2 * Math.PI * 9000.0 / Rate), y = 0;
            for (int i = 0; i < x.Length; i++) { y += (x[i] - y) * a; x[i] = (float)y; }
        }

        /// <summary>Freeverb benzeri küçük oda yankısı (4 tarak + 2 tüm geçiren süzgeç, kanal başına).</summary>
        static void Reverb(float[] L, float[] R, double wet)
        {
            ReverbChannel(L, wet, 0);
            ReverbChannel(R, wet, 23);
        }

        static void ReverbChannel(float[] x, double wet, int spread)
        {
            int[] combs = { 1116 + spread, 1188 + spread, 1277 + spread, 1356 + spread };
            int[] alls = { 556 + spread, 441 + spread };
            var outp = new float[x.Length];
            foreach (int d in combs)
            {
                var buf = new float[d];
                int idx = 0;
                float store = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    float o = buf[idx];
                    store = o * 0.75f + store * 0.25f; // sönümleme (tizler daha çabuk söner)
                    buf[idx] = x[i] + store * 0.80f;
                    outp[i] += o;
                    if (++idx >= d) idx = 0;
                }
            }
            foreach (int d in alls)
            {
                var buf = new float[d];
                int idx = 0;
                for (int i = 0; i < outp.Length; i++)
                {
                    float b = buf[idx];
                    float o = -outp[i] + b;
                    buf[idx] = outp[i] + b * 0.5f;
                    outp[i] = o;
                    if (++idx >= d) idx = 0;
                }
            }
            for (int i = 0; i < x.Length; i++) x[i] = (float)(x[i] * (1 - wet * 0.5) + outp[i] * wet * 0.25);
        }

        static byte[] Wav(float[] L, float[] R, double volume)
        {
            float peak = 0.0001f;
            for (int i = 0; i < L.Length; i++) peak = Math.Max(peak, Math.Max(Math.Abs(L[i]), Math.Abs(R[i])));
            double g = volume / peak;
            // Sondaki sessizliği kırp
            int n = L.Length;
            while (n > Rate / 10 && Math.Abs(L[n - 1] * g) < 0.0004 && Math.Abs(R[n - 1] * g) < 0.0004) n--;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int data = n * 4;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + data); w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(data);
                for (int i = 0; i < n; i++)
                {
                    double fade = i > n - 2205 ? (n - i) / 2205.0 : 1;
                    w.Write((short)Math.Max(-32767, Math.Min(32767, L[i] * g * fade * 32767)));
                    w.Write((short)Math.Max(-32767, Math.Min(32767, R[i] * g * fade * 32767)));
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Test için: sesi WAV dosyası olarak yazar.</summary>
        public static void Export(Sound s, string path) { File.WriteAllBytes(path, Render(s, 0.7)); }
    }
}
