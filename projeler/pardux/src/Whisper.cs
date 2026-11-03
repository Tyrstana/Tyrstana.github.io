using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Pardux
{
    /// <summary>
    /// Komutun yazıya dökülmesi: Whisper (whisper.cpp; varsa large-v3-turbo, yoksa small modeli), tamamen çevrimdışı. Türkçede Vosk küçük modelinden çok daha isabetli.
    /// "Hey Pardux" algılaması hâlâ hafif Vosk tanıyıcısıyla yapılır; Whisper yalnızca uyandıktan sonra söylenen kısmı yazar.
    /// </summary>
    internal static class Whisper
    {
        static string Dir { get { return Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "whisper"); } }
        static string Cli { get { return Path.Combine(Dir, "whisper-cli.exe"); } }
        /// <summary>Testte model seçmek için ("turbo" / "small").</summary>
        public static string ModelOverride;

        /// <summary>Türkçede çok daha isabetli large-v3-turbo varsa o, yoksa small.</summary>
        static string Model
        {
            get
            {
                string turbo = Path.Combine(Dir, "ggml-large-v3-turbo-q5_0.bin"), small = Path.Combine(Dir, "ggml-small-q5_1.bin");
                if (ModelOverride == "small") return small;
                return File.Exists(turbo) ? turbo : small;
            }
        }

        /// <summary>Komutlarda geçebilecek özel adlar (kişiler, özel komutlar): Whisper'a ipucu olarak verilir.</summary>
        public static string[] ExtraWords = new string[0];

        public static bool Available { get { return File.Exists(Cli) && File.Exists(Model); } }

        /// <summary>Sözcük ipucu kullanılsın mı (test için kapatılabilir).</summary>
        public static bool UsePrompt = true;

        // Sözcük dağarcığını komutlara doğru yönlendirir (yazım: WhatsApp, kronometre...)
        const string Prompt = "Pardux, müzik aç, şarkı çal, kronometre başlat, zamanlayıcı kur, hatırlat, WhatsApp, Instagram, Spotify, hava durumu, haberler.";

        static string FullPrompt
        {
            get
            {
                var names = (ExtraWords ?? new string[0]).Where(n => !string.IsNullOrWhiteSpace(n) && n.Length < 30).Take(12)
                                                         .Select(n => n.Replace("\"", ""));
                string extra = string.Join(", ", names);
                return extra.Length > 0 ? Prompt.TrimEnd('.') + ", " + extra + "." : Prompt;
            }
        }

        /// <summary>Sessiz mikrofonlarda konuşma Whisper için duyulur seviyeye getirilir (en çok 8 kat, kırpmadan).</summary>
        static byte[] Normalize(byte[] pcm)
        {
            int peak = 1;
            for (int i = 0; i + 1 < pcm.Length; i += 2) { int v = Math.Abs((int)BitConverter.ToInt16(pcm, i)); if (v > peak) peak = v; }
            double gain = Math.Min(8.0, 29000.0 / peak);
            if (gain < 1.15) return pcm;
            var outb = new byte[pcm.Length];
            for (int i = 0; i + 1 < pcm.Length; i += 2)
            {
                int v = (int)(BitConverter.ToInt16(pcm, i) * gain);
                short s = (short)Math.Max(-32768, Math.Min(32767, v));
                outb[i] = (byte)s; outb[i + 1] = (byte)(s >> 8);
            }
            return outb;
        }

        // Sessizlikte Whisper'ın "uydurduğu" bilinen kalıplar
        static readonly string[] Hallucinations = { "altyazı", "abone ol", "izlediğiniz için", "teşekkürler.", "m.k.", "beğenmeyi unutma" };

        // ---------------- Açık tutulan Whisper sunucusu ----------------
        // Büyük model her komutta diskten yüklenince ~1,5 sn kaybediliyordu. "Hey Pardux" duyulunca sunucu başlar (kullanıcı konuşurken
        // model yüklenir), birkaç dakika kullanılmazsa kapanıp belleği bırakır. Yalnızca bu bilgisayardan erişilebilir (127.0.0.1).
        // Ses bağlamı 512 (~10 sn): Whisper 30 sn'lik pencerenin tamamını işlemez, komut ~3 kat hızlı yazılır.
        const int AudioCtx = 512; // daha küçüğü Whisper'ın cümleyi tekrarlamasına yol açıyordu
        const double MaxServerSeconds = 9.8;
        static string ServerExe { get { return Path.Combine(Dir, "whisper-server.exe"); } }
        static int Threads { get { return Math.Max(2, Math.Min(10, Environment.ProcessorCount - 4)); } }
        static readonly object ServerGate = new object();
        static Process _server;
        static int _port;
        static System.Threading.Timer _serverIdle;
        static bool _orphansChecked;

        /// <summary>"Hey Pardux" duyulunca çağrılır: sunucu arka planda açılır, komut bitene kadar model yüklenmiş olur.</summary>
        public static void Warm()
        {
            if (!Available || !File.Exists(ServerExe)) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { EnsureServer(0); } catch { } });
        }

        static bool EnsureServer(int waitMs)
        {
            if (!File.Exists(ServerExe)) return false;
            lock (ServerGate)
            {
                if (!_orphansChecked)
                {
                    // Önceki oturumdan (çökme vb.) kalmış sunucuları kapat
                    _orphansChecked = true;
                    foreach (var p in Process.GetProcessesByName("whisper-server"))
                        try { if (string.Equals(p.MainModule.FileName, ServerExe, StringComparison.OrdinalIgnoreCase)) p.Kill(); } catch { }
                }
                if (_server == null || _server.HasExited)
                {
                    _port = FreePort();
                    var psi = new ProcessStartInfo(ServerExe, "-m \"" + Model + "\" -l tr -t " + Threads + " -ac " + AudioCtx + " -nf -mc 0 --host 127.0.0.1 --port " + _port)
                    {
                        UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Dir,
                        RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    var p = new Process { StartInfo = psi };
                    p.OutputDataReceived += (s, e) => { };
                    p.ErrorDataReceived += (s, e) => { };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    _server = p;
                }
                if (_serverIdle == null) _serverIdle = new System.Threading.Timer(_ => StopServer(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                _serverIdle.Change(TimeSpan.FromMinutes(10), TimeSpan.FromMilliseconds(-1));
            }
            // Sunucu dinlemeye başlayana kadar bekle (model yükleniyor)
            var end = DateTime.Now.AddMilliseconds(waitMs);
            do
            {
                try
                {
                    using (var c = new System.Net.Sockets.TcpClient())
                    {
                        var ar = c.BeginConnect("127.0.0.1", _port, null, null);
                        if (ar.AsyncWaitHandle.WaitOne(200) && c.Connected) { c.EndConnect(ar); return true; }
                    }
                }
                catch { }
                if (_server == null || _server.HasExited) return false;
                if (waitMs > 0) System.Threading.Thread.Sleep(100);
            } while (DateTime.Now < end);
            return false;
        }

        public static void StopServer()
        {
            lock (ServerGate)
            {
                try { if (_server != null && !_server.HasExited) _server.Kill(); } catch { }
                if (_server != null) _server.Dispose();
                _server = null;
            }
        }

        static int FreePort()
        {
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        /// <summary>WAV dosyasını sunucuya gönderir, düz metin döner (Türkçe ipucu UTF-8 olarak gider).</summary>
        static string PostToServer(byte[] wav)
        {
            try
            {
                string boundary = "----pardux" + Guid.NewGuid().ToString("N");
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://127.0.0.1:" + _port + "/inference");
                req.Method = "POST";
                req.ContentType = "multipart/form-data; boundary=" + boundary;
                req.Timeout = 20000;
                req.ReadWriteTimeout = 20000;
                req.Proxy = null;
                using (var ms = new MemoryStream())
                {
                    Action<string> text = s => { var b = Encoding.UTF8.GetBytes(s); ms.Write(b, 0, b.Length); };
                    Action<string, string> field = (name, value) =>
                        text("--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + name + "\"\r\n\r\n" + value + "\r\n");
                    field("response_format", "text");
                    field("temperature", "0.0");
                    if (UsePrompt) field("prompt", FullPrompt);
                    text("--" + boundary + "\r\nContent-Disposition: form-data; name=\"file\"; filename=\"komut.wav\"\r\nContent-Type: audio/wav\r\n\r\n");
                    ms.Write(wav, 0, wav.Length);
                    text("\r\n--" + boundary + "--\r\n");
                    req.ContentLength = ms.Length;
                    using (var s = req.GetRequestStream()) ms.WriteTo(s);
                }
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    lock (ServerGate) if (_serverIdle != null) _serverIdle.Change(TimeSpan.FromMinutes(10), TimeSpan.FromMilliseconds(-1));
                    return sr.ReadToEnd();
                }
            }
            catch { return null; }
        }

        /// <summary>16 kHz, 16 bit, tek kanal ham sesi yazıya döker. Başarısızsa null.</summary>
        public static string Transcribe(byte[] pcm)
        {
            if (!Available || pcm == null || pcm.Length < 9600) return null;
            string wav = Path.Combine(Path.GetTempPath(), "pardux_komut.wav");
            pcm = Normalize(pcm);
            try
            {
                using (var w = new BinaryWriter(File.Create(wav)))
                {
                    w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm.Length); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
                    w.Write(Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length); w.Write(pcm);
                }
                double seconds = pcm.Length / 32000.0;
                // Önce açık tutulan sunucu (model bellekte: yükleme beklenmez); olmazsa tek seferlik komut satırı aracı
                if (seconds <= MaxServerSeconds && EnsureServer(6000))
                {
                    string viaServer = PostToServer(File.ReadAllBytes(wav));
                    if (viaServer != null) return Clean(Regex.Replace(viaServer, @"\s+", " ").Trim());
                }
                int threads = Threads;
                var psi = new ProcessStartInfo(Cli,
                    "-m \"" + Model + "\" -f \"" + wav + "\" -l tr -nt -np -t " + threads + (seconds <= MaxServerSeconds ? " -ac " + AudioCtx : seconds <= 14.5 ? " -ac 768" : "") + " -nf" +
                    (UsePrompt ? " --prompt \"" + FullPrompt + "\"" : ""))
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = Dir
                };
                using (var p = Process.Start(psi))
                {
                    p.ErrorDataReceived += (s, e) => { }; // stderr'i boşalt (dolarsa süreç takılır)
                    p.BeginErrorReadLine();
                    string outp = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return null; }
                    string text = Regex.Replace(outp, @"\s+", " ").Trim();
                    return Clean(text);
                }
            }
            catch { return null; }
            finally { try { File.Delete(wav); } catch { } }
        }

        static readonly HashSet<string> HeyWords = new HashSet<string> { "hey", "hei", "hay", "he", "hi", "ey", "pey", "tey", "heyi", "hayi", "hej", "heh", "pay", "eyy", "heyy" };

        static bool IsHey(string t) { return HeyWords.Contains(t); }
        static bool LeadHey(string t) { return t == "hey" || t == "heyi" || t == "hay" || t == "hei" || t == "pey"; }

        /// <summary>
        /// "Pardux" gibi duyulan tek sözcük: "Pardux", "Parduks", "Pardüks", "Pardoks", "Bardux", "Paradoks"…
        /// "p/b" ile başlayıp "parduks"a çok yakın olmalı (Türkçe sözcüklerle karışmasın: "bardak", "parti", "park" sayılmaz).
        /// </summary>
        static bool IsName(string t)
        {
            if (t.Length < 5 || t.Length > 9 || (t[0] != 'p' && t[0] != 'b')) return false;
            return Distance(t, "pardux") <= 1 || Distance(t, "parduks") <= 2 || t == "paradoks" || t == "parduk" || t == "pardus";
        }

        /// <summary>Verilen sırada adın kaç sözcük tuttuğu: "Pardux" = 1, "Par dux" / "Par-düks" = 2, yoksa 0. "hey"den sonra "pardon" da ad sayılır.</summary>
        static int NameAt(string text, List<Match> tokens, int i, bool afterHey)
        {
            if (i >= tokens.Count) return 0;
            string t = TextUtil.Norm(tokens[i].Value);
            if (IsName(t) || (afterHey && t == "pardon")) return 1;
            // "Hey"den sonra p/b ile başlayan ve hitap gibi noktalamayla ayrılan sözcük de addır ("Hey Paget!", "Hey Pardit, …")
            int after = tokens[i].Index + tokens[i].Length;
            if (afterHey && t.Length >= 4 && t.Length <= 9 && (t[0] == 'p' || t[0] == 'b') && i + 1 < tokens.Count)
            {
                string between = text.Substring(after, tokens[i + 1].Index - after);
                if (between.IndexOfAny(new[] { ',', '!', '.', '?' }) >= 0) return 1;
            }
            // İki sözcüğe bölünmüş ad: "Par dux", "Par-düks", "Paper Dux"
            if (t.Length >= 2 && t.Length <= 5 && (t[0] == 'p' || t[0] == 'b') && i + 1 < tokens.Count)
            {
                string n = TextUtil.Norm(tokens[i + 1].Value);
                if (n.Length >= 3 && n.Length <= 5 && n[0] == 'd' && (n.Contains("x") || n.EndsWith("ks") || n.EndsWith("ps"))) return 2;
            }
            return 0;
        }

        /// <summary>Bitişik yazılmış uyandırma: "Heypardux", "Haypardüks".</summary>
        static bool IsJoinedWake(string t)
        {
            return t.Length >= 8 && (t.StartsWith("hey") || t.StartsWith("hay")) && IsName(t.Substring(3));
        }

        static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        /// <summary>Uyandırma sözcüğünü, noktalama işaretlerini ve bilinen "uydurmaları" temizler.</summary>
        /// <summary>Son yazıya dökmede "Hey Pardux" (ya da benzeri) duyuldu mu: duyulmadıysa uyanma büyük olasılıkla yanlıştı (TV, konuşma).</summary>
        public static bool LastHadWake;
        /// <summary>Son yazıya dökmenin temizlenmemiş hâli (tanı için).</summary>
        public static string LastRaw;

        public static string Clean(string text)
        {
            LastHadWake = false;
            LastRaw = text;
            if (string.IsNullOrWhiteSpace(text)) return "";
            string low = text.ToLower(TextUtil.Tr);
            if (Hallucinations.Any(h => low.Contains(h))) return "";
            if (low.Trim(' ', '.') == Prompt.ToLower(TextUtil.Tr).Trim(' ', '.')) return "";
            // "[MÜZİK ÇALIYOR]", "(Müzik)" gibi ses etiketleri konuşma değildir
            text = Regex.Replace(text, @"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*", " ").Trim();
            // Ses kaydı "Hey Pardux"tan önceki birkaç saniyeyi de içerir: o sırada söylenenler (başka bir konuşma, TV) komuta karışmasın.
            // Whisper "Hey Pardux"u farklı yazabilir: "Hey Parduks", "Hey Par-dux", "Heypardüks", "Hey pardon". Sözcük sözcük bakılır:
            // "hey" benzeri + ad benzeri ikilinin (ya da bitişik tek sözcüğün) ilkinden sonrası komuttur. Whisper sessiz kuyrukta
            // uyandırmayı bazen yeniden "duyar" ("Kronometreyi başlat. Hey Pardux! Hey Pardux!"): sonraki uyandırmalar atılır
            var tokens = Regex.Matches(text, @"[\p{L}\p{N}'’]+").Cast<Match>().ToList();
            int cut = -1, n;
            int limit = Math.Max(1, (int)Math.Ceiling(tokens.Count * 0.7));
            var spans = new List<int[]>(); // uyandırmaların [ilk sözcük, sonraki sözcük] sıraları
            for (int i = 0; i < tokens.Count; i++)
            {
                string t = TextUtil.Norm(tokens[i].Value);
                if (IsJoinedWake(t)) spans.Add(new[] { i, i + 1 });
                else if (IsHey(t) && (n = NameAt(text, tokens, i + 1, true)) > 0) { spans.Add(new[] { i, i + 1 + n }); i += n; }
                else if (IsHey(t) && i + 1 < tokens.Count && TextUtil.Norm(tokens[i + 1].Value) == "i" && (n = NameAt(text, tokens, i + 2, true)) > 0) { spans.Add(new[] { i, i + 2 + n }); i += n + 1; }
            }
            if (spans.Count > 0 && spans[0][0] < limit) cut = spans[0][1];
            LastHadWake = cut > 0;
            if (cut > 0)
            {
                var sb = new StringBuilder();
                int pos = cut < tokens.Count ? tokens[cut].Index : text.Length;
                foreach (var sp in spans.Skip(1))
                {
                    int s0 = tokens[sp[0]].Index, e0 = sp[1] < tokens.Count ? tokens[sp[1]].Index : text.Length;
                    if (s0 > pos) sb.Append(text, pos, s0 - pos);
                    pos = Math.Max(pos, e0);
                }
                if (pos < text.Length) sb.Append(text.Substring(pos));
                text = sb.ToString().Trim(); // boşsa yalnızca "Hey Pardux" denmiş, komut yok
            }
            else if (cut < 0)
            {
                // Başta kalmış ad ya da "hey" (ör. "Pardux, 15 dakika sonra...", "Hey, kronometreyi başlat")
                int k = 0;
                while (k < tokens.Count - 1 && k < 2)
                {
                    if (LeadHey(TextUtil.Norm(tokens[k].Value))) k++;
                    else if ((n = NameAt(text, tokens, k, false)) > 0 && k + n < tokens.Count) k += n;
                    else break;
                }
                if (k > 0) { text = text.Substring(tokens[k].Index); LastHadWake = true; }
            }
            // Whisper bazen cümleyi iki kez yazar ("… yaz. Eyyemu, … yaz"): tekrarlanan cümle atılır
            var parts = Regex.Split(text, @"(?<=[.!?])\s+").Where(p => p.Trim().Length > 0).ToList();
            if (parts.Count > 1)
            {
                var kept = new List<string>();
                foreach (string p in parts)
                {
                    string np = TextUtil.Norm(Regex.Replace(p, @"[^\p{L}\p{N}\s]", "")).Trim();
                    if (np.Length == 0 || kept.Any(k => { string nk = TextUtil.Norm(Regex.Replace(k, @"[^\p{L}\p{N}\s]", "")).Trim(); return nk.Contains(np) || np.EndsWith(nk); })) continue;
                    kept.Add(p);
                }
                text = string.Join(" ", kept);
            }
            // Sona eklenmiş ad tekrarı: "15 dakika sonra çayı hatırlat. Paper Dux"
            text = Regex.Replace(text, @"[\s,.!?]+(?:hey\s+)?(?:p\w{1,3}r[\s-]?d[uüo](?:x|ks|ps)\w?|pard[uüo](?:x|ks|ps)\w?)[\s,.!?]*$", "", RegexOptions.IgnoreCase);
            // Noktasız tekrar: "Müzik aç Müzik aç Müzik aç" → "Müzik aç"
            var rep = Regex.Match(text, @"^\s*(?<p>\S.{2,}?)(?:[\s,.!?]+\k<p>)+[\s,.!?]*$", RegexOptions.IgnoreCase);
            if (rep.Success) text = rep.Groups["p"].Value;
            // Büyük harfle yazılmışsa (bağırma gibi) sakinleştir
            if (text.Length > 3 && text == text.ToUpper(TextUtil.Tr)) text = text.ToLower(TextUtil.Tr);
            text = text.Trim().TrimEnd('.', '!', '?', ',', ';').Trim();
            return text;
        }
    }
}
