using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static ImageToMidi.Clusterisation;

namespace ImageToMidi
{
    public class ClusteriseOptions
    {
        public int ColorCount { get; set; }
        public Clusterisation.PaletteClusterMethod Method { get; set; }
        public BitmapSource Src { get; set; }

        // KMeans相关
        public double KMeansThreshold { get; set; } = 1.0;
        public int KMeansMaxIterations { get; set; } = 100;

        // KMeans++相关
        public int KMeansPlusPlusMaxSamples { get; set; } = 20000;
        public int KMeansPlusPlusSeed { get; set; } = 0;

        // Octree相关
        public int OctreeMaxLevel { get; set; } = 8;
        public int OctreeMaxSamples { get; set; } = 20000;

        // VarianceSplit相关
        public int VarianceSplitMaxSamples { get; set; } = 20000;

        // PCA相关
        public int PcaPowerIterations { get; set; } = 20;
        public int PcaMaxSamples { get; set; } = 20000;

        // MaxMin相关
        public int WeightedMaxMinIters { get; set; } = 3;
        public int WeightedMaxMinMaxSamples { get; set; } = 20000;

        // NativeKMeans相关
        public int NativeKMeansIterations { get; set; } = 10;
        public double NativeKMeansRate { get; set; } = 0.3;

        // MeanShift相关
        public double MeanShiftBandwidth { get; set; } = 32;
        public int MeanShiftMaxIter { get; set; } = 7;
        public int MeanShiftMaxSamples { get; set; } = 10000;

        // DBSCAN相关
        public double? DbscanEpsilon { get; set; } = null;
        public int DbscanMinPts { get; set; } = 4;
        public int DbscanMaxSamples { get; set; } = 2000;

        // GMM相关
        public int GmmMaxIter { get; set; } = 30;
        public double GmmTol { get; set; } = 1.0;
        public int GmmMaxSamples { get; set; } = 2000;

        // Hierarchical相关
        public int HierarchicalMaxSamples { get; set; } = 2000;
        public HierarchicalLinkage HierarchicalLinkage { get; set; } = HierarchicalLinkage.Single;
        public HierarchicalDistanceType HierarchicalDistanceType { get; set; } = HierarchicalDistanceType.Euclidean;

        // Spectral相关
        public int SpectralMaxSamples { get; set; } = 2000;
        public double SpectralSigma { get; set; } = 32.0;
        public int SpectralKMeansIters { get; set; } = 10;
        // LabKMeans相关
        public double LabKMeansThreshold { get; set; } = 1.0;
        public int LabKMeansMaxIterations { get; set; } = 100;
        // Floyd–Steinberg相关
        public Clusterisation.PaletteClusterMethod FloydBaseMethod { get; set; } = PaletteClusterMethod.OnlyWpf;
        public double FloydDitherStrength { get; set; } = 1.0; // 抖动强度，默认1.0
        public bool FloydSerpentine { get; set; } = true; // 蛇形扫描，默认true
        // OrderedDither相关
        public ImageToMidi.BayerMatrixSize OrderedDitherMatrixSize { get; set; } = ImageToMidi.BayerMatrixSize.Size4x4;
        // OPTICS相关
        public double? OpticsEpsilon { get; set; } = null;
        public int OpticsMinPts { get; set; } = 4;
        public int OpticsMaxSamples { get; set; } = 2000;
        public double OrderedDitherStrength { get; set; } = 1.0;
        public int BitDepth { get; set; }
        public bool UseGrayFixedPalette { get; set; } = false;


    }
    public static partial class Clusterisation
    {
        public enum PaletteClusterMethod
        {
            OnlyWpf,
            OnlyKMeansPlusPlus,
            KMeans,
            KMeansPlusPlus,
            Popularity,
            Octree,
            VarianceSplit,
            Pca,
            MaxMin,
            NativeKMeans,
            MeanShift,
            DBSCAN,
            GMM,
            Hierarchical,
            Spectral,
            LabKMeans,
            FloydSteinbergDither,
            OrderedDither, // 新增
            OPTICS,
            FixedBitPalette,
        }


        private static List<Color> SortPaletteByHsl(IEnumerable<Color> colors)
        {
            return colors.OrderBy(c => RgbToHslKey(c.R, c.G, c.B)).ToList();
        }

        private static double RgbToHslKey(double r, double g, double b)
        {
            // 归一化
            r /= 255.0;
            g /= 255.0;
            b /= 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double h = 0, s, l = (max + min) / 2.0;

            if (max == min)
            {
                h = s = 0; // 灰色
            }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

                if (max == r)
                    h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g)
                    h = (b - r) / d + 2;
                else
                    h = (r - g) / d + 4;

                h /= 6.0;
            }
            // 以H为主，S和L为次排序
            return h * 10000 + s * 100 + l;
        }

        public static BitmapPalette ClusteriseByMethod(
    byte[] image,
    ClusteriseOptions options,
    out double lastChange,
    out byte[] ditheredPixels,
    Action<double> progress = null)
        {
            ditheredPixels = null;
            BitmapPalette palette;
            switch (options.Method)
            {
                case PaletteClusterMethod.OnlyWpf:
                    lastChange = 0;
                    if (options.Src == null) throw new ArgumentNullException(nameof(options.Src), "OnlyWpf 需要 BitmapSource");
                    palette = new BitmapPalette(options.Src, options.ColorCount);
                    break;

                case PaletteClusterMethod.OnlyKMeansPlusPlus:
                    lastChange = 0;
                    palette = KMeansPlusPlusInit(
                        image,
                        options.ColorCount,
                        options.KMeansPlusPlusMaxSamples,
                        options.KMeansPlusPlusSeed,
                        progress
                    );
                    break;

                case PaletteClusterMethod.KMeans:
                    {
                        if (options.Src == null) throw new ArgumentNullException(nameof(options.Src), "KMeans 需要 BitmapSource");
                        var wpfPalette = new BitmapPalette(options.Src, options.ColorCount);
                        palette = KMeans(
                            wpfPalette,
                            image,
                            options.KMeansThreshold,
                            out lastChange,
                            options.KMeansMaxIterations,
                            20000,
                            0,
                            progress // 传递进度回调
                        );
                    }
                    break;

                case PaletteClusterMethod.KMeansPlusPlus:
                    {
                        var kppPalette = KMeansPlusPlusInit(
                            image,
                            options.ColorCount,
                            options.KMeansPlusPlusMaxSamples,
                            options.KMeansPlusPlusSeed
                        );
                        palette = KMeans(
                            kppPalette,
                            image,
                            options.KMeansThreshold,
                            out lastChange,
                            options.KMeansMaxIterations,
                            20000,
                            0,
                            progress // 传递进度回调
                        );
                    }
                    break;

                case PaletteClusterMethod.Popularity:
                    lastChange = 0;
                    palette = PopularityPalette(image, options.ColorCount);
                    break;

                case PaletteClusterMethod.Octree:
                    palette = OctreePalette.CreatePalette(
                        image,
                        options.ColorCount,
                        options.OctreeMaxLevel,
                        options.OctreeMaxSamples
                    );
                    lastChange = 0;
                    break;

                case PaletteClusterMethod.VarianceSplit:
                    lastChange = 0;
                    palette = VarianceSplitPalette(
                        image,
                        options.ColorCount,
                        options.VarianceSplitMaxSamples
                    );
                    break;

                case PaletteClusterMethod.Pca:
                    lastChange = 0;
                    palette = PcaPalette(
                        image,
                        options.ColorCount,
                        options.PcaPowerIterations,
                        options.PcaMaxSamples
                    );
                    break;

                case PaletteClusterMethod.MaxMin:
                    lastChange = 0;
                    palette = WeightedMaxMinKMeansPalette(
                        image,
                        options.ColorCount,
                        options.WeightedMaxMinIters,
                        options.WeightedMaxMinMaxSamples
                    );
                    break;

                case PaletteClusterMethod.NativeKMeans:
                    lastChange = 0;
                    if (options.Src == null) throw new ArgumentNullException(nameof(options.Src), "NativeKMeans 需要 BitmapSource");
                    var initPalette = new BitmapPalette(options.Src, options.ColorCount);
                    palette = NativeKMeansPalette(
                        initPalette,
                        image,
                        options.NativeKMeansIterations,
                        options.NativeKMeansRate
                    );
                    break;

                case PaletteClusterMethod.MeanShift:
                    lastChange = 0;
                    palette = MeanShiftPalette(
                        image,
                        options.ColorCount,
                        options.MeanShiftBandwidth,
                        options.MeanShiftMaxIter,
                        options.MeanShiftMaxSamples
                    );
                    break;

                case PaletteClusterMethod.DBSCAN:
                    lastChange = 0;
                    palette = DBSCANPalette(
                        image,
                        options.ColorCount,
                        options.DbscanEpsilon,
                        options.DbscanMinPts,
                        options.DbscanMaxSamples
                    );
                    break;

                case PaletteClusterMethod.GMM:
                    lastChange = 0;
                    palette = GMMPalette(
                        image,
                        options.ColorCount,
                        options.GmmMaxIter,
                        options.GmmTol,
                        options.GmmMaxSamples
                    );
                    break;

                case PaletteClusterMethod.Hierarchical:
                    lastChange = 0;
                    palette = HierarchicalPalette(
                        image,
                        options.ColorCount,
                        options.HierarchicalMaxSamples,
                        options.HierarchicalLinkage,
                        options.HierarchicalDistanceType
                    );
                    break;

                case PaletteClusterMethod.Spectral:
                    lastChange = 0;
                    palette = SpectralPalette(
                        image,
                        options.ColorCount,
                        options.SpectralMaxSamples,
                        options.SpectralSigma,
                        options.SpectralKMeansIters
                    );
                    break;

                case PaletteClusterMethod.LabKMeans:
                    lastChange = 0;
                    if (options.Src == null) throw new ArgumentNullException(nameof(options.Src), "LabKMeans 需要 BitmapSource");
                    var wpfLabPalette = new BitmapPalette(options.Src, options.ColorCount);
                    palette = LabKMeans(
                        wpfLabPalette,
                        image,
                        options.LabKMeansThreshold,
                        out lastChange,
                        options.LabKMeansMaxIterations
                    );
                    break;

                case PaletteClusterMethod.FloydSteinbergDither:
                    lastChange = 0;
                    if (options.Src == null) throw new ArgumentNullException(nameof(options.Src), "FloydSteinbergDither 需要 BitmapSource");
                    // 这里 image 应该是原始像素数据
                    var basePalette = ClusteriseByMethod(
                        image, // 原始像素
                        new ClusteriseOptions
                        {
                            ColorCount = options.ColorCount,
                            Method = options.FloydBaseMethod,
                            Src = options.Src,
                        }, out _, out _);
                    var dithered = FloydSteinbergDither.Dither(
                        image, // 必须是原始像素
                        options.Src.PixelWidth,
                        options.Src.PixelHeight,
                        basePalette,
                        options.FloydDitherStrength,
                        options.FloydSerpentine
                    );
                    ditheredPixels = dithered;
                    palette = basePalette;
                    break;
                case PaletteClusterMethod.OrderedDither:
                    lastChange = 0;
                    if (options.Src == null) throw new ArgumentNullException(nameof(options.Src), "OrderedDither 需要 BitmapSource");
                    var basePaletteOrdered = ClusteriseByMethod(
                        image,
                        new ClusteriseOptions
                        {
                            ColorCount = options.ColorCount,
                            Method = options.FloydBaseMethod, // 可自定义基础调色板算法
                            Src = options.Src,
                        }, out _, out _);
                    var ditheredOrdered = OrderedDither.Dither(
                        image,
                        options.Src.PixelWidth,
                        options.Src.PixelHeight,
                        basePaletteOrdered,
                        options.OrderedDitherStrength,
                        options.OrderedDitherMatrixSize
                    );
                    ditheredPixels = ditheredOrdered;
                    palette = basePaletteOrdered;
                    break;
                case PaletteClusterMethod.OPTICS:
                    lastChange = 0;
                    palette = OpticsPalette.Cluster(
                        image,
                        options.ColorCount,
                        options.OpticsEpsilon,
                        options.OpticsMinPts,
                        options.OpticsMaxSamples
                    );
                    break;
                case PaletteClusterMethod.FixedBitPalette:
                    palette = FixedBitPalette(
                        image,
                        options,
                        out lastChange,
                        out ditheredPixels,
                        progress
                    );
                    break;
                default:
                    lastChange = 0;
                    throw new ArgumentException("未知聚类方法");
            }


            // 统一HSL排序
            var sortedColors = SortPaletteByHsl(palette.Colors).ToList();
            // 强制补齐
            while (sortedColors.Count < options.ColorCount)
                sortedColors.Add(Color.FromRgb(0, 0, 0));
            if (sortedColors.Count > options.ColorCount)
                sortedColors = sortedColors.Take(options.ColorCount).ToList();
            return new BitmapPalette(sortedColors);
        }
        /// <summary>
        /// 高性能全图随机采样像素索引，排除透明像素
        /// </summary>
        /// <param name="image">像素数据（BGRA）</param>
        /// <param name="maxSamples">最大采样数</param>
        /// <param name="seed">随机种子</param>
        /// <returns>采样像素索引列表（每个为像素起始下标）</returns>
        public static List<int> SamplePixelIndices(byte[] image, int maxSamples, int seed = 0)
        {
            int pixelCount = image.Length / 4;
            Random rand = new Random(seed);
            HashSet<int> selected = new HashSet<int>(maxSamples);
            List<int> result = new List<int>(maxSamples);

            // 采样数量远小于像素总数时，直接随机采样
            while (result.Count < maxSamples && result.Count < pixelCount)
            {
                int idx = rand.Next(pixelCount);
                if (selected.Add(idx))
                {
                    int offset = idx * 4;
                    if (image[offset + 3] > 128) // 排除透明像素
                        result.Add(offset);
                }
            }

            // 如果有效像素太少，补齐所有非透明像素
            if (result.Count == 0)
            {
                for (int i = 0; i < pixelCount; i++)
                {
                    int offset = i * 4;
                    if (image[offset + 3] > 128)
                        result.Add(offset);
                    if (result.Count >= maxSamples) break;
                }
            }

            return result;
        }
        // 已有的KMeansPlusPlusInit和KMeans方法无需变动



        public static BitmapPalette PopularityPalette(byte[] image, int colorCount)
        {
            int length = image.Length;
            int processorCount = Environment.ProcessorCount;
            int blockSize = length / processorCount / 4 * 4;
            if (blockSize == 0) blockSize = length;

            // 每个线程一个字典，最后合并
            var dicts = new Dictionary<int, int>[processorCount];
            Parallel.For(0, processorCount, t =>
            {
                var dict = new Dictionary<int, int>(4096);
                int start = t * blockSize;
                int end = (t == processorCount - 1) ? length : start + blockSize;
                for (int i = start; i < end; i += 4)
                {
                    if (image[i + 3] < 128) continue;
                    int rgb = (image[i + 2] << 16) | (image[i + 1] << 8) | image[i + 0];
                    if (dict.ContainsKey(rgb)) dict[rgb]++;
                    else dict[rgb] = 1;
                }
                dicts[t] = dict;
            });

            // 合并所有线程的字典
            var totalDict = new Dictionary<int, int>(4096);
            foreach (var dict in dicts)
            {
                foreach (var kv in dict)
                {
                    if (totalDict.ContainsKey(kv.Key)) totalDict[kv.Key] += kv.Value;
                    else totalDict[kv.Key] = kv.Value;
                }
            }

            // 取出现次数最多的颜色
            var topColors = totalDict.OrderByDescending(p => p.Value).Take(colorCount)
                .Select(p => Color.FromRgb((byte)(p.Key >> 16), (byte)(p.Key >> 8), (byte)p.Key)).ToList();

            // 补足颜色数
            while (topColors.Count < colorCount)
                topColors.Add(Color.FromRgb(0, 0, 0));

            return new BitmapPalette(topColors);
        }
        public static BitmapPalette WpfMedianCutPalette(BitmapSource src, int colorCount)
        {
            return new BitmapPalette(src, colorCount);
        }

        public static BitmapPalette VarianceSplitPalette(byte[] image, int colorCount, int maxSamples = 20000)

        {
            // 采样像素索引
            List<int> sampleIndices = new List<int>();
            int sampleStep = image.Length > 1024 * 1024 ? 8 : 4;
            for (int i = 0; i < image.Length; i += 4 * sampleStep)
            {
                if (image[i + 3] > 128)
                    sampleIndices.Add(i);
            }
            // 降采样，极大图片时最多采样2万点
            //int maxSamples = 20000;
            if (sampleIndices.Count > maxSamples)
            {
                Random r = new Random(0);
                sampleIndices = sampleIndices.OrderBy(x => r.Next()).Take(maxSamples).ToList();
            }
            if (sampleIndices.Count == 0)
                throw new Exception("没有可用的采样像素");

            List<List<int>> boxes = new List<List<int>>() { sampleIndices };

            while (boxes.Count < colorCount)
            {
                int maxIdx = 0;
                double maxVar = 0;
                int maxAxis = 0;

                // 并行查找最大方差盒子
                object lockObj = new object();
                Parallel.For(0, boxes.Count, i =>
                {
                    var box = boxes[i];
                    if (box.Count < 2) return;
                    double[] mean = new double[3];
                    double[] var = new double[3];

                    // 并行累加均值
                    double sumR = 0, sumG = 0, sumB = 0;
                    Parallel.ForEach(box, () => (r: 0.0, g: 0.0, b: 0.0), (idx, state, local) =>
                    {
                        local.r += image[idx + 2];
                        local.g += image[idx + 1];
                        local.b += image[idx + 0];
                        return local;
                    }, local =>
                    {
                        lock (mean)
                        {
                            sumR += local.r;
                            sumG += local.g;
                            sumB += local.b;
                        }
                    });
                    mean[0] = sumR / box.Count;
                    mean[1] = sumG / box.Count;
                    mean[2] = sumB / box.Count;

                    // 并行累加方差
                    double varR = 0, varG = 0, varB = 0;
                    Parallel.ForEach(box, () => (vr: 0.0, vg: 0.0, vb: 0.0), (idx, state, local) =>
                    {
                        local.vr += (image[idx + 2] - mean[0]) * (image[idx + 2] - mean[0]);
                        local.vg += (image[idx + 1] - mean[1]) * (image[idx + 1] - mean[1]);
                        local.vb += (image[idx + 0] - mean[2]) * (image[idx + 0] - mean[2]);
                        return local;
                    }, local =>
                    {
                        lock (var)
                        {
                            varR += local.vr;
                            varG += local.vg;
                            varB += local.vb;
                        }
                    });
                    var[0] = varR;
                    var[1] = varG;
                    var[2] = varB;

                    double localMaxVar = var.Max();
                    int localAxis = Array.IndexOf(var, localMaxVar);

                    lock (lockObj)
                    {
                        if (localMaxVar > maxVar)
                        {
                            maxVar = localMaxVar;
                            maxIdx = i;
                            maxAxis = localAxis;
                        }
                    }
                });

                var maxBox = boxes[maxIdx];
                if (maxBox.Count < 2) break;

                int axis = maxAxis;
                maxBox.Sort((a, b) => image[a + (2 - axis)].CompareTo(image[b + (2 - axis)]));
                int mid = maxBox.Count / 2;
                var box1 = maxBox.Take(mid).ToList();
                var box2 = maxBox.Skip(mid).ToList();
                boxes.RemoveAt(maxIdx);
                boxes.Add(box1);
                boxes.Add(box2);
            }

            // 并行计算调色板均值
            Color[] palette = new Color[boxes.Count];
            Parallel.For(0, boxes.Count, i =>
            {
                var box = boxes[i];
                if (box.Count == 0)
                {
                    palette[i] = Color.FromRgb(0, 0, 0);
                    return;
                }
                double r = 0, g = 0, b = 0;
                foreach (var idx in box)
                {
                    r += image[idx + 2];
                    g += image[idx + 1];
                    b += image[idx + 0];
                }
                int cnt = box.Count;
                palette[i] = Color.FromRgb((byte)(r / cnt), (byte)(g / cnt), (byte)(b / cnt));
            });

            var paletteList = palette.ToList();
            if (paletteList.Count == 0)
                paletteList.Add(Color.FromRgb(0, 0, 0));
            else if (paletteList.Count > colorCount)
                paletteList = paletteList.Take(colorCount).ToList();
            else if (paletteList.Count < colorCount)
            {
                var fillColor = paletteList[0];
                while (paletteList.Count < colorCount)
                    paletteList.Add(fillColor);
            }
            return new BitmapPalette(paletteList);
        }
        public static BitmapPalette PcaPalette(byte[] image, int colorCount, int powerIterations = 20, int maxSamples = 20000)

        {
            // 采样像素索引
            List<int> sampleIndices = new List<int>();
            int sampleStep = image.Length > 1024 * 1024 ? 8 : 4;
            for (int i = 0; i < image.Length; i += 4 * sampleStep)
            {
                if (image[i + 3] > 128)
                    sampleIndices.Add(i);
            }
            //int maxSamples = 20000;
            if (sampleIndices.Count > maxSamples)
            {
                Random r = new Random(0);
                sampleIndices = sampleIndices.OrderBy(x => r.Next()).Take(maxSamples).ToList();
            }
            int n = sampleIndices.Count;
            if (n == 0)
                throw new Exception("没有可用的采样像素");

            // 并行计算均值
            double meanR = 0, meanG = 0, meanB = 0;
            object meanLock = new object();
            Parallel.ForEach(sampleIndices, () => (r: 0.0, g: 0.0, b: 0.0, cnt: 0),
                (idx, state, local) =>
                {
                    local.r += image[idx + 2];
                    local.g += image[idx + 1];
                    local.b += image[idx + 0];
                    local.cnt++;
                    return local;
                },
                local =>
                {
                    lock (meanLock)
                    {
                        meanR += local.r;
                        meanG += local.g;
                        meanB += local.b;
                    }
                });
            meanR /= n; meanG /= n; meanB /= n;

            // 并行计算协方差矩阵
            double[,] cov = new double[3, 3];
            object covLock = new object();
            Parallel.ForEach(sampleIndices, () => new double[6],
                (idx, state, local) =>
                {
                    double dr = image[idx + 2] - meanR;
                    double dg = image[idx + 1] - meanG;
                    double db = image[idx + 0] - meanB;
                    local[0] += dr * dr; // cov[0,0]
                    local[1] += dr * dg; // cov[0,1]
                    local[2] += dr * db; // cov[0,2]
                    local[3] += dg * dg; // cov[1,1]
                    local[4] += dg * db; // cov[1,2]
                    local[5] += db * db; // cov[2,2]
                    return local;
                },
                local =>
                {
                    lock (covLock)
                    {
                        cov[0, 0] += local[0];
                        cov[0, 1] += local[1];
                        cov[0, 2] += local[2];
                        cov[1, 0] += local[1];
                        cov[1, 1] += local[3];
                        cov[1, 2] += local[4];
                        cov[2, 0] += local[2];
                        cov[2, 1] += local[4];
                        cov[2, 2] += local[5];
                    }
                });
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    cov[i, j] /= n;

            // 幂迭代法求主特征向量
            double[] v = new double[] { 1, 1, 1 };
            double[] v2 = new double[3];
            for (int iter = 0; iter < powerIterations; iter++)
            {
                v2[0] = cov[0, 0] * v[0] + cov[0, 1] * v[1] + cov[0, 2] * v[2];
                v2[1] = cov[1, 0] * v[0] + cov[1, 1] * v[1] + cov[1, 2] * v[2];
                v2[2] = cov[2, 0] * v[0] + cov[2, 1] * v[1] + cov[2, 2] * v[2];
                double norm = Math.Sqrt(v2[0] * v2[0] + v2[1] * v2[1] + v2[2] * v2[2]);
                if (norm < 1e-8) break;
                v2[0] /= norm; v2[1] /= norm; v2[2] /= norm;
                if (Math.Abs(v2[0] - v[0]) < 1e-6 && Math.Abs(v2[1] - v[1]) < 1e-6 && Math.Abs(v2[2] - v[2]) < 1e-6)
                    break;
                Array.Copy(v2, v, 3);
            }

            // 并行计算投影
            double[] projections = new double[n];
            Parallel.For(0, n, i =>
            {
                int idx = sampleIndices[i];
                double dr = image[idx + 2] - meanR;
                double dg = image[idx + 1] - meanG;
                double db = image[idx + 0] - meanB;
                projections[i] = dr * v[0] + dg * v[1] + db * v[2];
            });

            // 记录索引，排序
            int[] indices = Enumerable.Range(0, n).ToArray();
            Array.Sort(projections, indices);

            // 沿主轴等分采样，避免重复
            var palette = new List<Color>();
            HashSet<int> used = new HashSet<int>();
            for (int i = 0; i < colorCount; i++)
            {
                int pos = (int)((double)i / (colorCount - 1) * (n - 1));
                // 防止重复
                while (pos < n && !used.Add(indices[pos])) pos++;
                if (pos >= n) pos = n - 1;
                int idx = sampleIndices[indices[pos]];
                palette.Add(Color.FromRgb(image[idx + 2], image[idx + 1], image[idx + 0]));
            }
            while (palette.Count < colorCount)
                palette.Add(Color.FromRgb(0, 0, 0));
            return new BitmapPalette(palette);
        }
        public static BitmapPalette WeightedMaxMinKMeansPalette(byte[] image, int colorCount, int kmeansIters = 3, int maxSamples = 20000)

        {
            // 采样像素索引
            List<int> sampleIndices = new List<int>();
            int sampleStep = image.Length > 1024 * 1024 ? 8 : 4;
            for (int i = 0; i < image.Length; i += 4 * sampleStep)
            {
                if (image[i + 3] > 128)
                    sampleIndices.Add(i);
            }
            //int maxSamples = 20000;
            if (sampleIndices.Count > maxSamples)
            {
                Random r = new Random(0);
                sampleIndices = sampleIndices.OrderBy(x => r.Next()).Take(maxSamples).ToList();
            }
            if (sampleIndices.Count == 0)
                throw new Exception("没有可用的采样像素");

            // 统计每个颜色的出现次数
            var colorFreq = new Dictionary<int, int>(4096);
            foreach (var idx in sampleIndices)
            {
                int rgb = (image[idx + 2] << 16) | (image[idx + 1] << 8) | image[idx + 0];
                if (colorFreq.ContainsKey(rgb)) colorFreq[rgb]++;
                else colorFreq[rgb] = 1;
            }

            // 1. 先选出现频率最高的颜色作为第一个中心
            int firstRgb = colorFreq.OrderByDescending(p => p.Value).First().Key;
            int firstIdx = sampleIndices.First(idx =>
                ((image[idx + 2] << 16) | (image[idx + 1] << 8) | image[idx + 0]) == firstRgb);
            List<int> centers = new List<int> { firstIdx };

            int n = sampleIndices.Count;
            double[] minDistSq = new double[n];

            // 初始化所有点到第一个中心的距离
            Parallel.For(0, n, i =>
            {
                int idx = sampleIndices[i];
                double dr = image[idx + 2] - image[firstIdx + 2];
                double dg = image[idx + 1] - image[firstIdx + 1];
                double db = image[idx + 0] - image[firstIdx + 0];
                minDistSq[i] = dr * dr + dg * dg + db * db;
            });

            // 2. 依次选取剩余中心
            for (int k = 1; k < colorCount; k++)
            {
                // 并行查找最大距离
                double maxDist = double.MinValue;
                int maxIdx = -1;
                object lockObj = new object();
                Parallel.For(0, n, i =>
                {
                    if (minDistSq[i] > maxDist)
                    {
                        lock (lockObj)
                        {
                            if (minDistSq[i] > maxDist)
                            {
                                maxDist = minDistSq[i];
                                maxIdx = i;
                            }
                        }
                    }
                });

                // 找到所有与maxDist接近的点
                List<int> candidateIdxs = new List<int>();
                double eps = 1e-3;
                for (int i = 0; i < n; i++)
                    if (Math.Abs(minDistSq[i] - maxDist) < eps)
                        candidateIdxs.Add(i);

                // 在这些点中选出现频率最高的颜色
                int bestIdx = candidateIdxs
                    .OrderByDescending(i =>
                    {
                        int idx = sampleIndices[i];
                        int rgb = (image[idx + 2] << 16) | (image[idx + 1] << 8) | image[idx + 0];
                        return colorFreq[rgb];
                    })
                    .First();

                int newCenterIdx = sampleIndices[bestIdx];
                centers.Add(newCenterIdx);

                // 并行更新所有点到新中心的最小距离
                Parallel.For(0, n, i =>
                {
                    int idx = sampleIndices[i];
                    double dr = image[idx + 2] - image[newCenterIdx + 2];
                    double dg = image[idx + 1] - image[newCenterIdx + 1];
                    double db = image[idx + 0] - image[newCenterIdx + 0];
                    double dist = dr * dr + dg * dg + db * db;
                    if (dist < minDistSq[i]) minDistSq[i] = dist;
                });
            }

            // 3. KMeans微调
            double[][] palette = new double[colorCount][];
            for (int i = 0; i < colorCount; i++)
            {
                int idx = centers[i];
                palette[i] = new double[] { image[idx + 2], image[idx + 1], image[idx + 0] };
            }

            int[] assignments = new int[n];
            for (int iter = 0; iter < kmeansIters; iter++)
            {
                // 分配
                Parallel.For(0, n, i =>
                {
                    int idx = sampleIndices[i];
                    double minDist = double.MaxValue;
                    int minId = 0;
                    for (int c = 0; c < colorCount; c++)
                    {
                        double dr = image[idx + 2] - palette[c][0];
                        double dg = image[idx + 1] - palette[c][1];
                        double db = image[idx + 0] - palette[c][2];
                        double dist = dr * dr + dg * dg + db * db;
                        if (dist < minDist)
                        {
                            minDist = dist;
                            minId = c;
                        }
                    }
                    assignments[i] = minId;
                });

                // 更新中心
                double[][] newPalette = new double[colorCount][];
                int[] counts = new int[colorCount];
                for (int c = 0; c < colorCount; c++)
                    newPalette[c] = new double[3];

                for (int i = 0; i < n; i++)
                {
                    int idx = sampleIndices[i];
                    int c = assignments[i];
                    newPalette[c][0] += image[idx + 2];
                    newPalette[c][1] += image[idx + 1];
                    newPalette[c][2] += image[idx + 0];
                    counts[c]++;
                }
                for (int c = 0; c < colorCount; c++)
                {
                    if (counts[c] > 0)
                    {
                        newPalette[c][0] /= counts[c];
                        newPalette[c][1] /= counts[c];
                        newPalette[c][2] /= counts[c];
                    }
                    else
                    {
                        // 若某中心无分配，保持原值
                        newPalette[c][0] = palette[c][0];
                        newPalette[c][1] = palette[c][1];
                        newPalette[c][2] = palette[c][2];
                    }
                }
                palette = newPalette;
            }

            // 4. 生成色板
            var result = new List<Color>(colorCount);
            for (int i = 0; i < colorCount; i++)
                result.Add(Color.FromRgb((byte)palette[i][0], (byte)palette[i][1], (byte)palette[i][2]));
            return new BitmapPalette(result);
        }
        public static BitmapPalette NativeKMeansPalette(BitmapPalette palette, byte[] image, int iterations = 10, double rate = 0.3)

        {
            Random rand = new Random();
            //double rate = 0.3;
            int clusterCount = palette.Colors.Count;
            double[][] positions = new double[clusterCount][];
            for (int i = 0; i < clusterCount; i++)
                positions[i] = new double[3];
            for (int i = 0; i < clusterCount; i++)
            {
                positions[i][0] = palette.Colors[i].R;
                positions[i][1] = palette.Colors[i].G;
                positions[i][2] = palette.Colors[i].B;
            }

            // 1. 预先收集有效像素索引
            List<int> validIndices = new List<int>(image.Length / 4);
            for (int i = 0; i < image.Length; i += 4)
            {
                if (image[i + 3] > 128)
                    validIndices.Add(i);
            }
            int pixelCount = validIndices.Count;
            if (pixelCount == 0)
                return new BitmapPalette(new List<Color> { Colors.Black });

            double[,] means = new double[clusterCount, 3];
            int[] pointCounts = new int[clusterCount];

            for (int iter = 0; iter < iterations; iter++)
            {
                Array.Clear(means, 0, means.Length);
                Array.Clear(pointCounts, 0, pointCounts.Length);

                // 2. 并行分配像素到聚类中心
                int processorCount = Environment.ProcessorCount;
                double[][,] localMeans = new double[processorCount][,];
                int[][] localCounts = new int[processorCount][];
                for (int t = 0; t < processorCount; t++)
                {
                    localMeans[t] = new double[clusterCount, 3];
                    localCounts[t] = new int[clusterCount];
                }

                Parallel.For(0, pixelCount, new ParallelOptions { MaxDegreeOfParallelism = processorCount }, idx =>
                {
                    int threadId = Thread.CurrentThread.ManagedThreadId % processorCount;
                    int i = validIndices[idx];
                    double r = image[i + 2];
                    double g = image[i + 1];
                    double b = image[i + 0];
                    double min = 0;
                    bool first = true;
                    int minid = 0;
                    for (int c = 0; c < clusterCount; c++)
                    {
                        double _r = r - positions[c][0];
                        double _g = g - positions[c][1];
                        double _b = b - positions[c][2];
                        double distsqr = _r * _r + _g * _g + _b * _b;
                        if (distsqr < min || first)
                        {
                            min = distsqr;
                            first = false;
                            minid = c;
                        }
                    }
                    int count = localCounts[threadId][minid];
                    localMeans[threadId][minid, 0] = (localMeans[threadId][minid, 0] * count + r) / (count + 1);
                    localMeans[threadId][minid, 1] = (localMeans[threadId][minid, 1] * count + g) / (count + 1);
                    localMeans[threadId][minid, 2] = (localMeans[threadId][minid, 2] * count + b) / (count + 1);
                    localCounts[threadId][minid]++;
                });

                // 3. 合并线程结果
                for (int c = 0; c < clusterCount; c++)
                {
                    double sumR = 0, sumG = 0, sumB = 0;
                    int totalCount = 0;
                    for (int t = 0; t < processorCount; t++)
                    {
                        int cnt = localCounts[t][c];
                        sumR += localMeans[t][c, 0] * cnt;
                        sumG += localMeans[t][c, 1] * cnt;
                        sumB += localMeans[t][c, 2] * cnt;
                        totalCount += cnt;
                    }
                    if (totalCount > 0)
                    {
                        means[c, 0] = sumR / totalCount;
                        means[c, 1] = sumG / totalCount;
                        means[c, 2] = sumB / totalCount;
                    }
                    pointCounts[c] = totalCount;
                }

                // 4. 更新聚类中心
                for (int i = 0; i < clusterCount; i++)
                {
                    for (int c = 0; c < 3; c++)
                        positions[i][c] = positions[i][c] * (1 - rate) + means[i, c] * rate;
                }
                // 5. 处理空聚类
                for (int i = 0; i < clusterCount; i++)
                {
                    if (pointCounts[i] == 0)
                    {
                        int p = rand.Next(pixelCount);
                        int idx = validIndices[p];
                        positions[i][0] = image[idx + 2];
                        positions[i][1] = image[idx + 1];
                        positions[i][2] = image[idx + 0];
                    }
                }
            }

            // 6. 输出结果，按pointCounts降序
            var result = new List<(double[] pos, int count)>(clusterCount);
            for (int i = 0; i < clusterCount; i++)
                result.Add((positions[i], pointCounts[i]));
            result.Sort((a, b) => b.count.CompareTo(a.count));

            var newcol = new List<Color>();
            for (int i = 0; i < clusterCount; i++)
                newcol.Add(Color.FromRgb((byte)result[i].pos[0], (byte)result[i].pos[1], (byte)result[i].pos[2]));
            return new BitmapPalette(newcol);
        }
        public static BitmapPalette MeanShiftPalette(byte[] image, int colorCount, double bandwidth = 32, int maxIter = 7, int maxSamples = 10000)

        {
            // 采样像素索引
            List<int> sampleIndices = new List<int>();
            int sampleStep = image.Length > 1024 * 1024 ? 8 : 4;
            for (int i = 0; i < image.Length; i += 4 * sampleStep)
            {
                if (image[i + 3] > 128)
                    sampleIndices.Add(i);
            }
            //int maxSamples = 10000; // 可进一步降低
            if (sampleIndices.Count > maxSamples)
            {
                Random r = new Random(0);
                sampleIndices = sampleIndices.OrderBy(x => r.Next()).Take(maxSamples).ToList();
            }
            if (sampleIndices.Count == 0)
                throw new Exception("没有可用的采样像素");

            // 1. 用float存储
            float[][] points = new float[sampleIndices.Count][];
            for (int i = 0; i < sampleIndices.Count; i++)
            {
                int idx = sampleIndices[i];
                points[i] = new float[] { image[idx + 2], image[idx + 1], image[idx + 0] };
            }
            float[][] shifted = points.Select(p => (float[])p.Clone()).ToArray();

            // 2. 网格分桶
            int gridSize = (int)Math.Max(1, bandwidth / 2);
            var grid = new Dictionary<(int, int, int), List<int>>();
            for (int i = 0; i < points.Length; i++)
            {
                var key = ((int)(points[i][0] / gridSize), (int)(points[i][1] / gridSize), (int)(points[i][2] / gridSize));
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                list.Add(i);
            }

            float bandwidthSq = (float)(bandwidth * bandwidth);

            for (int iter = 0; iter < maxIter; iter++)
            {
                Parallel.For(0, shifted.Length, i =>
                {
                    var center = shifted[i];
                    var key = ((int)(center[0] / gridSize), (int)(center[1] / gridSize), (int)(center[2] / gridSize));
                    float sumR = 0, sumG = 0, sumB = 0, weightSum = 0;

                    // 只遍历相邻27个桶
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                var nkey = (key.Item1 + dx, key.Item2 + dy, key.Item3 + dz);
                                if (!grid.TryGetValue(nkey, out var idxs)) continue;
                                foreach (var j in idxs)
                                {
                                    var p = points[j];
                                    float dr = center[0] - p[0], dg = center[1] - p[1], db = center[2] - p[2];
                                    float distSq = dr * dr + dg * dg + db * db;
                                    if (distSq <= bandwidthSq)
                                    {
                                        float weight = (float)Math.Exp(-distSq / (2 * bandwidthSq));
                                        sumR += p[0] * weight;
                                        sumG += p[1] * weight;
                                        sumB += p[2] * weight;
                                        weightSum += weight;
                                    }
                                }
                            }
                    if (weightSum > 0)
                    {
                        center[0] = sumR / weightSum;
                        center[1] = sumG / weightSum;
                        center[2] = sumB / weightSum;
                    }
                });
            }

            // 3. 合并中心并统计权重
            List<(float[] color, int count, float sumR, float sumG, float sumB)> centers = new List<(float[], int, float, float, float)>();
            float mergeDistSq = (float)((bandwidth / 2) * (bandwidth / 2));
            int[] assignments = new int[shifted.Length];
            for (int i = 0; i < shifted.Length; i++)
            {
                var p = shifted[i];
                int foundIdx = -1;
                for (int c = 0; c < centers.Count; c++)
                {
                    var center = centers[c].color;
                    float dr = p[0] - center[0], dg = p[1] - center[1], db = p[2] - center[2];
                    if (dr * dr + dg * dg + db * db < mergeDistSq)
                    {
                        foundIdx = c;
                        break;
                    }
                }
                if (foundIdx == -1)
                {
                    centers.Add(((float[])p.Clone(), 1, points[i][0], points[i][1], points[i][2]));
                    assignments[i] = centers.Count - 1;
                }
                else
                {
                    var tuple = centers[foundIdx];
                    tuple.count++;
                    tuple.sumR += points[i][0];
                    tuple.sumG += points[i][1];
                    tuple.sumB += points[i][2];
                    centers[foundIdx] = tuple;
                    assignments[i] = foundIdx;
                }
            }

            // 4. 用加权平均生成调色板，按count排序
            var palette = centers
                .OrderByDescending(c => c.count)
                .Take(colorCount)
                .Select(c => Color.FromRgb(
                    (byte)(c.sumR / c.count),
                    (byte)(c.sumG / c.count),
                    (byte)(c.sumB / c.count)))
                .ToList();

            while (palette.Count < colorCount)
                palette.Add(Color.FromRgb(0, 0, 0));

            return new BitmapPalette(palette);
        }
        public static BitmapPalette DBSCANPalette(byte[] image, int colorCount, double? epsilon = null, int minPts = 4, int maxSamples = 2000)

        {
            // 采样像素索引
            List<int> sampleIndices = new List<int>();
            int sampleStep = image.Length > 1024 * 1024 ? 8 : 4;
            for (int i = 0; i < image.Length; i += 4 * sampleStep)
            {
                if (image[i + 3] > 128)
                    sampleIndices.Add(i);
            }

            //int maxSamples = 2000; // 适当提升采样数
            if (sampleIndices.Count > maxSamples)
            {
                Random r = new Random(0);
                sampleIndices = sampleIndices.OrderBy(x => r.Next()).Take(maxSamples).ToList();
            }

            if (sampleIndices.Count == 0)
                throw new Exception("没有可用的采样像素");

            // 构建样本点
            var points = sampleIndices.Select(idx => new[]
            {
                (double)image[idx + 2],
                (double)image[idx + 1],
                (double)image[idx + 0]
            }).ToArray();

            int n = points.Length;

            // ====== 自适应 epsilon ======
            double eps = epsilon ?? EstimateEpsilon(points, minPts);
            double eps2 = eps * eps; // 用平方距离

            // 网格分桶加速邻域查找
            int gridSize = Math.Max(1, (int)(eps / 2));
            var grid = new Dictionary<(int, int, int), List<int>>();
            for (int i = 0; i < n; i++)
            {
                var key = ((int)(points[i][0] / gridSize), (int)(points[i][1] / gridSize), (int)(points[i][2] / gridSize));
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                list.Add(i);
            }

            // 初始化标签数组（-1 表示未访问）
            int[] labels = Enumerable.Repeat(-1, n).ToArray();
            int clusterId = 0;

            // DBSCAN 主循环
            for (int i = 0; i < n; i++)
            {
                if (labels[i] != -1) continue;

                var neighbors = RangeQuery(i);
                if (neighbors.Count < minPts)
                {
                    labels[i] = -2; // 噪声点
                }
                else
                {
                    ExpandCluster(i, clusterId, neighbors);
                    clusterId++;
                }
            }

            // 统计每个簇的颜色均值
            Dictionary<int, (double R, double G, double B, int Count)> clusterColors = new Dictionary<int, (double, double, double, int)>();
            for (int i = 0; i < n; i++)
            {
                int label = labels[i];
                if (label < 0) continue; // 跳过噪声
                if (!clusterColors.ContainsKey(label))
                    clusterColors[label] = (0, 0, 0, 0);
                var c = clusterColors[label];
                c.R += points[i][0];
                c.G += points[i][1];
                c.B += points[i][2];
                c.Count++;
                clusterColors[label] = c;
            }

            // 按簇大小排序，取前 colorCount 个
            var sortedClusters = clusterColors.Values
                .OrderByDescending(v => v.Count)
                .Take(colorCount)
                .ToList();

            // 构建调色板
            var palette = new List<Color>();
            foreach (var (r, g, b, count) in sortedClusters)
            {
                byte br = (byte)(r / count);
                byte bg = (byte)(g / count);
                byte bb = (byte)(b / count);
                palette.Add(Color.FromRgb(br, bg, bb));
            }
            while (palette.Count < colorCount)
                palette.Add(Color.FromRgb(0, 0, 0)); // 补黑

            return new BitmapPalette(palette);

            // ====== 内部方法 ======
            // 网格加速邻域查找
            List<int> RangeQuery(int idx)
            {
                var p = points[idx];
                var key = ((int)(p[0] / gridSize), (int)(p[1] / gridSize), (int)(p[2] / gridSize));
                var neighbors = new List<int>();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            var nkey = (key.Item1 + dx, key.Item2 + dy, key.Item3 + dz);
                            if (!grid.TryGetValue(nkey, out var idxs)) continue;
                            foreach (var j in idxs)
                            {
                                if (j == idx) continue;
                                if (Distance2(p, points[j]) <= eps2)
                                    neighbors.Add(j);
                            }
                        }
                return neighbors;
            }

            void ExpandCluster(int idx, int cid, List<int> neighbors)
            {
                labels[idx] = cid;
                var queue = new Queue<int>(neighbors);
                foreach (var neighbor in neighbors)
                    labels[neighbor] = cid;

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    var currentNeighbors = RangeQuery(current);
                    if (currentNeighbors.Count >= minPts)
                    {
                        foreach (var neighbor in currentNeighbors)
                        {
                            if (labels[neighbor] == -1)
                            {
                                labels[neighbor] = cid;
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                }
            }

            double EstimateEpsilon(double[][] pts, int minPtsLocal)
            {
                // 计算每个点到第minPtsLocal近邻的距离
                List<double> kthDistances = new List<double>(pts.Length);
                for (int i = 0; i < pts.Length; i++)
                {
                    List<double> dists = new List<double>(pts.Length - 1);
                    for (int j = 0; j < pts.Length; j++)
                    {
                        if (i == j) continue;
                        dists.Add(Distance2(pts[i], pts[j]));
                    }
                    dists.Sort();
                    kthDistances.Add(Math.Sqrt(dists[Math.Min(minPtsLocal, dists.Count) - 1]));
                }
                kthDistances.Sort();
                return kthDistances[kthDistances.Count / 2];
            }

            double Distance2(double[] p1, double[] p2)
            {
                double dr = p1[0] - p2[0];
                double dg = p1[1] - p2[1];
                double db = p1[2] - p2[2];
                return dr * dr + dg * dg + db * db;
            }
        }

        // 计算两点之间的欧氏距离
        private static double Distance(double[] p1, double[] p2)
        {
            double dr = p1[0] - p2[0];
            double dg = p1[1] - p2[1];
            double db = p1[2] - p2[2];
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }
        public static BitmapPalette GMMPalette(byte[] image, int colorCount, int maxIter = 30, double tol = 1.0, int maxSamples = 2000)

        {
            // 采样像素索引
            List<int> sampleIndices = new List<int>();
            int sampleStep = image.Length > 1024 * 1024 ? 8 : 4;
            for (int i = 0; i < image.Length; i += 4 * sampleStep)
            {
                if (image[i + 3] > 128)
                    sampleIndices.Add(i);
            }
            //int maxSamples = 2000; // 限制采样点数
            if (sampleIndices.Count > maxSamples)
            {
                Random r = new Random(0);
                sampleIndices = sampleIndices.OrderBy(x => r.Next()).Take(maxSamples).ToList();
            }
            if (sampleIndices.Count == 0)
                throw new Exception("没有可用的采样像素");

            int n = sampleIndices.Count;
            int k = colorCount;
            double[][] X = new double[n][];
            for (int i = 0; i < n; i++)
            {
                int idx = sampleIndices[i];
                X[i] = new double[] { image[idx + 2], image[idx + 1], image[idx + 0] };
            }

            // 初始化均值（用KMeans++初始化）
            var means = new double[k][];
            var kpp = KMeansPlusPlusInit(image, k);
            for (int i = 0; i < k; i++)
                means[i] = new double[] { kpp.Colors[i].R, kpp.Colors[i].G, kpp.Colors[i].B };

            // 初始化对角协方差和权重
            var vars = new double[k][];
            var weights = new double[k];
            for (int i = 0; i < k; i++)
            {
                vars[i] = new double[3] { 400.0, 400.0, 400.0 }; // 初始方差
                weights[i] = 1.0 / k;
            }

            double[,] resp = new double[n, k];
            double prevLogLik = double.MinValue;

            for (int iter = 0; iter < maxIter; iter++)
            {
                // E步：并行计算响应度
                Parallel.For(0, n, i =>
                {
                    double sum = 0;
                    double[] probs = new double[k];
                    for (int j = 0; j < k; j++)
                    {
                        probs[j] = weights[j] * GaussianDiag(X[i], means[j], vars[j]);
                        sum += probs[j];
                    }
                    if (sum < 1e-20) sum = 1e-20;
                    for (int j = 0; j < k; j++)
                        resp[i, j] = probs[j] / sum;
                });

                // M步：并行统计
                double[][] newMeans = new double[k][];
                double[][] newVars = new double[k][];
                double[] newWeights = new double[k];

                Parallel.For(0, k, j =>
                {
                    double sumResp = 0;
                    double[] mean = new double[3];
                    double[] var = new double[3];

                    for (int i = 0; i < n; i++)
                    {
                        double r = resp[i, j];
                        sumResp += r;
                        for (int d = 0; d < 3; d++)
                            mean[d] += X[i][d] * r;
                    }
                    if (sumResp < 1e-8) sumResp = 1e-8;
                    for (int d = 0; d < 3; d++)
                        mean[d] /= sumResp;

                    for (int i = 0; i < n; i++)
                    {
                        double r = resp[i, j];
                        for (int d = 0; d < 3; d++)
                        {
                            double diff = X[i][d] - mean[d];
                            var[d] += r * diff * diff;
                        }
                    }
                    for (int d = 0; d < 3; d++)
                        var[d] = Math.Max(var[d] / sumResp, 16.0); // 防止方差过小

                    newMeans[j] = mean;
                    newVars[j] = var;
                    newWeights[j] = sumResp / n;
                });

                means = newMeans;
                vars = newVars;
                weights = newWeights;

                // 收敛性检测（对数似然）
                double logLik = 0;
                for (int i = 0; i < n; i++)
                {
                    double sum = 0;
                    for (int j = 0; j < k; j++)
                        sum += weights[j] * GaussianDiag(X[i], means[j], vars[j]);
                    logLik += Math.Log(sum + 1e-20);
                }
                if (Math.Abs(logLik - prevLogLik) < tol)
                    break;
                prevLogLik = logLik;
            }

            // 生成调色板（用均值）
            var palette = means.Select(m => Color.FromRgb(
                (byte)Math.Max(0, Math.Min(255, m[0])),
                (byte)Math.Max(0, Math.Min(255, m[1])),
                (byte)Math.Max(0, Math.Min(255, m[2])))).ToList();
            while (palette.Count < colorCount)
                palette.Add(Color.FromRgb(0, 0, 0));
            return new BitmapPalette(palette);

            // 对角高斯概率密度
            double GaussianDiag(double[] x, double[] mean, double[] var)
            {
                double det = var[0] * var[1] * var[2];
                double norm = 1.0 / (Math.Pow(2 * Math.PI, 1.5) * Math.Sqrt(det));
                double sum = 0;
                for (int d = 0; d < 3; d++)
                    sum += (x[d] - mean[d]) * (x[d] - mean[d]) / var[d];
                return norm * Math.Exp(-0.5 * sum);
            }
        }
        public static BitmapPalette FixedBitPalette(
    byte[] image,
    ClusteriseOptions options,
    out double lastChange,
    out byte[] ditheredPixels,
    Action<double> progress = null)
        {
            lastChange = 0;
            ditheredPixels = null;

            int bitDepth = options.BitDepth > 0 ? options.BitDepth : 4; // 默认4位
            int colorCount = 1 << bitDepth;
            options.ColorCount = colorCount;

            List<Color> paletteColors = new List<Color>(colorCount);

            if (options.UseGrayFixedPalette)
            {
                // 灰度调色板
                int grayLevels = 1 << bitDepth;
                for (int i = 0; i < grayLevels; i++)
                {
                    byte gray = (byte)(i * 255 / (grayLevels - 1));
                    paletteColors.Add(Color.FromRgb(gray, gray, gray));
                }
            }
            else
            {
                paletteColors.AddRange(GenerateFixedBitPalette(bitDepth));
                // 补齐
                while (paletteColors.Count < colorCount)
                    paletteColors.Add(Color.FromRgb(0, 0, 0));
            }

            return new BitmapPalette(paletteColors);
        }
        private static List<Color> GenerateFixedBitPalette(int bitDepth)
        {
            int colorCount = 1 << bitDepth;
            // 2色：黑白
            if (colorCount == 2)
            {
                return new List<Color>
        {
            Color.FromRgb(0, 0, 0),
            Color.FromRgb(255, 255, 255)
        };
            }
            // 4色：GameBoy四灰阶（常用GB原色，近似值）
            if (colorCount == 4)
            {
                return new List<Color>
        {
            Color.FromRgb(0, 0, 0),    // 最深
            Color.FromRgb(85, 85, 85),    // 深
            Color.FromRgb(170, 170, 170),  // 浅
            Color.FromRgb(255, 255, 255)   // 最浅
        };
            }
            // 16色：Windows标准16色
            if (colorCount == 16)
            {
                return new List<Color>
        {
            Color.FromRgb(0,0,0),       // Black
            Color.FromRgb(128,0,0),     // Maroon
            Color.FromRgb(0,128,0),     // Green
            Color.FromRgb(128,128,0),   // Olive
            Color.FromRgb(0,0,128),     // Navy
            Color.FromRgb(128,0,128),   // Purple
            Color.FromRgb(0,128,128),   // Teal
            Color.FromRgb(192,192,192), // Silver
            Color.FromRgb(128,128,128), // Gray
            Color.FromRgb(255,0,0),     // Red
            Color.FromRgb(0,255,0),     // Lime
            Color.FromRgb(255,255,0),   // Yellow
            Color.FromRgb(0,0,255),     // Blue
            Color.FromRgb(255,0,255),   // Fuchsia
            Color.FromRgb(0,255,255),   // Aqua
            Color.FromRgb(255,255,255), // White
        };
            }
            // 其它bit自动分配
            int[] bitAlloc = AllocateBits(bitDepth, 3); // [r,g,b]
            int rBits = bitAlloc[0], gBits = bitAlloc[1], bBits = bitAlloc[2];
            int rLevels = 1 << rBits, gLevels = 1 << gBits, bLevels = 1 << bBits;
            var palette = new List<Color>(rLevels * gLevels * bLevels);
            for (int r = 0; r < rLevels; r++)
                for (int g = 0; g < gLevels; g++)
                    for (int b = 0; b < bLevels; b++)
                    {
                        byte rr = (byte)(rLevels == 1 ? 0 : r * 255 / (rLevels - 1));
                        byte gg = (byte)(gLevels == 1 ? 0 : g * 255 / (gLevels - 1));
                        byte bb = (byte)(bLevels == 1 ? 0 : b * 255 / (bLevels - 1));
                        palette.Add(Color.FromRgb(rr, gg, bb));
                    }
            return palette;
        }

        // 平均分配bit到n个通道，优先G>R>B
        private static int[] AllocateBits(int totalBits, int channels)
        {
            int[] bits = new int[channels];
            for (int i = 0; i < totalBits; i++)
                bits[i % channels]++;
            // G优先，R次之，B最后
            Array.Reverse(bits);
            return bits;
        }
    }
}
