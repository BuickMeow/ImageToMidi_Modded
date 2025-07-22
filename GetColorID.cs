using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace ImageToMidi
{
    public enum ColorIdMethod
    {
        RGB,
        HSV,
        Lab,
        CIEDE2000,
        HSL
        // 未来可扩展：Lab, CIEDE2000等
    }

    public static class GetColorID
    {
        // 定义调色板Lab缓存结构
        public class PaletteLabCache
        {
            public IList<Color> Palette { get; }
            public (double l, double a, double b)[] LabValues { get; }

            public PaletteLabCache(IList<Color> palette)
            {
                Palette = palette;
                LabValues = new (double l, double a, double b)[palette.Count];
                for (int i = 0; i < palette.Count; i++)
                {
                    RgbToLab(palette[i].R, palette[i].G, palette[i].B, out double l, out double a, out double b);
                    LabValues[i] = (l, a, b);
                }
            }
        }
        public static int FindColorID(ColorIdMethod method, int r, int g, int b, PaletteLabCache cache)
        {
            switch (method)
            {
                case ColorIdMethod.RGB:
                    return FindColorID_RGB(r, g, b, cache.Palette);
                case ColorIdMethod.HSV:
                    return FindColorID_HSV(r, g, b, cache.Palette);
                case ColorIdMethod.Lab:
                    return FindColorID_Lab(r, g, b, cache);
                case ColorIdMethod.CIEDE2000:
                    return FindColorID_CIEDE2000(r, g, b, cache);
                case ColorIdMethod.HSL:
                    return FindColorID_HSL(r, g, b, cache.Palette);
                default:
                    return FindColorID_RGB(r, g, b, cache.Palette);
            }
        }

        public static int FindColorID_RGB(int r, int g, int b, IList<Color> palette)
        {
            int minIdx = 0;
            double minDist = double.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                var c = palette[i];
                int dr = r - c.R, dg = g - c.G, db = b - c.B;
                double dist = dr * dr + dg * dg + db * db;
                if (dist < minDist)
                {
                    minDist = dist;
                    minIdx = i;
                }
            }
            return minIdx;
        }
        // HSV分别指: Hue色相, Saturation饱和度, Value明度
        public static int FindColorID_HSV(int r, int g, int b, IList<Color> palette)
        {
            RgbToHsv(r, g, b, out double h1, out double s1, out double v1);
            int minIdx = 0;
            double minScore = double.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                var c = palette[i];
                RgbToHsv(c.R, c.G, c.B, out double h2, out double s2, out double v2);

                double score;
                if (v1 < 0.22 || s1 < 0.22)
                {
                    score = Math.Abs(v1 - v2) * 2.0 + Math.Abs(s1 - s2) * 1.0;
                }
                else
                {
                    double dh = Math.Abs(h1 - h2);
                    if (dh > 180) dh = 360 - dh;
                    dh /= 180.0;
                    double ds = Math.Abs(s1 - s2);
                    double dv = Math.Abs(v1 - v2);
                    score = dh * 2.0 + ds * 0.5 + dv * 0.5;
                }
                if (score < minScore)
                {
                    minScore = score;
                    minIdx = i;
                }
            }
            return minIdx;
        }

        // Lab算法
        public static int FindColorID_Lab(int r, int g, int b, PaletteLabCache cache)
        {
            RgbToLab(r, g, b, out double l1, out double a1, out double b1);
            int minIdx = 0;
            double minDist = double.MaxValue;
            for (int i = 0; i < cache.LabValues.Length; i++)
            {
                var (l2, a2, b2) = cache.LabValues[i];
                double dist = LabDistance(l1, a1, b1, l2, a2, b2);
                if (dist < minDist)
                {
                    minDist = dist;
                    minIdx = i;
                }
            }
            return minIdx;
        }

        // CIEDE2000算法
        public static int FindColorID_CIEDE2000(int r, int g, int b, PaletteLabCache cache)
        {
            RgbToLab(r, g, b, out double l1, out double a1, out double b1);
            int minIdx = 0;
            double minDist = double.MaxValue;
            for (int i = 0; i < cache.LabValues.Length; i++)
            {
                var (l2, a2, b2) = cache.LabValues[i];
                double dist = CIEDE2000(l1, a1, b1, l2, a2, b2);
                if (dist < minDist)
                {
                    minDist = dist;
                    minIdx = i;
                }
            }
            return minIdx;
        }
        // RGB转HSL
        public static int FindColorID_HSL(int r, int g, int b, IList<Color> palette)
        {
            RgbToHsl(r, g, b, out double h1, out double s1, out double l1);
            int minIdx = 0;
            double minScore = double.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                var c = palette[i];
                RgbToHsl(c.R, c.G, c.B, out double h2, out double s2, out double l2);

                double score;
                if (l1 < 0.22 || s1 < 0.22)
                {
                    score = Math.Abs(l1 - l2) * 10.0;
                }
                else
                {
                    double dh = Math.Abs(h1 - h2);
                    if (dh > 180) dh = 360 - dh;
                    dh /= 180.0;
                    double ds = Math.Abs(s1 - s2);
                    double dl = Math.Abs(l1 - l2);
                    score = dh * 2.0 + ds * 0.5 + dl * 0.5;
                }
                if (score < minScore)
                {
                    minScore = score;
                    minIdx = i;
                }
            }
            return minIdx;
        }

        // RGB转Lab
        public static void RgbToLab(int r, int g, int b, out double l, out double a, out double lab_b)
        {
            // 1. RGB转XYZ
            double R = r / 255.0, G = g / 255.0, B = b / 255.0;
            R = R > 0.04045 ? Math.Pow((R + 0.055) / 1.055, 2.4) : R / 12.92;
            G = G > 0.04045 ? Math.Pow((G + 0.055) / 1.055, 2.4) : G / 12.92;
            B = B > 0.04045 ? Math.Pow((B + 0.055) / 1.055, 2.4) : B / 12.92;

            double X = (R * 0.4124 + G * 0.3576 + B * 0.1805) / 0.95047;
            double Y = (R * 0.2126 + G * 0.7152 + B * 0.0722) / 1.00000;
            double Z = (R * 0.0193 + G * 0.1192 + B * 0.9505) / 1.08883;

            // 2. XYZ转Lab
            Func<double, double> f = t => t > 0.008856 ? Math.Pow(t, 1.0 / 3.0) : (7.787 * t) + (16.0 / 116.0);
            double fx = f(X);
            double fy = f(Y);
            double fz = f(Z);

            l = 116.0 * fy - 16.0;
            a = 500.0 * (fx - fy);
            lab_b = 200.0 * (fy - fz);
        }

        // Lab欧氏距离
        public static double LabDistance(double l1, double a1, double b1, double l2, double a2, double b2)
        {
            double dl = l1 - l2;
            double da = a1 - a2;
            double db = b1 - b2;
            return Math.Sqrt(dl * dl + da * da + db * db);
        }

        // CIEDE2000色差
        // 参考: https://zh.wikipedia.org/wiki/%E9%A2%9C%E8%89%B2%E5%B7%AE%E5%BC%82
        public static double CIEDE2000(double l1, double a1, double b1, double l2, double a2, double b2)
        {
            // 步骤略长，直接实现标准公式
            double avgLp = (l1 + l2) / 2.0;
            double c1 = Math.Sqrt(a1 * a1 + b1 * b1);
            double c2 = Math.Sqrt(a2 * a2 + b2 * b2);
            double avgC = (c1 + c2) / 2.0;

            double G = 0.5 * (1 - Math.Sqrt(Math.Pow(avgC, 7) / (Math.Pow(avgC, 7) + Math.Pow(25.0, 7))));
            double a1p = (1 + G) * a1;
            double a2p = (1 + G) * a2;
            double c1p = Math.Sqrt(a1p * a1p + b1 * b1);
            double c2p = Math.Sqrt(a2p * a2p + b2 * b2);

            double h1p = Math.Atan2(b1, a1p);
            if (h1p < 0) h1p += 2 * Math.PI;
            double h2p = Math.Atan2(b2, a2p);
            if (h2p < 0) h2p += 2 * Math.PI;

            double dLp = l2 - l1;
            double dCp = c2p - c1p;

            double dhp;
            if (c1p * c2p == 0)
                dhp = 0;
            else
            {
                double dh = h2p - h1p;
                if (dh > Math.PI) dh -= 2 * Math.PI;
                if (dh < -Math.PI) dh += 2 * Math.PI;
                dhp = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(dh / 2.0);
            }

            double avgLp_ = (l1 + l2) / 2.0;
            double avgCp = (c1p + c2p) / 2.0;

            double hSum = h1p + h2p;
            double avgHp;
            if (c1p * c2p == 0)
                avgHp = hSum;
            else
            {
                double dh = Math.Abs(h1p - h2p);
                if (dh > Math.PI)
                    avgHp = (hSum + 2 * Math.PI) / 2.0;
                else
                    avgHp = hSum / 2.0;
            }

            double T = 1
                - 0.17 * Math.Cos(avgHp - Math.PI / 6)
                + 0.24 * Math.Cos(2 * avgHp)
                + 0.32 * Math.Cos(3 * avgHp + Math.PI / 30)
                - 0.20 * Math.Cos(4 * avgHp - 21 * Math.PI / 60);

            double dTheta = 30 * Math.PI / 180 * Math.Exp(-Math.Pow((avgHp * 180 / Math.PI - 275) / 25, 2));
            double Rc = 2 * Math.Sqrt(Math.Pow(avgCp, 7) / (Math.Pow(avgCp, 7) + Math.Pow(25.0, 7)));
            double Sl = 1 + ((0.015 * Math.Pow(avgLp_ - 50, 2)) / Math.Sqrt(20 + Math.Pow(avgLp_ - 50, 2)));
            double Sc = 1 + 0.045 * avgCp;
            double Sh = 1 + 0.015 * avgCp * T;
            double Rt = -Math.Sin(2 * dTheta) * Rc;

            double dE = Math.Sqrt(
                Math.Pow(dLp / Sl, 2) +
                Math.Pow(dCp / Sc, 2) +
                Math.Pow(dhp / Sh, 2) +
                Rt * (dCp / Sc) * (dhp / Sh)
            );
            return dE;
        }

        // HSV工具
        public static void RgbToHsv(int r, int g, int b, out double h, out double s, out double v)
        {
            double R = r / 255.0, G = g / 255.0, B = b / 255.0;
            double max = Math.Max(R, Math.Max(G, B));
            double min = Math.Min(R, Math.Min(G, B));
            v = max;
            double delta = max - min;
            s = max == 0 ? 0 : delta / max;
            if (delta == 0)
                h = 0;
            else if (max == R)
                h = 60 * (((G - B) / delta) % 6);
            else if (max == G)
                h = 60 * (((B - R) / delta) + 2);
            else
                h = 60 * (((R - G) / delta) + 4);
            if (h < 0) h += 360;
        }
        

        // RGB转HSL
        public static void RgbToHsl(int r, int g, int b, out double h, out double s, out double l)
        {
            double R = r / 255.0, G = g / 255.0, B = b / 255.0;
            double max = Math.Max(R, Math.Max(G, B));
            double min = Math.Min(R, Math.Min(G, B));
            l = (max + min) / 2.0;

            if (max == min)
            {
                h = 0;
                s = 0;
            }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

                if (max == R)
                    h = 60 * (((G - B) / d) % 6);
                else if (max == G)
                    h = 60 * (((B - R) / d) + 2);
                else
                    h = 60 * (((R - G) / d) + 4);

                if (h < 0) h += 360;
            }
        }
    }
}