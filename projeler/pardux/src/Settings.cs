using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Pardux
{
    public class CustomCommand
    {
        public string Name { get; set; }
        public string Target { get; set; }
        public string Args { get; set; }
    }

    public class Settings
    {
        public string Hotkey { get; set; }
        public string MailProvider { get; set; }
        public bool SpotifyAutoPlay { get; set; }
        public bool StartWithWindows { get; set; }
        public bool FirstRunDone { get; set; }
        public Dictionary<string, string> Contacts { get; set; }
        /// <summary>Kişi adı → telefon numarası (WhatsApp için).</summary>
        public Dictionary<string, string> Phones { get; set; }
        /// <summary>Kişi adı → Instagram kullanıcı adı.</summary>
        public Dictionary<string, string> Instagram { get; set; }
        /// <summary>Arama/ayar panelinin arkasında buzlu cam (bulanık masaüstü).</summary>
        public bool IslandGlass { get; set; }
        /// <summary>Hep görünen adanın kendisi (kapsül, müzik kartı, bildirimler) de buzlu cam olsun.</summary>
        public bool IslandBlur { get; set; }
        /// <summary>Boştaki adanın gözleri: renk (#RRGGBB ya da "vurgu"), boyut (kucuk/orta/buyuk), şekil (oval/yuvarlak/cizgi).</summary>
        public string EyeColor { get; set; }
        public string EyeSize { get; set; }
        public string EyeShape { get; set; }
        /// <summary>Ada stili: "yuzen" (ekranın üstünde boşlukla) ya da "yapisik" (ekranın üst kenarına yapışık).</summary>
        public string IslandStyle { get; set; }
        /// <summary>Sesle sorulunca Pardux da sesle cevap versin.</summary>
        public bool VoiceReplies { get; set; }
        /// <summary>Konuşma sesi: "dogal" (Piper, çevrimdışı yapay zekâ sesi) ya da "windows" (Tolga).</summary>
        public string VoiceEngine { get; set; }
        /// <summary>Uygulamada karşılığı olmayan sorularda internette araştırıp cevabı göster.</summary>
        public bool WebAnswers { get; set; }
        /// <summary>WhatsApp sohbeti açılınca hazırlanan mesajı otomatik gönder.</summary>
        public bool WhatsAppAutoSend { get; set; }
        public List<CustomCommand> CustomCommands { get; set; }
        public Dictionary<string, int> Usage { get; set; }
        public List<string> RecentEmails { get; set; }


        // Görünüm
        /// <summary>koyu, acik veya sistem</summary>
        public string Theme { get; set; }
        /// <summary>mor, mavi, yesil, turuncu, pembe, kirmizi</summary>
        public string Accent { get; set; }
        public bool Transparent { get; set; }
        public bool Animations { get; set; }
        // Ada (dynamic island)
        public bool IslandMusic { get; set; }
        public bool IslandIdle { get; set; }
        public bool IslandHideFullscreen { get; set; }
        public bool IslandCharging { get; set; }
        /// <summary>Medya uygulaması (Spotify, tarayıcı) öndeyken adada müziği gösterme.</summary>
        public bool IslandHideWhenAppFront { get; set; }
        /// <summary>Uzun süre işlem yapılmayınca "iyi misin?" diye sor.</summary>
        public bool CheckIn { get; set; }
        public int CheckInMinutes { get; set; }
        /// <summary>"Hey Pardux" ile sesli uyandırma.</summary>
        public bool WakeWord { get; set; }
        /// <summary>Ses efektleri ve düzeyi (0-100).</summary>
        public bool Sounds { get; set; }
        public int SoundVolume { get; set; }
        /// <summary>Pencere arka plan efekti: mika, akrilik (bulanık), sekmeli, yok.</summary>
        public string Backdrop { get; set; }
        /// <summary>Ada boyutu: kucuk, orta, buyuk.</summary>
        public string IslandSize { get; set; }
        /// <summary>Önemli gündem haberlerini sağ üstte bildir.</summary>
        public bool NewsAlerts { get; set; }
        /// <summary>Bildirim merkezinde bir süredir açılmadan duran bildirimleri hatırlat.</summary>
        public bool MissedReminders { get; set; }

        // Davranış
        public bool ClipboardHistory { get; set; }
        public bool ShowFrequent { get; set; }

        [ScriptIgnore]
        public DateTime Stamp { get; set; }

        public Settings()
        {
            Hotkey = "Alt+Space";
            MailProvider = "gmail";
            SpotifyAutoPlay = true;
            StartWithWindows = true;
            Contacts = new Dictionary<string, string>();
            Phones = new Dictionary<string, string>();
            Instagram = new Dictionary<string, string>();
            IslandGlass = true;
            IslandBlur = true;
            EyeColor = "#FFFFFF";
            EyeSize = "orta";
            EyeShape = "oval";
            IslandStyle = "yuzen";
            VoiceReplies = true;
            VoiceEngine = "dogal";
            WebAnswers = true;
            WhatsAppAutoSend = true;
            CustomCommands = new List<CustomCommand>();
            Usage = new Dictionary<string, int>();
            RecentEmails = new List<string>();
            Theme = "sistem";
            Accent = "mor";
            Transparent = true;
            Animations = true;
            IslandMusic = true;
            IslandIdle = false;
            IslandHideFullscreen = true;
            IslandCharging = true;
            NewsAlerts = true;
            MissedReminders = true;
            IslandHideWhenAppFront = true;
            CheckIn = true;
            CheckInMinutes = 60;
            WakeWord = true;
            Sounds = true;
            SoundVolume = 70;
            Backdrop = "akrilik";
            IslandSize = "orta";
            ClipboardHistory = true;
            ShowFrequent = true;
        }
        /// <summary>Testler gerçek ayar klasörüne dokunmasın diye başka bir klasör verir.</summary>
        public static string DirOverride;

        public static string Dir
        {
            get
            {
                string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string d = DirOverride ?? Path.Combine(app, "Pardux");
                if (DirOverride == null && !_migrated)
                {
                    _migrated = true;
                    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    Migrate(Path.Combine(app, "EmoAsistan"), d);
                    Migrate(Path.Combine(local, "EmoAsistan"), Path.Combine(local, "Pardux"));
                }
                Directory.CreateDirectory(d);
                return d;
            }
        }
        static bool _migrated;

        /// <summary>
        /// Uygulamanın eski adıyla (Emo Asistan) kaydedilmiş ayarlar, kişiler, komutlar ve yedekler yeni klasöre bir kez kopyalanır.
        /// Yeni klasörde zaten olan dosyaların üstüne yazılmaz; eski klasör olduğu gibi yedek olarak kalır.
        /// </summary>
        internal static void Migrate(string from, string to)
        {
            try
            {
                if (!Directory.Exists(from) || File.Exists(Path.Combine(to, "eski-ad-tasindi.txt"))) return;
                int n = 0;
                foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(to, f.Substring(from.Length).TrimStart('\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (!File.Exists(target)) { File.Copy(f, target); n++; }
                }
                File.WriteAllText(Path.Combine(to, "eski-ad-tasindi.txt"), from + " klasöründen " + n + " dosya kopyalandı (" + DateTime.Now + ")");
            }
            catch (Exception ex)
            {
                try { Directory.CreateDirectory(to); File.AppendAllText(Path.Combine(to, "hata.log"), DateTime.Now + "  [ayarlar] eski klasör taşınamadı: " + ex.Message + Environment.NewLine); } catch { }
            }
        }

        public static string FilePath { get { return Path.Combine(Dir, "ayarlar.json"); } }

        static string BackupPath { get { return Path.Combine(Dir, "ayarlar.yedek.json"); } }

        /// <summary>
        /// Kişiler ve özel komutlar ayar dosyasından ayrı olarak da saklanır (Roaming ve Local'da birer kopya, ayrıca günlük yedekler).
        /// Açılışta bütün kopyalar birleştirilir: bir kopya eski, bozuk ya da okunamaz olsa bile kayıtlı hiçbir kişi/komut kaybolmaz.
        /// Bir kişi ya da komut yalnızca kullanıcı onu ayarlardan sildiğinde gider (silinenler ayrıca not edilir, geri gelmez).
        /// </summary>
        static string[] ContactPaths
        {
            get
            {
                string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pardux");
                if (DirOverride != null) local = Path.Combine(DirOverride, "local");
                try { Directory.CreateDirectory(local); } catch { }
                return new[] { Path.Combine(Dir, "kisiler.json"), Path.Combine(local, "kisiler.json") };
            }
        }
        static string ContactBackupDir { get { string d = Path.Combine(Dir, "kisi-yedekleri"); Directory.CreateDirectory(d); return d; } }

        /// <summary>Eski sürüm uyumluluğu (artık silinenler adıyla tutulur).</summary>
        public bool ContactsCleared { get; set; }

        /// <summary>Kullanıcının ayarlardan sildiği kişi ve komut adları: diğer kopyalardan geri getirilmezler.</summary>
        public List<string> DeletedContacts { get; set; }
        public List<string> DeletedCommands { get; set; }

        /// <summary>Ayar dosyası açılışta okunamadıysa (ör. Windows açılırken dosya kilitli) diğer ayarlar dosyanın üstüne yazılmaz.</summary>
        [ScriptIgnore] public bool Unreadable;

        static Settings Read(string path, int tries = 12)
        {
            // Bilgisayar açılırken dosya kısa süre kilitli olabilir (eşitleme, virüs tarayıcı): birkaç kez dene
            for (int i = 0; i < tries; i++)
            {
                try
                {
                    if (!File.Exists(path)) return null;
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    return string.IsNullOrWhiteSpace(json) ? null : new JavaScriptSerializer().Deserialize<Settings>(json);
                }
                catch (IOException) { System.Threading.Thread.Sleep(250); }
                catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(250); }
                catch (Exception ex) { Log("ayar okunamadı (" + Path.GetFileName(path) + "): " + ex.Message); return null; }
            }
            Log("ayar dosyası kilitli kaldı: " + Path.GetFileName(path));
            return null;
        }

        class ContactBook
        {
            public Dictionary<string, string> Contacts { get; set; }
            public Dictionary<string, string> Phones { get; set; }
            public Dictionary<string, string> Instagram { get; set; }
            public List<CustomCommand> Commands { get; set; }
            public List<string> DeletedContacts { get; set; }
            public List<string> DeletedCommands { get; set; }
        }

        static ContactBook ReadBook(string f)
        {
            for (int i = 0; i < 8; i++)
            {
                try
                {
                    if (!File.Exists(f)) return null;
                    return new JavaScriptSerializer().Deserialize<ContactBook>(File.ReadAllText(f, Encoding.UTF8));
                }
                catch (IOException) { System.Threading.Thread.Sleep(250); }
                catch { return null; }
            }
            return null;
        }

        static int CountOf<T>(ICollection<T> d) { return d == null ? 0 : d.Count; }

        bool HasContacts { get { return CountOf(Contacts) + CountOf(Phones) + CountOf(Instagram) > 0; } }

        static bool Deleted(List<string> list, string name)
        {
            return list != null && list.Any(x => string.Equals(x, name, StringComparison.CurrentCultureIgnoreCase));
        }

        static bool HasKey(Dictionary<string, string> d, string name)
        {
            return d.Keys.Any(k => string.Equals(k, name, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>Başka bir kopyadaki kişi ve komutları ekler (var olanları değiştirmez, silinmiş olanları almaz). Eklenen sayısı.</summary>
        int MergeFrom(ContactBook b)
        {
            if (b == null) return 0;
            int added = 0;
            // Önce tamamen eksik kişileri bul (bütün alanlarıyla birlikte gelsinler)
            var names = new List<string>();
            foreach (var d in new[] { b.Contacts, b.Phones, b.Instagram })
                if (d != null) foreach (var k in d.Keys)
                        if (!names.Contains(k) && !Deleted(DeletedContacts, k) && !HasKey(Contacts, k) && !HasKey(Phones, k) && !HasKey(Instagram, k)) names.Add(k);
            foreach (string n in names)
            {
                string v;
                if (b.Contacts != null && b.Contacts.TryGetValue(n, out v) && !string.IsNullOrWhiteSpace(v)) Contacts[n] = v;
                if (b.Phones != null && b.Phones.TryGetValue(n, out v) && !string.IsNullOrWhiteSpace(v)) Phones[n] = v;
                if (b.Instagram != null && b.Instagram.TryGetValue(n, out v) && !string.IsNullOrWhiteSpace(v)) Instagram[n] = v;
                added++;
            }
            if (b.Commands != null)
                foreach (var c in b.Commands)
                {
                    if (c == null || string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Target) || Deleted(DeletedCommands, c.Name)) continue;
                    if (CustomCommands.Any(x => x != null && string.Equals(x.Name, c.Name, StringComparison.CurrentCultureIgnoreCase))) continue;
                    CustomCommands.Add(c); added++;
                }
            return added;
        }

        static ContactBook BookOf(Settings s)
        {
            return s == null ? null : new ContactBook { Contacts = s.Contacts, Phones = s.Phones, Instagram = s.Instagram, Commands = s.CustomCommands };
        }

        /// <summary>Kişi ve komutları ayrı dosyalara yazar (boş liste hiçbir zaman dolu kopyanın üstüne yazılmaz).</summary>
        void SaveContacts()
        {
            if (!HasContacts && CountOf(CustomCommands) == 0) return;
            string json = Indent(new JavaScriptSerializer().Serialize(new ContactBook
            {
                Contacts = Contacts, Phones = Phones, Instagram = Instagram, Commands = CustomCommands,
                DeletedContacts = DeletedContacts, DeletedCommands = DeletedCommands
            }));
            foreach (string f in ContactPaths)
            {
                try { if (File.Exists(f) && File.ReadAllText(f, Encoding.UTF8) == json) continue; } catch { }
                SafeWrite(f, json, null);
            }
            // Günlük yedek (son 30 gün tutulur)
            try
            {
                string day = Path.Combine(ContactBackupDir, "kisiler-" + DateTime.Now.ToString("yyyyMMdd") + ".json");
                File.WriteAllText(day, json, new UTF8Encoding(false));
                foreach (string old in Directory.GetFiles(ContactBackupDir, "kisiler-*.json").OrderByDescending(x => x).Skip(30)) File.Delete(old);
            }
            catch (Exception ex) { Log("günlük kişi yedeği yazılamadı: " + ex.Message); }
        }

        /// <summary>
        /// Dosyayı güvenle yazar: önce geçici dosya, sonra yer değiştirme. Yer değiştirme bu bilgisayarda zaman zaman başarısız
        /// oluyordu (dosya başka bir program tarafından kısa süre açık) ve kayıt sessizce kayboluyordu; şimdi birkaç kez denenir,
        /// olmazsa doğrudan üstüne yazılır ve sorun hata günlüğüne not edilir.
        /// </summary>
        static bool SafeWrite(string path, string text, string backup)
        {
            string tmp = path + ".tmp";
            try { File.WriteAllText(tmp, text, new UTF8Encoding(false)); }
            catch (Exception ex) { Log("geçici dosya yazılamadı (" + Path.GetFileName(path) + "): " + ex.Message); return false; }
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(tmp, path, backup, true); else File.Move(tmp, path);
                    return true;
                }
                catch (Exception ex)
                {
                    if (i == 3) Log("kayıt yer değiştirme başarısız (" + Path.GetFileName(path) + "): " + ex.Message);
                    System.Threading.Thread.Sleep(120);
                }
            }
            try
            {
                if (backup != null && File.Exists(path)) File.Copy(path, backup, true);
                File.Copy(tmp, path, true);
                File.Delete(tmp);
                return true;
            }
            catch (Exception ex) { Log("kayıt yazılamadı (" + Path.GetFileName(path) + "): " + ex.Message); return false; }
        }

        internal static void Log(string msg)
        {
            try { File.AppendAllText(Path.Combine(Dir, "hata.log"), DateTime.Now + "  [ayarlar] " + msg + Environment.NewLine); } catch { }
        }

        public static Settings Load()
        {
            // Açılışta dosya uzun süre kilitli kalabiliyor: ana dosya için daha sabırlı (~6 sn)
            Settings s = Read(FilePath, 24);
            bool unreadable = false;
            if (s == null && File.Exists(FilePath))
            {
                // Dosya bozulmuşsa kişiler ve komutlar kaybolmasın: bozuk dosyayı sakla, yedekten dön
                try { File.Copy(FilePath, Path.Combine(Dir, "ayarlar.bozuk-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true); } catch { }
                s = Read(BackupPath);
                unreadable = s == null;
            }
            if (s == null && !File.Exists(FilePath)) s = Read(BackupPath);
            bool fresh = s == null;
            if (s == null)
            {
                s = new Settings();
                s.Unreadable = unreadable;
            }
            if (s.Contacts == null) s.Contacts = new Dictionary<string, string>();
            if (s.Phones == null) s.Phones = new Dictionary<string, string>();
            if (s.Instagram == null) s.Instagram = new Dictionary<string, string>();
            if (s.CustomCommands == null) s.CustomCommands = new List<CustomCommand>();
            s.CustomCommands.RemoveAll(x => x == null);
            if (s.DeletedContacts == null) s.DeletedContacts = new List<string>();
            if (s.DeletedCommands == null) s.DeletedCommands = new List<string>();

            // Bütün kopyaları birleştir: ayrı kişi dosyaları, ayar yedeği ve günlük yedekler (en yeniden eskiye)
            int added = 0;
            var books = ContactPaths.Select(ReadBook).Where(b => b != null).ToList();
            if (fresh) foreach (var b in books)
                {
                    // Ayar dosyası hiç okunamadıysa silinenler listesi kişi dosyasından gelir
                    if (b.DeletedContacts != null) s.DeletedContacts.AddRange(b.DeletedContacts.Where(x => !Deleted(s.DeletedContacts, x)));
                    if (b.DeletedCommands != null) s.DeletedCommands.AddRange(b.DeletedCommands.Where(x => !Deleted(s.DeletedCommands, x)));
                }
            foreach (var b in books) added += s.MergeFrom(b);
            added += s.MergeFrom(BookOf(Read(BackupPath, 2)));
            try
            {
                foreach (string f in Directory.GetFiles(ContactBackupDir, "kisiler-*.json").OrderByDescending(x => x))
                    added += s.MergeFrom(ReadBook(f));
            }
            catch { }
            if (fresh && !unreadable && books.Count == 0 && s.CustomCommands.Count == 0) // yalnızca gerçekten ilk açılış
                s.CustomCommands.Add(new CustomCommand { Name = "github", Target = "https://github.com" });
            if (added > 0) Log(added + " kişi/komut diğer kopyalardan geri getirildi");

            if (string.IsNullOrEmpty(s.Backdrop)) s.Backdrop = "akrilik";
            if (string.IsNullOrEmpty(s.IslandSize)) s.IslandSize = "orta";
            if (string.IsNullOrEmpty(s.EyeColor)) s.EyeColor = "#FFFFFF";
            if (string.IsNullOrEmpty(s.EyeSize)) s.EyeSize = "orta";
            if (string.IsNullOrEmpty(s.EyeShape)) s.EyeShape = "oval";
            if (string.IsNullOrEmpty(s.IslandStyle)) s.IslandStyle = "yuzen";
            if (s.CheckInMinutes < 15) s.CheckInMinutes = 60;
            if (s.Usage == null) s.Usage = new Dictionary<string, int>();
            if (s.RecentEmails == null) s.RecentEmails = new List<string>();
            if (string.IsNullOrEmpty(s.Hotkey)) s.Hotkey = "Alt+Space";
            if (string.IsNullOrEmpty(s.MailProvider)) s.MailProvider = "gmail";
            if (string.IsNullOrEmpty(s.Theme)) s.Theme = "sistem";
            if (string.IsNullOrEmpty(s.Accent)) s.Accent = "mor";
            s.Stamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
            // Birleşik hâli hemen bütün kopyalara yaz (geri getirilenler bir daha kaybolmasın)
            if (added > 0) s.Save();
            else s.SaveContacts(); // ayrı kopyalar her açılışta güncel tutulur (değişmeyen dosya yeniden yazılmaz)
            return s;
        }

        public void Save()
        {
            SaveContacts(); // kişiler/komutlar her durumda (ayar dosyası okunamamış olsa bile) kendi dosyalarına yazılır
            // Açılışta okunamamış dosyanın üstüne varsayılan ayarlar yazılmaz; sonraki açılışta asıl dosya yeniden okunur
            if (Unreadable && File.Exists(FilePath))
            {
                if (Read(FilePath, 2) == null) return;
                Unreadable = false;
            }
            try
            {
                string json = new JavaScriptSerializer().Serialize(this);
                // Önce geçici dosyaya yaz, sonra yer değiştir: yazarken kapanma/çökme ayar dosyasını yarım bırakmaz.
                // Bir önceki sağlam sürüm ayarlar.yedek.json olarak kalır.
                if (SafeWrite(FilePath, Indent(json), BackupPath)) Stamp = File.GetLastWriteTimeUtc(FilePath);
            }
            catch (Exception ex) { Log("ayarlar kaydedilemedi: " + ex.Message); }
        }

        public void AddUsage(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            int c;
            Usage.TryGetValue(key, out c);
            Usage[key] = c + 1;
            Save();
        }

        public int GetUsage(string key)
        {
            int c;
            return key != null && Usage.TryGetValue(key, out c) ? c : 0;
        }

        public void ApplyStartup()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return;
                    // Eski adla eklenmiş başlangıç kaydı (Emo Asistan) kaldırılır, yoksa eski sürüm de açılırdı
                    if (k.GetValue("EmoAsistan") != null) k.DeleteValue("EmoAsistan", false);
                    if (StartWithWindows)
                        k.SetValue("Pardux", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --gizli");
                    else if (k.GetValue("Pardux") != null)
                        k.DeleteValue("Pardux", false);
                }
            }
            catch { }
        }

        static string Indent(string json)
        {
            var sb = new StringBuilder();
            int ind = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); }
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                        ind++; sb.AppendLine(); sb.Append(' ', ind * 2); break;
                    case '}':
                    case ']':
                        ind--; sb.AppendLine(); sb.Append(' ', ind * 2); sb.Append(c); break;
                    case ',': sb.Append(c); sb.AppendLine(); sb.Append(' ', ind * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
