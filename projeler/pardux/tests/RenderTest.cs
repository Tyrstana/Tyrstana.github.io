using System.Collections.Generic;
﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Pardux
{
    /// <summary>Ada ve ayar pencerelerini ekran dışında açıp PNG'ye çizer (ekrana hiçbir şey çıkmaz, odak çalınmaz).</summary>
    static class RenderTest
    {
        static string _out;

        static void Pump(int ms)
        {
            // UI iş parçacığını uyutmadan bekle (çizim kesintisiz sürsün)
            var frame = new DispatcherFrame();
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); frame.Continue = false; };
            t.Start();
            Dispatcher.PushFrame(frame);
        }

        static void Save(Window w, string name, Color bg, int cropH = 0, bool whole = false)
        {
            var fe = whole ? (FrameworkElement)w : (FrameworkElement)w.Content;
            int fullW = (int)Math.Ceiling(fe.ActualWidth), fullH = (int)Math.Ceiling(fe.ActualHeight);
            int width = fullW, height = cropH > 0 ? Math.Min(cropH, fullH) : fullH;
            var layer = new RenderTargetBitmap(fullW, fullH, 96, 96, PixelFormats.Pbgra32);
            layer.Render(fe);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(bg, Color.FromRgb((byte)(bg.R * 0.6), (byte)(bg.G * 0.6), (byte)(bg.B * 0.7)), 90), null, new Rect(0, 0, width, height));
                dc.DrawImage(layer, new Rect(0, 0, fullW, fullH));
            }
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(Path.Combine(_out, name + ".png"))) enc.Save(fs);
        }

        /// <summary>Türkçe (Tolga) sesle konuşmayı 16 kHz mono WAV olarak üretir: ses tanıma testi için.</summary>
        /// <summary>WAV'ı (her örnekleme hızı, 16 bit) Whisper için 16 kHz tek kanala çevirir.</summary>
        static byte[] To16k(byte[] wav)
        {
            int ch = BitConverter.ToInt16(wav, 22), rate = BitConverter.ToInt32(wav, 24);
            int p = 12, data = -1, len = 0;
            while (p + 8 <= wav.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(wav, p, 4);
                int sz = BitConverter.ToInt32(wav, p + 4);
                if (id == "data") { data = p + 8; len = Math.Min(sz, wav.Length - data); break; }
                p += 8 + sz;
            }
            int n = len / 2 / ch;
            int m = (int)((long)n * 16000 / rate);
            var outb = new byte[m * 2];
            for (int i = 0; i < m; i++)
            {
                double src = i * (double)rate / 16000;
                int a = (int)src; double f = src - a;
                int b = Math.Min(n - 1, a + 1);
                double v = BitConverter.ToInt16(wav, data + a * 2 * ch) * (1 - f) + BitConverter.ToInt16(wav, data + b * 2 * ch) * f;
                short s = (short)Math.Max(-32768, Math.Min(32767, v));
                outb[i * 2] = (byte)s; outb[i * 2 + 1] = (byte)(s >> 8);
            }
            return outb;
        }

        static void Tts(string text, string path)
        {
            var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
            var voice = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("tr"));
            if (voice != null) synth.Voice = voice;
            var op = synth.SynthesizeTextToStreamAsync(text);
            while (op.Status == Windows.Foundation.AsyncStatus.Started) Thread.Sleep(10);
            var stream = op.GetResults();
            var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
            var load = reader.LoadAsync((uint)stream.Size);
            while (load.Status == Windows.Foundation.AsyncStatus.Started) Thread.Sleep(10);
            var data = new byte[stream.Size];
            reader.ReadBytes(data);
            int rate = BitConverter.ToInt32(data, 24), ch = BitConverter.ToInt16(data, 22);
            int pos = 12, dataAt = 0, dataLen = 0;
            while (pos + 8 <= data.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                int len = BitConverter.ToInt32(data, pos + 4);
                if (id == "data") { dataAt = pos + 8; dataLen = Math.Min(len, data.Length - dataAt); break; }
                pos += 8 + len;
            }
            int n = dataLen / (2 * ch);
            int outN = (int)((long)n * 16000 / rate);
            int pad = 9600; // 0.6 sn sessizlik (gerçek mikrofondaki gibi)
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + (outN + 2 * pad) * 2); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write((outN + 2 * pad) * 2);
                for (int i = 0; i < pad; i++) w.Write((short)0);
                for (int i = 0; i < outN; i++)
                {
                    double src = (double)i * rate / 16000;
                    int a = (int)src;
                    double f = src - a;
                    int b = Math.Min(n - 1, a + 1);
                    double s0 = BitConverter.ToInt16(data, dataAt + a * 2 * ch), s1 = BitConverter.ToInt16(data, dataAt + b * 2 * ch);
                    w.Write((short)(s0 + (s1 - s0) * f));
                }
            }
        }

        static void Call(object o, string method, params object[] args)
        {
            o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(o, args);
        }

        [STAThread]
        static void Main(string[] args)
        {
            _out = args[0];
            // Testler gerçek ayar dosyasına (kişiler!) asla yazmasın
            Settings.DirOverride = Path.Combine(Path.GetTempPath(), "ParduxTest");
            if (args.Length > 1 && args[1] == "--tts")
            {
                foreach (string t in new[] { "Dinliyorum efendim", "Kronometreyi başlatıyorum", "Ankara için hava 18 derece, parçalı bulutlu" })
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    byte[] wav = Speech.Synthesize(t);
                    int rate = BitConverter.ToInt32(wav, 28);
                    Console.WriteLine(t + "  →  " + (wav.Length / 1024) + " KB, " + ((wav.Length - 44) / (double)rate).ToString("0.0") + " sn ses, " + clock.ElapsedMilliseconds + " ms'de üretildi");
                    File.WriteAllBytes(Path.Combine(_out, "tts_" + t.Split(' ')[0] + ".wav"), wav);
                }
                // Sesli yanıt mikrofonu doğru süre susturuyor mu ve konuşma "hey parduks" sanılıyor mu?
                Tts("Dinliyorum efendim", Path.Combine(_out, "tts.wav"));
                bool woke = false; string cmd = null;
                Voice.Simulate(Path.Combine(_out, "tts.wav"), () => woke = true, x => cmd = x);
                Console.WriteLine("Pardux'un kendi \"Dinliyorum efendim\" sesi uyandırma sayıldı mı: " + woke);
                return;
            }
            if (args.Length > 1 && args[1] == "--tts2")
            {
                // Telaffuz karşılaştırması: aynı cümleler Piper ve Windows sesiyle üretilir, Whisper ne duyduğunu yazar
                string[] lines =
                {
                    "Dinliyorum efendim", "Kronometreyi başlatıyorum", "Tamam, 15 dakika sonra hatırlatacağım",
                    "Ankara için hava 18 derece, parçalı bulutlu", "Everest Dağı'nın yüksekliği 8.848,86 metredir.",
                    "Annene WhatsApp'tan mesajını gönderiyorum", "Mustafa Kemal Atatürk 1881 yılında Selanik'te doğdu.",
                    "Zamanlayıcıyı 10 dakika olarak kuruyorum", "Saat 18:30'da ilacını hatırlatacağım"
                };
                foreach (string t in lines) Console.WriteLine("metin   : " + t + "\n okunuş : " + TrSpeech.Normalize(t));
                foreach (var engine in new[] { "piper", "windows" })
                {
                    Console.WriteLine("\n=== " + engine + " ===");
                    int i = 0;
                    foreach (string t in lines)
                    {
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        byte[] wav = engine == "piper" ? Piper.Synthesize(t) : Speech.SynthesizeWindows(t);
                        long ms = clock.ElapsedMilliseconds;
                        if (wav == null) { Console.WriteLine("ÜRETİLEMEDİ: " + t); continue; }
                        File.WriteAllBytes(Path.Combine(_out, "tts2_" + engine + "_" + (i++) + ".wav"), wav);
                        string heard = Whisper.Transcribe(To16k(wav));
                        Console.WriteLine(ms + " ms  | " + t + "\n          → " + heard);
                    }
                }
                Piper.Stop();
                return;
            }
            if (args.Length > 1 && args[1] == "--voicemem")
            {
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                if (args.Length > 2 && args[2] == "konusma")
                {
                    // Arka planda hiç susmayan konuşma (TV, video): genel tanıyıcı cümle sonu bulamadan sürekli dinler
                    string tv = Path.Combine(_out, "tv.wav");
                    Tts("Bugün hava çok güzel ve güneşli, akşam saatlerinde yağmur bekleniyor ve rüzgar kuzeyden esecek. Haberlerin devamında spor gelişmeleri var ve maç sonuçlarını konuşacağız", tv);
                    var pcm = File.ReadAllBytes(tv).Skip(44).ToArray();
                    Voice.SimulateLoop(pcm, 8, m => { GC.Collect(); proc.Refresh(); Console.WriteLine(m + ". dk (sürekli konuşma): özel " + (proc.PrivateMemorySize64 / 1048576) + " MB"); });
                    return;
                }
                if (args.Length > 2) Voice.TestSkip = int.Parse(args[2]);
                Voice.SimulateSilence(12, m => { GC.Collect(); proc.Refresh(); Console.WriteLine(m + ". dk: özel " + (proc.PrivateMemorySize64 / 1048576) + " MB, yönetilen " + (GC.GetTotalMemory(true) / 1048576) + " MB"); });
                return;
            }
            if (args.Length > 1 && args[1] == "--voice")
            {
                if (args.Length > 2 && args[2] == "noprompt") Whisper.UsePrompt = false;
                foreach (string phrase in new[] { "hey parduks müzik aç", "hey parduks on beş dakika sonra çayı hatırlat", "hey parduks hava durumu nasıl", "hey parduks kronometre başlat", "bugün hava çok güzel" })
                {
                    string wav = Path.Combine(_out, "tts.wav");
                    Tts(phrase, wav);
                    string heard = Voice.RecognizeFile(wav);
                    Console.WriteLine(phrase + "  →  \"" + heard + "\"  | uyanma: " + Voice.IsWake(heard) + "  | komut: " + Voice.Normalize(heard));
                }
                foreach (Sound snd in Enum.GetValues(typeof(Sound))) Sounds.Export(snd, Path.Combine(_out, "ses_" + snd + ".wav"));
                // Gerçek dinleme akışı: uyanma + komut, kelime dağarcığı dışı cümlelerde yanlış uyanma olmamalı
                foreach (string phrase in new[] { "hey parduks, müzik aç", "hey parduks, on beş dakika sonra çayı hatırlat", "hey parduks, kronometre başlat",
                                                  "hey parduks, ankara hava durumu", "hey parduks, annem'e whatsapp'tan akşam geliyorum yaz",
                                                  "hemen geliyorum", "hey millet nasılsınız", "emin misin", "bugün hava çok güzel" })
                {
                    string wav = Path.Combine(_out, "tts.wav");
                    Tts(phrase, wav);
                    bool woke = false; string cmd = null;
                    Voice.Simulate(wav, () => woke = true, t => cmd = t);
                    Console.WriteLine("[akış] " + phrase + "  →  uyandı: " + woke + "  " + string.Join(" ", System.Text.RegularExpressions.Regex.Matches(Voice.LastWakeJson ?? "", "\"conf\"\\s*:\\s*([0-9.]+)[^}]*\"word\"\\s*:\\s*\"(\\w+)\"").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[2].Value + "=" + m.Groups[1].Value.Substring(0, Math.Min(4, m.Groups[1].Value.Length)))) + (cmd != null ? "  komut: \"" + cmd + "\" → \"" + Voice.Normalize(cmd) + "\"" : ""));
                }
                return;
            }
            if (args.Length > 1 && args[1] == "--wakeprobe")
            {
                string a = Path.Combine(_out, "wp_a.wav"), b = Path.Combine(_out, "wp_b.wav");
                Tts("hey parduks", a); Tts("müzik aç", b);
                var pa = File.ReadAllBytes(a).Skip(44).ToArray(); var pb = File.ReadAllBytes(b).Skip(44).ToArray();
                double rms = 0; for (int i = 0; i + 1 < pa.Length; i += 2) { double v = BitConverter.ToInt16(pa, i); rms += v * v; } rms = Math.Sqrt(rms / (pa.Length / 2));
                Console.WriteLine("tolga hey parduks RMS: " + rms.ToString("0") + ", süre " + (pa.Length / 32000.0).ToString("0.00") + " sn");
                var all = new byte[12800].Concat(pa).Concat(new byte[11200]).Concat(pb).Concat(new byte[9600]).ToArray();
                string file = Path.Combine(_out, "wp.wav");
                using (var bw = new BinaryWriter(File.Create(file)))
                {
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + all.Length); bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                    bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(16000); bw.Write(32000); bw.Write((short)2); bw.Write((short)16);
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("data")); bw.Write(all.Length); bw.Write(all);
                }
                foreach (int skip in new[] { 0, 3 })
                {
                    Voice.TestSkip = skip;
                    bool woke = false; string cmd = null;
                    Voice.Simulate(file, () => woke = true, t => cmd = t);
                    Console.WriteLine("kapı " + (skip == 0 ? "açık" : "kapalı") + ": uyandı=" + woke + " komut=" + cmd + "  aday=" + Voice.LastWakeCandidate); Voice.LastWakeCandidate = null;
                }
                Whisper.StopServer();
                return;
            }
            if (args.Length > 1 && args[1] == "--clean")
            {
                foreach (string raw in new[] { "Hey Paget! Yarın İzmir'de hava nasıl?", "Hey pargit! Kuzeye Whatsapp'tan akşam geliyorum yaz!", "Hey Pardit! Not al süt be yumurta.",
                    "Paper Dux 15 dakika sonra çayı hatırlat. Paper Dux", "Hey Pardux! Kronometriyi başlat. Hey Pardux! Hey Pardux!", "Hey Pardux Müzik aç Müzik aç Müzik aç",
                    "Hey Parduxi 15 dakika sonra çayı hatırlat. Hey Parduxi Hey Parduxi", "Çok yoğundu ya. Neyse. Hey Pardups. Not az süt ve yumurta.", "Hey Par-dux, müzik aç",
                    "Heypardüks sesi aç", "Pardux, 10 dakika sonra hatırlat", "Hey Pardux, Pardux'u kapat", "Hey Pardux, bardağı getir", "Hey pardon, müzik aç", "Hey Burak'a yaz geliyorum",
                    "Hey Pardux, Pelin'e WhatsApp'tan yaz, geliyorum" })
                    Console.WriteLine(raw + "\n   → \"" + Whisper.Clean(raw) + "\"  uyandırma: " + Whisper.LastHadWake);
                return;
            }
            if (args.Length > 1 && args[1] == "--pardux")
            {
                // "Hey Pardux": iki sesle, farklı yazımlarla, tek nefeste ve duraklamalı; ayrıca uyandırmaması gereken benzer cümleler
                Func<string, bool, short[]> say = (t, piper) =>
                {
                    byte[] wav;
                    if (piper) wav = To16k(Piper.Synthesize(t));
                    else { string p = Path.Combine(_out, "px.wav"); Tts(t, p); wav = File.ReadAllBytes(p).Skip(44).ToArray(); }
                    var sm = new short[wav.Length / 2];
                    for (int i = 0; i < sm.Length; i++) sm[i] = BitConverter.ToInt16(wav, i * 2);
                    return sm;
                };
                Func<double, short[]> gap = sec => new short[(int)(sec * 16000)];
                Func<System.Collections.Generic.List<short[]>, Tuple<bool, string>> run = parts =>
                {
                    var pcm = parts.SelectMany(x => x).Concat(new short[19200]).ToArray(); // ardından gerçekteki gibi sessizlik
                    string file = Path.Combine(_out, "px_in.wav");
                    using (var bw = new BinaryWriter(File.Create(file)))
                    {
                        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + pcm.Length * 2); bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                        bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(16000); bw.Write(32000); bw.Write((short)2); bw.Write((short)16);
                        bw.Write(System.Text.Encoding.ASCII.GetBytes("data")); bw.Write(pcm.Length * 2);
                        foreach (short v in pcm) bw.Write(v);
                    }
                    bool woke = false; string got = null;
                    Voice.LastWakeCandidate = null;
                    Voice.Simulate(file, () => woke = true, t => got = t);
                    return Tuple.Create(woke, got);
                };
                int ok = 0, total = 0;
                foreach (bool piper in new[] { true, false })
                    foreach (string wake in new[] { "hey parduks", "hey pardüks" })
                        foreach (string cmd in new[] { "müzik aç", "kronometreyi başlat", "on beş dakika sonra çayı hatırlat" })
                            foreach (bool pause in new[] { false, true })
                            {
                                var r = run(new System.Collections.Generic.List<short[]> { gap(0.4), say(wake, piper), gap(pause ? 1.0 : 0.3), say(cmd, piper), gap(0.3) });
                                bool good = r.Item1 && r.Item2 != null && TextUtil.Norm(Voice.Normalize(r.Item2)).Contains(TextUtil.Norm(cmd.Split(' ')[0]));
                                total++; if (good) ok++;
                                Console.WriteLine((good ? "  ✓ " : "  ✗ ") + (piper ? "piper " : "tolga ") + wake + (pause ? " … " : ", ") + cmd + "  →  " + (r.Item1 ? "\"" + r.Item2 + "\"" : "(uyanmadı)") + "   [" + Voice.LastWakeCandidate + " | whisper: " + Whisper.LastRaw + "]");
                            }
                Console.WriteLine("uyanma + komut: " + ok + "/" + total);
                int falseWake = 0, neg = 0;
                foreach (bool piper in new[] { true, false })
                    foreach (string sent in new[] { "pardon, bir şey soracağım", "bu gerçekten bir paradoks", "bardak masanın üstünde duruyor", "dün parkta yürüyüş yaptık",
                                                 "hey millet nasılsınız", "hey dur bakalım", "partiye gidiyor musun", "pardon hocam geç kaldım", "hemen geliyorum" })
                    {
                        var r = run(new System.Collections.Generic.List<short[]> { gap(0.4), say(sent, piper), gap(0.5) });
                        neg++; if (r.Item1) falseWake++;
                        Console.WriteLine((r.Item1 ? "  ✗ YANLIŞ UYANDI " : "  ✓ uyanmadı      ") + (piper ? "piper " : "tolga ") + sent + (r.Item1 ? "  [" + Voice.LastWakeCandidate + "]" : ""));
                    }
                Console.WriteLine("yanlış uyanma: " + falseWake + "/" + neg);
                Whisper.StopServer();
                Piper.Stop();
                return;
            }
            if (args.Length > 1 && args[1] == "--voice2")
            {
                // Zor koşullarda tanıma: iki farklı ses, gürültü + kısık mikrofon, "hey parduks"dan önce konuşma, komutun ortasında duraklama
                if (args.Length > 2) Whisper.ModelOverride = args[2];
                Whisper.ExtraWords = new[] { "kuzey" };
                string[] cmds =
                {
                    "müzik aç", "spotify'da tarkan şımarık çal", "on beş dakika sonra çayı hatırlat", "yarın izmir'de hava nasıl",
                    "kuzey'e whatsapp'tan akşam geliyorum yaz", "kronometreyi başlat", "sesi yüzde kırk yap", "discord'u aç",
                    "everest dağı kaç metre", "sonraki şarkıya geç", "beş dakikalık zamanlayıcı kur", "not al süt ve yumurta"
                };
                var rnd = new Random(7);
                Func<string, bool, short[]> say = (t, piper) =>
                {
                    byte[] wav;
                    if (piper) wav = To16k(Piper.Synthesize(t));
                    else { string p = Path.Combine(_out, "v2.wav"); Tts(t, p); var all = File.ReadAllBytes(p); wav = all.Skip(44).ToArray(); }
                    var sm = new short[wav.Length / 2];
                    for (int i = 0; i < sm.Length; i++) sm[i] = BitConverter.ToInt16(wav, i * 2);
                    return sm;
                };
                Func<double, short[]> gap = sec => new short[(int)(sec * 16000)];
                string[] scen = { "temiz-tolga", "temiz-piper", "gurultu", "onceden-konusma", "duraklama" };
                var totals = new System.Collections.Generic.Dictionary<string, double[]>();
                long ms = 0; int runs = 0;
                foreach (string cmd in cmds)
                {
                    foreach (string sc in scen)
                    {
                        bool piper = sc != "temiz-tolga";
                        var parts = new System.Collections.Generic.List<short[]> { gap(0.4) };
                        if (sc == "onceden-konusma") { parts.Add(say("bugün işler çok yoğundu ya, neyse", piper)); parts.Add(gap(0.5)); }
                        parts.Add(say("hey parduks", piper)); parts.Add(gap(0.35));
                        var words = cmd.Split(' ');
                        if (sc == "duraklama" && words.Length >= 3)
                        {
                            int h = words.Length / 2;
                            parts.Add(say(string.Join(" ", words.Take(h)), piper)); parts.Add(gap(0.85)); parts.Add(say(string.Join(" ", words.Skip(h)), piper));
                        }
                        else parts.Add(say(cmd, piper));
                        parts.Add(gap(0.3));
                        var pcm = parts.SelectMany(x => x).Concat(new short[19200]).ToArray(); // ardından gerçekteki gibi sessizlik
                        if (sc == "gurultu")
                            for (int i = 0; i < pcm.Length; i++)
                                pcm[i] = (short)Math.Max(-32768.0, Math.Min(32767.0, pcm[i] * 0.25 + (rnd.NextDouble() * 2 - 1) * 700));
                        string file = Path.Combine(_out, "v2_in.wav");
                        using (var bw = new BinaryWriter(File.Create(file)))
                        {
                            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + pcm.Length * 2); bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                            bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(16000); bw.Write(32000); bw.Write((short)2); bw.Write((short)16);
                            bw.Write(System.Text.Encoding.ASCII.GetBytes("data")); bw.Write(pcm.Length * 2);
                            foreach (short v in pcm) bw.Write(v);
                        }
                        bool woke = false; string got = null;
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        Voice.Simulate(file, () => woke = true, t => got = t);
                        ms += clock.ElapsedMilliseconds; runs++;
                        string exp = TextUtil.Norm(Voice.Normalize(cmd)), act = TextUtil.Norm(Voice.Normalize(got ?? ""));
                        var ew = exp.Split(new[] { ' ', '\'' }, StringSplitOptions.RemoveEmptyEntries);
                        var aw = act.Split(new[] { ' ', '\'' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        int hit = ew.Count(x => { int k = aw.IndexOf(x); if (k < 0) return false; aw.RemoveAt(k); return true; });
                        double acc = woke ? hit / (double)ew.Length - 0.25 * Math.Min(aw.Count, 2) / Math.Max(1, ew.Length) : 0;
                        if (!totals.ContainsKey(sc)) totals[sc] = new double[2];
                        totals[sc][0] += Math.Max(0, acc); totals[sc][1] += exp == act ? 1 : 0;
                        Console.WriteLine((exp == act ? "  ✓ " : "  ✗ ") + sc.PadRight(16) + cmd + "  →  " + (woke ? "\"" + got + "\"" : "(uyanmadı)") + (exp != act && woke ? "   [whisper: " + Whisper.LastRaw + "]" : ""));
                    }
                }
                Whisper.StopServer();
                Console.WriteLine("\nmodel: " + (Whisper.ModelOverride ?? "turbo") + "   ortalama süre: " + (ms / Math.Max(1, runs)) + " ms/komut");
                foreach (var kv in totals) Console.WriteLine("  " + kv.Key.PadRight(16) + " sözcük doğruluğu %" + (100 * kv.Value[0] / cmds.Length).ToString("0") + "   tam doğru " + kv.Value[1] + "/" + cmds.Length);
                Piper.Stop();
                return;
            }
            if (args.Length > 1 && args[1] == "--news")
            {
                var items = News.Fetch();
                Console.WriteLine("haber sayısı: " + items.Count);
                foreach (var i in items.Take(8)) Console.WriteLine(News.Score(i) + "  " + i.Age + "  " + i.Title + " (" + i.Source + ")");
                var best = News.Best(items) ?? items[0];
                var sw0 = System.Diagnostics.Stopwatch.StartNew();
                News.LoadDetails(best);
                Console.WriteLine("seçilen: " + best.Title + "  [" + sw0.ElapsedMilliseconds + " ms]");
                Console.WriteLine("url: " + best.Url);
                Console.WriteLine("görsel: " + (best.Image != null ? best.Image.PixelWidth + "px" : "yok") + "  " + best.ImageUrl);
                Console.WriteLine("özet: " + best.Summary);
                Console.WriteLine("metin: " + (best.Body == null ? "yok" : best.Body.Substring(0, Math.Min(300, best.Body.Length))));
                Console.WriteLine("diğer kaynak: " + best.Related.Count);
                return;
            }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            IslandWindow.TestMode = true;
            var c = new Controller();
            var s = new Settings();
            typeof(Controller).GetProperty("Settings").SetValue(c, s);
            c.PinOpen = true;
            Controller.AppIcon = System.Drawing.SystemIcons.Application;
            AppIndex.LoadCache();
            IconLoader.Start(Dispatcher.CurrentDispatcher);
            c.Clips.Add(Tuple.Create(DateTime.Now.AddMinutes(-3), "Örnek kopyalanmış metin"));
            s.Theme = "koyu"; s.Accent = "mor";
            Theme.Apply(s);

            // Sahte "şu an çalıyor" (kullanıcının gerçek medyası okunmaz)
            var art = new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(255, 94, 98), Color.FromRgb(255, 195, 113), 45), null, new Rect(0, 0, 160, 160));
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), null, new Point(80, 80), 40, 40);
            }
            art.Render(dv); art.Freeze();
            var np = new NowPlaying { Title = "Örnek Şarkı Adı", Artist = "Örnek Sanatçı", App = "Spotify", IsPlaying = true, CanRepeat = true, Repeat = "list",
                Position = TimeSpan.FromSeconds(83), Duration = TimeSpan.FromSeconds(214), PositionAt = DateTime.Now, Art = art, ArtColor = Color.FromRgb(255, 140, 110) };
            typeof(MediaWatcher).GetProperty("Current").SetValue(null, np);

            var desk = Color.FromRgb(70, 96, 140);
            // Buzlu cam testi için sahte "arka plan": renkli, bulanık lekeler (gerçek ekran yakalanmaz)
            var glassBg = new RenderTargetBitmap(780, 640, 96, 96, PixelFormats.Pbgra32);
            var gdv = new DrawingVisual();
            using (var dc = gdv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(40, 80, 160), Color.FromRgb(200, 90, 140), 20), null, new Rect(0, 0, 780, 640));
                dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(220, 255, 210, 90), Color.FromArgb(0, 255, 210, 90)), null, new Point(250, 60), 220, 160);
                dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(200, 80, 230, 200), Color.FromArgb(0, 80, 230, 200)), null, new Point(560, 300), 260, 220);
            }
            glassBg.Render(gdv); glassBg.Freeze();
            IslandWindow.TestGlass = glassBg;
            if (args.Length > 1 && args[1] == "--bub")
            {
                // Pürüzsüz bulanıklık: keskin yazı ve çizgili sahte masaüstü gerçek bulanıklaştırıcıdan geçirilir
                using (var sharp = new System.Drawing.Bitmap(1560, 1280))
                {
                    using (var g = System.Drawing.Graphics.FromImage(sharp))
                    {
                        g.Clear(System.Drawing.Color.FromArgb(30, 60, 120));
                        for (int i = 0; i < 40; i++)
                            using (var br = new System.Drawing.SolidBrush(i % 2 == 0 ? System.Drawing.Color.FromArgb(250, 200, 60) : System.Drawing.Color.FromArgb(20, 20, 30)))
                                g.FillRectangle(br, i * 40, 0, 20, 1280);
                        using (var f = new System.Drawing.Font("Segoe UI", 40, System.Drawing.FontStyle.Bold))
                            g.DrawString("Masaüstü yazısı  BEYAZ", f, System.Drawing.Brushes.White, 300, 20);
                        g.FillEllipse(System.Drawing.Brushes.Red, 700, 40, 120, 120);
                    }
                    var sw0 = System.Diagnostics.Stopwatch.StartNew();
                    IslandWindow.TestGlass = GlassCapture.Blur(sharp);
                    Console.WriteLine("blur ms: " + sw0.ElapsedMilliseconds + "  " + IslandWindow.TestGlass.PixelWidth + "x" + IslandWindow.TestGlass.PixelHeight);
                }
            }
            var w = new IslandWindow(c);

            if (args.Length > 1 && args[1] == "--home")
            {
                Action keep3 = () => typeof(Controller).GetProperty("Settings").SetValue(c, s);
                c.Reminders.Add(new Controller.Reminder { Text = "Çay", Due = DateTime.Now.AddMinutes(9), Timer = new DispatcherTimer(), IsTimer = true });
                c.Reminders.Add(new Controller.Reminder { Text = "İlacını al", Due = DateTime.Today.AddHours(21), Timer = new DispatcherTimer() });
                w.OpenSearch(); keep3(); w.SetQuery(""); Pump(3500); Save(w, "h1_bugun_koyu", desk, 470);
                w.SetQuery("disc"); Pump(900); Save(w, "h2_yazinca", desk, 470);
                w.SetQuery(""); Pump(900); Save(w, "h3_geri", desk, 470);
                s.Theme = "acik"; Theme.Apply(s); Call(w, "CloseSearch", false); Pump(600);
                w.OpenSearch(); keep3(); w.SetQuery(""); Pump(1500); Save(w, "h4_bugun_acik", Color.FromRgb(225, 230, 240), 470);
                return;
            }
            if (args.Length > 1 && args[1] == "--ask")
            {
                // Uygulamanın gerçek akışı: soru arama kutusuna yazılır, cevap satırı gelene kadar beklenir
                s.WebAnswers = true;
                var list = args.Length > 2 ? args[2].Split(';') : new[] {
                    "messi kaç yaşında", "türkiye'nin en uzun nehri", "elon musk kimdir", "python nedir", "1 inç kaç cm",
                    "ankara istanbul arası kaç km", "dünyanın en büyük okyanusu", "kuantum bilgisayar nedir", "minecraft ne zaman çıktı",
                    "ay'a ilk kim gitti", "fenerbahçe ne zaman kuruldu", "ışık hızı kaç", "pi sayısı", "einstein", "türkiye nüfusu",
                    "google'da en çok satan kitap ara", "mars'ta su var mı", "fotosentez nasıl olur", "everest'in yüksekliği", "istanbul'un fethi" };
                Action keep2 = () => typeof(Controller).GetProperty("Settings").SetValue(c, s);
                int ok = 0;
                foreach (string q in list)
                {
                    w.OpenSearch(); keep2();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    w.SetQuery(q);
                    var results = (System.Windows.Controls.ListBox)typeof(IslandWindow).GetField("Results", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(w);
                    Func<List<ResultItem>> items = () => (results.ItemsSource as List<ResultItem>) ?? new List<ResultItem>();
                    while (sw.ElapsedMilliseconds < 20000 && items().Any(x => x.Title.StartsWith("İnternette araştırılıyor"))) Pump(100);
                    var top = items().FirstOrDefault();
                    bool answered = top != null && top.Info;
                    if (answered) ok++;
                    Console.WriteLine((answered ? "✓ " : "✗ ") + q + "  (" + sw.ElapsedMilliseconds + " ms)\n     " + (top == null ? "-" : top.Title + "  —  " + (top.Subtitle.Length > 200 ? top.Subtitle.Substring(0, 200) + "…" : top.Subtitle)));
                    Pump(1500);
                }
                Console.WriteLine("\ncevaplanan: " + ok + "/" + list.Length);
                return;
            }
            if (args.Length > 1 && args[1] == "--bub")
            {
                s.IslandBlur = true;
                var t1 = new Controller.Reminder { Text = "Çay", Due = DateTime.Now.AddMinutes(4).AddSeconds(30), Timer = new DispatcherTimer(), IsTimer = true };
                var t2 = new Controller.Reminder { Text = "Makarna", Due = DateTime.Now.AddMinutes(9), Timer = new DispatcherTimer(), IsTimer = true };
                var t3 = new Controller.Reminder { Text = "Toplantı", Due = DateTime.Now.AddHours(1).AddMinutes(5), Timer = new DispatcherTimer(), IsTimer = true };
                c.Watch.Start(); c.Reminders.Add(t1); c.Reminders.Add(t2); c.Reminders.Add(t3);
                Call(w, "GoRest"); Pump(1500); Save(w, "m1_muzik_3sayac", desk, 70);
                Call(w, "Hover", true); Pump(900); Save(w, "m2_hover", desk, 70);
                // Fare kapsülden baloncuğa geçerken kısa ayrılış: baloncuklar kapanmamalı
                Call(w, "Hover", true); Pump(80); Save(w, "m3_hover_tekrar", desk, 70);
                Call(w, "OnBubbleClick", 1); Pump(1200); Save(w, "m4_kart_makarna", desk, 120);
                Call(w, "CycleWatch"); Pump(600); Save(w, "m5_kart_sonraki", desk, 120);
                Call(w, "CycleWatch"); Pump(600); Save(w, "m6_kart_sonraki2", desk, 120);
                Call(w, "GoRest"); Pump(900);
                Call(w, "Hover", false); Pump(700);
                // Müzik yok: ana kapsül en yakın zamanlayıcı, diğerleri baloncukta
                typeof(MediaWatcher).GetProperty("Current").SetValue(null, null);
                Call(w, "RefreshRest"); Pump(1300); Save(w, "n1_muziksiz", desk, 70);
                Call(w, "Hover", true); Pump(900); Save(w, "n2_muziksiz_hover", desk, 70);
                Call(w, "Hover", false); Pump(600);
                // Biri bitti: baloncuklar yeniden dizilir
                c.Reminders.Remove(t1); Call(w, "RefreshRest"); Pump(1000); Save(w, "n3_biri_bitti", desk, 70);
                c.Reminders.Remove(t2); c.Reminders.Remove(t3); Call(w, "RefreshRest"); Pump(1000); Save(w, "n4_yalniz_krono", desk, 70);
                s.IslandStyle = "yapisik";
                c.Reminders.Add(t2); Call(w, "RefreshRest"); Pump(1300); Save(w, "n5_yapisik", desk, 70);
                Call(w, "Hover", true); Pump(900); Save(w, "n6_yapisik_hover", desk, 70);
                return;
            }

            if (args.Length > 1 && args[1] == "--new")
            {
                Action keep = () => typeof(Controller).GetProperty("Settings").SetValue(c, s); // arama paneli gerçek ayar dosyasını yükler
                var timerR = new Controller.Reminder { Text = "Çay", Due = DateTime.Now.AddMinutes(4).AddSeconds(30), Timer = new DispatcherTimer(), IsTimer = true };
                // Çoklu canlı etkinlik: müzik + kronometre + zamanlayıcı
                c.Watch.Start(); c.Reminders.Add(timerR);
                Call(w, "GoRest"); Pump(1500); Save(w, "b1_baloncuk", desk, 70);
                Call(w, "Hover", true); Pump(900); Save(w, "b2_baloncuk_hover", desk, 70);
                Call(w, "Hover", false); Pump(700);
                Call(w, "OnBubbleClick", 0); Pump(1200); Save(w, "b3_baloncuktan_kart", desk, 120);
                Call(w, "GoRest"); Pump(900);
                // Üste yapışık stil
                s.IslandStyle = "yapisik";
                Call(w, "ShowMedia"); Pump(300); Call(w, "GoRest"); Pump(1300); Save(w, "a1_yapisik_kompakt", desk, 70);
                Call(w, "ShowMedia"); Pump(1300); Save(w, "a2_yapisik_medya", desk, 220);
                w.OpenSearch(); keep(); w.SetQuery("discord"); Pump(1300); Save(w, "a3_yapisik_arama", desk, 250);
                Call(w, "CloseSearch", false); keep(); Pump(900);
                s.IslandStyle = "yuzen";
                // Gözler
                c.Watch.Reset(); c.Reminders.Clear(); s.IslandIdle = true;
                typeof(MediaWatcher).GetProperty("Current").SetValue(null, null);
                Call(w, "GoRest"); Pump(1200);
                foreach (var e in new[] { new[] { "#FFFFFF", "orta", "oval" }, new[] { "#5EEAD4", "buyuk", "yuvarlak" }, new[] { "#F472B6", "kucuk", "cizgi" }, new[] { "vurgu", "buyuk", "oval" } })
                {
                    s.EyeColor = e[0]; s.EyeSize = e[1]; s.EyeShape = e[2];
                    Call(w, "ApplyEyes"); Pump(300); Save(w, "e_" + e[1] + "_" + e[2], desk, 60);
                }
                // İnternetten cevap
                w.OpenSearch(); keep(); w.SetQuery("everest dağı kaç metre"); Pump(4500); Save(w, "r1_arastirma", desk, 520);
                w.ShowSettings("Ada"); keep(); Pump(1200); Save(w, "p_ada_yeni", desk, 680);
                ((SettingsPanel)typeof(IslandWindow).GetField("ViewSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(w)).ShowTab("Ses");
                Pump(900); Save(w, "p_ses_yeni", desk, 560);
                return;
            }
            if (args.Length > 1 && args[1] == "--overlap")
            {
                // Hızlı geçişlerden sonra yalnızca etkin görünüm görünür olmalı (üst üste binme testi)
                s.IslandIdle = true;
                typeof(MediaWatcher).GetProperty("Current").SetValue(null, null);
                Func<string> visible = () => string.Join(", ", new[] { "ViewIdle", "ViewCompact", "ViewDot", "ViewAlert", "ViewMedia", "ViewWatch", "ViewSearch", "ViewSettings" }
                    .Select(n => new { n, v = (UIElement)typeof(IslandWindow).GetField(n, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(w) })
                    .Where(x => x.v.Opacity > 0.02).Select(x => x.n.Substring(4) + "=" + x.v.Opacity.ToString("0.00")));
                var mode = typeof(IslandWindow).GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic);
                Action<string, Action, int> step = (name, act, ms) => { act(); Pump(ms); Console.WriteLine(name.PadRight(48) + " mod=" + mode.GetValue(w) + "  görünen: " + visible()); };
                step("boşta (gözler)", () => Call(w, "GoRest"), 1200);
                step("arama aç → kapat → hemen kronometre", () => { w.OpenSearch(); Pump(700); Call(w, "CloseSearch", false); c.Watch.Start(); w.RefreshRest(); }, 1500);
                step("kronometre kartı aç", () => Call(w, "ShowWatch"), 1200);
                step("kart → dışarı tıklama (dinlenme)", () => Call(w, "GoRest"), 1200);
                step("noktaya küçült", () => { typeof(IslandWindow).GetField("_dotted", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(w, true); Call(w, "GoRest"); }, 1200);
                step("noktadan geri aç", () => { typeof(IslandWindow).GetField("_dotted", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(w, false); Call(w, "GoRest"); }, 1200);
                step("bildirim → hemen sıfırla (boşta)", () => { w.ShowNotice("Deneme", null, null, null); Pump(60); c.Watch.Reset(); w.RefreshRest(); }, 4500);
                step("medya başla → hemen kart", () => { typeof(MediaWatcher).GetProperty("Current").SetValue(null, np); Call(w, "GoRest"); Pump(40); Call(w, "ShowMedia"); }, 1300);
                step("kart → arama → ayarlar → arama → kapat", () => { w.OpenSearch(); Pump(80); w.ShowSettings(null); Pump(80); w.OpenSearch(); Pump(80); Call(w, "CloseSearch", false); }, 1500);
                step("hızlı: uyarı → kompakt → nokta → boş", () => { w.ShowNotice("a", null, null, null); Pump(30); Call(w, "EndAlert"); Pump(30);
                    typeof(IslandWindow).GetField("_dotted", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(w, true); Call(w, "GoRest"); Pump(30);
                    typeof(MediaWatcher).GetProperty("Current").SetValue(null, null); Call(w, "GoRest"); }, 1500);
                c.Watch.Start(); Call(w, "GoRest"); Pump(1300); Save(w, "o1_kronometre_bosta", desk, 70);
                Call(w, "ShowMinButton", true); Pump(400); Save(w, "o2_kronometre_hover", desk, 70);
                return;
            }

            if (args.Length > 1 && args[1] == "--fps")
            {
                var gaps = new System.Collections.Generic.List<double>();
                var clock = new System.Diagnostics.Stopwatch();
                double last = 0;
                EventHandler onFrame = (o, e) => { double t = clock.Elapsed.TotalMilliseconds; if (last > 0) gaps.Add(t - last); last = t; };
                Action<string, Action, int> measure = (name, act, ms) =>
                {
                    gaps.Clear(); last = 0; clock.Restart();
                    CompositionTarget.Rendering += onFrame;
                    act();
                    Pump(ms);
                    CompositionTarget.Rendering -= onFrame;
                    gaps.Sort();
                    double p95 = gaps.Count > 0 ? gaps[(int)(gaps.Count * 0.95)] : 0;
                    Console.WriteLine(string.Format("{0,-28} kare={1,3}  ort={2,5:0.0}ms  p95={3,5:0.0}ms  en kötü={4,5:0.0}ms  >33ms takılma={5}",
                        name, gaps.Count, gaps.Count > 0 ? gaps.Average() : 0, p95, gaps.Count > 0 ? gaps.Max() : 0, gaps.Count(g => g > 33)));
                };
                Call(w, "ShowCompact"); Pump(1500);
                measure("Arama açılışı (kompakttan)", () => w.OpenSearch(), 900);
                measure("Yazınca yükseklik değişimi", () => w.SetQuery("discord"), 700);
                measure("Arama kapanışı", () => Call(w, "CloseSearch", false), 800);
                measure("Medya kartı açılışı", () => Call(w, "ShowMedia"), 900);
                measure("Medya kartı kapanışı", () => Call(w, "GoRest"), 800);
                measure("Bildirim", () => w.ShowNotice("Şarj oluyor", "%83", G.Battery, Palette.Calc), 900);
                Pump(3000);

                // Çoklu canlı etkinlik + buzlu cam (gerçek bulanıklaştırma maliyetiyle, saniyelik tazeleme dahil)
                var src = new System.Drawing.Bitmap(780, 640);
                using (var g = System.Drawing.Graphics.FromImage(src))
                {
                    g.Clear(System.Drawing.Color.FromArgb(40, 70, 130));
                    for (int i = 0; i < 30; i++) using (var br = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(200, (i * 37) % 255, (i * 91) % 255, 120))) g.FillEllipse(br, (i * 53) % 700, (i * 29) % 600, 90, 70);
                }
                IslandWindow.TestBlurSource = src;
                s.IslandBlur = true;
                Call(w, "GoRest"); Pump(1500);
                var t1 = new Controller.Reminder { Text = "Çay", Due = DateTime.Now.AddMinutes(4), Timer = new DispatcherTimer(), IsTimer = true };
                var t2 = new Controller.Reminder { Text = "Makarna", Due = DateTime.Now.AddMinutes(9), Timer = new DispatcherTimer(), IsTimer = true };
                measure("Baloncuk belirir (1)", () => { c.Reminders.Add(t1); Call(w, "RefreshRest"); }, 1200);
                measure("Baloncuk belirir (2, +N)", () => { c.Watch.Start(); c.Reminders.Add(t2); Call(w, "RefreshRest"); }, 1200);
                measure("Fare üstüne: baloncuklar açılır", () => Call(w, "Hover", true), 1200);
                measure("Fare ayrılır: kapanır", () => Call(w, "Hover", false), 1200);
                measure("Baloncuktan sayaç kartı", () => Call(w, "OnBubbleClick", 0), 1200);
                measure("Kartta sıradaki sayaç", () => Call(w, "CycleWatch"), 800);
                measure("Kart → kapsül", () => Call(w, "GoRest"), 1200);
                measure("Bekleme 3 sn (cam tazeleme)", () => { }, 3000);
                measure("Hover açıkken bekleme 3 sn", () => Call(w, "Hover", true), 3000);
                measure("Sayaç bitti (baloncuk kalkar)", () => { Call(w, "Hover", false); c.Reminders.Remove(t1); Call(w, "RefreshRest"); }, 1200);
                measure("Medya kartı (baloncuklu)", () => Call(w, "ShowMedia"), 1200);
                measure("Medya → kapsül", () => Call(w, "GoRest"), 1200);
                return;
            }

            if (args.Length > 1 && args[1] == "--cpu")
            {
                Call(w, "ShowCompact");
                Pump(1500);
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                var t0 = proc.TotalProcessorTime;
                Pump(10000);
                proc.Refresh();
                Console.WriteLine("kompakt ada (müzik çalıyor), 10 sn: " + (proc.TotalProcessorTime - t0).TotalSeconds.ToString("0.00") + " sn CPU");
                return;
            }

            s.IslandIdle = true;
            typeof(MediaWatcher).GetProperty("Current").SetValue(null, null);
            Call(w, "ShowIdle"); Pump(1100); Save(w, "i1_idle", desk, 80);

            typeof(MediaWatcher).GetProperty("Current").SetValue(null, np);
            Call(w, "ShowCompact"); Pump(1100); Save(w, "i2_compact", desk, 80);

            c.Reminders.Add(new Controller.Reminder { Text = "Çay", Due = DateTime.Now.AddMinutes(4).AddSeconds(30), Timer = new DispatcherTimer() });
            Call(w, "ShowCompact"); Pump(1100); Save(w, "i3_compact_timer", desk, 80);
            c.Reminders.Clear();

            Call(w, "ShowMinButton", true); Pump(400); Save(w, "i3b_compact_hover", desk, 80);
            Call(w, "ShowMinButton", false);
            typeof(IslandWindow).GetField("_dotted", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(w, true);
            Call(w, "GoRest"); Pump(1100); Save(w, "i3c_dot", desk, 70);
            typeof(IslandWindow).GetField("_dotted", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(w, false);
            Call(w, "GoRest"); Pump(900);

            Call(w, "ShowMedia"); Pump(1300); Save(w, "i4_media", desk, 240);

            w.ShowNotice("Şarj oluyor", "%83", G.Battery, Palette.Calc); Pump(1200); Save(w, "i5_notice", desk, 120);
            Call(w, "EndAlert"); Pump(600);
            w.ShowReminder("Çayı demlemeyi unutma", () => { }); Pump(1200); Save(w, "i6_reminder", desk, 160);
            Call(w, "EndAlert"); Pump(600);
            w.ShowNotice("Not kaydedildi", null, null, null); Pump(1200); Save(w, "i7_notice_logo", desk, 110);
            Call(w, "EndAlert"); Pump(600);

            w.OpenSearch();
            Pump(110); Save(w, "k1_acilis_110ms", desk, 520);
            Pump(130); Save(w, "k2_acilis_240ms", desk, 520);
            Pump(160); Save(w, "k3_acilis_400ms", desk, 520);
            Pump(1000); Save(w, "i8_search_bos", desk, 520);
            w.SetQuery("discord"); Pump(1000); Save(w, "i9_search_discord", desk, 260);
            w.SetQuery("ankara hava"); Pump(3500); Save(w, "j1_search_hava", desk, 460);
            w.ShowCompose("anne@ornek.com", "Hafta sonu", ""); Pump(1200); Save(w, "j2_compose", desk, 560);

            // Açık temada da ada siyah kalmalı; ayarlar açık temada
            s.Theme = "acik"; s.Accent = "mavi"; Theme.Apply(s);
            Call(w, "CloseCompose"); w.SetQuery("100 dolar kaç tl"); Pump(3000); Save(w, "j3_search_acik_dolar", Color.FromRgb(200, 210, 225), 240);

            // Adanın içindeki ayarlar
            typeof(Controller).GetProperty("Settings").SetValue(c, s);
            s.Theme = "koyu"; s.Accent = "mor"; Theme.Apply(s);
            c.Settings.Contacts["Ahmet"] = "ahmet@ornek.com"; c.Settings.Phones["Ahmet"] = "0555 111 22 33"; c.Settings.Instagram["Ahmet"] = "ahmet.yilmaz";
            w.ShowSettings("Genel"); Pump(1200); Save(w, "p1_ayar_genel", desk, 640);
            foreach (string p in new[] { "Gorunum", "Ada", "Ses", "Bildirim", "Kisi", "Hakkinda" })
            {
                ((SettingsPanel)typeof(IslandWindow).GetField("ViewSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(w)).ShowTab(p);
                Pump(900); Save(w, "p_ayar_" + p, desk, 640);
            }
            Call(w, "CloseSettings", false); Pump(900);

            // Kronometre ve zamanlayıcı
            c.Watch.Start(); Call(w, "GoRest"); Pump(1300); Save(w, "w1_compact_kronometre", desk, 70);
            Call(w, "ShowWatch"); Pump(1200); Save(w, "w2_kronometre_kart", desk, 120);
            c.Watch.Reset(); Call(w, "GoRest"); Pump(800);
            typeof(MediaWatcher).GetProperty("Current").SetValue(null, np);
            s.IslandSize = "kucuk"; Call(w, "GoRest"); Pump(900); Save(w, "z1_kucuk", desk, 70);
            s.IslandSize = "buyuk"; Call(w, "GoRest"); Pump(900); Save(w, "z2_buyuk", desk, 70);
            s.IslandSize = "orta";
            w.ShowListening(); Pump(900); w.UpdateListening("on beş dakika sonra çayı hatırlat"); Pump(500); Save(w, "v1_dinliyorum", desk, 120);
            w.Close();

            foreach (string theme in new[] { "acik", "koyu" })
            {
                // Arama paneli gerçek ayar dosyasını yeniden yükler; test ayarlarına geri dön
                typeof(Controller).GetProperty("Settings").SetValue(c, s);
                s.Theme = theme; s.Accent = theme == "acik" ? "mavi" : "mor"; Theme.Apply(s);
                var bgc = theme == "acik" ? Color.FromRgb(200, 210, 225) : Color.FromRgb(40, 44, 60);
                // Haber bildirimi ve ayrıntı penceresi (sahte haber; ağ kullanılmaz)
                var news = new NewsItem
                {
                    Id = "x", Title = "Merkez Bankası faiz kararını açıkladı: politika faizi yüzde 35'te sabit tutuldu", Source = "Örnek Haber",
                    Link = "https://example.com", Published = DateTime.UtcNow.AddMinutes(-12), DetailsLoaded = true, Image = art,
                    Summary = "Para Politikası Kurulu toplantısının ardından yapılan açıklamada enflasyondaki düşüş eğiliminin sürdüğü belirtildi.",
                    Body = "Kurul, öncü göstergelerin talep koşullarındaki dengelenmeye işaret ettiğini vurguladı. Açıklamada, sıkı para politikası duruşunun fiyat istikrarı sağlanana kadar korunacağı ifade edildi.\n\nPiyasalar kararın ardından sakin bir seyir izledi."
                };
                news.Related.Add(new NewsLink { Title = "Faiz kararı sonrası piyasalarda ilk tepki", Source = "Ekonomi Gazetesi", Link = "https://example.com/1" });
                news.Related.Add(new NewsLink { Title = "Uzmanlar kararı değerlendirdi", Source = "Haber Kanalı", Link = "https://example.com/2" });
                var nt = (Window)Activator.CreateInstance(typeof(NewsToast), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { news }, null);
                nt.Left = -8000; nt.Show(); Pump(1200);
                Save(nt, "n_toast_" + theme, desk, 0, true);
                nt.Close();
                var nw = new NewsWindow(news);
                nw.ShowActivated = false; nw.ShowInTaskbar = false; nw.WindowStartupLocation = WindowStartupLocation.Manual; nw.Left = -8000; nw.Show();
                Pump(1000);
                Save(nw, "n_window_" + theme, bgc);
                nw.Close();
            }
            Console.WriteLine("tamam");
        }
    }
}
