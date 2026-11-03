using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Pardux
{
    /// <summary>
    /// "Hey Pardux" sesli uyandırma ve sesli komut. Tamamen çevrimdışı: Vosk + Türkçe model (exe yanındaki vosk klasörü).
    /// Mikrofon Windows waveIn ile 16 kHz tek kanal okunur; ses hiçbir yere gönderilmez.
    /// Akış: uyandırma sözcüğü beklenir → duyulunca Woke → konuşma bitene (ya da 8 sn) kadar dinlenir → Command(metin).
    /// </summary>
    internal static class Voice
    {
        public static event Action Woke;
        public static event Action<string> Partial;
        public static event Action<string> Command;
        /// <summary>Söz bitti, Whisper yazıya döküyor.</summary>
        public static event Action Thinking;

        static string Dir { get { return Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "vosk"); } }
        static string ModelDir { get { return Path.Combine(Dir, "model-tr"); } }

        public static bool Available
        {
            get { return Environment.Is64BitProcess && File.Exists(Path.Combine(Dir, "libvosk.dll")) && File.Exists(Path.Combine(ModelDir, "final.mdl")); }
        }

        public static bool Running { get { return _thread != null; } }
        public static string LastError { get; private set; }

        static Thread _thread;
        static volatile bool _stop;
        static IntPtr _model;

        public static void Start()
        {
            if (_thread != null || !Available) return;
            _stop = false;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Ses", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        public static void Stop()
        {
            _stop = true;
            _thread = null;
        }

        // Kısıtlı tanıyıcı tek başına adı benzer sözcüklerde de duyabiliyor; "hey" ile ad birlikte gelmeli
        static readonly Regex WakeRx = new Regex(@"\bhey (pardux|parduks|pardoks|par ?du[kx]s?|pardon|paradoks)\b", RegexOptions.Compiled);

        // Vosk'un Türkçe sözlüğünde "pardux" yok: ad, sözlükteki en yakın ses dizileriyle aranır ("par duk", "par dük" ≈ "parduks").
        // "pardon" ve "paradoks" da hızlı söylenen "pardux"a çok yakın; uyanma ayrıca Whisper'ın yazdığıyla doğrulanır
        const string WakeGrammar = "[\"hey par duk\", \"hey par dük\", \"hey pardon\", \"hey paradoks\", \"[unk]\"]";
        static readonly HashSet<string> NameParts = new HashSet<string> { "par", "duk", "dük", "pardon", "paradoks" };

        public static bool IsWake(string text) { return WakeRx.IsMatch(TextUtil.Norm(text ?? "")); }

        // ---- Dinleme durumu (ses örnek sayısına göre zamanlanır; mikrofon ya da test dosyası aynı yoldan geçer) ----
        static IntPtr _wake, _cmd;
        static bool _listening;
        static double _clock, _since, _lastSpeech;
        static string _heard = "";

        static void Init()
        {
            SetDllDirectory(Dir);
            vosk_set_log_level(-1);
            if (_model == IntPtr.Zero) _model = vosk_model_new(Utf8(ModelDir));
            if (_model == IntPtr.Zero) throw new Exception("Ses modeli yüklenemedi");
            // Uyandırma için yalnızca "hey pardux" sözcüklerini bilen kısıtlı tanıyıcı: hem hafif çalışır hem de yanlış uyanmayı azaltır
            if (_wake == IntPtr.Zero) _wake = vosk_recognizer_new_grm(_model, 16000f, Utf8(WakeGrammar));
            if (_cmd == IntPtr.Zero) _cmd = vosk_recognizer_new(_model, 16000f);
            vosk_recognizer_set_words(_wake, 1);
            vosk_recognizer_set_words(_cmd, 1);
            vosk_recognizer_reset(_wake);
            vosk_recognizer_reset(_cmd);
            _listening = false;
            _clock = 0;
            _wakeBase = _cmdBase = _recycledAt = 0;
            _gated = false; _quietChunks = 0; _ring.Clear(); _ringVoice.Clear(); _noise = 200;
        }

        /// <summary>Bir ses parçasını işler: uyandırma bekleniyorsa onu arar, dinleniyorsa komutu toplar.</summary>
        static void Feed(byte[] chunk)
        {
            _clock += chunk.Length / 32000.0;
            // Pardux konuşurken mikrofon kendi sesini duymasın: o süre boyunca ses atılır, dinleme süresi baştan sayılır
            if (DateTime.UtcNow < _muteUntil)
            {
                if (_listening) { _since = _lastSpeech = _clock; if (_cmdAudio != null && _cmdAudio.Length > 0) { _cmdAudio.SetLength(0); _audioHasWake = false; } }
                _muteEnded = true;
                return;
            }
            if (_muteEnded)
            {
                _muteEnded = false;
                if (_listening) { vosk_recognizer_reset(_cmd); _cmdBase = _clock; _cmdFrom = 0; _heard = ""; _since = _lastSpeech = _clock; }
            }
            if (!_listening)
            {
                // Son 2,5 sn hep saklanır: "hey pardux, müzik aç" tek nefeste söylenince komutun başı kaybolmasın
                bool voice = IsVoice(chunk); // ortam gürültüsü düzeyini de izler
                _ring.Enqueue((byte[])chunk.Clone());
                _ringVoice.Enqueue(voice);
                while (_ring.Count > 40) { _ring.Dequeue(); _ringVoice.Dequeue(); } // son 4 sn
                // Pil: ortam sessizken (0,8 sn'den uzun) tanıyıcı hiç çalıştırılmaz; ses gelince son 0,3 sn ile birlikte yeniden başlar.
                // Beklerken yalnızca "hey pardux"u arayan küçük tanıyıcı çalışır (genel tanıyıcı yalnızca uyandıktan sonra).
                _quietChunks = _lastRms > Math.Max(90, _noise * 1.6) ? 0 : _quietChunks + 1; // en ufak ses bile (kısık mikrofon) uyandırır
                string json;
                if (TestSkip == 0 && _quietChunks > 8)
                {
                    if (_gated)
                    {
                        if (_clock - _recycledAt > 120) Recycle();
                        return;
                    }
                    // Sessizlik başladı: tanıyıcıda bekleyen söz atılmadan sonuçlandırılır. "Hey Pardux" deyip duraklayınca
                    // tanıyıcının kendi cümle sonu algısı bu kapıdan daha geç geliyordu ve uyanma hiç görülmüyordu
                    _gated = true;
                    json = Json(vosk_recognizer_final_result(_wake));
                }
                else
                {
                    if (_gated)
                    {
                        _gated = false;
                        vosk_recognizer_reset(_wake);
                        var pre = _ring.Skip(Math.Max(0, _ring.Count - 4)).Take(Math.Min(3, _ring.Count - 1)).ToList();
                        _wakeBase = _clock - (pre.Sum(c => c.Length) + chunk.Length) / 32000.0;
                        foreach (var c in pre) vosk_recognizer_accept_waveform(_wake, c, c.Length);
                    }
                    bool wf = TestSkip != 1 && vosk_recognizer_accept_waveform(_wake, chunk, chunk.Length) != 0;
                    if (!wf)
                    {
                        // Tanıyıcı uzun dinlemede yerel bellekte iç durum biriktirir; konuşma yokken 2 dakikada bir yeniden oluşturulur
                        if (_clock - _recycledAt > 120 && Field(vosk_recognizer_partial_result(_wake), "partial").Length == 0) Recycle();
                        return;
                    }
                    json = Json(vosk_recognizer_result(_wake));
                }
                LastWakeJson = json;
                double nameEnd = WakeEnd(json);
                if (nameEnd >= 0) nameEnd += _wakeBase; // tanıyıcıların zamanları kendi sıfırlanma anlarından sayılır: ortak saate çevir
                double heyStart = WakeStart(json);
                if (heyStart >= 0) heyStart += _wakeBase;
                vosk_recognizer_reset(_wake);
                _wakeBase = _clock;
                if (nameEnd < 0) { _pendingGeneral = null; return; }
                _listening = true;
                _spoke = false;
                _cmdFrom = nameEnd - _cmdBase + 0.12;
                // Saklanan son saniyeler (uyandırma sözcüğü dahil) komut sesine eklenir; "hey pardux"u Whisper'ın yazdığı metinden
                // çıkarmak, sesi tahmini bir yerden kesmekten çok daha güvenilir (kısıtlı tanıyıcının zamanlaması kayabiliyor)
                // "Hey"den yarım saniye öncesinden başlanır: daha önce söylenenler (başka konuşma, TV) Whisper'a hiç gitmez
                long skip = 0;
                if (heyStart >= 0)
                {
                    long ringBytes = _ring.Sum(c => (long)c.Length);
                    double ringStart = _clock - ringBytes / 32000.0;
                    skip = Math.Max(0, (long)((heyStart - ringStart - 0.5) * 32000)) & ~1L;
                    if (skip >= ringBytes) skip = 0;
                }
                _cmdAudio = new MemoryStream();
                foreach (var c in _ring)
                {
                    if (skip >= c.Length) { skip -= c.Length; continue; }
                    _cmdAudio.Write(c, (int)skip, c.Length - (int)skip);
                    skip = 0;
                }
                _audioHasWake = heyStart >= 0;
                // "hey pardux, müzik aç" tek nefeste söylendi mi: "pardux"tan sonra kayıtta konuşma sesi var mı (ses düzeyinden)
                double lastVoice = -1, t = _clock - _ring.Sum(c => c.Length) / 32000.0;
                var flags = _ringVoice.ToList();
                int ci = 0;
                foreach (var c in _ring) { t += c.Length / 32000.0; if (flags[ci++] && t > nameEnd + 0.25) lastVoice = t; }
                _ring.Clear();
                _ringVoice.Clear();
                Whisper.Warm(); // büyük model kullanıcı konuşurken yüklensin
                vosk_recognizer_reset(_cmd);
                _cmdBase = _clock;
                _cmdFrom = 0;
                _since = _clock;
                // Uyanma geç fark edildiyse (ör. bir duraklamadan sonra) komutun devamı için en az ~0,7 sn daha dinlenir
                _lastSpeech = lastVoice > 0 ? Math.Max(lastVoice, _clock - 0.3) : _clock;
                _spoke = lastVoice > 0;          // komut zaten söylendiyse kısa sessizlikten sonra hemen biter
                _heard = "";
                LastWakeHadCommand = lastVoice > 0;
                _pendingGeneral = null;
                if (_testWoke != null) _testWoke(); else Raise(Woke);
                return;
            }

            if (_cmdAudio != null) _cmdAudio.Write(chunk, 0, chunk.Length);
            bool done = vosk_recognizer_accept_waveform(_cmd, chunk, chunk.Length) != 0;
            if (done)
            {
                string t = After(Json(vosk_recognizer_result(_cmd)), _cmdFrom);
                if (_heard.Length == 0) t = StripWakeNoise(t);
                if (t.Length > 0) { _heard = (_heard + " " + t).Trim(); _lastSpeech = _clock; }
            }
            else
            {
                string p = Field(vosk_recognizer_partial_result(_cmd), "partial");
                if (p.Length > 0) { _lastSpeech = _clock; Raise(Partial, (_heard + " " + StripWakeNoise(p)).Trim()); }
            }
            // Ses düzeyi de konuşma sayılır: Vosk bir sözcüğü tanıyamasa bile kullanıcı konuşmaya devam ediyorsa dinleme kesilmez
            if (IsVoice(chunk)) { _lastSpeech = _clock; _spoke = true; }
            double total = _clock - _since, quiet = _clock - _lastSpeech;
            // Bitiş: söz bitti ve kısa bir sessizlik geldi (cümle içindeki kısa duraklamalar sayılmaz), hiç konuşulmadı ya da çok uzadı
            bool spoke = _heard.Length > 0 || _spoke;
            if ((spoke && quiet > 1.0) || (!spoke && total > 4.5) || total > 14) Finish();
        }

        static double _recycledAt;
        static double _noise = 200; // ortam gürültüsünün ses düzeyi (konuşma yokken yavaşça uyarlanır)
        static bool _spoke;         // uyandıktan sonra konuşma sesi duyuldu mu
        static double _lastRms;     // son parçanın ses düzeyi

        /// <summary>Son "hey pardux"un tanıyıcı güveni (sözcüklerin en düşüğü).</summary>
        public static double LastWakeConfidence;
        /// <summary>Tanı: son "hey pardux" adayı (güven, başlangıç-bitiş).</summary>
        public static string LastWakeCandidate;

        /// <summary>Parçada konuşma var mı: ses düzeyi ortam gürültüsünün belirgin üstünde mi.</summary>
        static bool IsVoice(byte[] chunk)
        {
            double sum = 0;
            int n = chunk.Length / 2;
            for (int i = 0; i + 1 < chunk.Length; i += 2) { double v = BitConverter.ToInt16(chunk, i); sum += v * v; }
            double rms = Math.Sqrt(sum / Math.Max(1, n));
            _lastRms = rms;
            bool voice = rms > Math.Max(350, _noise * 2.8);
            // Gürültü tabanı: sessiz parçalarda hızlı, konuşmada çok yavaş uyarlanır
            if (!voice) _noise = _noise * 0.9 + rms * 0.1;
            else _noise = _noise * 0.995 + rms * 0.005;
            return voice;
        }
        static readonly Queue<byte[]> _ring = new Queue<byte[]>();
        static readonly Queue<bool> _ringVoice = new Queue<bool>(); // her parçada konuşma sesi var mıydı
        static int _quietChunks;
        static bool _gated;
        static MemoryStream _cmdAudio;
        static DateTime _muteUntil = DateTime.MinValue;
        static bool _muteEnded;
        public static string LastWhisper;
        public static volatile bool LastWakeHadCommand;

        /// <summary>Pardux konuşurken (sesli yanıt) mikrofonu bu süre kadar yok say.</summary>
        public static void MuteFor(TimeSpan t) { _muteUntil = DateTime.UtcNow + t; }

        /// <summary>Uyanma anında "hey pardux"tan sonra söylenmeye başlanmış bir komut var mı? (Varsa sesli selamlama araya girmez.)</summary>
        public static bool CommandStarted
        {
            get { return _listening && _cmd != IntPtr.Zero && Field(vosk_recognizer_partial_result(_cmd), "partial").Split(' ').Count(w => w.Length > 1) >= 2; }
        }

        /// <summary>İki tanıyıcıyı serbest bırakıp yenisini oluşturur (birikmiş yerel belleği geri verir).</summary>
        static void Recycle()
        {
            if (_wake != IntPtr.Zero) vosk_recognizer_free(_wake);
            if (_cmd != IntPtr.Zero) vosk_recognizer_free(_cmd);
            _wake = vosk_recognizer_new_grm(_model, 16000f, Utf8(WakeGrammar));
            _cmd = vosk_recognizer_new(_model, 16000f);
            vosk_recognizer_set_words(_wake, 1);
            vosk_recognizer_set_words(_cmd, 1);
            _wakeBase = _cmdBase = _recycledAt = _clock;
            _pendingGeneral = null;
        }

        static bool _audioHasWake; // komut sesinin başında "hey pardux" var mı (Pardux konuşurken silinmişse yok)

        /// <summary>Son uyanma yanlış alarm mıydı (Whisper "hey pardux" duymadı).</summary>
        public static bool LastFalseWake;

        static void Finish()
        {
            string last = After(Json(vosk_recognizer_final_result(_cmd)), _cmdFrom);
            if (last.Length > 0) _heard = (_heard + " " + last).Trim();
            vosk_recognizer_reset(_cmd);
            _cmdBase = _clock;
            _listening = false;
            string text = _heard.Trim();
            byte[] audio = _cmdAudio != null ? _cmdAudio.ToArray() : null;
            _cmdAudio = null;
            // Asıl yazıya dökme Whisper ile (Türkçede çok daha isabetli); olmazsa Vosk'un duyduğu kullanılır
            if (Whisper.Available && audio != null && audio.Length > 16000)
            {
                if (_testCommand == null) Raise(Thinking);
                // Kişi adları ve özel komutlar ipucu olsun ("kuzey'e whatsapp'tan yaz" doğru yazılsın)
                var c = Controller.Instance;
                if (c != null && c.Settings != null)
                    Whisper.ExtraWords = c.Settings.Contacts.Keys.Concat(c.Settings.Phones.Keys).Concat(c.Settings.Instagram.Keys)
                        .Concat(c.Settings.CustomCommands.Where(x => x != null && x.Name != null).Select(x => x.Name))
                        .Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
                string w = Whisper.Transcribe(audio);
                LastWhisper = w;
                if (w != null) text = w;
                // Ses kaydı "Hey"den hemen önce başlıyor: Whisper hiçbir "hey pardux" duymadıysa uyanma yanlıştı (TV, başka bir konuşma).
                // Bu durumda hiçbir şey yapılmaz, sesli "duyamadım" da denmez
                // Yalnızca uzun cümlelerde uygulanır (TV, sohbet): kısa komutlarda Whisper "hey pardux"u bazen hiç yazmıyor,
                // onlar yine çalışsın (sesle yalnızca açıkça eşleşen komutlar kendiliğinden çalıştığı için risk düşük).
                // Vosk adı düşük güvenle duyduysa ("pardux" sözlüğünde yok) uyanma her zaman Whisper'ın adı duymasına bağlıdır
                int words = w == null ? 0 : w.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                LastFalseWake = w != null && _audioHasWake && !Whisper.LastHadWake
                                && ((words >= 7 && LastWakeConfidence < 0.95) || LastWakeConfidence < SureWake);
                if (LastFalseWake) text = "";
            }
            else LastFalseWake = false;
            if (_testCommand != null) _testCommand(text); else Raise(Command, text);
        }

        static void Loop()
        {
            var mic = new Mic();
            try
            {
                Init();
                mic.Open();
                while (!_stop)
                {
                    byte[] chunk = mic.Read(500);
                    if (chunk != null) Feed(chunk);
                }
            }
            catch (Exception ex) { LastError = ex.Message; }
            finally
            {
                mic.Close();
                _thread = null;
            }
        }

        /// <summary>Test için: WAV dosyasını mikrofondan geliyormuş gibi dinleme akışından geçirir.</summary>
        public static void Simulate(string wav, Action woke, Action<string> command)
        {
            Init();
            _testWoke = woke; _testCommand = command;
            var data = File.ReadAllBytes(wav);
            for (int i = 44; i < data.Length; i += 3200)
            {
                var chunk = new byte[Math.Min(3200, data.Length - i)];
                Array.Copy(data, i, chunk, 0, chunk.Length);
                Feed(chunk);
            }
            var silence = new byte[3200];
            for (int i = 0; i < 30 && _listening; i++) Feed(silence);
            if (_listening) Finish();
            _testWoke = null; _testCommand = null;
        }

        /// <summary>Test için: dakikalarca hafif gürültülü sessizliği dinleme akışından geçirir, bellek ölçümü yapılsın diye her dakikada geri çağırır.</summary>
        public static void SimulateSilence(int minutes, Action<int> perMinute)
        {
            Init();
            var rnd = new Random(1);
            var chunk = new byte[3200];
            for (int m = 1; m <= minutes; m++)
            {
                for (int i = 0; i < 600; i++)
                {
                    for (int j = 0; j < chunk.Length; j += 2) { short s = (short)rnd.Next(-120, 120); chunk[j] = (byte)s; chunk[j + 1] = (byte)(s >> 8); }
                    Feed(chunk);
                }
                perMinute(m);
            }
        }

        /// <summary>Bellek testi: arka planda sürekli konuşma/TV sesi varmış gibi verilen sesi döngüyle besler.</summary>
        public static void SimulateLoop(byte[] pcm, int minutes, Action<int> perMinute)
        {
            Init();
            var chunk = new byte[3200];
            int pos = 0;
            for (int m = 1; m <= minutes; m++)
            {
                for (int i = 0; i < 600; i++)
                {
                    for (int j = 0; j < chunk.Length; j++) { chunk[j] = pcm[pos]; pos = (pos + 1) % (pcm.Length & ~1); }
                    Feed(chunk);
                }
                perMinute(m);
            }
        }

        /// <summary>Yalnızca bellek testi için: 1 = uyandırma tanıyıcısını, 2 = genel tanıyıcıyı besleme.</summary>
        public static int TestSkip;

        static Action _testWoke;
        static Action<string> _testCommand;

        static void Raise(Action a)
        {
            var app = System.Windows.Application.Current;
            if (a != null && app != null) app.Dispatcher.BeginInvoke(a);
        }

        static void Raise(Action<string> a, string s)
        {
            var app = System.Windows.Application.Current;
            if (a != null && app != null) app.Dispatcher.BeginInvoke(a, s);
        }

        /// <summary>
        /// Uyanmak için "hey" ve adın parçalarının en düşük güveni. "Pardux" Vosk sözlüğünde olmadığından "par duk" ile yaklaşık eşleşir ve
        /// güven bazen 0,5'e iner; bu düzeyde uyanılır ama SureWake'in altındaki uyanmalar Whisper adı da duyarsa geçerli sayılır.
        /// </summary>
        public static double WakeConfidence = 0.45;
        const double SureWake = 0.8;
        public static string LastWakeJson;

        static string Json(IntPtr json)
        {
            if (json == IntPtr.Zero) return "";
            int len = 0;
            while (Marshal.ReadByte(json, len) != 0) len++;
            var bytes = new byte[len];
            Marshal.Copy(json, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }

        static string Field(IntPtr json, string name) { return Field(Json(json), name); }

        static string Field(string json, string name)
        {
            var m = Regex.Match(json ?? "", "\"" + name + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        static string _pendingGeneral;
        static double _cmdFrom, _wakeBase, _cmdBase;

        /// <summary>Sonuçta verilen andan sonra başlayan sözcükler (uyandırma sözcüğünün kendisi komuta karışmasın).</summary>
        static string After(string json, double from)
        {
            return string.Join(" ", WordList(json).Where(w => w.Item2 >= from).Select(w => w.Item1)).Trim();
        }

        // Genel tanıyıcı "hey pardux"u sözlüğündeki benzer sözcüklere çeviriyor; anlık yazıda bunları gösterme
        static readonly HashSet<string> WakeNoise = new HashSet<string> { "hey", "pardux", "parduks", "par", "pardon", "paradoks", "bardak", "park", "dük", "duk", "yağmur", "yeni", "eğer", "yemi", "hemen", "yemek", "hayır", "evet", "değil", "bu", "ve", "ya" };

        static string StripWakeNoise(string partial)
        {
            var w = partial.Split(' ').ToList();
            int k = 0;
            while (k < w.Count && k < 2 && WakeNoise.Contains(w[k])) k++;
            return string.Join(" ", w.Skip(k));
        }

        /// <summary>Sonuçtaki sözcükler: (sözcük, başlangıç sn, bitiş sn, güven).</summary>
        static List<Tuple<string, double, double, double>> WordList(string json)
        {
            var list = new List<Tuple<string, double, double, double>>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (Match o in Regex.Matches(json ?? "", "\\{[^{}]*\"word\"[^{}]*\\}"))
            {
                Func<string, double> num = k => { var m = Regex.Match(o.Value, "\"" + k + "\"\\s*:\\s*([0-9.eE+-]+)"); return m.Success ? double.Parse(m.Groups[1].Value, inv) : 0; };
                var w = Regex.Match(o.Value, "\"word\"\\s*:\\s*\"([^\"]*)\"");
                list.Add(Tuple.Create(w.Groups[1].Value, num("start"), num("end"), num("conf")));
            }
            return list;
        }

        static IEnumerable<Tuple<string, double>> Words(string json)
        {
            return WordList(json).Select(w => Tuple.Create(w.Item1, w.Item2));
        }

        /// <summary>"hey pardux"ta "hey"in başladığı an (tanıyıcının kendi saatiyle); yoksa -1.</summary>
        static double WakeStart(string json) { int i, j; return WakeIndex(json, out i, out j) ? WordList(json)[i].Item2 : -1; }

        /// <summary>Art arda, yüksek güvenle duyulmuş "hey pardux" varsa adın bittiği an; yoksa -1.</summary>
        static double WakeEnd(string json) { int i, j; return WakeIndex(json, out i, out j) ? WordList(json)[j].Item3 : -1; }

        /// <summary>
        /// Gerçek bir "hey pardux" mu: "hey" ve adın parçaları ("par duk", "pardon"…) yüksek güvenle, doğal sürelerde ve art arda
        /// (aralarında uzun boşluk yok) söylenmiş olmalı. TV/konuşma içinde rastgele eşleşmeler genelde çok kısa ya da kopuk çıkıyor; bunlar elenir.
        /// i = "hey"in sırası, j = adın son parçasının sırası.
        /// </summary>
        static bool WakeIndex(string json, out int i, out int j)
        {
            var w = WordList(json);
            for (i = 0; i + 1 < w.Count; i++)
            {
                if (w[i].Item1 != "hey") continue;
                j = i;
                double conf = w[i].Item4;
                while (j + 1 < w.Count && j - i < 2 && NameParts.Contains(w[j + 1].Item1)) { j++; conf = Math.Min(conf, w[j].Item4); }
                // Ad "par…" ile başlamalı: tek başına "duk" ("hey dur bakalım") ad sayılmaz
                if (j == i || !w[i + 1].Item1.StartsWith("par")) continue;
                LastWakeCandidate = string.Format(System.Globalization.CultureInfo.InvariantCulture, "hey {0:0.00}@{1:0.00}-{2:0.00} {3} {4:0.00}@{5:0.00}-{6:0.00}", w[i].Item4, w[i].Item2, w[i].Item3,
                    string.Join(" ", w.Skip(i + 1).Take(j - i).Select(x => x.Item1)), conf, w[i + 1].Item2, w[j].Item3);
                if (conf < WakeConfidence) continue;
                double nameLen = w[j].Item3 - w[i + 1].Item2, gap = w[i + 1].Item2 - w[i].Item3;
                if (nameLen < 0.15 || gap > 0.8) continue; // yalnızca belirgin kopukluk elenir (süre sınırları gerçek sesleri de eliyordu)
                LastWakeConfidence = conf;
                return true;
            }
            j = -1;
            return false;
        }

        static byte[] Utf8(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            Array.Resize(ref b, b.Length + 1);
            return b;
        }

        // ---------------- Konuşma metnini komuta çevirme ----------------
        static readonly Dictionary<string, int> Ones = new Dictionary<string, int>
        {
            { "bir", 1 }, { "iki", 2 }, { "uc", 3 }, { "dort", 4 }, { "bes", 5 }, { "alti", 6 }, { "yedi", 7 }, { "sekiz", 8 }, { "dokuz", 9 }
        };
        static readonly Dictionary<string, int> Tens = new Dictionary<string, int>
        {
            { "on", 10 }, { "yirmi", 20 }, { "otuz", 30 }, { "kirk", 40 }, { "elli", 50 }, { "altmis", 60 }, { "yetmis", 70 }, { "seksen", 80 }, { "doksan", 90 }
        };

        /// <summary>"on beş dakika sonra" → "15 dakika sonra", "yarım saat" → "30 dakika" (komut motoru rakam bekler).</summary>
        // Küçük modelin sözlüğünde olmayan ya da sık yanlış duyulan sözcükler
        static readonly string[][] Fixes =
        {
            new[] { @"\b(kurulu|kurumu|onu|kuru|kron|krom|koro) ?metre\b", "kronometre" },
            new[] { @"\bkronik( metre)?\b", "kronometre" },
            new[] { @"\bkronometr ?e\b", "kronometre" },
            new[] { @"\bhava ?durum\w*", "hava durumu" },
            new[] { @"\b(hat ?sap|vat ?sap|wats ?ap|what ?sap|whats ?app|vatsap)p?(['’]?(tan|dan|ten|den))?\b", "whatsapp'tan" },
            new[] { @"\b(hala|hale|hava) (durumu|dururdu|durum|durdu)\b", "hava durumu" },
            new[] { @"\b(vatsap|vatsapp|whats app|vats ap|vat sap|watsap)\b", "whatsapp" },
            new[] { @"\b(insta gram|ins tagram)\b", "instagram" },
            new[] { @"\bspot ifay\b|\bspotifay\b", "spotify" },
            new[] { @"\bkronometri", "kronometre" },
            new[] { @"\bmüzikl?aç\b", "müzik aç" },
            new[] { @"\bşumarık\b", "şımarık" },
            new[] { @"\b(dizcord|diskord|discort)", "discord" },
            new[] { @"\bdis ?[gcjk]i?or ?d[oa]\b", "discord" },
            new[] { @"\b(sonaki|sonra ki)\b", "sonraki" },
            new[] { @"\b(zamanlı yıcı|zamanla yıcı|zaman layıcı)\b", "zamanlayıcı" },
            new[] { @"\bkaç ?met(r)?[ei]\b", "kaç metre" },
            new[] { @"\b(not ar|notar)\b", "not al" },
            // Sona yapışmış "çal" / "aç": "şımarıkçal" → "şımarık çal", "müzikaç" → "müzik aç"
            new[] { @"\b(\w{3,})(çal|aç)\b(?=[\s.,!?]*$)", "$1 $2" },
        };

        public static string Normalize(string text)
        {
            text = text ?? "";
            foreach (var f in Fixes) text = Regex.Replace(text, f[0], f[1], RegexOptions.IgnoreCase);
            // Uyandırma sözcüğünün komutun başına sızan kısmı
            text = Regex.Replace(text, @"^\s*(hey\s+)?(pardux|parduks|pardüks|pardoks|par[ -]?d[uü][kx]s?)\b[\s,.!]*|^\s*hey\b[\s,]*", "", RegexOptions.IgnoreCase);
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var outp = new List<string>();
            for (int i = 0; i < words.Count; i++)
            {
                string n = TextUtil.Norm(words[i]);
                if (n == "yarim" && i + 1 < words.Count && TextUtil.Norm(words[i + 1]).StartsWith("saat")) { outp.Add("30"); outp.Add("dakika"); i++; continue; }
                int value = 0, used = 0;
                if (n == "yuz") { value = 100; used = 1; }
                // Bitişik yazılmış sayılar: "onbeş" → 15, "yirmibeş" → 25
                if (used == 0)
                    foreach (var t in Tens)
                        if (n.Length > t.Key.Length && n.StartsWith(t.Key) && Ones.ContainsKey(n.Substring(t.Key.Length))) { value = t.Value + Ones[n.Substring(t.Key.Length)]; used = 1; break; }
                if (used == 0 && Tens.ContainsKey(n)) { value = Tens[n]; used = 1; }
                if (used == 1 && value < 100 && i + 1 < words.Count && Ones.ContainsKey(TextUtil.Norm(words[i + 1]))) { value += Ones[TextUtil.Norm(words[i + 1])]; used = 2; }
                // "bir" tek başına çoğu zaman sayı değildir ("bir şarkı aç"); yalnızca birimden önce rakama çevir
                if (used == 0 && Ones.ContainsKey(n))
                {
                    string next = i + 1 < words.Count ? TextUtil.Norm(words[i + 1]) : "";
                    if (n != "bir" || Regex.IsMatch(next, "^(saniye|dakika|dk|saat|gun)"))
                    { value = Ones[n]; used = 1; }
                }
                if (used > 0) { outp.Add(value.ToString()); i += used - 1; }
                else outp.Add(words[i]);
            }
            return string.Join(" ", outp);
        }

        // ---------------- Mikrofon (waveIn) ----------------
        class Mic
        {
            const int Buffers = 4, BufferBytes = 3200; // 100 ms (16 kHz, 16 bit, mono)
            IntPtr _h = IntPtr.Zero;
            readonly IntPtr[] _hdrs = new IntPtr[Buffers];
            readonly AutoResetEvent _ev = new AutoResetEvent(false);
            readonly Queue<byte[]> _ready = new Queue<byte[]>();

            public void Open()
            {
                var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = 16000, wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = 32000 };
                int r = waveInOpen(out _h, -1, ref fmt, _ev.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, 0x50000 /* CALLBACK_EVENT */);
                if (r != 0) throw new Exception("Mikrofon açılamadı (" + r + "). Ayarlar → Gizlilik → Mikrofon iznini kontrol et.");
                int size = Marshal.SizeOf(typeof(WAVEHDR));
                for (int i = 0; i < Buffers; i++)
                {
                    var hdr = new WAVEHDR { lpData = Marshal.AllocHGlobal(BufferBytes), dwBufferLength = BufferBytes };
                    _hdrs[i] = Marshal.AllocHGlobal(size);
                    Marshal.StructureToPtr(hdr, _hdrs[i], false);
                    waveInPrepareHeader(_h, _hdrs[i], size);
                    waveInAddBuffer(_h, _hdrs[i], size);
                }
                waveInStart(_h);
            }

            /// <summary>Hazır ses parçasını döndürür; timeout içinde yoksa null.</summary>
            public byte[] Read(int timeoutMs)
            {
                if (_ready.Count == 0)
                {
                    _ev.WaitOne(timeoutMs);
                    Collect();
                }
                return _ready.Count > 0 ? _ready.Dequeue() : null;
            }

            void Collect()
            {
                int size = Marshal.SizeOf(typeof(WAVEHDR));
                foreach (IntPtr p in _hdrs)
                {
                    var hdr = (WAVEHDR)Marshal.PtrToStructure(p, typeof(WAVEHDR));
                    if ((hdr.dwFlags & 1 /* WHDR_DONE */) == 0) continue;
                    if (hdr.dwBytesRecorded > 0)
                    {
                        var data = new byte[hdr.dwBytesRecorded];
                        Marshal.Copy(hdr.lpData, data, 0, data.Length);
                        _ready.Enqueue(data);
                    }
                    hdr.dwFlags &= ~1u;
                    hdr.dwBytesRecorded = 0;
                    Marshal.StructureToPtr(hdr, p, false);
                    waveInAddBuffer(_h, p, size);
                }
            }

            public void Close()
            {
                if (_h == IntPtr.Zero) return;
                waveInStop(_h);
                waveInReset(_h);
                int size = Marshal.SizeOf(typeof(WAVEHDR));
                foreach (IntPtr p in _hdrs)
                {
                    if (p == IntPtr.Zero) continue;
                    waveInUnprepareHeader(_h, p, size);
                    var hdr = (WAVEHDR)Marshal.PtrToStructure(p, typeof(WAVEHDR));
                    Marshal.FreeHGlobal(hdr.lpData);
                    Marshal.FreeHGlobal(p);
                }
                waveInClose(_h);
                _h = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, int device, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInStop(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);

        // ---------------- Vosk ----------------
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern void vosk_set_log_level(int level);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr vosk_model_new(byte[] path);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr vosk_recognizer_new(IntPtr model, float rate);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern int vosk_recognizer_accept_waveform(IntPtr rec, byte[] data, int length);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr vosk_recognizer_result(IntPtr rec);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr vosk_recognizer_partial_result(IntPtr rec);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr vosk_recognizer_final_result(IntPtr rec);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern void vosk_recognizer_reset(IntPtr rec);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern void vosk_recognizer_free(IntPtr rec);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern void vosk_recognizer_set_words(IntPtr rec, int words);

        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr vosk_recognizer_new_grm(IntPtr model, float rate, byte[] grammar);

        /// <summary>Test için: 16 kHz mono 16 bit WAV dosyasını tanır (grammar verilirse yalnızca o sözcüklerle).</summary>
        public static string RecognizeFile(string wav, string grammar = null, int log = -1)
        {
            SetDllDirectory(Dir);
            vosk_set_log_level(log);
            if (_model == IntPtr.Zero) _model = vosk_model_new(Utf8(ModelDir));
            var rec = grammar == null ? vosk_recognizer_new(_model, 16000f) : vosk_recognizer_new_grm(_model, 16000f, Utf8(grammar));
            var data = File.ReadAllBytes(wav);
            var sb = new StringBuilder();
            for (int i = 44; i < data.Length; i += 3200)
            {
                var chunk = new byte[Math.Min(3200, data.Length - i)];
                Array.Copy(data, i, chunk, 0, chunk.Length);
                if (vosk_recognizer_accept_waveform(rec, chunk, chunk.Length) != 0) sb.Append(Field(vosk_recognizer_result(rec), "text")).Append(' ');
                else if (grammar != null)
                {
                    string p = Field(vosk_recognizer_partial_result(rec), "partial");
                    if (p.Length > 0) sb.Append("(" + p + ") ");
                }
            }
            sb.Append(Field(vosk_recognizer_final_result(rec), "text"));
            vosk_recognizer_free(rec);
            return sb.ToString().Trim();
        }
    }
}
