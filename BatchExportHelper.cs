/*using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    // 新增：批处理配置类
    public class BatchProcessConfig
    {
        public int ColorCount { get; set; }
        public int TicksPerPixelValue { get; set; }
        public int MidiPPQValue { get; set; }
        public int StartOffsetValue { get; set; }
        public int MidiBPMValue { get; set; }
        public int FirstKey { get; set; }
        public int LastKey { get; set; }
        public int NoteSplitLengthValue { get; set; }
        public int TargetHeight { get; set; }
        public ResizeAlgorithm ResizeAlgorithmValue { get; set; }
        public List<int> KeyList { get; set; }
        public NoteLengthMode NoteLengthModeValue { get; set; }
        public WhiteKeyMode WhiteKeyModeValue { get; set; }
        public int EffectiveWidth { get; set; }
        public Clusterisation.PaletteClusterMethod Method { get; set; }
        public Clusterisation.PaletteClusterMethod FloydBaseMethod { get; set; }

        // 聚类算法参数
        public double KmeansThreshold { get; set; }
        public int KmeansMaxIterations { get; set; }
        public int KmeansPlusPlusMaxSamples { get; set; }
        public int KmeansPlusPlusSeed { get; set; }
        public int OctreeMaxLevel { get; set; }
        public int OctreeMaxSamples { get; set; }
        public int VarianceSplitMaxSamples { get; set; }
        public int PcaPowerIterations { get; set; }
        public int PcaMaxSamples { get; set; }
        public int WeightedMaxMinIters { get; set; }
        public int WeightedMaxMinMaxSamples { get; set; }
        public int NativeKMeansIterations { get; set; }
        public double NativeKMeansRate { get; set; }
        public double MeanShiftBandwidth { get; set; }
        public int MeanShiftMaxIter { get; set; }
        public int MeanShiftMaxSamples { get; set; }
        public double? DbscanEpsilon { get; set; }
        public int DbscanMinPts { get; set; }
        public int DbscanMaxSamples { get; set; }
        public int GmmMaxIter { get; set; }
        public double GmmTol { get; set; }
        public int GmmMaxSamples { get; set; }
        public int HierarchicalMaxSamples { get; set; }
        public HierarchicalLinkage HierarchicalLinkage { get; set; }
        public HierarchicalDistanceType HierarchicalDistanceType { get; set; }
        public int SpectralMaxSamples { get; set; }
        public double SpectralSigma { get; set; }
        public int SpectralKMeansIters { get; set; }
        public int LabKMeansMaxIterations { get; set; }
        public double LabKMeansThreshold { get; set; }
        public double FloydDitherStrength { get; set; }
        public bool FloydSerpentine { get; set; }
        public double OrderedDitherStrength { get; set; }
        public BayerMatrixSize OrderedDitherMatrixSize { get; set; }
        public double? OpticsEpsilon { get; set; }
        public int OpticsMinPts { get; set; }
        public int OpticsMaxSamples { get; set; }
        public int FixedBitDepth { get; set; }
        public bool UseGrayFixedPalette { get; set; }
        public bool GenColorEvents { get; set; }
        public int PreviewRotation { get; set; }
        public bool PreviewFlip { get; set; }
        public bool IsGray { get; set; }
    }

    public static class BatchExportHelper
    {
        public class BatchExportParams
        {
            public int TicksPerPixelValue { get; set; }
            public int MidiPPQValue { get; set; }
            public int StartOffsetValue { get; set; }
            public int MidiBPMValue { get; set; }
            public bool GenColorEvents { get; set; }
        }

        public static async Task BatchExportMidiAsync(
            IEnumerable<BatchFileItem> items,
            string exportFolder,
            BatchProcessConfig config,
            MainWindow mainWindow,
            IProgress<(int current, int total, string fileName)> progress = null)
        {
            var (processes, exportParams) = await PrepareBatchConversionProcesses(items, config, mainWindow, progress);
            int i = 0;
            foreach (var convert in processes)
            {
                var item = items.ElementAt(i);
                string midiName = $"{item.Index:D2}_{Path.GetFileNameWithoutExtension(item.FileName)}.mid";
                string midiPath = Path.Combine(exportFolder, midiName);
                await Task.Run(() =>
                {
                    ConversionProcess.WriteMidi(
                        midiPath,
                        new[] { convert },
                        exportParams.TicksPerPixelValue,
                        exportParams.MidiPPQValue,
                        exportParams.StartOffsetValue,
                        exportParams.MidiBPMValue,
                        exportParams.GenColorEvents
                    );
                });
                i++;
            }
        }

        public static async Task BatchExportMidiConcatAsync(
            IEnumerable<BatchFileItem> items,
            string outputMidiPath,
            BatchProcessConfig config,
            MainWindow mainWindow,
            IProgress<(int current, int total, string fileName)> progress = null)
        {
            var (processes, exportParams) = await PrepareBatchConversionProcesses(items, config, mainWindow, progress);
            await Task.Run(() =>
            {
                ConversionProcess.WriteMidi(
                    outputMidiPath,
                    processes,
                    exportParams.TicksPerPixelValue,
                    exportParams.MidiPPQValue,
                    exportParams.StartOffsetValue,
                    exportParams.MidiBPMValue,
                    exportParams.GenColorEvents
                );
            });
        }

        public static async Task<(List<ConversionProcess> Processes, BatchExportParams ExportParams)> PrepareBatchConversionProcesses(
            IEnumerable<BatchFileItem> items,
            BatchProcessConfig config,
            MainWindow mainWindow,
            IProgress<(int current, int total, string fileName)> progress = null)
        {
            var itemList = items.ToList();
            int total = itemList.Count;

            var processList = new List<ConversionProcess>();
            for (int i = 0; i < total; i++)
            {
                var item = itemList[i];
                progress?.Report((i + 1, total, item.FileName));
                string imgPath = item.FullPath ?? item.FileName;
                if (!File.Exists(imgPath)) continue;

                BitmapSource src = null;
                string ext = Path.GetExtension(imgPath).ToLowerInvariant();

                string[] bitmapAllowed = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
                string[] svgAllowed = { ".svg" };
                string[] gsVectorAllowed = { ".eps", ".ai", ".pdf" };

                if (bitmapAllowed.Contains(ext))
                {
                    byte[] imageBytes = await Task.Run(() => File.ReadAllBytes(imgPath));
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        using (var ms = new MemoryStream(imageBytes))
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.EndInit();
                            bmp.Freeze();
                            src = bmp;
                        }
                    });
                }
                else if (svgAllowed.Contains(ext))
                {
                    int keyWidth = config.EffectiveWidth;
                    int targetHeight = config.TargetHeight;
                    await Task.Run(() =>
                    {
                        var svgDoc = Svg.SvgDocument.Open(imgPath);
                        using (var bitmap = new System.Drawing.Bitmap(keyWidth, targetHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                        {
                            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
                            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixel;
                            var viewBox = svgDoc.ViewBox;
                            graphics.TranslateTransform(-viewBox.MinX, -viewBox.MinY);
                            graphics.ScaleTransform((float)keyWidth / viewBox.Width, (float)targetHeight / viewBox.Height);
                            svgDoc.Draw(graphics);
                            src = mainWindow.ConvertBitmapToBitmapSource(bitmap);
                            src.Freeze();
                        }
                    });
                }
                else if (gsVectorAllowed.Contains(ext))
                {
                    int keyWidth = config.EffectiveWidth;
                    int targetHeight = config.TargetHeight;
                    src = await Task.Run(() => mainWindow.RenderGsVectorToBitmapSource(imgPath, keyWidth, targetHeight));
                }
                else
                {
                    continue;
                }

                // 旋转/翻转/灰度处理
                int rot = config.PreviewRotation;
                bool flip = config.PreviewFlip;
                if (flip)
                {
                    if (rot == 90) rot = 270;
                    else if (rot == 270) rot = 90;
                }
                switch (rot)
                {
                    case 90: src = await mainWindow.Rotate90(src); break;
                    case 180: src = await mainWindow.Rotate180(src); break;
                    case 270: src = await mainWindow.Rotate270(src); break;
                }
                if (flip) src = await mainWindow.FlipHorizontal(src);
                if (config.IsGray) src = mainWindow.ToGrayScale(src);
                if (src != null && !src.IsFrozen) src.Freeze();

                int width = src.PixelWidth, height = src.PixelHeight, stride = width * 4;
                byte[] pixels = new byte[height * stride];
                src.CopyPixels(pixels, stride, 0);

                // 生成调色板
                var options = new ClusteriseOptions
                {
                    ColorCount = config.ColorCount,
                    Method = config.Method,
                    Src = src,
                    KMeansThreshold = config.KmeansThreshold,
                    KMeansMaxIterations = config.KmeansMaxIterations,
                    KMeansPlusPlusMaxSamples = config.KmeansPlusPlusMaxSamples,
                    KMeansPlusPlusSeed = config.KmeansPlusPlusSeed,
                    OctreeMaxLevel = config.OctreeMaxLevel,
                    OctreeMaxSamples = config.OctreeMaxSamples,
                    VarianceSplitMaxSamples = config.VarianceSplitMaxSamples,
                    PcaPowerIterations = config.PcaPowerIterations,
                    PcaMaxSamples = config.PcaMaxSamples,
                    WeightedMaxMinIters = config.WeightedMaxMinIters,
                    WeightedMaxMinMaxSamples = config.WeightedMaxMinMaxSamples,
                    NativeKMeansIterations = config.NativeKMeansIterations,
                    NativeKMeansRate = config.NativeKMeansRate,
                    MeanShiftBandwidth = config.MeanShiftBandwidth,
                    MeanShiftMaxIter = config.MeanShiftMaxIter,
                    MeanShiftMaxSamples = config.MeanShiftMaxSamples,
                    DbscanEpsilon = config.DbscanEpsilon,
                    DbscanMinPts = config.DbscanMinPts,
                    DbscanMaxSamples = config.DbscanMaxSamples,
                    GmmMaxIter = config.GmmMaxIter,
                    GmmTol = config.GmmTol,
                    GmmMaxSamples = config.GmmMaxSamples,
                    HierarchicalMaxSamples = config.HierarchicalMaxSamples,
                    HierarchicalLinkage = config.HierarchicalLinkage,
                    HierarchicalDistanceType = config.HierarchicalDistanceType,
                    SpectralMaxSamples = config.SpectralMaxSamples,
                    SpectralSigma = config.SpectralSigma,
                    SpectralKMeansIters = config.SpectralKMeansIters,
                    LabKMeansMaxIterations = config.LabKMeansMaxIterations,
                    LabKMeansThreshold = config.LabKMeansThreshold,
                    FloydBaseMethod = (config.Method == Clusterisation.PaletteClusterMethod.FloydSteinbergDither) ? config.FloydBaseMethod : config.FloydBaseMethod,
                    FloydDitherStrength = config.FloydDitherStrength,
                    FloydSerpentine = config.FloydSerpentine,
                    OrderedDitherStrength = config.OrderedDitherStrength,
                    OrderedDitherMatrixSize = config.OrderedDitherMatrixSize,
                    OpticsEpsilon = config.OpticsEpsilon,
                    OpticsMinPts = config.OpticsMinPts,
                    OpticsMaxSamples = config.OpticsMaxSamples,
                    BitDepth = config.FixedBitDepth,
                    UseGrayFixedPalette = config.UseGrayFixedPalette,
                };

                double lastChange;
                byte[] ditheredPixels;
                var palette = Clusterisation.ClusteriseByMethod(
                    pixels, options, out lastChange, out ditheredPixels, null);

                var convert = new ConversionProcess(
                    palette,
                    pixels,
                    width * 4,
                    config.FirstKey,
                    config.LastKey + 1,
                    config.NoteLengthModeValue == NoteLengthMode.SplitToGrid,
                    config.NoteLengthModeValue != NoteLengthMode.Unlimited ? config.NoteSplitLengthValue : 0,
                    config.TargetHeight,
                    config.ResizeAlgorithmValue,
                    config.KeyList,
                    config.WhiteKeyModeValue == WhiteKeyMode.WhiteKeysFixed,
                    config.WhiteKeyModeValue == WhiteKeyMode.BlackKeysFixed,
                    config.WhiteKeyModeValue == WhiteKeyMode.WhiteKeysClipped,
                    config.WhiteKeyModeValue == WhiteKeyMode.BlackKeysClipped
                );
                convert.EffectiveWidth = config.EffectiveWidth;

                await convert.RunProcessAsync(null);

                processList.Add(convert);
            }

            var exportParams = new BatchExportParams
            {
                TicksPerPixelValue = config.TicksPerPixelValue,
                MidiPPQValue = config.MidiPPQValue,
                StartOffsetValue = config.StartOffsetValue,
                MidiBPMValue = config.MidiBPMValue,
                GenColorEvents = config.GenColorEvents
            };
            return (processList, exportParams);
        }
    }
}*/