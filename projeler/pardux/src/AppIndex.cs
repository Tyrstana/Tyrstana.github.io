using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Pardux
{
    public class AppEntry
    {
        public string Name;
        public string Path;
        public string NormName;
    }

    /// <summary>Başlat menüsündeki tüm uygulamaları (klasik + Store) shell:AppsFolder üzerinden listeler.</summary>
    internal static class AppIndex
    {
        static List<AppEntry> _apps = new List<AppEntry>();
        static readonly object Sync = new object();
        static DateTime _lastRefresh = DateTime.MinValue;
        static bool _refreshing;

        static string CachePath { get { return System.IO.Path.Combine(Settings.Dir, "uygulamalar.cache"); } }

        static readonly string[] Junk = { "uninstall", "kaldir", "readme", "documentation", "release notes", "help", "yardim", "website", "web sitesi", "license", "lisans" };

        public static List<AppEntry> Apps
        {
            get { lock (Sync) return _apps; }
        }

        public static void LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return;
                var list = new List<AppEntry>();
                foreach (string line in File.ReadAllLines(CachePath, Encoding.UTF8))
                {
                    int t = line.IndexOf('\t');
                    if (t <= 0) continue;
                    list.Add(Make(line.Substring(0, t), line.Substring(t + 1)));
                }
                lock (Sync) _apps = list;
            }
            catch { }
        }

        public static void RefreshIfStale()
        {
            if ((DateTime.Now - _lastRefresh).TotalMinutes > 3) RefreshAsync();
        }

        public static void RefreshAsync()
        {
            if (_refreshing) return;
            _refreshing = true;
            var t = new Thread(() =>
            {
                try
                {
                    var list = Enumerate();
                    if (list.Count > 0)
                    {
                        lock (Sync) _apps = list;
                        File.WriteAllLines(CachePath, list.Select(a => a.Name + "\t" + a.Path), new UTF8Encoding(false));
                    }
                    _lastRefresh = DateTime.Now;
                }
                catch (Exception ex) { Debug.WriteLine(ex); }
                finally { _refreshing = false; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        static AppEntry Make(string name, string path)
        {
            return new AppEntry { Name = name, Path = path, NormName = TextUtil.Norm(name) };
        }

        static List<AppEntry> Enumerate()
        {
            var result = new List<AppEntry>();
            var seen = new HashSet<string>();
            Type t = Type.GetTypeFromProgID("Shell.Application");
            dynamic shell = Activator.CreateInstance(t);
            dynamic folder = shell.NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
            if (folder == null) return result;
            dynamic items = folder.Items();
            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic item = items.Item(i);
                    string name = item.Name;
                    string path = item.Path;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                    if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                    string n = TextUtil.Norm(name);
                    if (Junk.Any(j => n.Contains(j))) continue;
                    string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".txt" || ext == ".chm" || ext == ".pdf" || ext == ".url" || ext == ".html" || ext == ".htm") continue;
                    if (!seen.Add(n)) continue;
                    result.Add(Make(name, path));
                }
                catch { }
            }
            // Masaüstündeki kısayollar (oyunlar çoğu zaman yalnızca burada: Epic/Steam ".url" kısayolları)
            foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
            {
                try
                {
                    if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) continue;
                    foreach (string f in System.IO.Directory.GetFiles(dir))
                    {
                        string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".lnk" && ext != ".url") continue;
                        string name = System.IO.Path.GetFileNameWithoutExtension(f).Replace("®", "").Replace("™", "").Trim();
                        string n = TextUtil.Norm(name);
                        if (n.Length == 0 || Junk.Any(j => n.Contains(j)) || !seen.Add(n)) continue;
                        result.Add(Make(name, f));
                    }
                }
                catch { }
            }
            return result;
        }

        public static void Launch(AppEntry app)
        {
            // Masaüstü kısayolu (dosya yolu) doğrudan açılır; diğerleri Başlat menüsü kaydından
            if (System.IO.Path.IsPathRooted(app.Path) && System.IO.File.Exists(app.Path))
            {
                Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", "\"shell:AppsFolder\\" + app.Path + "\"") { UseShellExecute = true });
        }

        public static string IconPath(AppEntry app)
        {
            if (System.IO.Path.IsPathRooted(app.Path) && System.IO.File.Exists(app.Path)) return app.Path;
            return "shell:AppsFolder\\" + app.Path;
        }
    }
}
