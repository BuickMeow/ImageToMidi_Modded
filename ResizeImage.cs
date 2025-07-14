using System;
using System.Threading.Tasks;
using System.Buffers;

namespace ImageToMidi
{
    public enum ResizeAlgorithm
    {
        AreaResampling,
        Bilinear,
        NearestNeighbor,
        Bicubic,
        Lanczos,
        Gaussian,
        Mitchell,
        BoxFilter,
        IntegralImage,
        ModePooling,
        Hermite,
    }

    public readonly struct PooledBufferHandle : IDisposable
    {
        public byte[] Buffer { get; }
        private readonly int _length;

        internal PooledBufferHandle(byte[] buffer, int length)
        {
            Buffer = buffer;
            _length = length;
        }

        public void Dispose()
        {
            if (Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(Buffer);
            }
        }
    }
    
    static class ResizeImage
    {
        /// <summary>
        /// 使用指定的缩放算法将图像缩放到新尺寸，并通过 ArrayPool<byte> 避免频繁分配内存。
        /// 调用者必须使用完后调用 ArrayPool<byte>.Shared.Return(result) 归还缓冲区。
        /// </summary>
        public static byte[] MakeResizedImage(
    byte[] imageData,
    int imageStride,
    int newWidth,
    int? newHeight = null,
    ResizeAlgorithm algorithm = ResizeAlgorithm.AreaResampling)
        {
            int originalWidth = imageStride / 4;
            int originalHeight = imageData.Length / imageStride;
            int targetWidth = newWidth;
            int targetHeight = newHeight ?? (int)((double)originalHeight / originalWidth * targetWidth);
            int bufferSize = targetWidth * targetHeight * 4;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

            try
            {
                switch (algorithm)
                {
                    case ResizeAlgorithm.Bilinear:
                        BilinearResize(imageData, imageStride, originalWidth, originalHeight, targetWidth, targetHeight, buffer);
                        break;
                    case ResizeAlgorithm.NearestNeighbor:
                        NearestNeighborResize(imageData, imageStride, originalWidth, originalHeight, targetWidth, targetHeight, buffer);
                        break;
                    case ResizeAlgorithm.Bicubic:
                        BicubicResize(imageData, imageStride, originalWidth, originalHeight, targetWidth, targetHeight, buffer);
                        break;
                    case ResizeAlgorithm.Lanczos:
                        LanczosResize(imageData, imageStride, originalWidth, originalHeight, targetWidth, targetHeight, buffer);
                        break;
                    case ResizeAlgorithm.Gaussian:
                        GaussianResize(imageData, imageStride, originalWidth, originalHeight, targetWidth, targetHeight, buffer);
                        break;
                    case ResizeAlgorithm.Mitchell:
                        MitchellResize(imageData, imageStride, originalWidth, originalHeight, targetWidth, targetHeight, buffer);
                        break;
                    case ResizeAlgorithm.BoxFilter:
                        BoxFilterResize(imageData, imageStride, originalWidth, originalHeight, targetWidth, targetHeight, buffer);
                        break;
                    case ResizeAlgorithm.IntegralImage:
                        IntegralImageResize(imageData, imageStride, originalWidth, originalHeight, targetWidth, targetHeight, buffer);
                        break;
                    case ResizeAlgorithm.ModePooling:
                        ModePoolingResize(imageData, imageStride, originalWidth, originalHeight, targetWidth, targetHeight, buffer);
                        break;
                    case ResizeAlgorithm.Hermite:
                        HermiteResize(imageData, imageStride, originalWidth, originalHeight, targetWidth, targetHeight, buffer);
                        break;
                    case ResizeAlgorithm.AreaResampling:
                    default:
                        AreaResampleResize(imageData, imageStride, originalWidth, originalHeight, targetWidth, targetHeight, buffer);
                        break;
                }
                // 只返回有效部分
                if (buffer.Length == bufferSize)
                    return buffer;
                // 否则复制有效部分
                var result = new byte[bufferSize];
                Buffer.BlockCopy(buffer, 0, result, 0, bufferSize);
                ArrayPool<byte>.Shared.Return(buffer);
                return result;
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
        }



        // 面积重采样算法
        private static void AreaResampleResize(
            byte[] imageData, 
            int imageStride, 
            int W, int H, 
            int newW, int newH,
            byte[] buffer)
        {
            double scaleX = (double)W / newW;
            double scaleY = (double)H / newH;

            Parallel.For(0, newW * newH, p =>
            {
                int x = p % newW;
                int y = p / newW;

                double srcX1 = x * scaleX;
                double srcY1 = y * scaleY;
                double srcX2 = (x + 1) * scaleX;
                double srcY2 = (y + 1) * scaleY;

                int startX = (int)Math.Floor(srcX1);
                int startY = (int)Math.Floor(srcY1);
                int endX = (int)Math.Ceiling(srcX2);
                int endY = (int)Math.Ceiling(srcY2);

                double r = 0, g = 0, b = 0, a = 0, total = 0;

                for (int i = startX; i < endX; i++)
                {
                    if (i < 0 || i >= W) continue;
                    for (int j = startY; j < endY; j++)
                    {
                        if (j < 0 || j >= H) continue;
                        double overlapX = Math.Min(srcX2, i + 1) - Math.Max(srcX1, i);
                        double overlapY = Math.Min(srcY2, j + 1) - Math.Max(srcY1, j);
                        double area = Math.Max(0, overlapX) * Math.Max(0, overlapY);
                        int idx = (j * W + i) * 4;
                        r += imageData[idx] * area;
                        g += imageData[idx + 1] * area;
                        b += imageData[idx + 2] * area;
                        a += imageData[idx + 3] * area;
                        total += area;
                    }
                }

                int dstIdx = (y * newW + x) * 4;
                if (total > 0)
                {
                    buffer[dstIdx] = (byte)(r / total);
                    buffer[dstIdx + 1] = (byte)(g / total);
                    buffer[dstIdx + 2] = (byte)(b / total);
                    buffer[dstIdx + 3] = (byte)(a / total);
                }
            });
        }


        // 双线性插值算法
        private static void BilinearResize(
            byte[] imageData, int imageStride, int W, int H, int newW, int newH, byte[] buffer)
        {
            double scaleX = (double)W / newW;
            double scaleY = (double)H / newH;

            Parallel.For(0, newW * newH, p =>
            {
                int x = p % newW;
                int y = p / newW;

                double srcX = x * scaleX;
                double srcY = y * scaleY;

                int x1 = (int)Math.Floor(srcX);
                int y1 = (int)Math.Floor(srcY);
                int x2 = Math.Min(x1 + 1, W - 1);
                int y2 = Math.Min(y1 + 1, H - 1);

                double dx = srcX - x1;
                double dy = srcY - y1;

                for (int c = 0; c < 4; c++)
                {
                    double v =
                        (1 - dx) * (1 - dy) * imageData[(y1 * W + x1) * 4 + c] +
                        dx * (1 - dy) * imageData[(y1 * W + x2) * 4 + c] +
                        (1 - dx) * dy * imageData[(y2 * W + x1) * 4 + c] +
                        dx * dy * imageData[(y2 * W + x2) * 4 + c];

                    buffer[(y * newW + x) * 4 + c] = (byte)v;
                }
            });
        }

        // 最近邻插值算法
        private static void NearestNeighborResize(
            byte[] imageData, int imageStride, int W, int H, int newW, int newH, byte[] buffer)
        {
            double scaleX = (double)W / newW;
            double scaleY = (double)H / newH;

            Parallel.For(0, newW * newH, p =>
            {
                int x = p % newW;
                int y = p / newW;

                int srcX = (int)Math.Min(Math.Round(x * scaleX), W - 1);
                int srcY = (int)Math.Min(Math.Round(y * scaleY), H - 1);

                int srcIdx = (srcY * W + srcX) * 4;
                int dstIdx = (y * newW + x) * 4;

                buffer[dstIdx] = imageData[srcIdx];
                buffer[dstIdx + 1] = imageData[srcIdx + 1];
                buffer[dstIdx + 2] = imageData[srcIdx + 2];
                buffer[dstIdx + 3] = imageData[srcIdx + 3];
            });
        }

        // 双三次插值算法
        private static void BicubicResize(
            byte[] imageData, int imageStride, int W, int H, int newW, int newH, byte[] buffer)
        {
            double scaleX = (double)W / newW;
            double scaleY = (double)H / newH;

            Parallel.For(0, newW * newH, p =>
            {
                int x = p % newW;
                int y = p / newW;

                double srcX = x * scaleX;
                double srcY = y * scaleY;

                int srcXi = (int)Math.Floor(srcX);
                int srcYi = (int)Math.Floor(srcY);

                for (int c = 0; c < 4; c++)
                {
                    double value = 0.0;
                    for (int m = -1; m <= 2; m++)
                    {
                        int yy = Clamp(srcYi + m, 0, H - 1);
                        double wy = CubicHermite(srcY - (srcYi + m));
                        for (int n = -1; n <= 2; n++)
                        {
                            int xx = Clamp(srcXi + n, 0, W - 1);
                            double wx = CubicHermite(srcX - (srcXi + n));
                            int idx = (yy * W + xx) * 4 + c;
                            value += imageData[idx] * wx * wy;
                        }
                    }
                    int dstIdx = (y * newW + x) * 4 + c;
                    buffer[dstIdx] = (byte)Math.Max(0, Math.Min(255, value));
                }
            });
        }


        // Hermite三次插值核函数
        private static double CubicHermite(double t)
        {
            t = Math.Abs(t);
            if (t <= 1)
                return (1.5 * t - 2.5) * t * t + 1;
            else if (t < 2)
                return ((-0.5 * t + 2.5) * t - 4) * t + 2;
            else
                return 0;
        }

        // 辅助Clamp
        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
        // Lanczos 重采样算法（a=3，常用）
        private static void LanczosResize(
            byte[] imageData, int imageStride, int W, int H, int newW, int newH, byte[] buffer)
        {
            const int a = 3;
            double scaleX = (double)W / newW;
            double scaleY = (double)H / newH;

            Parallel.For(0, newW * newH, p =>
            {
                int x = p % newW;
                int y = p / newW;

                double srcX = (x + 0.5) * scaleX - 0.5;
                double srcY = (y + 0.5) * scaleY - 0.5;

                for (int c = 0; c < 4; c++)
                {
                    double sum = 0.0, weightSum = 0.0;
                    for (int m = (int)Math.Floor(srcY) - a + 1; m <= (int)Math.Floor(srcY) + a; m++)
                    {
                        if (m < 0 || m >= H) continue;
                        double wy = LanczosKernel(srcY - m, a);
                        for (int n = (int)Math.Floor(srcX) - a + 1; n <= (int)Math.Floor(srcX) + a; n++)
                        {
                            if (n < 0 || n >= W) continue;
                            double wx = LanczosKernel(srcX - n, a);
                            double w = wx * wy;
                            int idx = (m * W + n) * 4 + c;
                            sum += imageData[idx] * w;
                            weightSum += w;
                        }
                    }
                    int dstIdx = (y * newW + x) * 4 + c;
                    buffer[dstIdx] = (byte)(weightSum > 0 ? Math.Max(0, Math.Min(255, sum / weightSum)) : 0);
                }
            });
        }


        // Lanczos 核函数
        private static double LanczosKernel(double x, int a)
        {
            if (x == 0) return 1.0;
            if (Math.Abs(x) >= a) return 0.0;
            double pix = Math.PI * x;
            return (a * Math.Sin(pix) * Math.Sin(pix / a)) / (pix * pix);
        }
        // 高斯重采样算法
        private static void GaussianResize(
            byte[] imageData, int imageStride, int W, int H, int newW, int newH, byte[] buffer)
        {
            double scaleX = (double)W / newW;
            double scaleY = (double)H / newH;
            double sigma = 1.0;
            int kernelRadius = 2;

            Parallel.For(0, newW * newH, p =>
            {
                int x = p % newW;
                int y = p / newW;

                double srcX = (x + 0.5) * scaleX - 0.5;
                double srcY = (y + 0.5) * scaleY - 0.5;

                for (int c = 0; c < 4; c++)
                {
                    double sum = 0.0, weightSum = 0.0;
                    for (int m = (int)Math.Floor(srcY) - kernelRadius; m <= (int)Math.Floor(srcY) + kernelRadius; m++)
                    {
                        if (m < 0 || m >= H) continue;
                        double wy = GaussianKernel(srcY - m, sigma);
                        for (int n = (int)Math.Floor(srcX) - kernelRadius; n <= (int)Math.Floor(srcX) + kernelRadius; n++)
                        {
                            if (n < 0 || n >= W) continue;
                            double wx = GaussianKernel(srcX - n, sigma);
                            double w = wx * wy;
                            int idx = (m * W + n) * 4 + c;
                            sum += imageData[idx] * w;
                            weightSum += w;
                        }
                    }
                    int dstIdx = (y * newW + x) * 4 + c;
                    buffer[dstIdx] = (byte)(weightSum > 0 ? Math.Max(0, Math.Min(255, sum / weightSum)) : 0);
                }
            });
        }


        // 高斯核函数
        private static double GaussianKernel(double x, double sigma)
        {
            return Math.Exp(-(x * x) / (2 * sigma * sigma));
        }
        // Mitchell-Netravali（Mitchell）重采样算法
        private static void MitchellResize(
            byte[] imageData, int imageStride, int W, int H, int newW, int newH, byte[] buffer)
        {
            const double B = 1.0 / 3.0;
            const double C = 1.0 / 3.0;
            int kernelRadius = 2;

            double scaleX = (double)W / newW;
            double scaleY = (double)H / newH;

            Parallel.For(0, newW * newH, p =>
            {
                int x = p % newW;
                int y = p / newW;

                double srcX = x * scaleX;
                double srcY = y * scaleY;

                int srcXi = (int)Math.Floor(srcX);
                int srcYi = (int)Math.Floor(srcY);

                for (int c = 0; c < 4; c++)
                {
                    double value = 0.0, weightSum = 0.0;
                    for (int m = -kernelRadius + 1; m <= kernelRadius; m++)
                    {
                        int yy = Clamp(srcYi + m, 0, H - 1);
                        double wy = MitchellKernel(srcY - (srcYi + m), B, C);
                        for (int n = -kernelRadius + 1; n <= kernelRadius; n++)
                        {
                            int xx = Clamp(srcXi + n, 0, W - 1);
                            double wx = MitchellKernel(srcX - (srcXi + n), B, C);
                            double w = wx * wy;
                            int idx = (yy * W + xx) * 4 + c;
                            value += imageData[idx] * w;
                            weightSum += w;
                        }
                    }
                    int dstIdx = (y * newW + x) * 4 + c;
                    buffer[dstIdx] = (byte)(weightSum > 0 ? Math.Max(0, Math.Min(255, value / weightSum)) : 0);
                }
            });
        }


        // Mitchell-Netravali核函数
        private static double MitchellKernel(double x, double B, double C)
        {
            x = Math.Abs(x);
            double x2 = x * x;
            double x3 = x2 * x;
            if (x < 1)
            {
                return ((12 - 9 * B - 6 * C) * x3 +
                        (-18 + 12 * B + 6 * C) * x2 +
                        (6 - 2 * B)) / 6.0;
            }
            else if (x < 2)
            {
                return ((-B - 6 * C) * x3 +
                        (6 * B + 30 * C) * x2 +
                        (-12 * B - 48 * C) * x +
                        (8 * B + 24 * C)) / 6.0;
            }
            else
            {
                return 0.0;
            }
        }
        // Box Filter 算法（均值池化，适合大幅缩小时）
        private static void BoxFilterResize(
            byte[] imageData, int imageStride, int W, int H, int newW, int newH, byte[] buffer)
        {
            double scaleX = (double)W / newW;
            double scaleY = (double)H / newH;

            Parallel.For(0, newW * newH, p =>
            {
                int x = p % newW;
                int y = p / newW;

                int srcX1 = (int)(x * scaleX);
                int srcY1 = (int)(y * scaleY);
                int srcX2 = Math.Max(srcX1 + 1, (int)Math.Min((x + 1) * scaleX, W));
                int srcY2 = Math.Max(srcY1 + 1, (int)Math.Min((y + 1) * scaleY, H));

                double r = 0, g = 0, b = 0, a = 0;
                int count = 0;

                for (int j = srcY1; j < srcY2; j++)
                {
                    for (int i = srcX1; i < srcX2; i++)
                    {
                        int idx = (j * W + i) * 4;
                        r += imageData[idx];
                        g += imageData[idx + 1];
                        b += imageData[idx + 2];
                        a += imageData[idx + 3];
                        count++;
                    }
                }

                int dstIdx = (y * newW + x) * 4;
                if (count > 0)
                {
                    buffer[dstIdx] = (byte)(r / count);
                    buffer[dstIdx + 1] = (byte)(g / count);
                    buffer[dstIdx + 2] = (byte)(b / count);
                    buffer[dstIdx + 3] = (byte)(a / count);
                }
            });
        }

        // 积分图像缩放（Integral Image Downsampling）
        private static void IntegralImageResize(
            byte[] imageData, int imageStride, int W, int H, int newW, int newH, byte[] buffer)
        {
            // 构建积分图像（每通道一张）
            long[,] sumR = new long[H + 1, W + 1];
            long[,] sumG = new long[H + 1, W + 1];
            long[,] sumB = new long[H + 1, W + 1];
            long[,] sumA = new long[H + 1, W + 1];

            for (int y = 1; y <= H; y++)
            {
                for (int x = 1; x <= W; x++)
                {
                    int idx = ((y - 1) * W + (x - 1)) * 4;
                    sumR[y, x] = sumR[y - 1, x] + sumR[y, x - 1] - sumR[y - 1, x - 1] + imageData[idx];
                    sumG[y, x] = sumG[y - 1, x] + sumG[y, x - 1] - sumG[y - 1, x - 1] + imageData[idx + 1];
                    sumB[y, x] = sumB[y - 1, x] + sumB[y, x - 1] - sumB[y - 1, x - 1] + imageData[idx + 2];
                    sumA[y, x] = sumA[y - 1, x] + sumA[y, x - 1] - sumA[y - 1, x - 1] + imageData[idx + 3];
                }
            }

            double scaleX = (double)W / newW;
            double scaleY = (double)H / newH;

            Parallel.For(0, newW * newH, p =>
            {
                int x = p % newW;
                int y = p / newW;

                int srcX1 = (int)(x * scaleX);
                int srcY1 = (int)(y * scaleY);
                int srcX2 = Math.Max(srcX1 + 1, (int)Math.Min((x + 1) * scaleX, W));
                int srcY2 = Math.Max(srcY1 + 1, (int)Math.Min((y + 1) * scaleY, H));

                int area = Math.Max(1, (srcX2 - srcX1) * (srcY2 - srcY1));

                int x1 = srcX1, y1 = srcY1, x2 = srcX2, y2 = srcY2;
                long r = sumR[y2, x2] - sumR[y1, x2] - sumR[y2, x1] + sumR[y1, x1];
                long g = sumG[y2, x2] - sumG[y1, x2] - sumG[y2, x1] + sumG[y1, x1];
                long b = sumB[y2, x2] - sumB[y1, x2] - sumB[y2, x1] + sumB[y1, x1];
                long a = sumA[y2, x2] - sumA[y1, x2] - sumA[y2, x1] + sumA[y1, x1];

                int dstIdx = (y * newW + x) * 4;
                buffer[dstIdx] = (byte)(r / area);
                buffer[dstIdx + 1] = (byte)(g / area);
                buffer[dstIdx + 2] = (byte)(b / area);
                buffer[dstIdx + 3] = (byte)(a / area);
            });
        }
        private static void ModePoolingResize(
    byte[] imageData, int imageStride, int W, int H, int newW, int newH, byte[] buffer)
        {
            double scaleX = (double)W / newW;
            double scaleY = (double)H / newH;

            Parallel.For(0, newW * newH, p =>
            {
                int x = p % newW;
                int y = p / newW;

                int srcX1 = (int)(x * scaleX);
                int srcY1 = (int)(y * scaleY);
                int srcX2 = Math.Max(srcX1 + 1, (int)Math.Min((x + 1) * scaleX, W));
                int srcY2 = Math.Max(srcY1 + 1, (int)Math.Min((y + 1) * scaleY, H));

                // 用字典统计颜色出现次数
                var colorCount = new System.Collections.Generic.Dictionary<int, int>();
                for (int j = srcY1; j < srcY2; j++)
                {
                    for (int i = srcX1; i < srcX2; i++)
                    {
                        int idx = (j * W + i) * 4;
                        int color = (imageData[idx] << 24) | (imageData[idx + 1] << 16) | (imageData[idx + 2] << 8) | imageData[idx + 3];
                        if (colorCount.ContainsKey(color))
                            colorCount[color]++;
                        else
                            colorCount[color] = 1;
                    }
                }
                // 找出现最多的颜色
                int maxColor = 0, maxCount = 0;
                foreach (var kv in colorCount)
                {
                    if (kv.Value > maxCount)
                    {
                        maxCount = kv.Value;
                        maxColor = kv.Key;
                    }
                }
                int dstIdx = (y * newW + x) * 4;
                buffer[dstIdx] = (byte)((maxColor >> 24) & 0xFF);
                buffer[dstIdx + 1] = (byte)((maxColor >> 16) & 0xFF);
                buffer[dstIdx + 2] = (byte)((maxColor >> 8) & 0xFF);
                buffer[dstIdx + 3] = (byte)(maxColor & 0xFF);
            });
        }
        // Hermite插值算法
        private static void HermiteResize(
            byte[] imageData, int imageStride, int W, int H, int newW, int newH, byte[] buffer)
        {
            double scaleX = (double)W / newW;
            double scaleY = (double)H / newH;

            Parallel.For(0, newW * newH, p =>
            {
                int x = p % newW;
                int y = p / newW;

                double srcX = x * scaleX;
                double srcY = y * scaleY;

                int x0 = (int)Math.Floor(srcX);
                int y0 = (int)Math.Floor(srcY);
                int x1 = Math.Min(x0 + 1, W - 1);
                int y1 = Math.Min(y0 + 1, H - 1);

                double dx = srcX - x0;
                double dy = srcY - y0;

                for (int c = 0; c < 4; c++)
                {
                    // 取四个点
                    double v00 = imageData[(y0 * W + x0) * 4 + c];
                    double v10 = imageData[(y0 * W + x1) * 4 + c];
                    double v01 = imageData[(y1 * W + x0) * 4 + c];
                    double v11 = imageData[(y1 * W + x1) * 4 + c];

                    // Hermite插值（先x后y）
                    double i0 = HermiteInterpolate(v00, v10, dx);
                    double i1 = HermiteInterpolate(v01, v11, dx);
                    double value = HermiteInterpolate(i0, i1, dy);

                    buffer[(y * newW + x) * 4 + c] = (byte)Math.Max(0, Math.Min(255, value));
                }
            });
        }

        // 一维Hermite插值函数（简化版，t为0~1）
        private static double HermiteInterpolate(double a, double b, double t)
        {
            // Hermite基函数：h = 3t^2 - 2t^3
            double h = t * t * (3 - 2 * t);
            return a + (b - a) * h;
        }
    }
}