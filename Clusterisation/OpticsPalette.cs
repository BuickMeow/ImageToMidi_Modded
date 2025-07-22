using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ImageToMidi
{
    /// <summary>
    /// OPTICS 聚类调色板生成（极致优化版）
    /// </summary>
    public static class OpticsPalette
    {
        /// <summary>
        /// 基于 OPTICS 算法的调色板生成
        /// </summary>
        public static BitmapPalette Cluster(byte[] image, int colorCount, double? epsilon = null, int minPts = 4, int maxSamples = 2000)
        {
            // 采样像素索引
            List<int> sampleIndices = new List<int>();
            int sampleStep = image.Length > 1024 * 1024 ? 8 : 4;
            for (int i = 0; i < image.Length; i += 4 * sampleStep)
            {
                if (image[i + 3] > 128)
                    sampleIndices.Add(i);
            }
            if (sampleIndices.Count > maxSamples)
            {
                Random r = new Random(0);
                sampleIndices = sampleIndices.OrderBy(x => r.Next()).Take(maxSamples).ToList();
            }
            if (sampleIndices.Count == 0)
                throw new Exception("没有可用的采样像素");

            // 构建样本点
            double[][] points = new double[sampleIndices.Count][];
            Parallel.For(0, sampleIndices.Count, i =>
            {
                int idx = sampleIndices[i];
                points[i] = new double[] { image[idx + 2], image[idx + 1], image[idx + 0] };
            });

            int n = points.Length;
            double eps = epsilon ?? EstimateEpsilon(points, minPts);
            double eps2 = eps * eps;

            // 网格分桶加速邻域查找
            int gridSize = Math.Max(1, (int)(eps / 2));
            var grid = new Dictionary<(int, int, int), List<int>>(n / 8);
            for (int i = 0; i < n; i++)
            {
                var key = ((int)(points[i][0] / gridSize), (int)(points[i][1] / gridSize), (int)(points[i][2] / gridSize));
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                list.Add(i);
            }

            // OPTICS 主体
            double[] reachDist = new double[n];
            for (int i = 0; i < n; i++) reachDist[i] = double.PositiveInfinity;
            bool[] processed = new bool[n];
            List<int> order = new List<int>(n);

            // 优化：用SortedDictionary模拟优先队列
            for (int i = 0; i < n; i++)
            {
                if (processed[i]) continue;
                var neighbors = RangeQuery(i, points, grid, gridSize, eps2);
                processed[i] = true;
                order.Add(i);

                if (neighbors.Count >= minPts)
                {
                    var seeds = new SortedDictionary<double, Queue<int>>();
                    Update(i, neighbors, seeds, points, minPts, processed, reachDist, eps2);
                    while (seeds.Count > 0)
                    {
                        var first = seeds.First();
                        double dist = first.Key;
                        int idx = first.Value.Dequeue();
                        if (first.Value.Count == 0) seeds.Remove(dist);
                        if (processed[idx]) continue;
                        var nbs = RangeQuery(idx, points, grid, gridSize, eps2);
                        processed[idx] = true;
                        order.Add(idx);
                        if (nbs.Count >= minPts)
                            Update(idx, nbs, seeds, points, minPts, processed, reachDist, eps2);
                    }
                }
            }

            // 选取聚类中心
            var peakIndices = FindPeaks(reachDist, order, colorCount);
            int peakCount = peakIndices.Count;

            // 并行分配每个点到最近中心
            int[] assignments = new int[n];
            Parallel.For(0, n, i =>
            {
                double minDist = double.MaxValue;
                int minId = 0;
                for (int c = 0; c < peakCount; c++)
                {
                    double d = Distance2(points[i], points[peakIndices[c]]);
                    if (d < minDist)
                    {
                        minDist = d;
                        minId = c;
                    }
                }
                assignments[i] = minId;
            });

            // 并行计算每个簇的均值
            Color[] palette = new Color[peakCount];
            Parallel.For(0, peakCount, c =>
            {
                double r = 0, g = 0, b = 0;
                int cnt = 0;
                for (int i = 0; i < n; i++)
                {
                    if (assignments[i] == c)
                    {
                        r += points[i][0];
                        g += points[i][1];
                        b += points[i][2];
                        cnt++;
                    }
                }
                if (cnt == 0)
                    palette[c] = Color.FromRgb(0, 0, 0);
                else
                    palette[c] = Color.FromRgb((byte)(r / cnt), (byte)(g / cnt), (byte)(b / cnt));
            });

            var paletteList = palette.ToList();
            while (paletteList.Count < colorCount)
                paletteList.Add(Color.FromRgb(0, 0, 0));
            return new BitmapPalette(paletteList);

            // ===== 内部方法 =====
            List<int> RangeQuery(int idx, double[][] pts, Dictionary<(int, int, int), List<int>> gridDict, int gsize, double eps2val)
            {
                var p = pts[idx];
                var key = ((int)(p[0] / gsize), (int)(p[1] / gsize), (int)(p[2] / gsize));
                var neighbors = new List<int>(32);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            var nkey = (key.Item1 + dx, key.Item2 + dy, key.Item3 + dz);
                            if (!gridDict.TryGetValue(nkey, out var idxs)) continue;
                            foreach (var j in idxs)
                            {
                                if (j == idx) continue;
                                if (Distance2(p, pts[j]) <= eps2val)
                                    neighbors.Add(j);
                            }
                        }
                return neighbors;
            }

            void Update(int center, List<int> neighbors, SortedDictionary<double, Queue<int>> seeds, double[][] pts, int minPtsVal, bool[] proc, double[] reach, double eps2val)
            {
                // 计算 core distance
                double[] dists = new double[neighbors.Count];
                Parallel.For(0, neighbors.Count, i =>
                {
                    dists[i] = Distance2(pts[center], pts[neighbors[i]]);
                });
                Array.Sort(dists);
                double coreDist = dists.Length >= minPtsVal ? Math.Sqrt(dists[minPtsVal - 1]) : double.PositiveInfinity;
                Parallel.For(0, neighbors.Count, ni =>
                {
                    int j = neighbors[ni];
                    if (proc[j]) return;
                    double newReach = Math.Max(coreDist, Math.Sqrt(Distance2(pts[center], pts[j])));
                    lock (seeds)
                    {
                        if (newReach < reach[j])
                        {
                            reach[j] = newReach;
                            if (!seeds.TryGetValue(newReach, out var q))
                            {
                                q = new Queue<int>();
                                seeds[newReach] = q;
                            }
                            q.Enqueue(j);
                        }
                    }
                });
            }

            List<int> FindPeaks(double[] reach, List<int> ord, int k)
            {
                // 局部极大值点
                var localPeaks = new List<int>();
                for (int i = 1; i < ord.Count - 1; i++)
                {
                    int idx = ord[i];
                    if (reach[idx] > reach[ord[i - 1]] && reach[idx] > reach[ord[i + 1]])
                        localPeaks.Add(idx);
                }
                // 按 reachDist 降序，取前 k 个
                localPeaks = localPeaks.OrderByDescending(i => reach[i]).Take(k).ToList();
                if (localPeaks.Count == 0) localPeaks.Add(ord[0]);
                return localPeaks;
            }

            double EstimateEpsilon(double[][] pts, int minPtsLocal)
            {
                // 计算每个点到第minPtsLocal近邻的距离（并行）
                double[] kthDistances = new double[pts.Length];
                Parallel.For(0, pts.Length, i =>
                {
                    double[] dists = new double[pts.Length - 1];
                    int idx = 0;
                    for (int j = 0; j < pts.Length; j++)
                    {
                        if (i == j) continue;
                        dists[idx++] = Distance2(pts[i], pts[j]);
                    }
                    Array.Sort(dists);
                    kthDistances[i] = Math.Sqrt(dists[Math.Min(minPtsLocal, dists.Length) - 1]);
                });
                Array.Sort(kthDistances);
                return kthDistances[kthDistances.Length / 2];
            }

            double Distance2(double[] p1, double[] p2)
            {
                double dr = p1[0] - p2[0];
                double dg = p1[1] - p2[1];
                double db = p1[2] - p2[2];
                return dr * dr + dg * dg + db * db;
            }
        }
    }
}
