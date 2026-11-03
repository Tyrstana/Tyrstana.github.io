using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Pardux
{
    /// <summary>
    /// Doğal Türkçe ses: Piper (çevrimdışı yapay zekâ seslendirme, dist\piper). Model bir kez yüklenip arka planda
    /// açık tutulur, her cümle yarım saniyenin altında hazırlanır; bir süre kullanılmayınca kapanıp belleği bırakır.
    /// </summary>
    internal static class Piper
    {
        static string Dir { get { return Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "piper"); } }
        static string Exe { get { return Path.Combine(Dir, "piper.exe"); } }
        static string Model { get { return Path.Combine(Dir, "tr_TR-dfki-medium.onnx"); } }

        public static bool Available { get { return File.Exists(Exe) && File.Exists(Model); } }

        static readonly object Gate = new object();
        static Process _proc;
        static readonly Queue<string> _lines = new Queue<string>();
        static readonly AutoResetEvent _lineReady = new AutoResetEvent(false);
        static Timer _idle;
        static int _seq;

        /// <summary>Ses modelini önceden yükler (ör. "Hey Pardux" duyulunca), ilk cevap beklemesin.</summary>
        public static void Warm()
        {
            if (!Available) return;
            ThreadPool.QueueUserWorkItem(_ => { try { lock (Gate) Ensure(); } catch { } });
        }

        static void Ensure()
        {
            if (_proc != null && !_proc.HasExited) { KeepAlive(); return; }
            // Doğal tempo (hızlandırınca "r" sesleri yutuluyordu); cümle arası kısa sessizlik
            var psi = new ProcessStartInfo(Exe, "-m \"" + Model + "\" --json-input --length_scale 1.0 --sentence_silence 0.12 -q")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Dir,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                lock (_lines) _lines.Enqueue(e.Data);
                _lineReady.Set();
            };
            p.ErrorDataReceived += (s, e) => { };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
            _proc = p;
            KeepAlive();
        }

        /// <summary>5 dakika konuşulmazsa süreç kapanır (model ~100 MB bellek tutar).</summary>
        static void KeepAlive()
        {
            if (_idle == null) _idle = new Timer(_ => Stop(), null, Timeout.Infinite, Timeout.Infinite);
            _idle.Change(TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(-1));
        }

        public static void Stop()
        {
            lock (Gate)
            {
                try { if (_proc != null && !_proc.HasExited) { _proc.StandardInput.Close(); if (!_proc.WaitForExit(800)) _proc.Kill(); } } catch { }
                if (_proc != null) _proc.Dispose();
                _proc = null;
            }
        }

        /// <summary>Metni WAV (22 kHz, 16 bit, tek kanal) olarak üretir. Başarısızsa null.</summary>
        public static byte[] Synthesize(string text)
        {
            if (!Available || string.IsNullOrWhiteSpace(text)) return null;
            lock (Gate)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    string file = Path.Combine(Path.GetTempPath(), "pardux_ses_" + Process.GetCurrentProcess().Id + "_" + (++_seq) + ".wav");
                    try
                    {
                        Ensure();
                        lock (_lines) _lines.Clear();
                        string json = "{\"text\": " + JsonString(TrSpeech.Normalize(text)) + ", \"output_file\": " + JsonString(file) + "}";
                        _proc.StandardInput.WriteLine(json);
                        _proc.StandardInput.Flush();
                        // Piper her cümle bitince dosyanın yolunu yazar
                        var end = DateTime.Now.AddSeconds(attempt == 0 ? 15 : 25);
                        while (DateTime.Now < end)
                        {
                            string line = null;
                            lock (_lines) if (_lines.Count > 0) line = _lines.Dequeue();
                            if (line == null) { _lineReady.WaitOne(100); if (_proc.HasExited) break; continue; }
                            if (line.Trim().EndsWith(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                                return ReadAndDelete(file);
                        }
                    }
                    catch { }
                    // Süreç takıldı ya da kapandı: yeniden başlatıp bir kez daha dene
                    try { if (_proc != null && !_proc.HasExited) _proc.Kill(); } catch { }
                    _proc = null;
                    try { File.Delete(file); } catch { }
                }
                return null;
            }
        }

        static byte[] ReadAndDelete(string file)
        {
            byte[] data = null;
            for (int i = 0; i < 10 && data == null; i++)
            {
                try { data = File.ReadAllBytes(file); } catch (IOException) { Thread.Sleep(20); }
            }
            try { File.Delete(file); } catch { }
            return data != null && data.Length > 44 ? data : null;
        }

        static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append(' ');
                // Türkçe harfler \uXXXX olarak yazılır: pencereli uygulamada sürecin girişi UTF-8 değil ANSI kod sayfasıyla
                // yazılıyor, "ı", "ş" gibi harfler Piper'ın JSON okuyucusunu bozuyordu
                else if (c > 126) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }

    /// <summary>
    /// Seslendirmeden önce Türkçe metni okunuşa çevirir: sayılar, ondalıklar, saatler, yüzdeler, birimler ve kısaltmalar
    /// yazıyla söylenir ("8.848,86 m" → "sekiz bin sekiz yüz kırk sekiz virgül seksen altı metre").
    /// </summary>
    internal static class TrSpeech
    {
        static readonly string[] Ones = { "", "bir", "iki", "üç", "dört", "beş", "altı", "yedi", "sekiz", "dokuz" };
        static readonly string[] Tens = { "", "on", "yirmi", "otuz", "kırk", "elli", "altmış", "yetmiş", "seksen", "doksan" };
        static readonly string[] Groups = { "", "bin", "milyon", "milyar", "trilyon" };

        public static string Words(long n)
        {
            if (n == 0) return "sıfır";
            if (n < 0) return "eksi " + Words(-n);
            var parts = new List<string>();
            int g = 0;
            while (n > 0 && g < Groups.Length)
            {
                int chunk = (int)(n % 1000);
                n /= 1000;
                if (chunk > 0)
                {
                    string w = Hundreds(chunk);
                    if (g == 1 && chunk == 1) w = ""; // "bir bin" değil "bin"
                    parts.Insert(0, (w + " " + Groups[g]).Trim());
                }
                g++;
            }
            return string.Join(" ", parts);
        }

        static string Hundreds(int n)
        {
            var sb = new List<string>();
            int h = n / 100, t = (n / 10) % 10, o = n % 10;
            if (h > 0) sb.Add((h > 1 ? Ones[h] + " " : "") + "yüz");
            if (t > 0) sb.Add(Tens[t]);
            if (o > 0) sb.Add(Ones[o]);
            return string.Join(" ", sb);
        }

        static string Digits(string d)
        {
            // Ondalık kısım: baştaki sıfırlar tek tek okunur ("0,05" → "sıfır virgül sıfır beş")
            var sb = new StringBuilder();
            int i = 0;
            while (i < d.Length - 1 && d[i] == '0') { sb.Append("sıfır "); i++; }
            long v;
            if (long.TryParse(d.Substring(i), out v)) sb.Append(Words(v));
            return sb.ToString().Trim();
        }

        static readonly KeyValuePair<string, string>[] Units =
        {
            new KeyValuePair<string, string>(@"°\s*C\b", " derece"), new KeyValuePair<string, string>("°", " derece"),
            new KeyValuePair<string, string>(@"\bkm/sa\b", " kilometre hız"), new KeyValuePair<string, string>(@"\bkm²", " kilometrekare"),
            new KeyValuePair<string, string>(@"(?<=\d)\s*km\b", " kilometre"), new KeyValuePair<string, string>(@"(?<=\d)\s*cm\b", " santimetre"),
            new KeyValuePair<string, string>(@"(?<=\d)\s*mm\b", " milimetre"), new KeyValuePair<string, string>(@"(?<=\d)\s*m\b", " metre"),
            new KeyValuePair<string, string>(@"(?<=\d)\s*kg\b", " kilogram"), new KeyValuePair<string, string>(@"(?<=\d)\s*gr?\b", " gram"),
            new KeyValuePair<string, string>(@"(?<=\d)\s*dk\b\.?", " dakika"), new KeyValuePair<string, string>(@"(?<=\d)\s*sn\b\.?", " saniye"),
            new KeyValuePair<string, string>(@"(?<=\d)\s*sa\b\.?", " saat"), new KeyValuePair<string, string>(@"(?<=\d)\s*TL\b", " lira"),
            new KeyValuePair<string, string>(@"₺", " lira"), new KeyValuePair<string, string>(@"\$", " dolar"), new KeyValuePair<string, string>(@"€", " avro"),
            new KeyValuePair<string, string>(@"\bvb\.", "ve benzeri"), new KeyValuePair<string, string>(@"\bvs\.", "vesaire"),
            new KeyValuePair<string, string>(@"\bM\.Ö\.", "milattan önce"), new KeyValuePair<string, string>(@"\bM\.S\.", "milattan sonra"),
            new KeyValuePair<string, string>(@"\bDr\.", "doktor"), new KeyValuePair<string, string>(@"\bProf\.", "profesör"),
            new KeyValuePair<string, string>(@"\bWhatsApp\b", "vatsap"), new KeyValuePair<string, string>(@"\bSpotify\b", "spotifay"),
            new KeyValuePair<string, string>(@"\bYouTube\b", "yutub"), new KeyValuePair<string, string>(@"\bInstagram\b", "instagram"),
        };

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("–", ", ").Replace("—", ", ").Replace("·", ", ").Replace("&", " ve ");
            // Asistanın adı Türkçe okunuşuyla: "Pardux" → "Parduks" (yoksa "x" harf adıyla okunuyor)
            s = Regex.Replace(s, @"\bPardux", "Parduks", RegexOptions.IgnoreCase);
            foreach (var u in Units) s = Regex.Replace(s, u.Key, u.Value, RegexOptions.IgnoreCase);
            // Saat: 18:30 → "on sekiz otuz", 09:05 → "dokuz sıfır beş"
            s = Regex.Replace(s, @"\b(\d{1,2})[:.](\d{2})\b(?![.,]\d)", m =>
            {
                int h = int.Parse(m.Groups[1].Value), mi = int.Parse(m.Groups[2].Value);
                if (h > 24 || mi > 59) return m.Value;
                return Words(h) + (mi == 0 ? "" : " " + (mi < 10 ? "sıfır " : "") + Words(mi));
            });
            // Yüzde: %35 ya da 35% → "yüzde otuz beş"
            s = Regex.Replace(s, @"%\s*(\d+(?:[.,]\d+)?)", "yüzde $1");
            s = Regex.Replace(s, @"(\d+(?:[.,]\d+)?)\s*%", "yüzde $1");
            // Binlik ayraçlı sayılar: Türkçe (8.848,86) ve İngilizce (8,848.86) biçimleri
            s = Regex.Replace(s, @"\b(\d{1,3}(?:\.\d{3})+)(?:,(\d+))?\b", m => Num(m.Groups[1].Value.Replace(".", ""), m.Groups[2].Value));
            s = Regex.Replace(s, @"\b(\d{1,3}(?:,\d{3})+)(?:\.(\d+))?\b", m => Num(m.Groups[1].Value.Replace(",", ""), m.Groups[2].Value));
            // Ondalık: 3,14 / 3.14
            s = Regex.Replace(s, @"\b(\d+)[,.](\d+)\b", m => Num(m.Groups[1].Value, m.Groups[2].Value));
            // Sıra sayıları: "1." cümle sonu değilse ("1. Dünya Savaşı" → "birinci")
            s = Regex.Replace(s, @"\b(\d{1,4})\.(?=\s+\p{Ll}|\s+[A-ZÇĞİÖŞÜ]\p{Ll}+\s)", m => Ordinal(long.Parse(m.Groups[1].Value)));
            // Kalan tam sayılar (Türkçe ekleriyle: "1923'te" → "bin dokuz yüz yirmi üç'te" → kesme işareti atılır)
            s = Regex.Replace(s, @"\d+", m =>
            {
                long v;
                return long.TryParse(m.Value, out v) && m.Value.Length <= 15 ? " " + Words(v) + " " : m.Value;
            });
            s = Regex.Replace(s, @"(\p{L})\s*'\s*(\p{Ll})", "$1$2");
            s = Regex.Replace(s, @"\s+([,.!?;:])", "$1");
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();
            return s;
        }

        static string Num(string whole, string frac)
        {
            long w;
            if (!long.TryParse(whole, out w)) return whole + (frac.Length > 0 ? "," + frac : "");
            string r = Words(w);
            if (!string.IsNullOrEmpty(frac)) r += " virgül " + Digits(frac);
            return " " + r + " ";
        }

        static string Ordinal(long n)
        {
            string w = Words(n);
            char last = ' ';
            for (int i = w.Length - 1; i >= 0; i--) if ("aeıioöuü".IndexOf(w[i]) >= 0) { last = w[i]; break; }
            bool endsVowel = "aeıioöuü".IndexOf(w[w.Length - 1]) >= 0;
            string suf = "aı".IndexOf(last) >= 0 ? "ıncı" : "ei".IndexOf(last) >= 0 ? "inci" : "ou".IndexOf(last) >= 0 ? "uncu" : "üncü";
            return w + (endsVowel ? suf.Substring(1) : suf);
        }
    }
}
