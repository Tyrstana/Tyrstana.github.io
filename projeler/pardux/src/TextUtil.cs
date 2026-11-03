using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Pardux
{
    internal static class TextUtil
    {
        public static readonly CultureInfo Tr = new CultureInfo("tr-TR");

        /// <summary>Karşılaştırma için metni sadeleştirir: küçük harf, Türkçe karakter yok, noktalama yok.</summary>
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToLower(Tr);
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                char c = ch;
                switch (c)
                {
                    case 'ç': c = 'c'; break;
                    case 'ğ': c = 'g'; break;
                    case 'ı': c = 'i'; break;
                    case 'i': c = 'i'; break;
                    case 'ö': c = 'o'; break;
                    case 'ş': c = 's'; break;
                    case 'ü': c = 'u'; break;
                    case 'â': c = 'a'; break;
                    case 'î': c = 'i'; break;
                    case 'û': c = 'u'; break;
                    case 'é': c = 'e'; break;
                }
                if (char.IsLetterOrDigit(c) || c == '+' || c == '#') sb.Append(c);
                else sb.Append(' ');
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        /// <summary>"spotify'ı" → "spotify", "Tarkan'dan" → "Tarkan"</summary>
        public static string StripSuffix(string word)
        {
            int i = word.IndexOfAny(new[] { '\'', '’', '`' });
            return i > 0 ? word.Substring(0, i) : word;
        }

        public static string StripAllSuffixes(string text)
        {
            return string.Join(" ", text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(StripSuffix));
        }

        /// <summary>Normalize edilmiş sorgu ve hedef için bulanık eşleşme puanı (0 = eşleşme yok).</summary>
        public static int Score(string q, string target)
        {
            if (q.Length == 0 || target.Length == 0) return 0;
            if (target == q) return 1000;
            if (target.StartsWith(q)) return 900 - Math.Min(target.Length - q.Length, 100);

            string[] words = target.Split(' ');
            foreach (string w in words)
                if (w.StartsWith(q)) return 800 - Math.Min(target.Length - q.Length, 100);

            string[] qWords = q.Split(' ');
            if (qWords.Length > 1 && qWords.All(qw => words.Any(w => w.StartsWith(qw))))
                return 750;

            int idx = target.IndexOf(q, StringComparison.Ordinal);
            if (idx >= 0 && q.Length >= 2) return 600 - Math.Min(idx, 100);

            string acr = new string(words.Where(w => w.Length > 0).Select(w => w[0]).ToArray());
            if (q.Length >= 2 && acr.StartsWith(q)) return 550;

            if (q.Length >= 3 && IsSubsequence(q.Replace(" ", ""), target.Replace(" ", "")))
                return 300 - Math.Min(target.Length - q.Length, 100);
            return 0;
        }

        static bool IsSubsequence(string q, string t)
        {
            int j = 0;
            for (int i = 0; i < t.Length && j < q.Length; i++)
                if (t[i] == q[j]) j++;
            return j == q.Length;
        }
    }
}
