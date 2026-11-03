using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Text.RegularExpressions;
using System.Threading;
using Windows.Foundation;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

namespace Pardux
{
    /// <summary>
    /// Pardux'un sesli yanıtları (Windows'un Türkçe sesi, ör. "Tolga"). Konuşurken mikrofon kendi sesini duymasın diye
    /// dinleme o süre boyunca susturulur.
    /// </summary>
    internal static class Speech
    {
        static readonly object Gate = new object();
        static SoundPlayer _player;

        static T Wait<T>(IAsyncOperation<T> op)
        {
            var end = DateTime.Now.AddSeconds(10);
            while (op.Status == AsyncStatus.Started && DateTime.Now < end) Thread.Sleep(10);
            if (op.Status != AsyncStatus.Completed) throw new TimeoutException("TTS");
            return op.GetResults();
        }

        static bool Enabled
        {
            get
            {
                var c = Controller.Instance;
                return !IslandWindow.TestMode && c != null && c.Settings != null && c.Settings.VoiceReplies;
            }
        }

        /// <summary>Metni sesli okur (arka planda). delayMs: önce çalan efekt sesinin bitmesini bekle.</summary>
        public static void Say(string text, int delayMs = 0)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(text)) return;
            // Daha konuşma gibi okunsun: parantez içlerini ve adresleri atla
            text = Regex.Replace(text, @"\([^)]*\)|https?://\S+", "").Trim();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (delayMs > 0) Thread.Sleep(delayMs);
                    byte[] wav = Synthesize(text);
                    if (wav == null) return;
                    // WAV başlığından süre: konuşma boyunca (+ yankı payı) mikrofonu yok say
                    int byteRate = BitConverter.ToInt32(wav, 28);
                    double seconds = byteRate > 0 ? (wav.Length - 44) / (double)byteRate : 2;
                    Voice.MuteFor(TimeSpan.FromSeconds(seconds + 0.45));
                    lock (Gate)
                    {
                        _player = new SoundPlayer(new MemoryStream(wav));
                        _player.PlaySync();
                    }
                }
                catch { }
            });
        }

        // Sık söylenen kısa cümleler ("Dinliyorum efendim") bellekte tutulur: anında çalar
        static readonly Dictionary<string, byte[]> Cache = new Dictionary<string, byte[]>();
        static readonly Queue<string> CacheOrder = new Queue<string>();

        static bool UseNatural
        {
            get
            {
                var c = Controller.Instance;
                return Piper.Available && (c == null || c.Settings == null || c.Settings.VoiceEngine != "windows");
            }
        }

        /// <summary>Seçili sesle (doğal Piper, yoksa Windows sesi) WAV üretir.</summary>
        internal static byte[] Synthesize(string text)
        {
            bool natural = UseNatural;
            string key = (natural ? "p|" : "w|") + text;
            lock (Cache) { byte[] hit; if (Cache.TryGetValue(key, out hit)) return hit; }
            byte[] wav = natural ? Piper.Synthesize(text) : null;
            if (wav == null) wav = SynthesizeWindows(text);
            if (wav != null && text.Length <= 48)
                lock (Cache)
                {
                    if (!Cache.ContainsKey(key)) { Cache[key] = wav; CacheOrder.Enqueue(key); }
                    while (CacheOrder.Count > 24) Cache.Remove(CacheOrder.Dequeue());
                }
            return wav;
        }

        /// <summary>Uygulama açılırken en sık cümleleri hazırlar (ilk "Hey Pardux"ta gecikme olmasın).</summary>
        public static void Prepare()
        {
            if (!Enabled) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { foreach (var t in new[] { "Dinliyorum efendim", "Seni duyamadım" }) Synthesize(t); } catch { }
            });
        }

        static SpeechSynthesizer _synth;

        /// <summary>Windows'un yerleşik Türkçe sesi (Tolga); Piper yoksa ya da ayarlardan seçildiyse.</summary>
        internal static byte[] SynthesizeWindows(string text)
        {
            if (_synth == null)
            {
                _synth = new SpeechSynthesizer();
                var tr = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("tr", StringComparison.OrdinalIgnoreCase));
                if (tr == null) return null; // Türkçe ses yüklü değil
                _synth.Voice = tr;
                try { _synth.Options.SpeakingRate = 1.08; } catch { }
            }
            var stream = Wait(_synth.SynthesizeTextToStreamAsync(TrSpeech.Normalize(text)));
            var reader = new DataReader(stream.GetInputStreamAt(0));
            Wait(reader.LoadAsync((uint)stream.Size));
            var data = new byte[stream.Size];
            reader.ReadBytes(data);
            return data;
        }

        // ---------------- Komuttan konuşma cümlesi ----------------
        static readonly Dictionary<string, string> Verbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "aç", "açıyorum" }, { "başlat", "başlatıyorum" }, { "kapat", "kapatıyorum" }, { "duraklat", "duraklatıyorum" },
            { "sıfırla", "sıfırlıyorum" }, { "kur", "kuruyorum" }, { "ara", "arıyorum" }, { "göster", "gösteriyorum" },
            { "gönder", "gönderiyorum" }, { "çal", "çalıyorum" }, { "yaz", "yazıyorum" }, { "al", "alıyorum" }, { "kilitle", "kilitliyorum" },
            { "durdur", "durduruyorum" }, { "ettir", "ettiriyorum" }, { "et", "ediyorum" }, { "geç", "geçiyorum" }, { "boşalt", "boşaltıyorum" },
            { "yenile", "yeniliyorum" }, { "kopyala", "kopyalıyorum" }, { "kaydet", "kaydediyorum" }, { "temizle", "temizliyorum" }
        };

        /// <summary>Çalıştırılan sonuçtan kısa bir sözlü onay üretir: "Kronometreyi başlat" → "Kronometreyi başlatıyorum".</summary>
        public static string Confirm(ResultItem it)
        {
            if (it == null) return null;
            if (!string.IsNullOrEmpty(it.Speak)) return it.Speak;
            string title = (it.Title ?? "").Split(new[] { ':', '·', '→' }, 2)[0].Trim();
            if (it.Subtitle == "Uygulama" || it.Subtitle == "Sık kullandığın") return title + " açılıyor";
            var words = title.Split(' ').ToList();
            string last = words.Count > 0 ? words[words.Count - 1] : "";
            string cont;
            if (words.Count > 0 && Verbs.TryGetValue(last, out cont))
            {
                words[words.Count - 1] = cont;
                string s = string.Join(" ", words);
                return char.ToUpper(s[0], TextUtil.Tr) + s.Substring(1);
            }
            return "Tamam";
        }
    }
}
