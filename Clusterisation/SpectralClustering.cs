using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace ImageToMidi
{
    public static partial class Clusterisation
    {
        // RGB转Lab（近似，适合聚类）
        private static double[] RgbToLab(byte r, byte g, byte b)
        {
            // 归一化
            double R = r / 255.0, G = g / 255.0, B = b / 255.0;
            // sRGB to XYZ
            Func<double, double> f = v => v > 0.04045 ? Math.Pow((v + 0.055) / 1.055, 2.4) : v / 12.92;
            R = f(R); G = f(G); B = f(B);
            double x = (R * 0.4124 + G * 0.3576 + B * 0.1805) / 0.95047;
            double y = (R * 0.2126 + G * 0.7152 + B * 0.0722) / 1.00000;
            double z = (R * 0.0193 + G * 0.1192 + B * 0.9505) / 1.08883;
            Func<double, double> labf = t => t > 0.008856 ? Math.Pow(t, 1.0 / 3) : (7.787 * t + 16.0 / 116);
            double fx = labf(x), fy = labf(y), fz = labf(z);
            return new double[] { 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz) };
        }

        public static BitmapPalette SpectralPalette(
    byte[] image, int colorCount, int maxSamples = 1000, double sigma = 32.0, int kmeansIters = 8, int knn = 10)
        {
            // 1. 收集所有有效像素索引
            int totalPixels = image.Length / 4;
            List<int> allValid = new List<int>();
            for (int i = 0; i < totalPixels; i++)
            {
                if (image[i * 4 + 3] > 128)
                    allValid.Add(i);
            }

            // 2. 随机打乱并取前 maxSamples 个
            Random rand = new Random(0);
            for (int i = allValid.Count - 1; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                int tmp = allValid[i];
                allValid[i] = allValid[j];
                allValid[j] = tmp;
            }
            var sampleIndices = allValid.Take(Math.Min(maxSamples, allValid.Count)).ToList();

            int n = sampleIndices.Count;
            if (n == 0)
                throw new Exception("没有可用的采样像素");

            // 3. 转Lab空间
            double[][] points = new double[n][];
            for (int i = 0; i < n; i++)
            {
                int idx = sampleIndices[i] * 4;
                points[i] = RgbToLab(image[idx + 2], image[idx + 1], image[idx + 0]);
            }

            // 4. 构建KNN高斯相似度矩阵
            double sigma2 = sigma * sigma;
            var edges = new List<(int, int, double)>(n * knn);
            Parallel.For(0, n, i =>
            {
                var dists = new List<(double, int)>(n);
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    double dist2 = 0;
                    for (int d = 0; d < 3; d++)
                        dist2 += (points[i][d] - points[j][d]) * (points[i][d] - points[j][d]);
                    dists.Add((dist2, j));
                }
                var knnPairs = dists.OrderBy(x => x.Item1).Take(knn).ToArray();
                lock (edges)
                {
                    foreach (var pair in knnPairs)
                    {
                        double sim = Math.Exp(-pair.Item1 / (2 * sigma2));
                        edges.Add((i, pair.Item2, sim));
                    }
                }
            });

            // 5. 构建稀疏W和D
            double[] D = new double[n];
            var W = new Dictionary<(int, int), double>(edges.Count * 2);
            foreach (var (i, j, sim) in edges)
            {
                if (!W.ContainsKey((i, j))) W[(i, j)] = sim;
                if (!W.ContainsKey((j, i))) W[(j, i)] = sim;
                D[i] += sim;
                D[j] += sim;
            }

            // 6. 拉普拉斯特征向量（只取前2-3维，极大加速）
            int eigCount = Math.Min(3, colorCount);
            double[][] eigVecs = new double[eigCount][];
            for (int k = 0; k < eigCount; k++)
            {
                double[] v = new double[n];
                for (int i = 0; i < n; i++)
                    v[i] = rand.NextDouble();
                // Gram-Schmidt正交化
                for (int j = 0; j < k; j++)
                {
                    double dot = 0;
                    for (int i = 0; i < n; i++)
                        dot += v[i] * eigVecs[j][i];
                    for (int i = 0; i < n; i++)
                        v[i] -= dot * eigVecs[j][i];
                }
                // 幂迭代
                for (int iter = 0; iter < 10; iter++)
                {
                    double[] v2 = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        double sum = D[i] * v[i];
                        for (int j = 0; j < n; j++)
                        {
                            if (i == j) continue;
                            if (W.TryGetValue((i, j), out double wij))
                                sum -= wij * v[j];
                        }
                        v2[i] = sum;
                    }
                    // Gram-Schmidt正交化
                    for (int j = 0; j < k; j++)
                    {
                        double dot = 0;
                        for (int i = 0; i < n; i++)
                            dot += v2[i] * eigVecs[j][i];
                        for (int i = 0; i < n; i++)
                            v2[i] -= dot * eigVecs[j][i];
                    }
                    // 归一化
                    double norm = Math.Sqrt(v2.Sum(x => x * x));
                    if (norm < 1e-8) break;
                    for (int i = 0; i < n; i++)
                        v[i] = v2[i] / norm;
                }
                eigVecs[k] = v;
            }

            // 7. 用特征向量组成n x eigCount矩阵，KMeans++聚类
            double[][] features = new double[n][];
            for (int i = 0; i < n; i++)
            {
                features[i] = new double[eigCount];
                for (int k = 0; k < eigCount; k++)
                    features[i][k] = eigVecs[k][i];
            }

            int[] labels = KMeansPlusPlus(features, colorCount, kmeansIters, rand);

            // 8. 计算每个簇的RGB均值
            var palette = new List<Color>();
            for (int c = 0; c < colorCount; c++)
            {
                double r = 0, g = 0, b = 0;
                int cnt = 0;
                for (int i = 0; i < n; i++)
                {
                    if (labels[i] == c)
                    {
                        int idx = sampleIndices[i] * 4;
                        r += image[idx + 2];
                        g += image[idx + 1];
                        b += image[idx + 0];
                        cnt++;
                    }
                }
                if (cnt > 0)
                    palette.Add(Color.FromRgb((byte)(r / cnt), (byte)(g / cnt), (byte)(b / cnt)));
                else
                    palette.Add(Color.FromRgb(0, 0, 0));
            }
            return new BitmapPalette(palette);
        }

        // KMeans++初始化+聚类
        private static int[] KMeansPlusPlus(double[][] data, int k, int iters, Random rand)
        {
            int n = data.Length, dim = data[0].Length;
            var centers = new List<double[]>();
            centers.Add(data[rand.Next(n)].ToArray());
            for (int c = 1; c < k; c++)
            {
                double[] dists = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double minDist = double.MaxValue;
                    foreach (var center in centers)
                    {
                        double dist = 0;
                        for (int d = 0; d < dim; d++)
                            dist += (data[i][d] - center[d]) * (data[i][d] - center[d]);
                        if (dist < minDist) minDist = dist;
                    }
                    dists[i] = minDist;
                }
                double sum = dists.Sum();
                double r = rand.NextDouble() * sum;
                double acc = 0;
                for (int i = 0; i < n; i++)
                {
                    acc += dists[i];
                    if (acc >= r)
                    {
                        centers.Add(data[i].ToArray());
                        break;
                    }
                }
            }
            int[] labels = new int[n];
            for (int iter = 0; iter < iters; iter++)
            {
                // 分配
                Parallel.For(0, n, i =>
                {
                    double minDist = double.MaxValue;
                    int minId = 0;
                    for (int c = 0; c < k; c++)
                    {
                        double dist = 0;
                        for (int d = 0; d < dim; d++)
                        {
                            double diff = data[i][d] - centers[c][d];
                            dist += diff * diff;
                        }
                        if (dist < minDist)
                        {
                            minDist = dist;
                            minId = c;
                        }
                    }
                    labels[i] = minId;
                });
                // 更新中心
                var newCenters = new double[k][];
                var counts = new int[k];
                for (int c = 0; c < k; c++)
                    newCenters[c] = new double[dim];
                for (int i = 0; i < n; i++)
                {
                    int c = labels[i];
                    for (int d = 0; d < dim; d++)
                        newCenters[c][d] += data[i][d];
                    counts[c]++;
                }
                for (int c = 0; c < k; c++)
                    if (counts[c] > 0)
                        for (int d = 0; d < dim; d++)
                            centers[c][d] = newCenters[c][d] / counts[c];
            }
            return labels;
        }
    }
}