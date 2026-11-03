using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Pardux
{
    /// <summary>Uygulama/dosya simgelerini arka planda yükler ve önbelleğe alır.</summary>
    internal static class IconLoader
    {
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP
        {
            public int bmType; public int bmWidth; public int bmHeight; public int bmWidthBytes;
            public ushort bmPlanes; public ushort bmBitsPixel; public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount;
            public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
            public uint biClrUsed; public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DIBSECTION
        {
            public BITMAP dsBm; public BITMAPINFOHEADER dsBmih;
            public uint b0; public uint b1; public uint b2; public IntPtr dshSection; public uint dsOffset;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        static extern int GetObject(IntPtr h, int size, out DIBSECTION ds);

        static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly BlockingCollection<Tuple<string, Action<ImageSource>>> Queue = new BlockingCollection<Tuple<string, Action<ImageSource>>>();
        static Dispatcher _ui;

        public static void Start(Dispatcher ui)
        {
            _ui = ui;
            var t = new Thread(Worker) { IsBackground = true, Name = "IconLoader" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public static void Request(string path, Action<ImageSource> done)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (Cache)
            {
                ImageSource img;
                if (Cache.TryGetValue(path, out img)) { if (img != null) done(img); return; }
            }
            Queue.Add(Tuple.Create(path, done));
        }

        static void Worker()
        {
            foreach (var job in Queue.GetConsumingEnumerable())
            {
                ImageSource img;
                bool cached;
                lock (Cache) cached = Cache.TryGetValue(job.Item1, out img);
                if (!cached)
                {
                    img = Load(job.Item1);
                    lock (Cache) Cache[job.Item1] = img;
                }
                if (img != null)
                {
                    var cb = job.Item2;
                    var image = img;
                    _ui.BeginInvoke(new Action(() => cb(image)));
                }
            }
        }

        static ImageSource Load(string path)
        {
            IShellItemImageFactory f = null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
                // SIIGBF_BIGGERSIZEOK | SIIGBF_ICONONLY
                if (f.GetImage(new SIZE { cx = 64, cy = 64 }, 0x1 | 0x4, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                DIBSECTION ds;
                if (GetObject(hbm, Marshal.SizeOf(typeof(DIBSECTION)), out ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero) return null;
                int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight, stride = ds.dsBm.bmWidthBytes;
                BitmapSource src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, ds.dsBm.bmBits, stride * h, stride);
                if (ds.dsBmih.biHeight > 0)
                    src = new TransformedBitmap(src, new ScaleTransform(1, -1));
                src.Freeze();
                return src;
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) Native.DeleteObject(hbm);
                if (f != null) Marshal.ReleaseComObject(f);
            }
        }
    }
}
