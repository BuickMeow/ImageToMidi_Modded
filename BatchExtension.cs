using MIDIModificationFramework;
using MIDIModificationFramework.MIDI_Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    public partial class MainWindow : Window
    {
        #region Batch

        // 在MainWindow类中添加
        public ObservableCollection<BatchFileItem> BatchFileList = new ObservableCollection<BatchFileItem>();
        private BatchWindow batchWindow;

        // BatchButton点击事件
        private void BatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (batchWindow == null)
            {
                batchWindow = new BatchWindow(BatchFileList);
            }
            // 如果窗口已最小化，则恢复为正常状态
            if (batchWindow.WindowState == WindowState.Minimized)
            {
                batchWindow.WindowState = WindowState.Normal;
            }
            if (!batchWindow.IsVisible)
            {
                batchWindow.Show();
            }
            else
            {
                batchWindow.Activate();
            }
        }
        public class BatchExportParams
        {
            public int TicksPerPixelValue { get; set; }
            public int MidiPPQValue { get; set; }
            public int StartOffsetValue { get; set; }
            public int MidiBPMValue { get; set; }
            public bool GenColorEvents { get; set; }
        }
        public async Task BatchExportMidiAsync(
    IEnumerable<BatchFileItem> items,
    string exportFolder,
    IProgress<(int current, int total, string fileName)> progress = null)
        {
            var (processes, exportParams) = await PrepareBatchConversionProcesses(items, progress);
            int i = 0;
            foreach (var convert in processes)
            {
                var item = items.ElementAt(i);
                string midiName = $"{item.Index:D2}_{System.IO.Path.GetFileNameWithoutExtension(item.FileName)}.mid";
                string midiPath = System.IO.Path.Combine(exportFolder, midiName);
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

        public async Task BatchExportMidiConcatAsync(
    IEnumerable<BatchFileItem> items,
    string outputMidiPath,
    IProgress<(int current, int total, string fileName)> progress = null)
        {
            var itemList = items.ToList();
            int total = itemList.Count;

            // 获取导出参数
            var exportParams = GetCurrentExportParams();

            // 添加空值检查
            if (exportParams == null)
            {
                throw new InvalidOperationException("无法获取导出参数");
            }

            // 第一步：预计算所有帧的MIDI事件，但分批处理避免内存爆炸
            var allTrackEvents = new List<List<(ulong absTick, MIDIEvent e)>>();

            // 初始化轨道事件列表
            int tracks = (int)trackCount.Value; // 从UI获取轨道数
            for (int i = 0; i < tracks; i++)
            {
                allTrackEvents.Add(new List<(ulong absTick, MIDIEvent e)>());
            }

            ulong globalTick = (ulong)exportParams.StartOffsetValue;
            BitmapPalette sharedPalette = null;

            // 分批处理，每批最多处理5个文件
            const int batchSize = 5;
            for (int batchStart = 0; batchStart < total; batchStart += batchSize)
            {
                int batchEnd = Math.Min(batchStart + batchSize, total);
                var batchItems = itemList.GetRange(batchStart, batchEnd - batchStart);

                // 处理当前批次
                var batchProcesses = await ProcessBatchItemsAsync(batchItems, progress, batchStart, total);

                // 提取MIDI事件并立即释放ConversionProcess对象
                foreach (var process in batchProcesses)
                {
                    // 添加空值检查
                    if (process == null)
                    {
                        System.Diagnostics.Debug.WriteLine("警告：遇到空的ConversionProcess对象，跳过");
                        continue;
                    }

                    using (process) // 确保及时释放
                    {
                        if (sharedPalette == null)
                            sharedPalette = process.Palette;

                        // 提取每个轨道的事件
                        for (int trackIdx = 0; trackIdx < tracks; trackIdx++)
                        {
                            var eventBuffers = GetEventBuffers(process);
                            if (eventBuffers != null && trackIdx < eventBuffers.Length)
                            {
                                ulong tick = globalTick;
                                foreach (MIDIEvent e in eventBuffers[trackIdx])
                                {
                                    if (e != null) // 添加事件空值检查
                                    {
                                        tick += e.DeltaTime * (ulong)exportParams.TicksPerPixelValue;
                                        var clonedEvent = e.Clone();
                                        if (clonedEvent != null)
                                        {
                                            allTrackEvents[trackIdx].Add((tick, clonedEvent));
                                        }
                                    }
                                }
                            }
                        }

                        // 确保 process.targetHeight 不会导致空引用异常
                        if (process.targetHeight > 0)
                        {
                            globalTick += (ulong)(process.targetHeight * exportParams.TicksPerPixelValue);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"警告：ConversionProcess的targetHeight为0或负数：{process.targetHeight}");
                        }
                    }
                }

                // 强制垃圾回收
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            // 第二步：写入MIDI文件
            await Task.Run(() => WriteMidiFromEvents(outputMidiPath, allTrackEvents, sharedPalette, exportParams));
        }

        private async Task<List<ConversionProcess>> ProcessBatchItemsAsync(
            List<BatchFileItem> batchItems,
            IProgress<(int current, int total, string fileName)> progress,
            int startIndex,
            int total)
        {
            var processes = new List<ConversionProcess>();

            for (int i = 0; i < batchItems.Count; i++)
            {
                var item = batchItems[i];
                progress?.Report((startIndex + i + 1, total, item.FileName));

                try
                {
                    // 创建单个ConversionProcess
                    var singleProcess = await CreateSingleConversionProcessAsync(item);
                    if (singleProcess != null)
                    {
                        processes.Add(singleProcess);
                    }
                    else
                    {
                        // 记录警告但不中断处理
                        System.Diagnostics.Debug.WriteLine($"警告：文件 {item.FileName} 无法创建ConversionProcess对象");
                    }
                }
                catch (Exception ex)
                {
                    // 记录错误但继续处理其他文件
                    System.Diagnostics.Debug.WriteLine($"处理文件 {item.FileName} 时出错：{ex.Message}");
                    // 可选：报告给用户
                    // MessageBox.Show($"处理文件 {item.FileName} 时出错：{ex.Message}", "处理错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            return processes;
        }
        // 获取当前导出参数
        private BatchExportParams GetCurrentExportParams()
        {
            return new BatchExportParams
            {
                TicksPerPixelValue = (int)ticksPerPixel.Value,
                MidiPPQValue = (int)midiPPQ.Value,
                StartOffsetValue = (int)startOffset.Value,
                MidiBPMValue = (int)midiBPM.Value,
                GenColorEvents = (bool)genColorEventsCheck.IsChecked
            };
        }

        // 为单个文件创建ConversionProcess对象
        private async Task<ConversionProcess> CreateSingleConversionProcessAsync(BatchFileItem item)
        {
            string imgPath = item.FullPath ?? item.FileName;
            if (!File.Exists(imgPath)) return null;

            BitmapSource src = null;
            byte[] pixels = null;
            byte[] ditheredPixels = null;
            byte[] imageBytes = null;

            try
            {
                // 获取当前参数
                int colorCount = (int)trackCount.Value;
                int firstKey = (int)firstKeyNumber.Value;
                int lastKey = (int)lastKeyNumber.Value;
                int noteSplitLengthValue = (int)noteSplitLength.Value;
                int targetHeight = GetTargetHeight();
                ResizeAlgorithm resizeAlgorithmValue = currentResizeAlgorithm;
                var keyList = GetKeyList();
                var noteLengthModeValue = noteLengthMode;
                var whiteKeyModeValue = whiteKeyMode;
                int effectiveWidth = GetEffectiveKeyWidth();

                // 获取聚类算法参数
                var selectedItem = ClusterMethodComboBox.SelectedItem as ComboBoxItem;
                var method = Clusterisation.PaletteClusterMethod.OnlyWpf;
                var floydBaseMethod = Clusterisation.PaletteClusterMethod.OnlyWpf;

                if (selectedItem != null)
                {
                    var tag = selectedItem.Tag as string;
                    switch (tag)
                    {
                        case "OnlyWpf": method = Clusterisation.PaletteClusterMethod.OnlyWpf; break;
                        case "OnlyKMeansPlusPlus": method = Clusterisation.PaletteClusterMethod.OnlyKMeansPlusPlus; break;
                        case "KMeans": method = Clusterisation.PaletteClusterMethod.KMeans; break;
                        case "KMeansPlusPlus": method = Clusterisation.PaletteClusterMethod.KMeansPlusPlus; break;
                        case "Popularity": method = Clusterisation.PaletteClusterMethod.Popularity; break;
                        case "Octree": method = Clusterisation.PaletteClusterMethod.Octree; break;
                        case "VarianceSplit": method = Clusterisation.PaletteClusterMethod.VarianceSplit; break;
                        case "Pca": method = Clusterisation.PaletteClusterMethod.Pca; break;
                        case "MaxMin": method = Clusterisation.PaletteClusterMethod.MaxMin; break;
                        case "NativeKMeans": method = Clusterisation.PaletteClusterMethod.NativeKMeans; break;
                        case "MeanShift": method = Clusterisation.PaletteClusterMethod.MeanShift; break;
                        case "DBSCAN": method = Clusterisation.PaletteClusterMethod.DBSCAN; break;
                        case "GMM": method = Clusterisation.PaletteClusterMethod.GMM; break;
                        case "Hierarchical": method = Clusterisation.PaletteClusterMethod.Hierarchical; break;
                        case "Spectral": method = Clusterisation.PaletteClusterMethod.Spectral; break;
                        case "LabKMeans": method = Clusterisation.PaletteClusterMethod.LabKMeans; break;
                        case "FloydSteinbergDither": method = Clusterisation.PaletteClusterMethod.FloydSteinbergDither; break;
                        case "OrderedDither": method = Clusterisation.PaletteClusterMethod.OrderedDither; break;
                        case "OPTICS": method = Clusterisation.PaletteClusterMethod.OPTICS; break;
                        case "FixedBitPalette": method = Clusterisation.PaletteClusterMethod.FixedBitPalette; break;
                    }

                    if (FloydBaseMethodBox != null)
                    {
                        var baseTag = ((ComboBoxItem)FloydBaseMethodBox.SelectedItem)?.Tag as string;
                        switch (baseTag)
                        {
                            case "OnlyWpf": floydBaseMethod = Clusterisation.PaletteClusterMethod.OnlyWpf; break;
                            case "OnlyKMeansPlusPlus": floydBaseMethod = Clusterisation.PaletteClusterMethod.OnlyKMeansPlusPlus; break;
                            case "KMeans": floydBaseMethod = Clusterisation.PaletteClusterMethod.KMeans; break;
                            case "Pca": floydBaseMethod = Clusterisation.PaletteClusterMethod.Pca; break;
                            case "DBSCAN": floydBaseMethod = Clusterisation.PaletteClusterMethod.DBSCAN; break;
                        }
                    }
                }

                // 加载图像
                string ext = System.IO.Path.GetExtension(imgPath).ToLowerInvariant();
                string[] bitmapAllowed = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
                string[] svgAllowed = { ".svg" };
                string[] gsVectorAllowed = { ".eps", ".ai", ".pdf" };

                if (bitmapAllowed.Contains(ext))
                {
                    imageBytes = await Task.Run(() => File.ReadAllBytes(imgPath));
                    await Application.Current.Dispatcher.InvokeAsync(() =>
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
                    await Task.Run(() =>
                    {
                        var svgDoc = Svg.SvgDocument.Open(imgPath);
                        using (var bitmap = new System.Drawing.Bitmap(effectiveWidth, targetHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
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
                            graphics.ScaleTransform((float)effectiveWidth / viewBox.Width, (float)targetHeight / viewBox.Height);
                            svgDoc.Draw(graphics);
                            src = ConvertBitmapToBitmapSource(bitmap);
                            src.Freeze();
                        }
                    });
                }
                else if (gsVectorAllowed.Contains(ext))
                {
                    src = await Task.Run(() => RenderGsVectorToBitmapSource(imgPath, effectiveWidth, targetHeight));
                }
                else
                {
                    return null; // 不支持的格式
                }

                // 处理旋转/翻转/灰度
                int rot = previewRotation;
                bool flip = previewFlip;
                bool isGray = grayScaleCheckBox.IsChecked == true;

                if (flip)
                {
                    if (rot == 90) rot = 270;
                    else if (rot == 270) rot = 90;
                }

                switch (rot)
                {
                    case 90: src = await Rotate90(src); break;
                    case 180: src = await Rotate180(src); break;
                    case 270: src = await Rotate270(src); break;
                }

                if (flip) src = await FlipHorizontal(src);
                if (isGray) src = ToGrayScale(src);
                if (src != null && !src.IsFrozen) src.Freeze();

                // 提取像素
                int width = src.PixelWidth;
                int height = src.PixelHeight;
                int stride = width * 4;
                pixels = new byte[height * stride];
                src.CopyPixels(pixels, stride, 0);

                // 生成色板
                var options = new ClusteriseOptions
                {
                    ColorCount = colorCount,
                    Method = method,
                    Src = src,
                    KMeansThreshold = kmeansThreshold,
                    KMeansMaxIterations = kmeansMaxIterations,
                    KMeansPlusPlusMaxSamples = kmeansPlusPlusMaxSamples,
                    KMeansPlusPlusSeed = kmeansPlusPlusSeed,
                    OctreeMaxLevel = octreeMaxLevel,
                    OctreeMaxSamples = octreeMaxSamples,
                    VarianceSplitMaxSamples = varianceSplitMaxSamples,
                    PcaPowerIterations = pcaPowerIterations,
                    PcaMaxSamples = pcaMaxSamples,
                    WeightedMaxMinIters = weightedMaxMinIters,
                    WeightedMaxMinMaxSamples = weightedMaxMinMaxSamples,
                    NativeKMeansIterations = nativeKMeansIterations,
                    NativeKMeansRate = nativeKMeansRate,
                    MeanShiftBandwidth = meanShiftBandwidth,
                    MeanShiftMaxIter = meanShiftMaxIter,
                    MeanShiftMaxSamples = meanShiftMaxSamples,
                    DbscanEpsilon = dbscanEpsilon,
                    DbscanMinPts = dbscanMinPts,
                    DbscanMaxSamples = dbscanMaxSamples,
                    GmmMaxIter = gmmMaxIter,
                    GmmTol = gmmTol,
                    GmmMaxSamples = gmmMaxSamples,
                    HierarchicalMaxSamples = hierarchicalMaxSamples,
                    HierarchicalLinkage = hierarchicalLinkage,
                    HierarchicalDistanceType = hierarchicalDistanceType,
                    SpectralMaxSamples = spectralMaxSamples,
                    SpectralSigma = spectralSigma,
                    SpectralKMeansIters = spectralKMeansIters,
                    LabKMeansMaxIterations = labKMeansMaxIterations,
                    LabKMeansThreshold = labKMeansThreshold,
                    FloydBaseMethod = (method == Clusterisation.PaletteClusterMethod.FloydSteinbergDither) ? floydBaseMethod : floydBaseMethod,
                    FloydDitherStrength = floydDitherStrength,
                    FloydSerpentine = floydSerpentine,
                    OrderedDitherStrength = orderedDitherStrength,
                    OrderedDitherMatrixSize = orderedDitherMatrixSize,
                    OpticsEpsilon = opticsEpsilon,
                    OpticsMinPts = opticsMinPts,
                    OpticsMaxSamples = opticsMaxSamples,
                    BitDepth = fixedBitDepth,
                    UseGrayFixedPalette = useGrayFixedPalette,
                };

                double lastChange;
                var palette = Clusterisation.ClusteriseByMethod(
                    pixels, options, out lastChange, out ditheredPixels, null);

                // 创建ConversionProcess
                var convert = new ConversionProcess(
                    palette,
                    pixels,
                    width * 4,
                    firstKey,
                    lastKey + 1,
                    noteLengthModeValue == NoteLengthMode.SplitToGrid,
                    noteLengthModeValue != NoteLengthMode.Unlimited ? noteSplitLengthValue : 0,
                    targetHeight,
                    resizeAlgorithmValue,
                    keyList,
                    whiteKeyModeValue == WhiteKeyMode.WhiteKeysFixed,
                    whiteKeyModeValue == WhiteKeyMode.BlackKeysFixed,
                    whiteKeyModeValue == WhiteKeyMode.WhiteKeysClipped,
                    whiteKeyModeValue == WhiteKeyMode.BlackKeysClipped,
                    currentColorIdMethod
                );
                convert.EffectiveWidth = effectiveWidth;

                await convert.RunProcessAsync(null);
                return convert;
            }
            finally
            {
                // 立即清理临时对象
                src = null;
                if (imageBytes != null)
                {
                    Array.Clear(imageBytes, 0, imageBytes.Length);
                    imageBytes = null;
                }
                if (pixels != null)
                {
                    Array.Clear(pixels, 0, pixels.Length);
                    pixels = null;
                }
                if (ditheredPixels != null)
                {
                    Array.Clear(ditheredPixels, 0, ditheredPixels.Length);
                    ditheredPixels = null;
                }
            }
        }
        private FastList<MIDIEvent>[] GetEventBuffers(ConversionProcess process)
        {
            // 使用反射获取私有字段
            var eventBuffersField = typeof(ConversionProcess).GetField("EventBuffers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return eventBuffersField.GetValue(process) as FastList<MIDIEvent>[];
        }

        private void WriteMidiFromEvents(
            string filename,
            List<List<(ulong absTick, MIDIEvent e)>> allTrackEvents,
            BitmapPalette palette,
            BatchExportParams exportParams)
        {
            using (var stream = new BufferedStream(File.Open(filename, FileMode.Create)))
            {
                MidiWriter writer = new MidiWriter(stream);
                writer.Init();
                writer.WriteFormat(1);
                writer.WritePPQ((ushort)exportParams.MidiPPQValue);
                writer.WriteNtrks((ushort)allTrackEvents.Count);

                int tempo = 60000000 / exportParams.MidiBPMValue;

                for (int trackIdx = 0; trackIdx < allTrackEvents.Count; trackIdx++)
                {
                    writer.InitTrack();

                    if (trackIdx == 0)
                    {
                        writer.Write(new TempoEvent(0, tempo));
                    }

                    var trackEvents = allTrackEvents[trackIdx];

                    // 添加色彩事件
                    if (exportParams.GenColorEvents && palette != null && trackIdx < palette.Colors.Count)
                    {
                        var c = palette.Colors[trackIdx];
                        trackEvents.Insert(0, (0, new ColorEvent(0, 0, c.R, c.G, c.B, c.A)));
                    }

                    // 排序事件
                    trackEvents.Sort((a, b) => a.absTick.CompareTo(b.absTick));

                    // 写入事件
                    ulong lastTick = 0;
                    foreach (var (absTick, e) in trackEvents)
                    {
                        e.DeltaTime = (uint)(absTick - lastTick);
                        writer.Write(e);
                        lastTick = absTick;
                    }

                    writer.EndTrack();
                }

                writer.Close();
            }
        }
        // 合并导出：所有图片音符拼接到一个轨道
        // 合并导出：所有图片音符拼接到一个轨道
        private async Task<(List<ConversionProcess> Processes, BatchExportParams ExportParams)> PrepareBatchConversionProcesses(
            IEnumerable<BatchFileItem> items,
            IProgress<(int current, int total, string fileName)> progress = null)
        {
            var itemList = items.ToList();
            int total = itemList.Count;
            int colorCount = (int)trackCount.Value;
            int ticksPerPixelValue = (int)ticksPerPixel.Value;
            int midiPPQValue = (int)midiPPQ.Value;
            int startOffsetValue = (int)startOffset.Value;
            int midiBPMValue = (int)midiBPM.Value;
            int firstKey = (int)firstKeyNumber.Value;
            int lastKey = (int)lastKeyNumber.Value;
            int noteSplitLengthValue = (int)noteSplitLength.Value;
            int targetHeight = GetTargetHeight();
            ResizeAlgorithm resizeAlgorithmValue = currentResizeAlgorithm;
            var keyList = GetKeyList();
            var noteLengthModeValue = noteLengthMode;
            var whiteKeyModeValue = whiteKeyMode;
            int effectiveWidth = GetEffectiveKeyWidth();

            // 聚类算法参数
            var selectedItem = ClusterMethodComboBox.SelectedItem as ComboBoxItem;
            var method = Clusterisation.PaletteClusterMethod.OnlyWpf;
            var floydBaseMethod = Clusterisation.PaletteClusterMethod.OnlyWpf;
            if (selectedItem != null)
            {
                var tag = selectedItem.Tag as string;
                switch (tag)
                {
                    case "OnlyWpf": method = Clusterisation.PaletteClusterMethod.OnlyWpf; break;
                    case "OnlyKMeansPlusPlus": method = Clusterisation.PaletteClusterMethod.OnlyKMeansPlusPlus; break;
                    case "KMeans": method = Clusterisation.PaletteClusterMethod.KMeans; break;
                    case "KMeansPlusPlus": method = Clusterisation.PaletteClusterMethod.KMeansPlusPlus; break;
                    case "Popularity": method = Clusterisation.PaletteClusterMethod.Popularity; break;
                    case "Octree": method = Clusterisation.PaletteClusterMethod.Octree; break;
                    case "VarianceSplit": method = Clusterisation.PaletteClusterMethod.VarianceSplit; break;
                    case "Pca": method = Clusterisation.PaletteClusterMethod.Pca; break;
                    case "MaxMin": method = Clusterisation.PaletteClusterMethod.MaxMin; break;
                    case "NativeKMeans": method = Clusterisation.PaletteClusterMethod.NativeKMeans; break;
                    case "MeanShift": method = Clusterisation.PaletteClusterMethod.MeanShift; break;
                    case "DBSCAN": method = Clusterisation.PaletteClusterMethod.DBSCAN; break;
                    case "GMM": method = Clusterisation.PaletteClusterMethod.GMM; break;
                    case "Hierarchical": method = Clusterisation.PaletteClusterMethod.Hierarchical; break;
                    case "Spectral": method = Clusterisation.PaletteClusterMethod.Spectral; break;
                    case "LabKMeans": method = Clusterisation.PaletteClusterMethod.LabKMeans; break;
                    case "FloydSteinbergDither": method = Clusterisation.PaletteClusterMethod.FloydSteinbergDither; break;
                    case "OrderedDither": method = Clusterisation.PaletteClusterMethod.OrderedDither; break;
                    case "OPTICS": method = Clusterisation.PaletteClusterMethod.OPTICS; break;
                    case "FixedBitPalette": method = Clusterisation.PaletteClusterMethod.FixedBitPalette; break;
                }
                if (FloydBaseMethodBox != null)
                {
                    var baseTag = ((ComboBoxItem)FloydBaseMethodBox.SelectedItem)?.Tag as string;
                    switch (baseTag)
                    {
                        case "OnlyWpf": floydBaseMethod = Clusterisation.PaletteClusterMethod.OnlyWpf; break;
                        case "OnlyKMeansPlusPlus": floydBaseMethod = Clusterisation.PaletteClusterMethod.OnlyKMeansPlusPlus; break;
                        case "KMeans": floydBaseMethod = Clusterisation.PaletteClusterMethod.KMeans; break;
                        case "Pca": floydBaseMethod = Clusterisation.PaletteClusterMethod.Pca; break;
                        case "DBSCAN": floydBaseMethod = Clusterisation.PaletteClusterMethod.DBSCAN; break;
                    }
                }
            }

            // 收集所有图片的ConversionProcess
            var processList = new List<ConversionProcess>();
            for (int i = 0; i < total; i++)
            {
                var item = itemList[i];
                progress?.Report((i + 1, total, item.FileName));
                string imgPath = item.FullPath ?? item.FileName;
                if (!File.Exists(imgPath)) continue;

                BitmapSource src = null;
                byte[] pixels = null;
                byte[] ditheredPixels = null;
                byte[] imageBytes = null;

                try
                {
                    string ext = System.IO.Path.GetExtension(imgPath).ToLowerInvariant();

                    // 1. 位图格式
                    string[] bitmapAllowed = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
                    string[] svgAllowed = { ".svg" };
                    string[] gsVectorAllowed = { ".eps", ".ai", ".pdf" };

                    if (bitmapAllowed.Contains(ext))
                    {
                        imageBytes = await Task.Run(() => File.ReadAllBytes(imgPath));
                        await Application.Current.Dispatcher.InvokeAsync(() =>
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
                    // 2. SVG格式
                    else if (svgAllowed.Contains(ext))
                    {
                        int keyWidth = GetEffectiveKeyWidth();
                        int targetHeightBatch = GetTargetHeight();
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
                                src = ConvertBitmapToBitmapSource(bitmap);
                                src.Freeze();
                            }
                        });
                    }
                    // 3. EPS/AI/PDF格式
                    else if (gsVectorAllowed.Contains(ext))
                    {
                        int keyWidth = GetEffectiveKeyWidth();
                        int targetHeightBatch = GetTargetHeight();
                        src = await Task.Run(() => RenderGsVectorToBitmapSource(imgPath, keyWidth, targetHeight));
                    }
                    else
                    {
                        // 不支持的格式，跳过
                        continue;
                    }

                    // 2. 旋转/翻转/灰度
                    int rot = previewRotation;
                    bool flip = previewFlip;
                    bool isGray = grayScaleCheckBox.IsChecked == true;
                    if (flip)
                    {
                        if (rot == 90) rot = 270;
                        else if (rot == 270) rot = 90;
                    }
                    switch (rot)
                    {
                        case 90: src = await Rotate90(src); break;
                        case 180: src = await Rotate180(src); break;
                        case 270: src = await Rotate270(src); break;
                    }
                    if (flip) src = await FlipHorizontal(src);
                    if (isGray) src = ToGrayScale(src);
                    if (src != null && !src.IsFrozen) src.Freeze();

                    // 3. 提取像素
                    int width = src.PixelWidth, height = src.PixelHeight, stride = width * 4;
                    pixels = new byte[height * stride];
                    src.CopyPixels(pixels, stride, 0);

                    // 4. 生成色板
                    var options = new ClusteriseOptions
                    {
                        ColorCount = colorCount,
                        Method = method,
                        Src = src,
                        KMeansThreshold = kmeansThreshold,
                        KMeansMaxIterations = kmeansMaxIterations,
                        KMeansPlusPlusMaxSamples = kmeansPlusPlusMaxSamples,
                        KMeansPlusPlusSeed = kmeansPlusPlusSeed,
                        OctreeMaxLevel = octreeMaxLevel,
                        OctreeMaxSamples = octreeMaxSamples,
                        VarianceSplitMaxSamples = varianceSplitMaxSamples,
                        PcaPowerIterations = pcaPowerIterations,
                        PcaMaxSamples = pcaMaxSamples,
                        WeightedMaxMinIters = weightedMaxMinIters,
                        WeightedMaxMinMaxSamples = weightedMaxMinMaxSamples,
                        NativeKMeansIterations = nativeKMeansIterations,
                        NativeKMeansRate = nativeKMeansRate,
                        MeanShiftBandwidth = meanShiftBandwidth,
                        MeanShiftMaxIter = meanShiftMaxIter,
                        MeanShiftMaxSamples = meanShiftMaxSamples,
                        DbscanEpsilon = dbscanEpsilon,
                        DbscanMinPts = dbscanMinPts,
                        DbscanMaxSamples = dbscanMaxSamples,
                        GmmMaxIter = gmmMaxIter,
                        GmmTol = gmmTol,
                        GmmMaxSamples = gmmMaxSamples,
                        HierarchicalMaxSamples = hierarchicalMaxSamples,
                        HierarchicalLinkage = hierarchicalLinkage,
                        HierarchicalDistanceType = hierarchicalDistanceType,
                        SpectralMaxSamples = spectralMaxSamples,
                        SpectralSigma = spectralSigma,
                        SpectralKMeansIters = spectralKMeansIters,
                        LabKMeansMaxIterations = labKMeansMaxIterations,
                        LabKMeansThreshold = labKMeansThreshold,
                        FloydBaseMethod = (method == Clusterisation.PaletteClusterMethod.FloydSteinbergDither) ? floydBaseMethod : floydBaseMethod,
                        FloydDitherStrength = floydDitherStrength,
                        FloydSerpentine = floydSerpentine,
                        OrderedDitherStrength = orderedDitherStrength,
                        OrderedDitherMatrixSize = orderedDitherMatrixSize,
                        OpticsEpsilon = opticsEpsilon,
                        OpticsMinPts = opticsMinPts,
                        OpticsMaxSamples = opticsMaxSamples,
                        BitDepth = fixedBitDepth,
                        UseGrayFixedPalette = useGrayFixedPalette,
                    };

                    double lastChange;
                    var palette = Clusterisation.ClusteriseByMethod(
                        pixels, options, out lastChange, out ditheredPixels, null);

                    // 5. 生成音符
                    var convert = new ConversionProcess(
                        palette,
                        pixels,
                        width * 4,
                        firstKey,
                        lastKey + 1,
                        noteLengthModeValue == NoteLengthMode.SplitToGrid,
                        noteLengthModeValue != NoteLengthMode.Unlimited ? noteSplitLengthValue : 0,
                        targetHeight,
                        resizeAlgorithmValue,
                        keyList,
                        whiteKeyModeValue == WhiteKeyMode.WhiteKeysFixed,
                        whiteKeyModeValue == WhiteKeyMode.BlackKeysFixed,
                        whiteKeyModeValue == WhiteKeyMode.WhiteKeysClipped,
                        whiteKeyModeValue == WhiteKeyMode.BlackKeysClipped
                    );
                    convert.EffectiveWidth = effectiveWidth;

                    await convert.RunProcessAsync(null);

                    processList.Add(convert);
                }
                finally
                {
                    // 立即清理大对象引用
                    src = null;

                    if (imageBytes != null)
                    {
                        Array.Clear(imageBytes, 0, imageBytes.Length);
                        imageBytes = null;
                    }

                    if (pixels != null)
                    {
                        Array.Clear(pixels, 0, pixels.Length);
                        pixels = null;
                    }

                    if (ditheredPixels != null)
                    {
                        Array.Clear(ditheredPixels, 0, ditheredPixels.Length);
                        ditheredPixels = null;
                    }

                    // 定期强制垃圾回收 - 每处理5个文件强制GC一次
                    if ((i + 1) % 5 == 0)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                }
            }

            var exportParams = new BatchExportParams
            {
                TicksPerPixelValue = ticksPerPixelValue,
                MidiPPQValue = midiPPQValue,
                StartOffsetValue = startOffsetValue,
                MidiBPMValue = midiBPMValue,
                GenColorEvents = (bool)genColorEventsCheck.IsChecked
            };

            // 最终强制垃圾回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            return (processList, exportParams);
        }
        #endregion
    }
}
