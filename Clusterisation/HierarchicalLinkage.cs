using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    /// <summary>
    /// 层次聚类链接方式
    /// </summary>
    public enum HierarchicalLinkage
    {
        Single,   // 单链接
        Complete, // 全链接
        Average   // 均值链接
    }

    /// <summary>
    /// 层次聚类距离类型
    /// </summary>
    public enum HierarchicalDistanceType
    {
        Euclidean, // 欧氏距离
        Manhattan  // 曼哈顿距离
    }

    public static partial class Clusterisation
    {
        /// <summary>
        /// 层次聚类调色板算法
        /// </summary>
        /// <param name="image">BGRA字节数组</param>
        /// <param name="colorCount">目标颜色数</param>
        /// <param name="maxSamples">最大采样点数</param>
        /// <param name="linkage">链接方式</param>
        /// <param name="distanceType">距离类型</param>
        /// <returns>BitmapPalette</returns>
        public static BitmapPalette HierarchicalPalette(
    byte[] image, int colorCount, int maxSamples = 2000,
    HierarchicalLinkage linkage = HierarchicalLinkage.Single,
    HierarchicalDistanceType distanceType = HierarchicalDistanceType.Euclidean)
        {
            // 采样像素索引
            int sampleStep = image.Length > 1024 * 1024 ? 8 : 4;
            int estSampleCount = image.Length / (4 * sampleStep);
            var sampleIndices = new int[Math.Min(estSampleCount, maxSamples)];
            int sampleCount = 0;
            for (int i = 0; i < image.Length; i += 4 * sampleStep)
            {
                if (image[i + 3] > 128)
                {
                    if (sampleCount < sampleIndices.Length)
                        sampleIndices[sampleCount++] = i;
                    else
                        break;
                }
            }
            if (sampleCount == 0)
                throw new Exception("没有可用的采样像素");

            // 洗牌采样
            if (sampleCount > maxSamples)
            {
                var rand = new Random(0);
                for (int i = sampleCount - 1; i > 0; i--)
                {
                    int j = rand.Next(i + 1);
                    int tmp = sampleIndices[i];
                    sampleIndices[i] = sampleIndices[j];
                    sampleIndices[j] = tmp;
                }
                sampleCount = maxSamples;
            }

            // 每个点的RGB
            var points = new double[sampleCount][];
            for (int i = 0; i < sampleCount; i++)
            {
                int idx = sampleIndices[i];
                points[i] = new double[] { image[idx + 2], image[idx + 1], image[idx + 0] };
            }

            // 初始化每个点为一个簇
            var clusters = new int[sampleCount][];
            for (int i = 0; i < sampleCount; i++)
                clusters[i] = new int[] { i };

            // 活跃簇标记
            var active = new bool[sampleCount];
            for (int i = 0; i < sampleCount; i++) active[i] = true;
            int activeCount = sampleCount;

            // 距离矩阵（下三角一维存储）
            int n = sampleCount;
            int distLen = n * (n - 1) / 2;
            var dist = new double[distLen];
            System.Threading.Tasks.Parallel.For(0, n, i =>
            {
                for (int j = 0; j < i; j++)
                    dist[GetDistIndex(i, j)] = Distance(points[i], points[j], distanceType);
            });

            // 最小堆，存储(距离, i, j)
            var heap = new MinHeap(distLen);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < i; j++)
                    heap.Push(dist[GetDistIndex(i, j)], i, j);

            // 主循环
            while (activeCount > colorCount)
            {
                double minDist; int a, b;
                while (true)
                {
                    if (!heap.Pop(out minDist, out a, out b))
                        break;
                    if (active[a] && active[b]) break;
                }
                if (!active[a] || !active[b]) continue;

                // 合并b到a
                int[] merged = new int[clusters[a].Length + clusters[b].Length];
                Array.Copy(clusters[a], 0, merged, 0, clusters[a].Length);
                Array.Copy(clusters[b], 0, merged, clusters[a].Length, clusters[b].Length);
                clusters[a] = merged;
                clusters[b] = null;
                active[b] = false;
                activeCount--;

                // 更新a与其它簇的距离
                for (int k = 0; k < n; k++)
                {
                    if (k == a || !active[k]) continue;
                    double newDist = 0;
                    switch (linkage)
                    {
                        case HierarchicalLinkage.Single:
                            newDist = double.MaxValue;
                            for (int i1 = 0; i1 < clusters[a].Length; i1++)
                                for (int i2 = 0; i2 < clusters[k].Length; i2++)
                                    newDist = Math.Min(newDist, Distance(points[clusters[a][i1]], points[clusters[k][i2]], distanceType));
                            break;
                        case HierarchicalLinkage.Complete:
                            newDist = double.MinValue;
                            for (int i1 = 0; i1 < clusters[a].Length; i1++)
                                for (int i2 = 0; i2 < clusters[k].Length; i2++)
                                    newDist = Math.Max(newDist, Distance(points[clusters[a][i1]], points[clusters[k][i2]], distanceType));
                            break;
                        case HierarchicalLinkage.Average:
                            double sum = 0; int cnt = 0;
                            for (int i1 = 0; i1 < clusters[a].Length; i1++)
                                for (int i2 = 0; i2 < clusters[k].Length; i2++)
                                { sum += Distance(points[clusters[a][i1]], points[clusters[k][i2]], distanceType); cnt++; }
                            newDist = sum / cnt;
                            break;
                    }
                    int idx = a > k ? GetDistIndex(a, k) : GetDistIndex(k, a);
                    dist[idx] = newDist;
                    heap.Push(newDist, a, k);
                }
            }

            // 计算每个簇的均值作为调色板
            var palette = new List<Color>(colorCount);
            for (int i = 0; i < n; i++)
            {
                if (!active[i] || clusters[i] == null) continue;
                double r = 0, g = 0, b = 0;
                for (int j = 0; j < clusters[i].Length; j++)
                {
                    var pt = points[clusters[i][j]];
                    r += pt[0]; g += pt[1]; b += pt[2];
                }
                int cnt = clusters[i].Length;
                palette.Add(Color.FromRgb((byte)(r / cnt), (byte)(g / cnt), (byte)(b / cnt)));
            }
            while (palette.Count < colorCount)
                palette.Add(Color.FromRgb(0, 0, 0));
            if (palette.Count > colorCount)
                palette = palette.Take(colorCount).ToList();
            return new BitmapPalette(palette);

            // 距离函数
            double Distance(double[] p1, double[] p2, HierarchicalDistanceType type)
            {
                if (type == HierarchicalDistanceType.Euclidean)
                {
                    double dr = p1[0] - p2[0];
                    double dg = p1[1] - p2[1];
                    double db = p1[2] - p2[2];
                    return Math.Sqrt(dr * dr + dg * dg + db * db);
                }
                else
                {
                    return Math.Abs(p1[0] - p2[0]) + Math.Abs(p1[1] - p2[1]) + Math.Abs(p1[2] - p2[2]);
                }
            }
            // 距离矩阵一维索引
            int GetDistIndex(int i, int j)
            {
                if (i < j) { int t = i; i = j; j = t; }
                return i * (i - 1) / 2 + j;
            }
        }
        // 最小堆实现
        struct HeapItem
        {
            public double dist; public int i, j;
            public HeapItem(double d, int a, int b) { dist = d; i = a; j = b; }
        }
        class MinHeap
        {
            HeapItem[] arr; int size;
            public MinHeap(int cap) { arr = new HeapItem[cap * 2]; size = 0; }
            public void Push(double d, int i, int j)
            {
                int k = size++;
                arr[k] = new HeapItem(d, i, j);
                while (k > 0)
                {
                    int p = (k - 1) / 2;
                    if (arr[p].dist <= arr[k].dist) break;
                    var tmp = arr[p]; arr[p] = arr[k]; arr[k] = tmp; k = p;
                }
            }
            public bool Pop(out double d, out int i, out int j)
            {
                if (size == 0) { d = 0; i = 0; j = 0; return false; }
                d = arr[0].dist; i = arr[0].i; j = arr[0].j;
                arr[0] = arr[--size];
                int k = 0;
                while (true)
                {
                    int l = 2 * k + 1, r = 2 * k + 2, min = k;
                    if (l < size && arr[l].dist < arr[min].dist) min = l;
                    if (r < size && arr[r].dist < arr[min].dist) min = r;
                    if (min == k) break;
                    var tmp = arr[k]; arr[k] = arr[min]; arr[min] = tmp; k = min;
                }
                return true;
            }
        }
    }
}

