using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    public static class FloydSteinbergDither
    {
        /// <summary>
        /// ��BGRAͼ�����Floyd�CSteinberg��������ɫ��ӳ�䣨�Ż��棩
        /// </summary>
        /// <param name="src">ԭʼBGRA�ֽ�����</param>
        /// <param name="width">ͼ����</param>
        /// <param name="height">ͼ��߶�</param>
        /// <param name="palette">Ŀ���ɫ�壨BitmapPalette��</param>
        /// <returns>�������BGRA�ֽ�����</returns>
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

            // ֻ����������ʹ��һά��������ѰַЧ��
            float[] errorCurr = new float[width * 3];
            float[] errorNext = new float[width * 3];

            // Ԥ�����ɫ��Ϊfloat���飬���ں�������
            int palLen = palette.Colors.Count;
            float[][] pal = new float[palLen][];
            for (int i = 0; i < palLen; i++)
            {
                var c = palette.Colors[i];
                pal[i] = new float[] { c.R, c.G, c.B };
            }

            // ��ѡ���������ص���ɫ���ӳ�䣨�ʺϵ�ɫ���С�ĳ�����
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
                    if (dst[idx + 3] < 128) continue; // ����͸��

                    // ��ǰ���ؼ������
                    float r = dst[idx + 2] + errorCurr[x * 3 + 0];
                    float g = dst[idx + 1] + errorCurr[x * 3 + 1];
                    float b = dst[idx + 0] + errorCurr[x * 3 + 2];

                    // ������0-255��Χ
                    r = Math.Min(255, Math.Max(0, r));
                    g = Math.Min(255, Math.Max(0, g));
                    b = Math.Min(255, Math.Max(0, b));

                    // ���û�����ٵ�ɫ�����
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

                    // д��
                    dst[idx + 2] = nr;
                    dst[idx + 1] = ng;
                    dst[idx + 0] = nb;

                    // �������
                    float errR = (float)((r - nr) * strength);
                    float errG = (float)((g - ng) * strength);
                    float errB = (float)((b - nb) * strength);

                    // Floyd�CSteinberg��ɢ
                    // ��
                    int xr = x + dx;
                    if (xr >= 0 && xr < width)
                    {
                        errorCurr[xr * 3 + 0] += errR * 7 / 16f;
                        errorCurr[xr * 3 + 1] += errG * 7 / 16f;
                        errorCurr[xr * 3 + 2] += errB * 7 / 16f;
                    }
                    // ��
                    if (y + 1 < height)
                    {
                        // ����
                        int xl = x - dx;
                        if (xl >= 0 && xl < width)
                        {
                            errorNext[xl * 3 + 0] += errR * 3 / 16f;
                            errorNext[xl * 3 + 1] += errG * 3 / 16f;
                            errorNext[xl * 3 + 2] += errB * 3 / 16f;
                        }
                        // ��
                        errorNext[x * 3 + 0] += errR * 5 / 16f;
                        errorNext[x * 3 + 1] += errG * 5 / 16f;
                        errorNext[x * 3 + 2] += errB * 5 / 16f;
                        // ����
                        if (xr >= 0 && xr < width)
                        {
                            errorNext[xr * 3 + 0] += errR * 1 / 16f;
                            errorNext[xr * 3 + 1] += errG * 1 / 16f;
                            errorNext[xr * 3 + 2] += errB * 1 / 16f;
                        }
                    }
                }
                // ���л�����������
                var tmp = errorCurr;
                errorCurr = errorNext;
                errorNext = tmp;
                Array.Clear(errorNext, 0, errorNext.Length);
            }
            return dst;
        }

        /// <summary>
        /// ֱ�Ӷ�BitmapSource���ж���������WriteableBitmap
        /// </summary>
        /// <param name="src">ԭʼBitmapSource</param>
        /// <param name="palette">Ŀ���ɫ��</param>
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
