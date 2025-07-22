using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Concurrent;

namespace ImageToMidi
{
    public static partial class Clusterisation
    {
        /// <summary>
        /// KMeans++ 初始化，支持进度回调，极致并行优化
        /// </summary>
        /// <param name="image">BGRA像素数据</param>
        /// <param name="clusterCount">聚类数</param>
        /// <param name="maxSamples">最大采样数</param>
        /// <param name="seed">随机种子</param>
        /// <param name="progress">进度回调(0~1)</param>
        /// <returns>BitmapPalette</returns>
        public static BitmapPalette KMeansPlusPlusInit(
            byte[] image,
            int clusterCount,
            int maxSamples = 20000,
            int seed = 0,
            Action<double> progress = null)
        {
            // 采样像素索引
            List<int> sampleIndices = SamplePixelIndices(image, maxSamples, seed);
            if (sampleIndices.Count == 0)
                throw new Exception("没有可用的采样像素");

            Random rand = new Random(seed); // 固定种子保证可复现
            List<double[]> centers = new List<double[]>();

            // 1. 随机选第一个中心
            int firstIdx = sampleIndices[rand.Next(sampleIndices.Count)];
            centers.Add(new double[] { image[firstIdx + 2], image[firstIdx + 1], image[firstIdx + 0] });

            double[] minDistSq = new double[sampleIndices.Count];

            // 初始化所有点到第一个中心的距离（并行）
            Parallel.For(0, sampleIndices.Count, i =>
            {
                int idx = sampleIndices[i];
                double r = image[idx + 2];
                double g = image[idx + 1];
                double b = image[idx + 0];
                double dr = r - centers[0][0];
                double dg = g - centers[0][1];
                double db = b - centers[0][2];
                minDistSq[i] = dr * dr + dg * dg + db * db;
            });

            for (int k = 1; k < clusterCount; k++)
            {
                // 计算总距离（并行归约）
                double total = 0;
                object totalLock = new object();
                Parallel.ForEach(
                    Partitioner.Create(0, minDistSq.Length),
                    () => 0.0,
                    (range, state, localSum) =>
                    {
                        for (int i = range.Item1; i < range.Item2; i++)
                            localSum += minDistSq[i];
                        return localSum;
                    },
                    localSum =>
                    {
                        lock (totalLock) { total += localSum; }
                    });

                // 按距离平方加权随机选一个新中心
                double pick = rand.NextDouble() * total;
                double acc = 0;
                int chosen = 0;
                for (int i = 0; i < minDistSq.Length; i++)
                {
                    acc += minDistSq[i];
                    if (acc >= pick)
                    {
                        chosen = i;
                        break;
                    }
                }
                int idx2 = sampleIndices[chosen];
                var newCenter = new double[] { image[idx2 + 2], image[idx2 + 1], image[idx2 + 0] };
                centers.Add(newCenter);

                // 并行更新所有点到新中心的最小距离
                Parallel.For(0, sampleIndices.Count, i =>
                {
                    int idx = sampleIndices[i];
                    double r = image[idx + 2];
                    double g = image[idx + 1];
                    double b = image[idx + 0];
                    double dr = r - newCenter[0];
                    double dg = g - newCenter[1];
                    double db = b - newCenter[2];
                    double dist = dr * dr + dg * dg + db * db;
                    if (dist < minDistSq[i]) minDistSq[i] = dist;
                });

                // 进度回调
                progress?.Invoke((double)(k + 1) / clusterCount);
            }

            // 转为Color列表
            var colorList = new List<Color>(clusterCount);
            foreach (var c in centers)
                colorList.Add(Color.FromRgb((byte)c[0], (byte)c[1], (byte)c[2]));
            return new BitmapPalette(colorList);
        }
    }
}
