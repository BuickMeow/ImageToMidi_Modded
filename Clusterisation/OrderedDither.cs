using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    public enum BayerMatrixSize
    {
        Size2x2,
        Size4x4,
        Size8x8
    }

    public static class OrderedDither
    {
        // 2x2 Bayer矩阵
        private static readonly int[,] Bayer2x2 =
        {
            { 0, 2 },
            { 3, 1 }
        };

        // 4x4 Bayer矩阵
        private static readonly int[,] Bayer4x4 =
        {
            {  0,  8,  2, 10 },
            { 12,  4, 14,  6 },
            {  3, 11,  1,  9 },
            { 15,  7, 13,  5 }
        };

        // 8x8 Bayer矩阵
        private static readonly int[,] Bayer8x8 =
        {
            {  0, 32,  8, 40,  2, 34, 10, 42 },
            { 48, 16, 56, 24, 50, 18, 58, 26 },
            { 12, 44,  4, 36, 14, 46,  6, 38 },
            { 60, 28, 52, 20, 62, 30, 54, 22 },
            {  3, 35, 11, 43,  1, 33,  9, 41 },
            { 51, 19, 59, 27, 49, 17, 57, 25 },
            { 15, 47,  7, 39, 13, 45,  5, 37 },
            { 63, 31, 55, 23, 61, 29, 53, 21 }
        };

        /// <summary>
        /// 对BGRA图像进行有序抖动并调色板映射
        /// </summary>
        /// <param name="src">原始BGRA字节数组</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <param name="palette">目标调色板（BitmapPalette）</param>
        /// <param name="strength">抖动强度（0~1，默认1）</param>
        /// <param name="matrixSize">Bayer矩阵大小</param>
        /// <returns>抖动后的BGRA字节数组</returns>
        public static byte[] Dither(
            byte[] src,
            int width,
            int height,
            BitmapPalette palette,
            double strength = 1.0,
            BayerMatrixSize matrixSize = BayerMatrixSize.Size4x4)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderedDither] strength = {strength}, matrixSize = {matrixSize}");
            int[,] bayer;
            int bayerSize;
            int bayerMax;

            switch (matrixSize)
            {
                case BayerMatrixSize.Size2x2:
                    bayer = Bayer2x2;
                    bayerSize = 2;
                    bayerMax = 3;
                    break;
                case BayerMatrixSize.Size8x8:
                    bayer = Bayer8x8;
                    bayerSize = 8;
                    bayerMax = 63;
                    break;
                default:
                    bayer = Bayer4x4;
                    bayerSize = 4;
                    bayerMax = 15;
                    break;
            }

            int stride = width * 4;
            byte[] dst = new byte[src.Length];
            Buffer.BlockCopy(src, 0, dst, 0, src.Length);

            int palLen = palette.Colors.Count;
            float[][] pal = new float[palLen][];
            for (int i = 0; i < palLen; i++)
            {
                var c = palette.Colors[i];
                pal[i] = new float[] { c.R, c.G, c.B };
            }

            var colorCache = new Dictionary<int, int>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * width + x) * 4;
                    if (dst[idx + 3] < 128) continue; // 跳过透明

                    // Bayer矩阵阈值归一化到0~1
                    int bayerVal = bayer[y % bayerSize, x % bayerSize];
                    double threshold = (bayerVal + 0.5) / (bayerMax + 1.0); // 0~1

                    // 抖动偏移，强度可调
                    float r = dst[idx + 2] + (float)((threshold - 0.5) * 255 * strength);
                    float g = dst[idx + 1] + (float)((threshold - 0.5) * 255 * strength);
                    float b = dst[idx + 0] + (float)((threshold - 0.5) * 255 * strength);

                    r = Math.Min(255, Math.Max(0, r));
                    g = Math.Min(255, Math.Max(0, g));
                    b = Math.Min(255, Math.Max(0, b));

                    int rgbKey = ((int)r << 16) | ((int)g << 8) | (int)b;
                    int best;
                    if (!colorCache.TryGetValue(rgbKey, out best))
                    {
                        float minDist = float.MaxValue;
                        best = 0;
                        for (int i = 0; i < palLen; i++)
                        {
                            float dr = r - pal[i][0];
                            float dg = g - pal[i][1];
                            float db = b - pal[i][2];
                            float dist = dr * dr + dg * dg + db * db;
                            if (dist < minDist)
                            {
                                minDist = dist;
                                best = i;
                            }
                        }
                        colorCache[rgbKey] = best;
                    }

                    dst[idx + 2] = (byte)pal[best][0];
                    dst[idx + 1] = (byte)pal[best][1];
                    dst[idx + 0] = (byte)pal[best][2];
                }
            }
            return dst;
        }

        /// <summary>
        /// 直接对BitmapSource进行有序抖动并返回WriteableBitmap
        /// </summary>
        public static WriteableBitmap Dither(
            BitmapSource src,
            BitmapPalette palette,
            double strength = 1.0,
            BayerMatrixSize matrixSize = BayerMatrixSize.Size4x4)
        {
            int width = src.PixelWidth;
            int height = src.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            src.CopyPixels(pixels, stride, 0);

            var dithered = Dither(pixels, width, height, palette, strength, matrixSize);

            var wb = new WriteableBitmap(width, height, src.DpiX, src.DpiY, PixelFormats.Bgra32, null);
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), dithered, stride, 0);
            return wb;
        }
    }
}
