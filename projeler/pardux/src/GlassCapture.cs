using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Pardux
{
    /// <summary>
    /// Buzlu cam için ekranın bir parçasını yakalayıp bulanıklaştırır.
    /// Windows yarı saydam (katmanlı) pencereleri de ekran görüntüsüne katar; adanın kendisi yakalanırsa her tazelemede
    /// kendi koyu görüntüsünü tekrar bulanıklaştırıp gitgide koyulaşır. Bu yüzden yakalama anında pencere görüntü
    /// yakalamadan hariç tutulur (ekranda görünmeye devam eder), hemen ardından eski haline döner.
    /// </summary>
    public static class GlassCapture
    {
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("dwmapi.dll")] static extern int DwmFlush();

        const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;

        /// <summary>Verilen pencereleri hariç tutarak ekran bölgesini (fiziksel piksel) yakalar.</summary>
        public static System.Drawing.Bitmap Grab(IntPtr[] exclude, int x, int y, int w, int h)
        {
            var excluded = new bool[exclude.Length];
            try
            {
                bool any = false;
                for (int i = 0; i < exclude.Length; i++)
                    if (exclude[i] != IntPtr.Zero) any |= excluded[i] = SetWindowDisplayAffinity(exclude[i], WDA_EXCLUDEFROMCAPTURE);
                if (any) DwmFlush(); // hariç tutmanın ekran birleştirmesine yansımasını bekle
                var shot = new System.Drawing.Bitmap(w, h);
                using (var g = System.Drawing.Graphics.FromImage(shot)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
                return shot;
            }
            finally
            {
                for (int i = 0; i < exclude.Length; i++)
                    if (excluded[i]) SetWindowDisplayAffinity(exclude[i], WDA_NONE);
            }
        }

        /// <summary>
        /// Pürüzsüz (Gauss benzeri) bulanıklık; sonuç dondurulmuş (her iş parçacığından kullanılabilir).
        /// Eskiden görüntü 1/22'ye küçültülüp geri büyütülüyordu: büyütme bloklu, yıldız biçimli izler bırakıyordu.
        /// Şimdi 1/4 boyutta üç geçişli kutu bulanıklığı (Gauss'a çok yakın) uygulanır, ardından yarım boyuta yumuşakça
        /// büyütülür; ekranda yalnızca 2 kat büyütüldüğü için kenar ve geçişler kadife gibi görünür.
        /// </summary>
        public static BitmapSource Blur(System.Drawing.Bitmap shot)
        {
            int w = shot.Width, h = shot.Height;
            int sw = Math.Max(8, w / 4), sh = Math.Max(8, h / 4);
            int ow = Math.Max(8, w / 2), oh = Math.Max(8, h / 2);
            using (var small = Scale(shot, sw, sh))
            {
                var px = ReadPixels(small);
                // Yarıçap: yaklaşık 18 ekran pikseli (macOS'taki yoğun buzlu cama yakın)
                int r = Math.Max(2, Math.Min(5, Math.Min(sw, sh) / 3));
                for (int pass = 0; pass < 3; pass++)
                {
                    BoxH(px, sw, sh, r);
                    BoxV(px, sw, sh, r);
                }
                Dither(px);
                using (var blurred = WritePixels(px, sw, sh))
                using (var big = Scale(blurred, ow, oh))
                {
                    // Pikseller yönetilen bir diziye kopyalanır: HBITMAP tabanlı görüntüler çöp toplayıcıya görünmediği için
                    // saniyede bir yenilenince bellek birikiyordu
                    var buf = ReadPixels(big);
                    var bs = BitmapSource.Create(ow, oh, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, buf, ow * 4);
                    bs.Freeze();
                    return bs;
                }
            }
        }

        static byte[] ReadPixels(System.Drawing.Bitmap bmp)
        {
            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var buf = new byte[bmp.Width * 4 * bmp.Height];
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, buf, y * bmp.Width * 4, bmp.Width * 4);
                return buf;
            }
            finally { bmp.UnlockBits(data); }
        }

        static System.Drawing.Bitmap WritePixels(byte[] buf, int w, int h)
        {
            var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(buf, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        /// <summary>Yatay kutu bulanıklığı (kayan toplam, kenarlarda kenar pikseli tekrarlanır).</summary>
        static void BoxH(byte[] px, int w, int h, int r)
        {
            var line = new int[w * 4];
            int div = 2 * r + 1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 4;
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0;
                    for (int k = -r; k <= r; k++) sum += px[row + Clamp(k, w) * 4 + c];
                    for (int x = 0; x < w; x++)
                    {
                        line[x * 4 + c] = sum;
                        sum += px[row + Clamp(x + r + 1, w) * 4 + c] - px[row + Clamp(x - r, w) * 4 + c];
                    }
                }
                for (int i = 0; i < w * 4; i++) px[row + i] = (byte)((line[i] + div / 2) / div);
            }
        }

        /// <summary>Dikey kutu bulanıklığı.</summary>
        static void BoxV(byte[] px, int w, int h, int r)
        {
            var col = new int[h];
            int div = 2 * r + 1, stride = w * 4;
            for (int x = 0; x < w; x++)
                for (int c = 0; c < 4; c++)
                {
                    int o = x * 4 + c, sum = 0;
                    for (int k = -r; k <= r; k++) sum += px[Clamp(k, h) * stride + o];
                    for (int y = 0; y < h; y++)
                    {
                        col[y] = sum;
                        sum += px[Clamp(y + r + 1, h) * stride + o] - px[Clamp(y - r, h) * stride + o];
                    }
                    for (int y = 0; y < h; y++) px[y * stride + o] = (byte)((col[y] + div / 2) / div);
                }
        }

        static int Clamp(int i, int n) { return i < 0 ? 0 : i >= n ? n - 1 : i; }

        /// <summary>Koyu, yumuşak geçişlerde renk bantları (basamaklanma) görünmesin diye çok hafif sabit desenli gürültü.</summary>
        static void Dither(byte[] px)
        {
            uint seed = 2463534242;
            for (int i = 0; i < px.Length; i += 4)
            {
                seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                int n = (int)(seed % 3) - 1;
                for (int c = 0; c < 3; c++)
                {
                    int v = px[i + c] + n;
                    px[i + c] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                }
                px[i + 3] = 255;
            }
        }

        static System.Drawing.Bitmap Scale(System.Drawing.Image img, int w, int h)
        {
            var b = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(b))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                using (var attr = new System.Drawing.Imaging.ImageAttributes())
                {
                    attr.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY); // kenarlarda koyu hale olmasın
                    g.DrawImage(img, new System.Drawing.Rectangle(0, 0, w, h), 0, 0, img.Width, img.Height, System.Drawing.GraphicsUnit.Pixel, attr);
                }
            }
            return b;
        }
    }
}
