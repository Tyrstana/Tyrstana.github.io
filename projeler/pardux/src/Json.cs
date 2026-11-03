using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace Pardux
{
    /// <summary>JavaScriptSerializer üzerinde küçük yardımcılar (sözlük tabanlı JSON).</summary>
    internal static class Json
    {
        static JavaScriptSerializer S()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 };
        }

        public static string Ser(object o) { return S().Serialize(o); }
        public static object Parse(string s) { return S().DeserializeObject(s); }
        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }

        public static Dictionary<string, object> D(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        public static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static double? Num(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v == null) return null;
            double r;
            return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : (double?)null;
        }

        public static bool Bool(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            return v is bool && (bool)v;
        }

        public static IEnumerable<object> Arr(object o)
        {
            var a = o as System.Collections.IEnumerable;
            if (a == null || o is string) yield break;
            foreach (object x in a) yield return x;
        }
    }
}
