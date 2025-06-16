using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    public static class FloydSteinbergDither
    {
        /// <summary>
        /// 对BGRA图像进行Floyd–Steinberg抖动并调色板映射（优化版）
        /// </summary>
        /// <param name="src">原始BGRA字节数组</param>
        /// <param name="width">图像宽度</param>
        /// <param name="height">图像高度</param>
        /// <param name="palette">目标调色板（BitmapPalette）</param>
        /// <returns>抖动后的BGRA字节数组</returns>
        public static byte[] Dither(
            byte[] src,
            int width,
            int height,
            BitmapPalette palette,
            double strength = 1.0,
            bool serpentine = true
            )
        {
            ;
            int stride = width * 4;
            byte[] dst = new byte[src.Length];
            Buffer.BlockCopy(src, 0, dst, 0, src.Length);

            // 只保留两行误差，使用一维数组提升寻址效率
            float[] errorCurr = new float[width * 3];
            float[] errorNext = new float[width * 3];

            // 预处理调色板为float数组，便于后续计算
            int palLen = palette.Colors.Count;
            float[][] pal = new float[palLen][];
            for (int i = 0; i < palLen; i++)
            {
                var c = palette.Colors[i];
                pal[i] = new float[] { c.R, c.G, c.B };
            }

            // 可选：缓存像素到调色板的映射（适合调色板较小的场景）
            var colorCache = new Dictionary<int, int>();

            for (int y = 0; y < height; y++)
            {
                bool leftToRight = serpentine ? (y % 2 == 0) : true;
                int x0 = leftToRight ? 0 : width - 1;
                int x1 = leftToRight ? width : -1;
                int dx = leftToRight ? 1 : -1;

                for (int x = x0; x != x1; x += dx)
                {
                    int idx = (y * width + x) * 4;
                    if (dst[idx + 3] < 128) continue; // 跳过透明

                    // 当前像素加上误差
                    float r = dst[idx + 2] + errorCurr[x * 3 + 0];
                    float g = dst[idx + 1] + errorCurr[x * 3 + 1];
                    float b = dst[idx + 0] + errorCurr[x * 3 + 2];

                    // 量化到0-255范围
                    r = Math.Min(255, Math.Max(0, r));
                    g = Math.Min(255, Math.Max(0, g));
                    b = Math.Min(255, Math.Max(0, b));

                    // 利用缓存加速调色板查找
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

                    byte nr = (byte)pal[best][0], ng = (byte)pal[best][1], nb = (byte)pal[best][2];

                    // 写回
                    dst[idx + 2] = nr;
                    dst[idx + 1] = ng;
                    dst[idx + 0] = nb;

                    // 计算误差
                    float errR = (float)((r - nr) * strength);
                    float errG = (float)((g - ng) * strength);
                    float errB = (float)((b - nb) * strength);

                    // Floyd–Steinberg扩散
                    // 右
                    int xr = x + dx;
                    if (xr >= 0 && xr < width)
                    {
                        errorCurr[xr * 3 + 0] += errR * 7 / 16f;
                        errorCurr[xr * 3 + 1] += errG * 7 / 16f;
                        errorCurr[xr * 3 + 2] += errB * 7 / 16f;
                    }
                    // 下
                    if (y + 1 < height)
                    {
                        // 左下
                        int xl = x - dx;
                        if (xl >= 0 && xl < width)
                        {
                            errorNext[xl * 3 + 0] += errR * 3 / 16f;
                            errorNext[xl * 3 + 1] += errG * 3 / 16f;
                            errorNext[xl * 3 + 2] += errB * 3 / 16f;
                        }
                        // 下
                        errorNext[x * 3 + 0] += errR * 5 / 16f;
                        errorNext[x * 3 + 1] += errG * 5 / 16f;
                        errorNext[x * 3 + 2] += errB * 5 / 16f;
                        // 右下
                        if (xr >= 0 && xr < width)
                        {
                            errorNext[xr * 3 + 0] += errR * 1 / 16f;
                            errorNext[xr * 3 + 1] += errG * 1 / 16f;
                            errorNext[xr * 3 + 2] += errB * 1 / 16f;
                        }
                    }
                }
                // 行切换，复用数组
                var tmp = errorCurr;
                errorCurr = errorNext;
                errorNext = tmp;
                Array.Clear(errorNext, 0, errorNext.Length);
            }
            return dst;
        }

        /// <summary>
        /// 直接对BitmapSource进行抖动并返回WriteableBitmap
        /// </summary>
        /// <param name="src">原始BitmapSource</param>
        /// <param name="palette">目标调色板</param>
        /// <returns>WriteableBitmap</returns>
        public static WriteableBitmap Dither(
            BitmapSource src,
            BitmapPalette palette,
            double strength = 1.0,
            bool serpentine = true)
        {
            int width = src.PixelWidth;
            int height = src.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            src.CopyPixels(pixels, stride, 0);

            var dithered = Dither(pixels, width, height, palette, strength, serpentine);

            var wb = new WriteableBitmap(width, height, src.DpiX, src.DpiY, PixelFormats.Bgra32, null);
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), dithered, stride, 0);
            return wb;
        }
    }
}
