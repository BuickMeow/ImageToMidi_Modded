using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ImageToMidi
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly DependencyProperty FadeInStoryboard =
            DependencyProperty.RegisterAttached("FadeInStoryboard", typeof(Storyboard), typeof(MainWindow), new PropertyMetadata(default(Storyboard)));
        public static readonly DependencyProperty FadeOutStoryboard =
            DependencyProperty.RegisterAttached("FadeOutStoryboard", typeof(Storyboard), typeof(MainWindow), new PropertyMetadata(default(Storyboard)));

        //private BitmapSource landscapeColorPreviewSrc, portraitColorPreviewSrc, landscapeGrayPreviewSrc, portraitGrayPreviewSrc;
        //private byte[] landscapeColorPreviewPixels, portraitColorPreviewPixels, landscapeGrayPreviewPixels, portraitGrayPreviewPixels;



        BitmapSource openedImageSrc = null; //1 开启图片
        byte[] openedImagePixels = null; // 2 开启图片像素


        BitmapSource originalImageSrc = null; // 原图
        //byte[] originalImagePixels = null;    // 原图像素

        // 横向彩色
        private BitmapSource landscapeColorPreviewSrc = null;
        private byte[] landscapeColorPreviewPixels = null;
        // 纵向彩色
        private BitmapSource portraitColorPreviewSrc = null;
        private byte[] portraitColorPreviewPixels = null;
        // 横向灰度
        private BitmapSource landscapeGrayPreviewSrc = null;
        private byte[] landscapeGrayPreviewPixels = null;
        // 纵向灰度
        private BitmapSource portraitGrayPreviewSrc = null;
        private byte[] portraitGrayPreviewPixels = null;

        private byte[] ditheredImagePixels = null; // 用于存储抖动后像素

        //bool leftSelected = true; //左侧面板是否打开
        //private bool settingsSelected = false;
        int openedImageWidth = 0;
        int openedImageHeight = 0;
        string openedImagePath = "";
        public BitmapPalette chosenPalette = null;

        ConversionProcess convert = null;
        private int lastPaletteColorCount = -1;

        bool colorPick = false;

        private bool midiExported = false;      // 是否已导出MIDI文件    

        private NoteLengthMode noteLengthMode = NoteLengthMode.Unlimited;

        private HeightModeEnum heightMode = HeightModeEnum.SameAsWidth;
        private int customHeight = 0;
        private int originalImageWidth = 0;
        private int originalImageHeight = 0;
        private CancellationTokenSource _paletteCts; //  用于取消上一次计算
        private ResizeAlgorithm currentResizeAlgorithm = ResizeAlgorithm.AreaResampling; // 默认resizeimage的算法
        private object lastClusterMethodSelectedItem = null;


        // 预览旋转角度和镜像状态
        public int previewRotation = 0; // 0, 90, 180, 270
        private bool previewFlip = false;


        private WhiteKeyMode whiteKeyMode = WhiteKeyMode.AllKeys;//白键状态按钮
        // 异步旋转/翻转原图并可打断
        //private CancellationTokenSource _transformCts;
        //private CancellationTokenSource _thumbnailCts;

        private int highResPreviewWidth = 4096;
        private string lastExportedMidiPath = null;
        public enum HeightModeEnum
        {
            SameAsWidth,
            OriginalHeight,
            CustomHeight,
            OriginalAspectRatio // 新增的枚举值
        }

        // 在MainWindow类中添加
        private enum NoteLengthMode
        {
            Unlimited,      // 不限制音符长度
            FlowWithColor,  // 随颜色流动
            SplitToGrid     // 切成小方格
        }

        private enum WhiteKeyMode
        {
            AllKeys,           // 黑白键都显示
            WhiteKeysFilled,   // 只显示白键，填充，宽度=白键数
            WhiteKeysClipped,  // 只显示白键，裁剪，宽度=白键数，黑键透明
            WhiteKeysFixed,    // 只显示白键，等宽，宽度=总键数，黑键列空

            BlackKeysFilled,   // 只显示黑键，填充，宽度=黑键数
            BlackKeysClipped,  // 只显示黑键，裁剪，宽度=黑键数，白键透明
            BlackKeysFixed,    // 只显示黑键，等宽，宽度=总键数，白键列空

        }


        void MakeFadeInOut(DependencyObject e)
        {
            DoubleAnimation fadeIn = new DoubleAnimation();
            fadeIn.From = 0.0;
            fadeIn.To = 1.0;
            fadeIn.Duration = new Duration(TimeSpan.FromSeconds(0.2));

            Storyboard fadeInBoard = new Storyboard();
            fadeInBoard.Children.Add(fadeIn);
            Storyboard.SetTarget(fadeIn, e);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));

            e.SetValue(FadeInStoryboard, fadeInBoard);

            DoubleAnimation fadeOut = new DoubleAnimation();
            fadeOut.From = 1.0;
            fadeOut.To = 0.0;
            fadeOut.Duration = new Duration(TimeSpan.FromSeconds(0.2));

            Storyboard fadeOutBoard = new Storyboard();
            fadeOutBoard.Children.Add(fadeOut);
            Storyboard.SetTarget(fadeOut, e);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));

            e.SetValue(FadeOutStoryboard, fadeOutBoard);
        }

        // 修改TriggerMenuTransition，支持三栏
        /*void TriggerMenuTransition(bool left, bool settings = false)
        {
            if (settings)
            {
                selectedHighlightLeft.Opacity = 0;
                selectedHighlightMiddle.Opacity = 0;
                selectedHighlightRight.Opacity = 1;
                SettingsPanel.Visibility = Visibility.Visible;
                ManualColorPanel.Visibility = Visibility.Collapsed;
                AutoColorPanel.Visibility = Visibility.Collapsed;
            }
            else if (left)
            {
                selectedHighlightLeft.Opacity = 1;
                selectedHighlightMiddle.Opacity = 0;
                selectedHighlightRight.Opacity = 0;
                SettingsPanel.Visibility = Visibility.Collapsed;
                ManualColorPanel.Visibility = Visibility.Visible;
                AutoColorPanel.Visibility = Visibility.Collapsed;
                settingsSelected = false;
            }
            else
            {
                selectedHighlightLeft.Opacity = 0;
                selectedHighlightMiddle.Opacity = 1;
                selectedHighlightRight.Opacity = 0;
                SettingsPanel.Visibility = Visibility.Collapsed;
                ManualColorPanel.Visibility = Visibility.Collapsed;
                AutoColorPanel.Visibility = Visibility.Visible;
                settingsSelected = false;
            }
        }*/

        public MainWindow()
        {
            InitializeComponent();
            //Debug.WriteLine($"KMeansParamPanel: {KMeansParamPanel}"); // 验证是否为 null
            MakeFadeInOut(selectedHighlightLeft);
            MakeFadeInOut(selectedHighlightMiddle);
            MakeFadeInOut(selectedHighlightRight);
            MakeFadeInOut(colPickerOptions);
            MakeFadeInOut(openedImage);
            MakeFadeInOut(genImage);
            MakeFadeInOut(randomSeedBox);
            MakeFadeInOut(noteSplitLength);

            SwitchPanel(PanelType.Manual);
            lastNonSettingsPanelType = PanelType.Manual;

            colPicker.PickStart += ColPicker_PickStart;
            colPicker.PickStop += ColPicker_PickStop;
            colPicker.PaletteChanged += ReloadPreview;

            CustomHeightNumberSelect.Value = GetTargetHeight();
            UpdateWhiteKeyModeButtonContent();

            ClusterMethodComboBox.SelectionChanged += ClusterMethodComboBox_SelectionChanged;
            ClusterMethodComboBox.PreviewMouseDown += ClusterMethodComboBox_PreviewMouseDown;
            lastClusterMethodSelectedItem = ClusterMethodComboBox.SelectedItem;
            FloydBaseMethodBox.SelectionChanged += FloydBaseMethodBox_SelectionChanged;
            OrderedDitherStrengthBox.ValueChanged += AlgorithmParamBox_ValueChanged;
            OrderedDitherMatrixSizeBox.SelectionChanged += OrderedDitherMatrixSizeComboBox_SelectionChanged;

            HighResWidthNumberSelect.Value = highResPreviewWidth;

            this.Closing += MainWindow_Closing;
            HeightModeComboBox.SelectionChanged += HeightModeComboBox_SelectionChanged;
            NoteLengthModeComboBox.SelectionChanged += NoteLengthModeComboBox_SelectionChanged;
            //UpdateNoteLengthModeUI();
            // JIT预热，异步后台执行
            /*Dispatcher.BeginInvoke(new Action(async () =>
            {
                var bmp = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, new byte[8 * 8 * 4], 8 * 4);
                await Rotate90(bmp);
                await Rotate180(bmp);
                await Rotate270(bmp);
                await FlipHorizontal(bmp);
            }));*/
        }
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (batchWindow != null)
            {
                batchWindow.ForceClose();
            }
        }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 关闭批处理窗口
            if (batchWindow != null)
            {
                batchWindow.ForceClose();
            }
            base.OnClosing(e);
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                Close();
                return;
            }
            double start = windowContent.ActualWidth;
            windowContent.Width = start;
            for (double i = 1; i > 0; i -= 0.05)
            {
                double smooth;
                double strength = 10;
                if (i < 0.5f)
                {
                    smooth = Math.Pow(i * 2, strength) / 2;
                }
                else
                {
                    smooth = 1 - Math.Pow((1 - i) * 2, strength) / 2;
                }
                Width = start * smooth;
                Thread.Sleep(1000 / 60);
            }
            Close();
        }
        private enum PanelType { Manual, Auto, Settings }
        private PanelType currentPanel = PanelType.Manual;
        // 在 MainWindow 类中添加一个字段，记录上一次的面板类型
        //private PanelType? lastPanelTypeForPreview = null;
        private PanelType? lastNonSettingsPanelType = null;
        private void RawMidiSelect_Click(object sender, RoutedEventArgs e)
        {
            colPicker.CancelPick();
            // 只在“自动”与“手动”之间切换时刷新
            if (lastNonSettingsPanelType == PanelType.Auto)
            {
                SwitchPanel(PanelType.Manual);
                ReloadPreview();
            }
            else
            {
                SwitchPanel(PanelType.Manual);
            }
            //lastPanelTypeForPreview = PanelType.Manual;
            lastNonSettingsPanelType = PanelType.Manual;
        }

        private void ColorEventsSelect_Click(object sender, RoutedEventArgs e)
        {
            colPicker.CancelPick();
            if (lastNonSettingsPanelType == PanelType.Manual)
            {
                SwitchPanel(PanelType.Auto);
                ReloadPreview();
            }
            else
            {
                SwitchPanel(PanelType.Auto);
            }
            //lastPanelTypeForPreview = PanelType.Auto;
            lastNonSettingsPanelType = PanelType.Auto;
        }

        private void SettingsSelect_Click(object sender, RoutedEventArgs e)
        {
            colPicker.CancelPick();
            SwitchPanel(PanelType.Settings);
            //lastPanelTypeForPreview = PanelType.Settings;
            // 不更新 lastNonSettingsPanelType
        }

        private void SwitchPanel(PanelType panel)
        {
            currentPanel = panel;
            ManualColorPanel.Visibility = panel == PanelType.Manual ? Visibility.Visible : Visibility.Collapsed;
            AutoColorPanel.Visibility = panel == PanelType.Auto ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = panel == PanelType.Settings ? Visibility.Visible : Visibility.Collapsed;

            selectedHighlightLeft.Opacity = panel == PanelType.Manual ? 1 : 0;
            selectedHighlightMiddle.Opacity = panel == PanelType.Auto ? 1 : 0;
            selectedHighlightRight.Opacity = panel == PanelType.Settings ? 1 : 0;

            // 新增：切换到自动选色时刷新聚类算法参数面板
            if (panel == PanelType.Auto)
            {
                // 异步刷新参数面板，防止UI阻塞
                Dispatcher.BeginInvoke(new Action(UpdateAlgorithmParamPanel), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        //private const int MaxPreviewWidth = 256; // 最大宽度限制

        // 在 MainWindow 类中添加缓冲区池
        private static readonly ConcurrentQueue<byte[]> _bufferPool = new ConcurrentQueue<byte[]>();
        private static readonly object _poolLock = new object();

        private static byte[] RentBuffer(int size)
        {
            // 只池化小于1MB的缓冲区
            if (size < 1024 * 1024)
            {
                while (_bufferPool.TryDequeue(out var buffer))
                {
                    if (buffer.Length >= size)
                        return buffer;
                }
                return new byte[size];
            }
            else
            {
                return new byte[size];
            }
        }

        private static void ReturnBuffer(byte[] buffer)
        {
            if (buffer != null && buffer.Length > 0 && buffer.Length < 1024 * 1024)
            {
                _bufferPool.Enqueue(buffer);
            }
            // 大缓冲区直接丢弃
        }

        // 添加一个大型缓冲区池用于 LOH 对象
        private static readonly ConcurrentQueue<byte[]> _largeBufferPool = new ConcurrentQueue<byte[]>();
        private const int LOH_THRESHOLD = 85000;

        // 获取 LOH 对齐的缓冲区
        private static byte[] RentLargeBuffer(int minSize)
        {
            int alignedSize = Math.Max(minSize, LOH_THRESHOLD);

            // 尝试从池中获取
            while (_largeBufferPool.TryDequeue(out var buffer))
            {
                if (buffer.Length >= alignedSize)
                    return buffer;
            }

            // 创建新的 LOH 对齐缓冲区
            alignedSize = ((alignedSize + 8191) / 8192) * 8192; // 8KB 对齐
            return new byte[alignedSize];
        }

        private static void ReturnLargeBuffer(byte[] buffer)
        {
            if (buffer != null && buffer.Length >= LOH_THRESHOLD)
            {
                _largeBufferPool.Enqueue(buffer);
            }
        }
        /// <summary>
        /// 优化后的缩放方法，减少内存分配
        /// </summary>
        /// <summary>
        /// 优化后的缩放方法，去掉缓冲区池，直接分配内存
        /// </summary>
        private BitmapSource Downsample(BitmapSource src, int? maxWidth = null, int? maxHeight = null, Action<double> progress = null)
        {
            int width = src.PixelWidth;
            int height = src.PixelHeight;

            double scaleX = 1.0, scaleY = 1.0;

            if (maxWidth.HasValue && width > maxWidth.Value)
                scaleX = (double)maxWidth.Value / width;
            if (maxHeight.HasValue && height > maxHeight.Value)
                scaleY = (double)maxHeight.Value / height;

            if (scaleX == 1.0 && scaleY == 1.0)
                return src; // 不需要缩放

            int dstW = (int)Math.Round(width * scaleX);
            int dstH = (int)Math.Round(height * scaleY);

            // 转换为统一格式
            var src32 = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            src32.Freeze();

            int srcStride = width * 4;
            int srcSize = height * srcStride;
            int dstSize = dstH * dstW * 4;

            // 直接分配缓冲区
            byte[] srcPixels = new byte[srcSize];
            byte[] dstPixels = new byte[dstSize];

            // 清零目标缓冲区（重要：确保数据干净）
            Array.Clear(dstPixels, 0, dstSize);

            src32.CopyPixels(srcPixels, srcStride, 0);

            // 使用更保守的并行策略
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
            };

            Parallel.For(0, dstH, parallelOptions, y =>
            {
                ProcessRow(y, dstW, dstH, width, height, srcPixels, dstPixels, srcStride, progress);
            });

            // 创建结果位图
            var bmp = BitmapSource.Create(dstW, dstH, src.DpiX, src.DpiY, PixelFormats.Bgra32, null, dstPixels, dstW * 4);
            bmp.Freeze();
            return bmp;
        }

        private async void HighResWidthNumberSelect_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            highResPreviewWidth = (int)HighResWidthNumberSelect.Value;
            await RefreshHighResPreview();
        }

        private static void ProcessRow(int y, int dstW, int dstH, int width, int height,
            byte[] srcPixels, byte[] dstPixels, int srcStride, Action<double> progress)
        {
            for (int x = 0; x < dstW; x++)
            {
                double srcX0 = x / (double)dstW * width;
                double srcX1 = (x + 1) / (double)dstW * width;
                double srcY0 = y / (double)dstH * height;
                double srcY1 = (y + 1) / (double)dstH * height;

                int ix0 = (int)Math.Floor(srcX0);
                int ix1 = (int)Math.Min(Math.Ceiling(srcX1), width);
                int iy0 = (int)Math.Floor(srcY0);
                int iy1 = (int)Math.Min(Math.Ceiling(srcY1), height);

                long b = 0, g = 0, r = 0, a = 0, cnt = 0;
                for (int sy = iy0; sy < iy1; sy++)
                {
                    for (int sx = ix0; sx < ix1; sx++)
                    {
                        int idx = sy * srcStride + sx * 4;
                        b += srcPixels[idx + 0];
                        g += srcPixels[idx + 1];
                        r += srcPixels[idx + 2];
                        a += srcPixels[idx + 3];
                        cnt++;
                    }
                }

                int didx = y * dstW * 4 + x * 4;
                if (cnt > 0)
                {
                    dstPixels[didx + 0] = (byte)(b / cnt);
                    dstPixels[didx + 1] = (byte)(g / cnt);
                    dstPixels[didx + 2] = (byte)(r / cnt);
                    dstPixels[didx + 3] = (byte)(a / cnt);
                }
            }

            // 定期报告进度
            if (progress != null && (y % 8 == 0 || y == dstH - 1))
            {
                progress((y + 1) / (double)dstH);
            }
        }
        private void OpenedImage_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && IsSupportedImageFile(files[0]))
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OpenedImage_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && IsSupportedImageFile(files[0]))
                {
                    LoadImageForPreview(files[0]);
                }
            }
            e.Handled = true;
        }

        // 判断文件是否为受支持的图片格式
        private bool IsSupportedImageFile(string filePath)
        {
            string[] supportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".svg", ".eps", ".ai", ".pdf" };
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            return supportedExtensions.Contains(ext);
        }
        private async void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            colPicker.CancelPick();
            ((Storyboard)openedImage.GetValue(FadeInStoryboard)).Begin();
            //await Task.Delay(100);

            OpenFileDialog open = new OpenFileDialog();
            open.Filter = $"{Languages.Strings.CS_OpenFilter} (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.svg;*.eps;*.ai;*.pdf)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.svg;*.eps;*.ai;*.pdf";
            if (!(bool)open.ShowDialog()) return;
            openedImagePath = open.FileName;

            string ext = System.IO.Path.GetExtension(openedImagePath).ToLowerInvariant();
            string[] bitmapAllowed = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            string[] vectorAllowed = { ".svg" };
            string[] gsVectorAllowed = { ".eps", ".ai", ".pdf" };

            // 清理所有缓存 - 重要：先清理 ZoomableImage 的资源
            openedImage.Source = null;
            openedImage.SetSKBitmap(null);

            openedImageSrc = null;
            openedImagePixels = null;
            originalImageSrc = null;
            ditheredImagePixels = null;
            chosenPalette = null;
            convert = null;
            genImage.Source = null;
            landscapeColorPreviewSrc = null;
            landscapeColorPreviewPixels = null;
            landscapeGrayPreviewSrc = null;
            landscapeGrayPreviewPixels = null;
            portraitColorPreviewSrc = null;
            portraitColorPreviewPixels = null;
            portraitGrayPreviewSrc = null;
            portraitGrayPreviewPixels = null;
            previewRotation = 0;
            previewFlip = false;
            openedImage.ImageRotation = 0;
            openedImage.ImageFlip = false;

            colPicker.ClearPalette();

            // 强制 GC
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            saveMidi.IsEnabled = false;
            var progress = new Progress<string>(msg => saveMidi.Content = msg);

            BitmapSource src = null;

            // 位图流程
            if (bitmapAllowed.Contains(ext))
            {
                var result = await LoadAndProcessImageAsync(openedImagePath, false, progress);
                src = result.original;
                originalImageSrc = src;
                await UpdateOpenedImageByAngleAndFlip();

                if (originalImageSrc != null)
                {
                    openedImage.Opacity = 0;
                    originalImageWidth = originalImageSrc.PixelWidth;
                    originalImageHeight = originalImageSrc.PixelHeight;
                    var skBitmap = originalImageSrc.ToSKBitmap();
                    src = null;
                    originalImageSrc = null;
                    openedImage.SetSKBitmap(skBitmap);

                    var fadeIn = (Storyboard)openedImage.GetValue(FadeInStoryboard);
                    fadeIn?.Begin();

                    await Dispatcher.BeginInvoke(new Action(() =>
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }

            // SVG流程
            else if (vectorAllowed.Contains(ext))
            {
                int keyWidth = (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1;
                int targetHeight = GetTargetHeight();
                var settings = new SharpVectors.Renderers.Wpf.WpfDrawingSettings();
                var reader = new SharpVectors.Converters.FileSvgReader(settings);
                DrawingGroup drawing = null;

                await Task.Run(() => GenerateSVGThumbnails(openedImagePath, keyWidth, targetHeight, previewRotation, previewFlip, progress));

                await Dispatcher.InvokeAsync(() =>
                {
                    src = landscapeColorPreviewSrc;
                    openedImageSrc = src;
                    openedImageWidth = src.PixelWidth;
                    openedImageHeight = src.PixelHeight;
                    // 新增：赋值原图宽高
                    originalImageWidth = src.PixelWidth;
                    originalImageHeight = src.PixelHeight;
                    ExtractPixels(src, out openedImagePixels);

                    if (src != null)
                    {
                        var skBitmap = src.ToSKBitmap();
                        openedImage.SetSKBitmap(skBitmap);
                    }
                });

                drawing = reader.Read(openedImagePath);
                if (drawing != null)
                {
                    openedImage.SvgDrawing = drawing;
                }
                else
                {
                    MessageBox.Show("SVG加载失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // Ghostscript流程（EPS/AI/PDF）
            else if (gsVectorAllowed.Contains(ext))
            {
                if (!IsGhostscriptAvailable())
                {
                    ShowGhostscriptDownloadDialog();
                    return;
                }

                int keyWidth = (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1;
                int targetHeight = GetTargetHeight();

                try
                {
                    src = await Task.Run(() => RenderGsVectorToBitmapSource(openedImagePath, keyWidth, targetHeight));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"{ext.ToUpper()}加载失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                openedImageSrc = src;
                openedImageWidth = src.PixelWidth;
                openedImageHeight = src.PixelHeight;
                // 新增：赋值原图宽高
                originalImageWidth = src.PixelWidth;
                originalImageHeight = src.PixelHeight;
                ExtractPixels(src, out openedImagePixels);

                try
                {
                    var highResBitmap = await RenderGsVectorHighResPreview(openedImagePath, src, highResPreviewWidth, progress);
                    var skBitmap = highResBitmap.ToSKBitmap();
                    openedImage.SetSKBitmap(skBitmap);
                }
                catch
                {
                    if (src != null)
                    {
                        var skBitmap = src.ToSKBitmap();
                        openedImage.SetSKBitmap(skBitmap);
                    }
                }
            }
            else
            {
                MessageBox.Show("请选择有效的图片或矢量图文件（png, jpg, jpeg, bmp, svg, eps）。", "文件类型不支持", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CustomHeightNumberSelect.Value = GetTargetHeight();

            saveMidi.Content = $"{Languages.Strings.CS_GeneratingPalette} 0%";
            await ReloadAutoPalette();

            // 新增：导入图片后，若已勾选灰度，主动生成灰度缩略图
            if (grayScaleCheckBox.IsChecked == true)
            {
                if (landscapeColorPreviewSrc != null)
                {
                    landscapeGrayPreviewSrc = ToGrayScale(landscapeColorPreviewSrc);
                    ExtractPixels(landscapeGrayPreviewSrc, out landscapeGrayPreviewPixels);
                }
                if (portraitColorPreviewSrc != null)
                {
                    portraitGrayPreviewSrc = ToGrayScale(portraitColorPreviewSrc);
                    ExtractPixels(portraitGrayPreviewSrc, out portraitGrayPreviewPixels);
                }
                // 关键：让 openedImageSrc/Source 用灰度数据
                await UpdateOpenedImageByAngleAndFlip();
                //openedImage.Source = openedImageSrc;
            }
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (originalImageSrc != null || openedImageSrc != null)
            {
                var eitherSrc = originalImageSrc ?? openedImageSrc;
                BatchFileList.Add(new BatchFileItem
                {
                    Index = BatchFileList.Count + 1,
                    Format = System.IO.Path.GetExtension(openedImagePath).TrimStart('.').ToUpperInvariant(),
                    FileName = System.IO.Path.GetFileName(openedImagePath),
                    FrameCount = 1,
                    Resolution = $"{eitherSrc.PixelWidth}x{eitherSrc.PixelHeight}",
                    FullPath = openedImagePath
                });
            }
            else
            {
                MessageBox.Show("图片加载失败，无法添加到批量列表。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
        }
        /// <summary>
        /// 生成 EPS/AI/PDF 文件的 4096 宽度高分辨率预览图
        /// </summary>
        private async Task<BitmapSource> RenderGsVectorHighResPreview(string filePath, BitmapSource lowResSrc, int highResWidth, IProgress<string> progress = null)
        {
            int highResHeight = 4096;
            if (lowResSrc != null)
            {
                double aspect = (double)lowResSrc.PixelHeight / lowResSrc.PixelWidth;
                highResHeight = (int)Math.Round(highResWidth * aspect);
            }
            var highResBitmap = await Task.Run(() => RenderGsVectorToBitmapSource(filePath, highResWidth, highResHeight, progress));
            highResBitmap.Freeze();
            return highResBitmap;
        }
        /// <summary>
        /// 使用 Ghostscript 渲染矢量图到 BitmapSource
        /// </summary>
        /// <returns></returns>
        private async Task RefreshHighResPreview()
        {
            // 仅在已加载矢量图时刷新
            if (string.IsNullOrEmpty(openedImagePath) || openedImageSrc == null)
                return;

            string ext = System.IO.Path.GetExtension(openedImagePath).ToLowerInvariant();
            string[] gsVectorAllowed = { ".eps", ".ai", ".pdf" };
            if (!gsVectorAllowed.Contains(ext))
                return;

            var highResProgress = new Progress<string>(msg => saveMidi.Content = msg);
            try
            {
                var highResBitmap = await RenderGsVectorHighResPreview(openedImagePath, openedImageSrc, highResPreviewWidth, highResProgress);
                openedImage.Source = highResBitmap;
            }
            catch
            {
                // 回退到普通分辨率
                openedImage.Source = openedImageSrc;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        /// <summary>
        /// Ghostscript检测方法
        /// </summary>
        /// <returns>
        /// 如果检测到Ghostscript，返回true；否则返回false
        /// </returns>
        private bool IsGhostscriptAvailable()
        {
            // 只检测常见的64位和32位命令行版本
            string[] candidates = { "gswin64c.exe", "gswin32c.exe" };
            foreach (var exe in candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "-v",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var proc = Process.Start(psi))
                    {
                        if (!proc.WaitForExit(1000)) // 最多等1秒
                            continue;
                        if (proc.ExitCode == 0 || proc.ExitCode == 1)
                            return true;
                    }
                }
                catch
                {
                    // 忽略异常，继续检测下一个
                }
            }
            return false;
        }

        // 在MainWindow类中添加Ghostscript下载提示方法
        private void ShowGhostscriptDownloadDialog()
        {
            var result = MessageBox.Show(
                "未检测到Ghostscript，无法读取EPS/AI/PDF文件。\n请前往 www.ghostscript.com 下载并安装 Ghostscript。\n\n是否现在打开官网下载页面？",
                "缺少Ghostscript",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://www.ghostscript.com/",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
        public async void LoadImageForPreview(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return;

            openedImagePath = imagePath;

            // 清理所有缓存
            openedImageSrc = null;
            openedImagePixels = null;
            originalImageSrc = null;
            ditheredImagePixels = null;
            chosenPalette = null;
            convert = null;
            openedImage.Source = null;
            genImage.Source = null;
            landscapeColorPreviewSrc = null;
            landscapeColorPreviewPixels = null;
            landscapeGrayPreviewSrc = null;
            landscapeGrayPreviewPixels = null;
            portraitColorPreviewSrc = null;
            portraitColorPreviewPixels = null;
            portraitGrayPreviewSrc = null;
            portraitGrayPreviewPixels = null;
            previewRotation = 0;
            previewFlip = false;
            openedImage.ImageRotation = 0;
            openedImage.ImageFlip = false;

            colPicker.ClearPalette();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            saveMidi.IsEnabled = false;
            var progress = new Progress<string>(msg => saveMidi.Content = msg);

            string ext = System.IO.Path.GetExtension(openedImagePath).ToLowerInvariant();
            string[] bitmapAllowed = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            string[] vectorAllowed = { ".svg" };
            string[] gsVectorAllowed = { ".eps", ".ai", ".pdf" };

            BitmapSource src = null;

            if (bitmapAllowed.Contains(ext))
            {
                // 位图流程
                var result = await LoadAndProcessImageAsync(openedImagePath, false, progress);
                src = result.original;
                originalImageSrc = src;
                await UpdateOpenedImageByAngleAndFlip();
                openedImage.Source = originalImageSrc;
            }
            else if (vectorAllowed.Contains(ext))
            {
                // SVG流程
                int keyWidth = (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1;
                int targetHeight = GetTargetHeight();
                await Task.Run(() => GenerateSVGThumbnails(openedImagePath, keyWidth, targetHeight, previewRotation, previewFlip, progress));
                await Dispatcher.InvokeAsync(() =>
                {
                    src = landscapeColorPreviewSrc;
                    openedImageSrc = src;
                    openedImageWidth = src.PixelWidth;
                    openedImageHeight = src.PixelHeight;
                    ExtractPixels(src, out openedImagePixels);
                    openedImage.Source = src;
                });
            }
            else if (gsVectorAllowed.Contains(ext))
            {
                // EPS/AI/PDF流程
                if (!IsGhostscriptAvailable())
                {
                    ShowGhostscriptDownloadDialog();
                    return;
                }
                int keyWidth = (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1;
                int targetHeight = GetTargetHeight();
                try
                {
                    src = await Task.Run(() => RenderGsVectorToBitmapSource(openedImagePath, keyWidth, targetHeight));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"{ext.ToUpper()}加载失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                openedImageSrc = src;
                openedImageWidth = src.PixelWidth;
                openedImageHeight = src.PixelHeight;
                ExtractPixels(src, out openedImagePixels);
                // 渲染高分辨率预览
                var highResProgress = new Progress<string>(msg => saveMidi.Content = msg);
                try
                {
                    var highResBitmap = await RenderGsVectorHighResPreview(openedImagePath, src, highResPreviewWidth, highResProgress);
                    openedImage.Source = highResBitmap;
                }
                catch
                {
                    openedImage.Source = src;
                }
            }
            else
            {
                MessageBox.Show("请选择有效的图片或矢量图文件（png, jpg, jpeg, bmp, svg, eps, ai, pdf）。", "文件类型不支持", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CustomHeightNumberSelect.Value = GetTargetHeight();
            saveMidi.Content = $"{Languages.Strings.CS_GeneratingPalette} 0%";
            await ReloadAutoPalette();
            // 新增：导入图片后，若已勾选灰度，主动生成灰度缩略图
            if (grayScaleCheckBox.IsChecked == true)
            {
                if (landscapeColorPreviewSrc != null)
                {
                    landscapeGrayPreviewSrc = ToGrayScale(landscapeColorPreviewSrc);
                    ExtractPixels(landscapeGrayPreviewSrc, out landscapeGrayPreviewPixels);
                }
                if (portraitColorPreviewSrc != null)
                {
                    portraitGrayPreviewSrc = ToGrayScale(portraitColorPreviewSrc);
                    ExtractPixels(portraitGrayPreviewSrc, out portraitGrayPreviewPixels);
                }
                await UpdateOpenedImageByAngleAndFlip();
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        private async Task<(BitmapSource original, BitmapSource preview, byte[] previewPixels)> LoadAndProcessImageAsync(
    string path, bool previewToGray, IProgress<string> progress = null)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            string[] bitmapAllowed = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            if (!bitmapAllowed.Contains(ext))
                throw new NotSupportedException("仅支持位图格式。");

            progress?.Report($"{Languages.Strings.CS_LoadingFile}");

            BitmapSource src = null;
            await Dispatcher.InvokeAsync(() =>
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = fs;
                    bmp.EndInit();
                    bmp.Freeze();
                    src = bmp;
                }
            });

            progress?.Report($"{Languages.Strings.CS_DecodingCompleted}");

            var thumbnailTask = Task.Run(() => GenerateThumbnailsOptimized(src, progress));
            await thumbnailTask;

            BitmapSource previewSrc;
            byte[] previewPixels;
            if (previewToGray)
            {
                previewSrc = landscapeGrayPreviewSrc;
                previewPixels = landscapeGrayPreviewPixels;
            }
            else
            {
                previewSrc = landscapeColorPreviewSrc;
                previewPixels = landscapeColorPreviewPixels;
            }

            progress?.Report($"{Languages.Strings.CS_ThumbGenCompleted}");
            return (src, previewSrc, previewPixels);
        }

        private void GenerateThumbnailsOptimized(BitmapSource src, IProgress<string> progress)
        {
            // 只生成彩色缩略图
            progress?.Report($"{Languages.Strings.CS_GeneratingLandscape}");
            var landscapeColor = Downsample(src, null, 256, p =>
                progress?.Report($"{Languages.Strings.CS_GeneratingLandscape} {(int)(p * 100)}%"));
            ExtractPixels(landscapeColor, out var landscapeColorPixels);

            progress?.Report($"{Languages.Strings.CS_GeneratingPortrait}");
            var portraitColor = Downsample(src, 256, null, p =>
                progress?.Report($"{Languages.Strings.CS_GeneratingPortrait} {(int)(p * 100)}%"));
            ExtractPixels(portraitColor, out var portraitColorPixels);

            // 只缓存彩色缩略图
            landscapeColorPreviewSrc = landscapeColor;
            landscapeColorPreviewPixels = landscapeColorPixels;
            portraitColorPreviewSrc = portraitColor;
            portraitColorPreviewPixels = portraitColorPixels;

            // 清空灰度缩略图缓存
            landscapeGrayPreviewSrc = null;
            landscapeGrayPreviewPixels = null;
            portraitGrayPreviewSrc = null;
            portraitGrayPreviewPixels = null;

            progress?.Report($"{Languages.Strings.CS_ThumbGenCompleted}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        private async Task ReloadVectorBitmapAsync()
        {
            if (string.IsNullOrEmpty(openedImagePath) || !File.Exists(openedImagePath))
                return;
            string ext = System.IO.Path.GetExtension(openedImagePath).ToLowerInvariant();

            int keyWidth = (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1;
            int targetHeight = GetTargetHeight();
            var progress = new Progress<string>(msg => saveMidi.Content = msg);

            BitmapSource src = null;

            if (ext == ".svg")
            {
                // SVG 走原有流程
                await Task.Run(() =>
                {
                    GenerateSVGThumbnails(openedImagePath, keyWidth, targetHeight, previewRotation, previewFlip, progress);
                });

                src = landscapeColorPreviewSrc;
            }
            else if (ext == ".eps" || ext == ".ai" || ext == ".pdf")
            {
                // EPS/AI/PDF 走 Ghostscript 渲染
                if (!IsGhostscriptAvailable())
                {
                    ShowGhostscriptDownloadDialog();
                    return;
                }
                src = await Task.Run(() => RenderGsVectorToBitmapSource(openedImagePath, keyWidth, targetHeight));
                // 旋转/镜像
                int rot = previewRotation;
                if (previewFlip)
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
                if (previewFlip && src != null)
                    src = await FlipHorizontal(src);
            }
            else
            {
                // 其它类型不处理
                return;
            }

            if (src == null)
                return;

            originalImageSrc = src;
            openedImageSrc = src;
            openedImageWidth = src.PixelWidth;
            openedImageHeight = src.PixelHeight;
            ExtractPixels(src, out openedImagePixels);

            CustomHeightNumberSelect.Value = GetTargetHeight();
            ReloadPreview();
        }
        private void GenerateSVGThumbnails(
    string vectorPath, int targetWidth, int targetHeight, int angle = 0, bool flip = false, IProgress<string> progress = null)
        {
            progress?.Report($"{Languages.Strings.CS_SVGAnalyzing}");

            var svgDocument = Svg.SvgDocument.Open(vectorPath);
            if (svgDocument == null)
                throw new Exception("SVG文件解析失败");

            progress?.Report($"{Languages.Strings.CS_SVGRendering}");
            // 获取SVG的ViewBox
            var viewBox = svgDocument.ViewBox;
            float viewBoxX = viewBox.MinX;
            float viewBoxY = viewBox.MinY;
            float viewBoxWidth = viewBox.Width > 0 ? viewBox.Width : 1f;
            float viewBoxHeight = viewBox.Height > 0 ? viewBox.Height : 1f;


            using (var bitmap = new System.Drawing.Bitmap(targetWidth, targetHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                // 关键设置：完全禁用抗锯齿和平滑处理
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;           // 这个是最重要的设置项
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixel;

                // 先平移到ViewBox原点，再缩放
                graphics.TranslateTransform(-viewBoxX, -viewBoxY);
                graphics.ScaleTransform((float)targetWidth / viewBoxWidth, (float)targetHeight / viewBoxHeight);


                // 应用旋转和镜像（注意要在缩放后再平移/旋转）
                if (angle != 0 || flip)
                {
                    graphics.TranslateTransform(viewBoxWidth / 2f, viewBoxHeight / 2f);
                    if (angle != 0)
                        graphics.RotateTransform(angle);
                    if (flip)
                        graphics.ScaleTransform(-1, 1);
                    graphics.TranslateTransform(-viewBoxWidth / 2f, -viewBoxHeight / 2f);
                }

                // 渲染SVG
                svgDocument.Draw(graphics);

                // 转换为WPF BitmapSource
                progress?.Report($"{Languages.Strings.CS_SVGConverting}");
                var bitmapSource = ConvertBitmapToBitmapSource(bitmap);
                bitmapSource.Freeze();

                landscapeColorPreviewSrc = bitmapSource;
                ExtractPixels(bitmapSource, out landscapeColorPreviewPixels);
            }

            progress?.Report($"{Languages.Strings.CS_SVGCompleted}");
        }

        // 辅助方法：System.Drawing.Bitmap转WPF BitmapSource
        private BitmapSource ConvertBitmapToBitmapSource(System.Drawing.Bitmap bitmap)
        {
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bitmapData = bitmap.LockBits(
                rect,
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                int stride = bitmapData.Stride;
                int bytes = stride * bitmap.Height;
                byte[] pixelData = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, pixelData, 0, bytes);

                // 应用与SVG相同的alpha通道处理逻辑
                for (int i = 0; i < pixelData.Length; i += 4)
                {
                    byte a = pixelData[i + 3];
                    // 类似SVG处理：要么完全透明，要么完全不透明
                    if (a >= 1)
                        pixelData[i + 3] = 255;
                    else
                        pixelData[i + 3] = 0;
                }

                var bitmapSource = BitmapSource.Create(
                    bitmap.Width, bitmap.Height,
                    bitmap.HorizontalResolution, bitmap.VerticalResolution,
                    PixelFormats.Bgra32, // BGRA顺序
                    null,
                    pixelData, stride);

                bitmapSource.Freeze();
                return bitmapSource;
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }
        private BitmapSource RenderGsVectorToBitmapSource(string filePath, int targetWidth, int targetHeight, IProgress<string> progress = null)
        {
            progress?.Report($"{Languages.Strings.CS_GSBoundingBox}");
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            int rawW = targetWidth, rawH = targetHeight;
            if (ext == ".eps" || ext == ".ai")
            {
                // 尝试读取BoundingBox
                double bboxX = 0, bboxY = 0, bboxW = 0, bboxH = 0;
                using (var reader = new StreamReader(filePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("%%HiResBoundingBox:"))
                        {
                            var parts = line.Substring(19).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 4 &&
                                double.TryParse(parts[0], out bboxX) &&
                                double.TryParse(parts[1], out bboxY) &&
                                double.TryParse(parts[2], out double x2) &&
                                double.TryParse(parts[3], out double y2))
                            {
                                bboxW = x2 - bboxX;
                                bboxH = y2 - bboxY;
                                break;
                            }
                        }
                        else if (line.StartsWith("%%BoundingBox:"))
                        {
                            var parts = line.Substring(14).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 4 &&
                                double.TryParse(parts[0], out bboxX) &&
                                double.TryParse(parts[1], out bboxY) &&
                                double.TryParse(parts[2], out double x2) &&
                                double.TryParse(parts[3], out double y2))
                            {
                                bboxW = x2 - bboxX;
                                bboxH = y2 - bboxY;
                            }
                        }
                    }
                }
                if (bboxW > 0 && bboxH > 0)
                {
                    rawW = (int)Math.Round(bboxW);
                    rawH = (int)Math.Round(bboxH);
                }
            }
            progress?.Report($"{Languages.Strings.CS_GSRendering}");
            // 1. 限制最小渲染尺寸
            int minRenderSize = 16; // Ghostscript推荐10以上，16更保险
            int renderWidth = Math.Max(targetWidth, minRenderSize);
            int renderHeight = Math.Max(targetHeight, minRenderSize);

            string tempPng = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".png");

            // 2. 计算DPI，避免极端值
            double xDpi = (double)renderWidth / Math.Max(rawW, 1) * 72.0;
            double yDpi = (double)renderHeight / Math.Max(rawH, 1) * 72.0;
            xDpi = Math.Max(10, Math.Min(xDpi, 1200));
            yDpi = Math.Max(10, Math.Min(yDpi, 1200));

            string gsArgs = $"-dSAFER -dBATCH -dNOPAUSE -sDEVICE=pngalpha " +
                $"-dDEVICEWIDTHPOINTS={rawW} -dDEVICEHEIGHTPOINTS={rawH} " +
                $"-dFIXEDMEDIA -dPDFFitPage " +
                $"-r{xDpi.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}x{yDpi.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} " +
                $"-dGraphicsAlphaBits=1 " +
                $"-dTextAlphaBits=1 " +
                $"-dDownScaleFactor=1 " +
                $"-dColorConversionStrategy=/LeaveColorUnchanged " +
                $"-dAutoFilterColorImages=false " +
                $"-dAutoFilterGrayImages=false " +
                $"-dColorImageFilter=/FlateEncode " +
                $"-dGrayImageFilter=/FlateEncode " +
                $"-dMonoImageFilter=/CCITTFaxEncode " +
                $"-dOptimize=false " +
                $"-dUseCropBox=false " +
                $"-dEPSCrop=true " +
                $"-g{renderWidth}x{renderHeight} " +
                $"-sOutputFile=\"{tempPng}\" \"{filePath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "gswin64c.exe",
                Arguments = gsArgs,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var proc = Process.Start(psi))
            {
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    string error = proc.StandardError.ReadToEnd();
                    throw new Exception("Ghostscript渲染失败: " + error);
                }
            }
            progress?.Report($"{Languages.Strings.CS_GSReading}");
            BitmapSource bmp;
            using (var fs = new FileStream(tempPng, FileMode.Open, FileAccess.Read))
            {
                using (var bitmap = new System.Drawing.Bitmap(fs))
                {
                    bmp = ConvertBitmapToBitmapSource(bitmap);
                }
            }
            try { File.Delete(tempPng); } catch { }

            bmp.Freeze();

            progress?.Report($"{Languages.Strings.CS_GSScaling}");
            // 3. 如果目标尺寸比渲染尺寸小，WPF再缩放
            if (targetWidth < renderWidth || targetHeight < renderHeight)
            {
                var scale = new ScaleTransform(
                    targetWidth / (double)renderWidth,
                    targetHeight / (double)renderHeight);
                var tb = new TransformedBitmap(bmp, scale);
                tb.Freeze();
                return tb;
            }
            return bmp;
        }

        private void ExtractPixels(BitmapSource source, out byte[] pixels)
        {
            int w = source.PixelWidth;
            int h = source.PixelHeight;
            int stride = source.Format.BitsPerPixel / 8 * w;
            pixels = new byte[h * stride];
            source.CopyPixels(pixels, stride, 0);
        }

        private void MinimiseButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Minimized;
                return;
            }
            double start = windowContent.ActualHeight;
            double startpos = Top;
            windowContent.Height = start;
            for (double i = 1; i > 0; i -= 0.08)
            {
                double smooth;
                double strength = 10;
                if (i < 0.5f)
                {
                    smooth = Math.Pow(i * 2, strength) / 2;
                }
                else
                {
                    smooth = 1 - Math.Pow((1 - i) * 2, strength) / 2;
                }
                Height = start * smooth;
                Top = startpos + start * (1 - smooth);
                Thread.Sleep(1000 / 60);
            }
            WindowState = WindowState.Minimized;
            windowContent.Height = double.NaN;
            Height = start;
            Top = startpos;
        }

        private void ColPicker_PickStart()
        {
            colHex.Text = "";
            Cursor = Cursors.Cross;
            ((Storyboard)colPickerOptions.GetValue(FadeInStoryboard)).Begin();
            colorPick = true;
        }

        private void ColPicker_PickStop()
        {
            Cursor = Cursors.Arrow;
            ((Storyboard)colPickerOptions.GetValue(FadeOutStoryboard)).Begin();
            colorPick = false;
        }

        private async void TrackCount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            await ReloadAutoPalette();
        }

        private int kmeansMaxIterations = 100; // KMeansParamPanel -> KMeansMaxIterationsBox.Text="100"
        private double kmeansThreshold = 1.0;     // KMeansParamPanel -> KMeansThresholdBox.Text="1.0"
        private int octreeMaxLevel = 8;        // OctreeParamPanel -> OctreeMaxLevelBox.Text="8"
        private int octreeMaxSamples = 20000;
        private int kmeansPlusPlusMaxSamples = 20000;
        private int kmeansPlusPlusSeed = 0;
        private int varianceSplitMaxSamples = 20000;
        private int pcaPowerIterations = 20, pcaMaxSamples = 20000;
        private int weightedMaxMinIters = 3, weightedMaxMinMaxSamples = 20000;
        private int nativeKMeansIterations = 10;
        private double nativeKMeansRate = 0.3;
        private double meanShiftBandwidth = 32;
        private int meanShiftMaxIter = 7, meanShiftMaxSamples = 10000;
        private double? dbscanEpsilon = null;
        private int dbscanMinPts = 4, dbscanMaxSamples = 2000;
        private int gmmMaxIter = 30, gmmMaxSamples = 2000;
        private double gmmTol = 1.0;
        private int hierarchicalMaxSamples = 2000;
        private HierarchicalLinkage hierarchicalLinkage = HierarchicalLinkage.Single;
        private HierarchicalDistanceType hierarchicalDistanceType = HierarchicalDistanceType.Euclidean;
        private int spectralMaxSamples = 2000;
        private double spectralSigma = 32.0;
        private int spectralKMeansIters = 10;
        private int labKMeansMaxIterations = 100;
        private double labKMeansThreshold = 1.0;
        private double floydDitherStrength = 1.0;
        private bool floydSerpentine = true;
        private double orderedDitherStrength = 1.0;
        private BayerMatrixSize orderedDitherMatrixSize = BayerMatrixSize.Size4x4;
        private Clusterisation.PaletteClusterMethod orderedDitherBaseMethod = Clusterisation.PaletteClusterMethod.OnlyWpf;
        private double? opticsEpsilon = null;
        private int opticsMinPts = 4, opticsMaxSamples = 2000;
        private int fixedBitDepth = 8;
        private bool useGrayFixedPalette = false;


        private async Task ReloadAutoPalette()
        {
            if (openedImageSrc == null) return;
            midiExported = false;
            // 1. 取消前一个任务，防止并发
            _paletteCts?.Cancel();
            _paletteCts = new CancellationTokenSource();
            var token = _paletteCts.Token;

            // ====== 在这里清理抖动像素 ======
            ditheredImagePixels = null;

            int tracks = (int)trackCount.Value;
            int colorCount = tracks;//因为我改了！原来是*16

            // 获取当前聚类方法
            var selectedItem = ClusterMethodComboBox.SelectedItem as ComboBoxItem;
            var method = Clusterisation.PaletteClusterMethod.OnlyWpf;
            var floydBaseMethod = Clusterisation.PaletteClusterMethod.OnlyWpf;

            if (selectedItem != null)
            {
                var tag = selectedItem.Tag as string;
                switch (tag)
                {
                    case "OnlyWpf":
                        method = Clusterisation.PaletteClusterMethod.OnlyWpf;
                        break;
                    case "OnlyKMeansPlusPlus":
                        method = Clusterisation.PaletteClusterMethod.OnlyKMeansPlusPlus;
                        break;
                    case "KMeans":
                        method = Clusterisation.PaletteClusterMethod.KMeans;
                        break;
                    case "KMeansPlusPlus":
                        method = Clusterisation.PaletteClusterMethod.KMeansPlusPlus;
                        break;
                    case "Popularity":
                        method = Clusterisation.PaletteClusterMethod.Popularity;
                        break;
                    case "Octree":
                        method = Clusterisation.PaletteClusterMethod.Octree;
                        break;
                    case "VarianceSplit":
                        method = Clusterisation.PaletteClusterMethod.VarianceSplit;
                        break;
                    case "Pca":
                        method = Clusterisation.PaletteClusterMethod.Pca;
                        break;
                    case "MaxMin":
                        method = Clusterisation.PaletteClusterMethod.MaxMin;
                        break;
                    case "NativeKMeans":
                        method = Clusterisation.PaletteClusterMethod.NativeKMeans;
                        break;
                    case "MeanShift":
                        method = Clusterisation.PaletteClusterMethod.MeanShift;
                        break;
                    case "DBSCAN":
                        method = Clusterisation.PaletteClusterMethod.DBSCAN;
                        break;
                    case "GMM":
                        method = Clusterisation.PaletteClusterMethod.GMM;
                        break;
                    case "Hierarchical":
                        method = Clusterisation.PaletteClusterMethod.Hierarchical;
                        break;
                    case "Spectral":
                        method = Clusterisation.PaletteClusterMethod.Spectral;
                        break;
                    case "LabKMeans":
                        method = Clusterisation.PaletteClusterMethod.LabKMeans;
                        break;
                    case "FloydSteinbergDither":
                        method = Clusterisation.PaletteClusterMethod.FloydSteinbergDither;
                        break;
                    case "OrderedDither":
                        method = Clusterisation.PaletteClusterMethod.OrderedDither;
                        break;
                    case "OPTICS":
                        method = Clusterisation.PaletteClusterMethod.OPTICS;
                        break;
                    case "FixedBitPalette":
                        method = Clusterisation.PaletteClusterMethod.FixedBitPalette;
                        break;

                }
                // 同步参数（防止未输入时未更新）
                if (KMeansMaxIterationsBox != null)
                    kmeansMaxIterations = (int)KMeansMaxIterationsBox.Value;
                if (KMeansThresholdBox != null)
                    kmeansThreshold = (double)KMeansThresholdBox.Value;
                if (OctreeMaxLevelBox != null)
                    octreeMaxLevel = (int)OctreeMaxLevelBox.Value;
                if (OctreeMaxSamplesBox != null)
                    octreeMaxSamples = (int)OctreeMaxSamplesBox.Value;
                if (VarianceSplitMaxSamplesBox != null)
                    varianceSplitMaxSamples = (int)VarianceSplitMaxSamplesBox.Value;
                if (PcaPowerIterationsBox != null)
                    pcaPowerIterations = (int)PcaPowerIterationsBox.Value;
                if (PcaMaxSamplesBox != null)
                    pcaMaxSamples = (int)PcaMaxSamplesBox.Value;
                if (WeightedMaxMinItersBox != null)
                    weightedMaxMinIters = (int)WeightedMaxMinItersBox.Value;
                if (WeightedMaxMinMaxSamplesBox != null)
                    weightedMaxMinMaxSamples = (int)WeightedMaxMinMaxSamplesBox.Value;
                if (NativeKMeansIterationsBox != null)
                    nativeKMeansIterations = (int)NativeKMeansIterationsBox.Value;
                if (NativeKMeansRateBox != null)
                    nativeKMeansRate = (double)NativeKMeansRateBox.Value;
                if (MeanShiftBandwidthBox != null)
                    meanShiftBandwidth = (double)MeanShiftBandwidthBox.Value;
                if (MeanShiftMaxIterBox != null)
                    meanShiftMaxIter = (int)MeanShiftMaxIterBox.Value;
                if (MeanShiftMaxSamplesBox != null)
                    meanShiftMaxSamples = (int)MeanShiftMaxSamplesBox.Value;
                if (DBSCANEpsilonBox != null)
                    dbscanEpsilon = (double)DBSCANEpsilonBox.Value;
                if (DBSCANMinPtsBox != null)
                    dbscanMinPts = (int)DBSCANMinPtsBox.Value;
                if (DBSCANMaxSamplesBox != null)
                    dbscanMaxSamples = (int)DBSCANMaxSamplesBox.Value;
                if (GMMMaxIterBox != null)
                    gmmMaxIter = (int)GMMMaxIterBox.Value;
                if (GMMTolBox != null)
                    gmmTol = (double)GMMTolBox.Value;
                if (GMMMaxSamplesBox != null)
                    gmmMaxSamples = (int)GMMMaxSamplesBox.Value;
                if (KMeansPlusPlusMaxSamplesBox != null)
                    kmeansPlusPlusMaxSamples = (int)KMeansPlusPlusMaxSamplesBox.Value;
                if (KMeansPlusPlusSeedBox != null)
                    kmeansPlusPlusSeed = (int)KMeansPlusPlusSeedBox.Value;
                if (HierarchicalMaxSamplesBox != null)
                    hierarchicalMaxSamples = (int)HierarchicalMaxSamplesBox.Value;
                if (HierarchicalLinkageBox != null)
                {
                    var linkageTag = ((ComboBoxItem)HierarchicalLinkageBox.SelectedItem)?.Tag as string;
                    switch (linkageTag)
                    {
                        case "Single": hierarchicalLinkage = HierarchicalLinkage.Single; break;
                        case "Complete": hierarchicalLinkage = HierarchicalLinkage.Complete; break;
                        case "Average": hierarchicalLinkage = HierarchicalLinkage.Average; break;
                    }
                }
                if (HierarchicalDistanceTypeBox != null)
                {
                    var distTag = ((ComboBoxItem)HierarchicalDistanceTypeBox.SelectedItem)?.Tag as string;
                    switch (distTag)
                    {
                        case "Euclidean": hierarchicalDistanceType = HierarchicalDistanceType.Euclidean; break;
                        case "Manhattan": hierarchicalDistanceType = HierarchicalDistanceType.Manhattan; break;
                    }
                }
                if (SpectralMaxSamplesBox != null)
                    spectralMaxSamples = (int)SpectralMaxSamplesBox.Value;
                if (SpectralSigmaBox != null)
                    spectralSigma = (double)SpectralSigmaBox.Value;
                if (SpectralKMeansItersBox != null)
                    spectralKMeansIters = (int)SpectralKMeansItersBox.Value;
                if (LabKMeansMaxIterationsBox != null)
                    labKMeansMaxIterations = (int)LabKMeansMaxIterationsBox.Value;
                if (LabKMeansThresholdBox != null)
                    labKMeansThreshold = (double)LabKMeansThresholdBox.Value;

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
                if (FloydDitherStrengthBox != null)
                    floydDitherStrength = (double)FloydDitherStrengthBox.Value;
                if (FloydSerpentineBox != null)
                    floydSerpentine = FloydSerpentineBox.IsChecked == true;
                if (OrderedDitherStrengthBox != null)
                    orderedDitherStrength = (double)OrderedDitherStrengthBox.Value;
                if (OrderedDitherMatrixSizeBox != null)
                {
                    var matrixTag = ((ComboBoxItem)OrderedDitherMatrixSizeBox.SelectedItem)?.Tag as string;
                    switch (matrixTag)
                    {
                        case "Size2x2": orderedDitherMatrixSize = ImageToMidi.BayerMatrixSize.Size2x2; break;
                        case "Size4x4": orderedDitherMatrixSize = ImageToMidi.BayerMatrixSize.Size4x4; break;
                        case "Size8x8": orderedDitherMatrixSize = ImageToMidi.BayerMatrixSize.Size8x8; break;
                    }
                }
                if (OrderedDitherBaseMethodBox != null)
                {
                    var baseTag = ((ComboBoxItem)OrderedDitherBaseMethodBox.SelectedItem)?.Tag as string;
                    switch (baseTag)
                    {
                        case "OnlyWpf": orderedDitherBaseMethod = Clusterisation.PaletteClusterMethod.OnlyWpf; break;
                        case "OnlyKMeansPlusPlus": orderedDitherBaseMethod = Clusterisation.PaletteClusterMethod.OnlyKMeansPlusPlus; break;
                        case "KMeans": orderedDitherBaseMethod = Clusterisation.PaletteClusterMethod.KMeans; break;
                        case "Pca": orderedDitherBaseMethod = Clusterisation.PaletteClusterMethod.Pca; break;
                        case "DBSCAN": orderedDitherBaseMethod = Clusterisation.PaletteClusterMethod.DBSCAN; break;
                    }
                }
                if (OPTICSEpsilonBox != null)
                    opticsEpsilon = (double)OPTICSEpsilonBox.Value;
                if (OPTICSMinPtsBox != null)
                    opticsMinPts = (int)OPTICSMinPtsBox.Value;
                if (OPTICSMaxSamplesBox != null)
                    opticsMaxSamples = (int)OPTICSMaxSamplesBox.Value;
                if (FixedBitDepthBox != null)
                    fixedBitDepth = (int)FixedBitDepthBox.Value;
                if (UseGrayFixedPaletteBox != null)
                    useGrayFixedPalette = UseGrayFixedPaletteBox.IsChecked == true;
            }

            double lastChange = 0;
            BitmapPalette palette = null;
            try
            {
                saveMidi.IsEnabled = false;
                saveMidi.Content = $"{Languages.Strings.CS_GeneratingPalette}";
                await Task.Run(() =>
                {
                    // 检查是否已取消
                    if (token.IsCancellationRequested) return;
                    // ...参数同步后
                    Debug.WriteLine($"[UI] OrderedDitherStrength = {orderedDitherStrength}, OrderedDitherMatrixSize = {orderedDitherMatrixSize}");

                    var options = new ClusteriseOptions
                    {
                        ColorCount = colorCount,
                        Method = method,
                        Src = openedImageSrc,
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
                        FloydBaseMethod =
                            (method == Clusterisation.PaletteClusterMethod.FloydSteinbergDither) ? floydBaseMethod :
                            (method == Clusterisation.PaletteClusterMethod.OrderedDither) ? orderedDitherBaseMethod :
                            /* 未来有新算法可继续加分支 */
                            floydBaseMethod, // 默认

                        // 下面可加自定义抖动参数
                        FloydDitherStrength = floydDitherStrength,
                        FloydSerpentine = floydSerpentine,
                        OrderedDitherStrength = orderedDitherStrength,
                        OrderedDitherMatrixSize = orderedDitherMatrixSize,
                        //FloydBaseMethod = orderedDitherBaseMethod, // 复用字段
                        OpticsEpsilon = opticsEpsilon,
                        OpticsMinPts = opticsMinPts,
                        OpticsMaxSamples = opticsMaxSamples,
                        BitDepth = fixedBitDepth,
                        UseGrayFixedPalette = useGrayFixedPalette,
                    };
                    double lastReport = 0;
                    DateTime lastReportTime = DateTime.Now;
                    byte[] ditheredPixels = null;
                    palette = Clusterisation.ClusteriseByMethod(
                        openedImagePixels,
                        options,
                        out lastChange,
                        out ditheredPixels,
                        progress =>
                        {
                            if (progress - lastReport >= 0.01 || (DateTime.Now - lastReportTime).TotalMilliseconds > 100 || progress == 1.0)
                            {
                                lastReport = progress;
                                lastReportTime = DateTime.Now;
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    int percent = (int)(progress * 100);
                                    saveMidi.Content = $"{Languages.Strings.CS_GeneratingPalette} {percent}%";
                                }));
                            }
                        }
                    );
                    if ((method == Clusterisation.PaletteClusterMethod.FloydSteinbergDither ||
        method == Clusterisation.PaletteClusterMethod.OrderedDither) && ditheredPixels != null)
                        ditheredImagePixels = ditheredPixels; // 直接用，不再Clone
                                                              // 否则保持 openedImagePixels 不变

                }, token);

                // 再次检查取消
                if (token.IsCancellationRequested) return;

                chosenPalette = palette;
                //ReloadPalettePreview();
                //我改过让调色板显示音符数的功能，为了避免多余刷新，所以不再调用ReloadPreview()
                //这个补全我挪到了ReloadPreview()中
                ReloadPreview();
            }
            catch (OperationCanceledException)
            {
                // 被取消，安全退出
            }
            catch (OutOfMemoryException)
            {
                MessageBox.Show("内存不足，建议重启程序或减少图片尺寸/颜色数。", "内存不足", MessageBoxButton.OK, MessageBoxImage.Error);
                midiExported = false;
                lastExportedMidiPath = null;
                UpdateSaveMidiButton();
            }
            catch (Exception ex)
            {
                MessageBox.Show("调色板生成失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 主动GC，帮助释放内存
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private void UpdateAlgorithmParamPanel()
        {
            // 所有参数面板集合
            var panels = new[]
            {
                KMeansParamPanel,
                KMeansPlusPlusParamPanel,
                OctreeParamPanel,
                EmptyParamPanel,
                VarianceSplitParamPanel,
                PcaParamPanel,
                WeightedMaxMinParamPanel,
                NativeKMeansParamPanel,
                MeanShiftParamPanel,
                DBSCANParamPanel,
                GMMParamPanel,
                HierarchicalParamPanel,
                SpectralParamPanel,
                LabKMeansParamPanel,
                FloydSteinbergParamPanel,
                OrderedDitherParamPanel,
                OPTICSParamPanel,
                FixedBitPaletteParamPanel,
            };

            // 先全部隐藏（前提是控件已初始化）
            foreach (var panel in panels)
            {
                if (panel != null)
                    panel.Visibility = Visibility.Collapsed;
            }

            var selectedItem = ClusterMethodComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem == null)
                return;

            string tag = selectedItem.Tag as string;

            // 根据tag显示对应面板
            switch (tag)
            {
                case "OnlyKMeansPlusPlus":
                    if (KMeansPlusPlusParamPanel != null)
                        KMeansPlusPlusParamPanel.Visibility = Visibility.Visible;
                    break;
                case "KMeans":
                case "KMeansPlusPlus":
                    if (KMeansParamPanel != null)
                        KMeansParamPanel.Visibility = Visibility.Visible;
                    break;
                case "Octree":
                    if (OctreeParamPanel != null)
                        OctreeParamPanel.Visibility = Visibility.Visible;
                    break;
                case "VarianceSplit":
                    if (VarianceSplitParamPanel != null)
                        VarianceSplitParamPanel.Visibility = Visibility.Visible;
                    break;
                case "Pca":
                    if (PcaParamPanel != null)
                        PcaParamPanel.Visibility = Visibility.Visible;
                    break;
                case "MaxMin":
                    if (WeightedMaxMinParamPanel != null)
                        WeightedMaxMinParamPanel.Visibility = Visibility.Visible;
                    break;
                case "NativeKMeans":
                    if (NativeKMeansParamPanel != null)
                        NativeKMeansParamPanel.Visibility = Visibility.Visible;
                    break;
                case "MeanShift":
                    if (MeanShiftParamPanel != null)
                        MeanShiftParamPanel.Visibility = Visibility.Visible;
                    break;
                case "DBSCAN":
                    if (DBSCANParamPanel != null)
                        DBSCANParamPanel.Visibility = Visibility.Visible;
                    break;
                case "GMM":
                    if (GMMParamPanel != null)
                        GMMParamPanel.Visibility = Visibility.Visible;
                    break;
                case "Hierarchical":
                    if (HierarchicalParamPanel != null)
                        HierarchicalParamPanel.Visibility = Visibility.Visible;
                    break;
                case "Spectral":
                    if (SpectralParamPanel != null)
                        SpectralParamPanel.Visibility = Visibility.Visible;
                    break;
                case "LabKMeans":
                    if (LabKMeansParamPanel != null)
                        LabKMeansParamPanel.Visibility = Visibility.Visible;
                    break;
                case "FloydSteinbergDither":
                    if (FloydSteinbergParamPanel != null)
                        FloydSteinbergParamPanel.Visibility = Visibility.Visible;
                    break;
                case "OrderedDither":
                    if (OrderedDitherParamPanel != null)
                        OrderedDitherParamPanel.Visibility = Visibility.Visible;
                    break;
                case "OPTICS":
                    if (OPTICSParamPanel != null)
                        OPTICSParamPanel.Visibility = Visibility.Visible;
                    break;
                case "FixedBitPalette":
                    if (FixedBitPaletteParamPanel != null)
                        FixedBitPaletteParamPanel.Visibility = Visibility.Visible;
                    break;
                case "OnlyWpf":
                case "Popularity":
                default:
                    if (EmptyParamPanel != null)
                        EmptyParamPanel.Visibility = Visibility.Visible;
                    break;
            }
        }

        void ReloadPalettePreview()
        {
            if (convert != null)
                colPicker.GetNoteCountForColor = convert.GetNoteCountForColor;
            else
                colPicker.GetNoteCountForColor = null;
            if (chosenPalette == null) return;
            lastPaletteColorCount = chosenPalette.Colors.Count;
            autoPaletteBox.Children.Clear();
            int tracks = (chosenPalette.Colors.Count + 15) / 16;
            for (int i = 0; i < tracks; i++)
            {
                var dock = new Grid();
                for (int j = 0; j < 16; j++)
                    dock.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                autoPaletteBox.Children.Add(dock);
                DockPanel.SetDock(dock, Dock.Top);
                for (int j = 0; j < 16; j++)
                {
                    int colorIndex = i * 16 + j;
                    if (colorIndex < chosenPalette.Colors.Count)
                    {
                        var color = chosenPalette.Colors[colorIndex];
                        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                        int noteCount = 0;
                        if (convert != null)
                            noteCount = convert.GetNoteCountForColor(colorIndex);

                        var rect = new Rectangle()
                        {
                            Width = 40,
                            Height = 40,
                            Fill = new SolidColorBrush(color)
                        };

                        // 设置ToolTip
                        var tooltip = new ToolTip
                        {
                            Content = $"{Languages.Strings.CS_TrackHex} {hex}\n{Languages.Strings.CS_TrackNoteCount} {noteCount}"
                        };
                        rect.ToolTip = tooltip;

                        var box = new Viewbox()
                        {
                            Stretch = Stretch.Uniform,
                            Child = rect
                        };
                        Grid.SetColumn(box, j);
                        dock.Children.Add(box);
                    }
                }
            }
        }

        public static bool IsWhiteKey(int midiKey)
        {
            int n = midiKey % 12;
            return n == 0 || n == 2 || n == 4 || n == 5 || n == 7 || n == 9 || n == 11;
        }
        public List<int> GetKeyList()
        {
            int start = (int)firstKeyNumber.Value;
            int end = (int)lastKeyNumber.Value;
            switch (whiteKeyMode)
            {
                case WhiteKeyMode.WhiteKeysClipped:
                case WhiteKeyMode.BlackKeysClipped:
                    // 裁剪模式：始终返回全键列表
                    return Enumerable.Range(start, end - start + 1).ToList();
                case WhiteKeyMode.WhiteKeysFilled:
                    return Enumerable.Range(start, end - start + 1).Where(IsWhiteKey).ToList();
                case WhiteKeyMode.BlackKeysFilled:
                    return Enumerable.Range(start, end - start + 1).Where(i => !IsWhiteKey(i)).ToList();
                default:
                    return Enumerable.Range(start, end - start + 1).ToList();
            }
        }
        private async void ReloadPreview()
        {
            if (!IsInitialized || openedImagePixels == null || openedImageSrc == null || colPicker == null)
                return;
            if (noteLengthMode != NoteLengthMode.Unlimited && (int)noteSplitLength.Value <= 0)
            {
                MessageBox.Show("分割长度必须为正整数！", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 等待当前转换完成（最多等待3秒）
            if (convert != null)
            {
                await convert.WaitForCompletionAsync(3000);
                convert.Cancel();
            }

            midiExported = false;

            var selectedItem = ClusterMethodComboBox.SelectedItem as ComboBoxItem;
            string methodTag = selectedItem?.Tag as string ?? "";

            byte[] previewPixels = ((methodTag == "FloydSteinbergDither" || methodTag == "OrderedDither") && ditheredImagePixels != null)
                ? ditheredImagePixels
                : openedImagePixels;

            if (previewPixels == null) return;

            BitmapPalette palette = null;
            PanelType panelToUse = currentPanel;
            if (currentPanel == PanelType.Settings && lastNonSettingsPanelType.HasValue)
                panelToUse = lastNonSettingsPanelType.Value;

            if (panelToUse == PanelType.Manual)
                palette = colPicker.GetPalette();
            else if (panelToUse == PanelType.Auto)
                palette = chosenPalette;
            else
                return;

            if (palette == null || palette.Colors == null || palette.Colors.Count == 0)
                return;

            int targetHeight = GetTargetHeight();
            var keyList = GetKeyList();
            bool whiteKeyFixed = (whiteKeyMode == WhiteKeyMode.WhiteKeysFixed);
            bool blackKeyFixed = (whiteKeyMode == WhiteKeyMode.BlackKeysFixed);
            bool whiteKeyClipped = (whiteKeyMode == WhiteKeyMode.WhiteKeysClipped);
            bool blackKeyClipped = (whiteKeyMode == WhiteKeyMode.BlackKeysClipped);

            if (whiteKeyMode == WhiteKeyMode.WhiteKeysFilled || whiteKeyMode == WhiteKeyMode.BlackKeysFilled)
                whiteKeyFixed = blackKeyFixed = whiteKeyClipped = blackKeyClipped = false;

            int effectiveWidth = GetEffectiveKeyWidth();
            bool useNoteLengthChecked = noteLengthMode != NoteLengthMode.Unlimited;
            int maxNoteLength = (int)noteSplitLength.Value;
            bool measureFromStart = noteLengthMode == NoteLengthMode.SplitToGrid;

            convert = new ConversionProcess(
                palette,
                previewPixels,
                openedImageWidth * 4,
                (int)firstKeyNumber.Value,
                (int)lastKeyNumber.Value + 1,
                measureFromStart,
                useNoteLengthChecked ? maxNoteLength : 0,
                targetHeight,
                currentResizeAlgorithm,
                keyList,
                whiteKeyClipped,
                blackKeyClipped,
                whiteKeyFixed,
                blackKeyFixed,
                currentColorIdMethod
            );
            convert.EffectiveWidth = effectiveWidth;

            if (!(bool)genColorEventsCheck.IsChecked)
            {
                convert.RandomColors = true;
                convert.RandomColorSeed = (int)randomColorSeed.Value;
            }

            saveMidi.IsEnabled = false;
            saveMidi.Content = $"{Languages.Strings.CS_GeneratingNotes} 0%";
            genImage.Source = null;

            double lastReport = 0;
            DateTime lastReportTime = DateTime.Now;

            // 启用保护模式确保完整转换
            await convert.RunProcessAsync(async () =>
            {
                Dispatcher.Invoke(() => saveMidi.Content = $"{Languages.Strings.CS_GeneratingImage} 0%");
                var wb = await convert.GeneratePreviewWriteableBitmapAsync(8, progress =>
                {
                    int percent = (int)(progress * 100);
                    Dispatcher.Invoke(() => saveMidi.Content = $"{Languages.Strings.CS_GeneratingImage} {percent}%");
                });
                Dispatcher.Invoke(() =>
                {
                    saveMidi.Content = $"{Languages.Strings.CS_GeneratingImage} 100%";
                    ReloadPalettePreview();
                    genImage.Source = wb;
                    ShowPreview();
                    midiExported = false;
                    lastExportedMidiPath = null;
                    UpdateSaveMidiButton();
                    genImage.Opacity = 0;
                    var fadeIn = (Storyboard)genImage.GetValue(FadeInStoryboard);
                    fadeIn?.Begin();
                });
            },
            progress =>
            {
                if (progress - lastReport >= 0.01 || (DateTime.Now - lastReportTime).TotalMilliseconds > 100 || progress == 1.0)
                {
                    lastReport = progress;
                    lastReportTime = DateTime.Now;
                    int percent = (int)(progress * 100);
                    Dispatcher.Invoke(() => saveMidi.Content = $"{Languages.Strings.CS_GeneratingNotes} {percent}%");
                }
            },
            enableProtection: true); // 启用保护模式

            genImage.Source = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }



        public int GetTargetHeight()
        {
            int effectiveWidth = GetEffectiveKeyWidth();
            switch (heightMode)
            {
                case HeightModeEnum.SameAsWidth:
                    return effectiveWidth;
                case HeightModeEnum.OriginalHeight:
                    return openedImageHeight;
                case HeightModeEnum.CustomHeight:
                    return customHeight;
                case HeightModeEnum.OriginalAspectRatio:
                    if (openedImageSrc == null)
                    {
                        return 0;
                    }
                    if (previewRotation == 90 || previewRotation == 270)
                    {
                        double aspectRatio = (double)originalImageWidth / originalImageHeight;
                        return (int)(effectiveWidth * aspectRatio);
                    }
                    else
                    {
                        double aspectRatio = (double)originalImageHeight / originalImageWidth;
                        return (int)(effectiveWidth * aspectRatio);
                    }
                default:
                    return originalImageSrc != null ? originalImageHeight : openedImageHeight;
            }
        }
        private async void ShowPreview()
        {
            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.BeginInvoke((Action)ShowPreview);
                return;
            }
            if (convert != null)
            {
                // 优先用WriteableBitmap预览（异步）
                var wb = await convert.GeneratePreviewWriteableBitmapAsync();
                if (wb != null)
                {
                    genImage.Source = wb;
                    previewTipTextBlock.Visibility = Visibility.Collapsed;
                    return;
                }
            }
            genImage.Source = null;
            previewTipTextBlock.Visibility = Visibility.Visible;
            midiExported = false;
            lastExportedMidiPath = null;
            UpdateSaveMidiButton();
            /*genImage.Opacity = 0;
            var fadeIn = (Storyboard)genImage.GetValue(FadeInStoryboard);
            fadeIn?.Begin();*/
            //await FakeFlip();
            // 也可在此处GC，防止极端情况下内存未释放
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private async void SaveMidi_Click(object sender, RoutedEventArgs e)
        {
            // 如果刚导出成功，点击则打开文件夹
            if (midiExported && !string.IsNullOrEmpty(lastExportedMidiPath) && File.Exists(lastExportedMidiPath))
            {
                // 打开文件夹并选中文件
                Process.Start("explorer.exe", $"/select,\"{lastExportedMidiPath}\"");
                return;
            }

            colPicker.CancelPick();
            SaveFileDialog save = new SaveFileDialog();
            save.Filter = $"{Languages.Strings.CS_MidiFile} (*.mid)|*.mid";
            if (!(bool)save.ShowDialog()) return;
            try
            {
                if (convert != null)
                {
                    bool colorEvents = (bool)genColorEventsCheck.IsChecked;
                    saveMidi.IsEnabled = false;
                    saveMidi.Content = $"{Languages.Strings.CS_ExportingMidi} 0%";

                    // 先在UI线程读取所有需要的值
                    int ticksPerPixelValue = (int)ticksPerPixel.Value;
                    int midiPPQValue = (int)midiPPQ.Value;
                    int startOffsetValue = (int)startOffset.Value;
                    int midiBPMValue = (int)midiBPM.Value;

                    double lastReport = 0;
                    DateTime lastReportTime = DateTime.Now;

                    string midiPath = save.FileName;

                    await Task.Run(() =>
                    {
                        ConversionProcess.WriteMidi(
                            midiPath,
                            new[] { convert },
                            ticksPerPixelValue,
                            midiPPQValue,
                            startOffsetValue,
                            midiBPMValue,
                            colorEvents,
                            progress =>
                            {
                                if (progress - lastReport >= 0.01 || (DateTime.Now - lastReportTime).TotalMilliseconds > 100 || progress == 1.0)
                                {
                                    lastReport = progress;
                                    lastReportTime = DateTime.Now;
                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        int percent = (int)(progress * 100);
                                        if (progress >= 1.0)
                                        {
                                            midiExported = true;
                                            lastExportedMidiPath = midiPath;
                                            saveMidi.IsEnabled = true;
                                            saveMidi.Content = $"{Languages.Strings.CS_ExportingFolder}";
                                        }
                                        else
                                        {
                                            saveMidi.Content = $"{Languages.Strings.CS_ExportingMidi} {percent}%";
                                        }
                                    }));
                                }
                            }
                        );
                    });
                    midiExported = true;
                    lastExportedMidiPath = midiPath; // 记录导出路径

                    saveMidi.IsEnabled = true;
                    saveMidi.Content = $"{Languages.Strings.CS_ExportingFolder}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Image To MIDI被玩坏了！\n这肯定不是节能酱的问题！\n绝对不是！");
                UpdateSaveMidiButton();
            }
        }

        private void NoteSplitLength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            ReloadPreview();
        }

        /*private void StartOfImage_Checked(object sender, RoutedEventArgs e)
        {
            ReloadPreview();
        }

        private void StartOfNotes_Checked(object sender, RoutedEventArgs e)
        {
            ReloadPreview();
        }

        private void UseNoteLength_Checked(object sender, RoutedEventArgs e)
        {
            ReloadPreview();
        }*/

        /*private async void ResetPalette_Click(object sender, RoutedEventArgs e)
        {
            await ReloadAutoPalette();
        }*/

        private void OpenedImage_ColorClicked(object sender, Color c)
        {
            if (colorPick)
                colPicker.SendColor(c);
        }

        // 新增方法：更新“宽高相等”模式下的高度数值
        /*private void UpdateHeightForSameAsWidth()
        {
            CustomHeightNumberSelect.Value = GetTargetHeight();
        }*/

        private async void FirstKeyNumber_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            lastKeyNumber.Minimum = firstKeyNumber.Value + 1;
            CustomHeightNumberSelect.Value = GetTargetHeight();
            UpdateWhiteKeyModeButtonContent();
            await ReloadVectorBitmapAsync(); // 新增
            ReloadPreview();
        }

        private async void LastKeyNumber_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            firstKeyNumber.Maximum = lastKeyNumber.Value - 1;
            CustomHeightNumberSelect.Value = GetTargetHeight();
            UpdateWhiteKeyModeButtonContent();
            await ReloadVectorBitmapAsync(); // 新增
            ReloadPreview();
        }

        private void GenColorEventsCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            ReloadPreview();
            if ((bool)genColorEventsCheck.IsChecked)
                ((Storyboard)randomSeedBox.GetValue(FadeOutStoryboard)).Begin();
            else
                ((Storyboard)randomSeedBox.GetValue(FadeInStoryboard)).Begin();
        }

        string prevHexText = "";

        Color ParseHex(string hex, bool checkLen = false)
        {
            if (hex.Length != 6 && checkLen) throw new Exception("十六进制值无效");
            if (hex.Length == 0 && !checkLen) return Colors.Black;
            try
            {
                int col = int.Parse(hex.ToUpper(), System.Globalization.NumberStyles.HexNumber);
                Color c = Color.FromRgb(
                        (byte)((col >> 16) & 0xFF),
                        (byte)((col >> 8) & 0xFF),
                        (byte)((col >> 0) & 0xFF)
                    );
                return c;
            }
            catch { throw new Exception("十六进制RGB色号无效"); }
        }

        private void ColHex_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                ParseHex(colHex.Text);
                prevHexText = colHex.Text;
            }
            catch { colHex.Text = prevHexText; }
        }

        private void ColHex_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Color c;
                try
                {
                    c = ParseHex(colHex.Text, true);
                    errorTextBlock.Visibility = Visibility.Collapsed; // 隐藏错误信息
                }
                catch
                {
                    //errorTextBlock.Text = "色号无效";
                    errorTextBlock.Visibility = Visibility.Visible; // 显示错误信息
                    return;
                }
                ColPicker_PickStop();
                colPicker.SendColor(c);
            }
        }

        private void SetHexButton_Click(object sender, RoutedEventArgs e)
        {
            Color c;
            try
            {
                c = ParseHex(colHex.Text, true);
                errorTextBlock.Visibility = Visibility.Collapsed; // 隐藏错误信息
            }
            catch
            {
                //errorTextBlock.Text = "色号无效";
                errorTextBlock.Visibility = Visibility.Visible; // 显示错误信息
                return;
            }
            ColPicker_PickStop();
            colPicker.SendColor(c);
        }

        private async void CustomHeightNumberSelect_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            if (heightMode == HeightModeEnum.CustomHeight)
            {
                int value = (int)CustomHeightNumberSelect.Value;
                if (value == 0)
                    return; // 输入为0时不传出参数，直接返回

                customHeight = value;
                await ReloadVectorBitmapAsync(); // 新增
                ReloadPreview();
            }
        }
        /*private async void HeightModeButton_Click(object sender, RoutedEventArgs e)
        {
            CustomHeightNumberSelect.IsEnabled = !CustomHeightNumberSelect.IsEnabled;
            if (heightMode == HeightModeEnum.SameAsWidth)
            {
                heightMode = HeightModeEnum.OriginalHeight;
                HeightModeButton.Content = "原图高度";
                CustomHeightNumberSelect.IsEnabled = false;
                int openedImageHeight = GetTargetHeight();
                CustomHeightNumberSelect.Value = openedImageHeight;
            }
            else if (heightMode == HeightModeEnum.OriginalHeight)
            {
                heightMode = HeightModeEnum.CustomHeight;
                HeightModeButton.Content = "自定高度";
                CustomHeightNumberSelect.IsEnabled = true;
                customHeight = (int)CustomHeightNumberSelect.Value;
            }
            else if (heightMode == HeightModeEnum.CustomHeight)
            {
                heightMode = HeightModeEnum.OriginalAspectRatio;
                HeightModeButton.Content = "原图比例";
                CustomHeightNumberSelect.IsEnabled = false;
                int targetHeight = GetTargetHeight();
                CustomHeightNumberSelect.Value = targetHeight;
            }
            else if (heightMode == HeightModeEnum.OriginalAspectRatio)
            {
                heightMode = HeightModeEnum.SameAsWidth;
                HeightModeButton.Content = "宽高相等";
                CustomHeightNumberSelect.IsEnabled = false;
                CustomHeightNumberSelect.Value = (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1;
            }
            await ReloadVectorBitmapAsync(); // 新增
            ReloadPreview();
        }*/
        private async void HeightModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HeightModeComboBox == null || CustomHeightNumberSelect == null)
                return;

            if (HeightModeComboBox.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag as string;
                switch (tag)
                {
                    case "SameAsWidth":
                        heightMode = HeightModeEnum.SameAsWidth;
                        CustomHeightNumberSelect.IsEnabled = false;
                        CustomHeightNumberSelect.Value = GetEffectiveKeyWidth();
                        break;
                    case "OriginalHeight":
                        heightMode = HeightModeEnum.OriginalHeight;
                        CustomHeightNumberSelect.IsEnabled = false;
                        CustomHeightNumberSelect.Value = openedImageHeight;
                        break;
                    case "CustomHeight":
                        heightMode = HeightModeEnum.CustomHeight;
                        CustomHeightNumberSelect.IsEnabled = true;
                        customHeight = (int)CustomHeightNumberSelect.Value;
                        break;
                    case "OriginalAspectRatio":
                        heightMode = HeightModeEnum.OriginalAspectRatio;
                        CustomHeightNumberSelect.IsEnabled = false;
                        CustomHeightNumberSelect.Value = GetTargetHeight();
                        break;
                }
                await ReloadVectorBitmapAsync();
                ReloadPreview();
            }
        }
        private void UpdateSaveMidiButton()
        {
            if (midiExported)
            {
                saveMidi.IsEnabled = true;
                saveMidi.Content = $"{Languages.Strings.CS_ExportingCompleted}";
                return;
            }
            if (convert != null && convert.NoteCount > 0)
            {
                saveMidi.IsEnabled = true;
                saveMidi.Content = $"{Languages.Strings.CS_ExportNotecount1} {convert.NoteCount} {Languages.Strings.CS_ExportNotecount2}";
            }
            else
            {
                saveMidi.IsEnabled = false;
                saveMidi.Content = $"{Languages.Strings.CS_PleaseWait}";
            }
        }
        private void ClusterMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            lastClusterMethodSelectedItem = ClusterMethodComboBox.SelectedItem;
            UpdateAlgorithmParamPanel();
            _ = ReloadAutoPalette();
        }

        private ColorIdMethod currentColorIdMethod = ColorIdMethod.RGB; // 默认值
        private void ColorIdMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = ColorIdMethodComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem != null)
            {
                var tag = selectedItem.Tag as string;
                switch (tag)
                {
                    case "RGB":
                        currentColorIdMethod = ColorIdMethod.RGB;
                        break;
                    case "HSV":
                        currentColorIdMethod = ColorIdMethod.HSV;
                        break;
                    case "HSL":
                        currentColorIdMethod = ColorIdMethod.HSL;
                        break;
                    case "Lab":
                        currentColorIdMethod = ColorIdMethod.Lab;
                        break;
                    case "CIEDE2000":
                        currentColorIdMethod = ColorIdMethod.CIEDE2000;
                        break;
                    // 未来可扩展更多算法
                    default:
                        currentColorIdMethod = ColorIdMethod.RGB;
                        break;
                }
                ReloadPreview(); // 切换算法时自动刷新预览
            }
        }

        private void ResizeMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = ResizeMethodComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem != null)
            {
                var tag = selectedItem.Tag as string;
                switch (tag)
                {
                    case "AreaResampling":
                        currentResizeAlgorithm = ResizeAlgorithm.AreaResampling;
                        break;
                    case "Bilinear":
                        currentResizeAlgorithm = ResizeAlgorithm.Bilinear;
                        break;
                    case "NearestNeighbor": // 新增
                        currentResizeAlgorithm = ResizeAlgorithm.NearestNeighbor;
                        break;
                    case "Bicubic": // 新增
                        currentResizeAlgorithm = ResizeAlgorithm.Bicubic;
                        break;
                    case "Lanczos":
                        currentResizeAlgorithm = ResizeAlgorithm.Lanczos;
                        break;
                    case "Gaussian": // 新增
                        currentResizeAlgorithm = ResizeAlgorithm.Gaussian;
                        break;
                    case "Mitchell": // 新增
                        currentResizeAlgorithm = ResizeAlgorithm.Mitchell;
                        break;
                    case "BoxFilter": // 新增
                        currentResizeAlgorithm = ResizeAlgorithm.BoxFilter;
                        break;
                    case "IntegralImage": // 新增
                        currentResizeAlgorithm = ResizeAlgorithm.IntegralImage;
                        break;
                    case "ModePooling":
                        currentResizeAlgorithm = ResizeAlgorithm.ModePooling;
                        break;
                    case "Hermite":
                        currentResizeAlgorithm = ResizeAlgorithm.Hermite;
                        break;
                }
                ReloadPreview(); // 切换算法时自动刷新预览
            }
        }
        private void ClusterMethodComboBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var comboBox = sender as ComboBox;
            if (comboBox == null) return;

            // 仅在下拉框已展开时才拦截
            if (!comboBox.IsDropDownOpen)
                return;

            // 获取鼠标位置
            var point = e.GetPosition(comboBox);

            // 遍历所有 ComboBoxItem
            foreach (var obj in comboBox.Items)
            {
                var itemContainer = comboBox.ItemContainerGenerator.ContainerFromItem(obj) as ComboBoxItem;
                if (itemContainer != null)
                {
                    // 判断鼠标是否在当前 ComboBoxItem 区域内
                    Rect bounds = new Rect(itemContainer.TranslatePoint(new Point(0, 0), comboBox), itemContainer.RenderSize);
                    if (bounds.Contains(point))
                    {
                        // 如果是当前已选中项
                        if (obj == comboBox.SelectedItem)
                        {
                            _ = ReloadAutoPalette();
                            e.Handled = true;
                        }
                        break;
                    }
                }
            }
        }
        private void AlgorithmParamBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            if (!IsInitialized) return;
            _ = ReloadAutoPalette();
        }
        private void HierarchicalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;
            _ = ReloadAutoPalette();
        }
        private void FloydSerpentineBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            _ = ReloadAutoPalette();
        }
        private void FloydBaseMethodBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;
            _ = ReloadAutoPalette();
        }
        private void OrderedDitherMatrixSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;
            _ = ReloadAutoPalette();
        }
        private void OrderedDitherBaseMethodBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;
            _ = ReloadAutoPalette();
        }

        private void UseGrayFixedPaletteBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // 读取CheckBox状态，直接赋值给useGrayFixedPalette
            useGrayFixedPalette = UseGrayFixedPaletteBox.IsChecked == true;
            // 立即刷新色板
            _ = ReloadAutoPalette();
        }
        private void WhiteKeyModeButton_Click(object sender, RoutedEventArgs e)
        {
            // 循环切换模式
            //whiteKeyMode = (WhiteKeyMode)(((int)whiteKeyMode + 1) % 7);
            do
            {
                whiteKeyMode = (WhiteKeyMode)(((int)whiteKeyMode + 1) % 7);
            }
            while (whiteKeyMode == WhiteKeyMode.WhiteKeysFixed || whiteKeyMode == WhiteKeyMode.BlackKeysFixed);
            UpdateWhiteKeyModeButtonContent();
            CustomHeightNumberSelect.Value = GetTargetHeight();
            ReloadPreview();
        }
        public int GetEffectiveKeyWidth()
        {
            int start = (int)firstKeyNumber.Value;
            int end = (int)lastKeyNumber.Value;
            switch (whiteKeyMode)
            {
                case WhiteKeyMode.WhiteKeysClipped:
                case WhiteKeyMode.BlackKeysClipped:
                    // 裁剪模式：宽度始终为全键数
                    return end - start + 1;
                case WhiteKeyMode.WhiteKeysFilled:
                case WhiteKeyMode.BlackKeysFilled:
                    return Enumerable.Range(start, end - start + 1).Count(whiteKeyMode == WhiteKeyMode.WhiteKeysFilled ? IsWhiteKey : (Func<int, bool>)(i => !IsWhiteKey(i)));
                default:
                    return end - start + 1;
            }
        }
        private void UpdateWhiteKeyModeButtonContent()
        {
            int start = (int)firstKeyNumber.Value;
            int end = (int)lastKeyNumber.Value;
            int total = end - start + 1;
            int whiteCount = Enumerable.Range(start, total).Count(IsWhiteKey);
            int blackCount = total - whiteCount;

            switch (whiteKeyMode)
            {
                case WhiteKeyMode.AllKeys:
                    WhiteKeyModeButton.Content = $"{total}{Languages.Strings.CS_Keys}";
                    break;
                case WhiteKeyMode.WhiteKeysFilled:
                    WhiteKeyModeButton.Content = $"{whiteCount}{Languages.Strings.CS_KeysWhiteFilled}";
                    break;
                case WhiteKeyMode.WhiteKeysClipped:
                    WhiteKeyModeButton.Content = $"{whiteCount}{Languages.Strings.CS_KeysWhiteClipped}";
                    break;
                case WhiteKeyMode.WhiteKeysFixed:
                    WhiteKeyModeButton.Content = $"{whiteCount}{Languages.Strings.CS_KeysWhiteFixed}";
                    break;
                case WhiteKeyMode.BlackKeysFilled:
                    WhiteKeyModeButton.Content = $"{blackCount}{Languages.Strings.CS_KeysBlackFilled}";
                    break;
                case WhiteKeyMode.BlackKeysClipped:
                    WhiteKeyModeButton.Content = $"{blackCount}{Languages.Strings.CS_KeysBlackClipped}";
                    break;
                case WhiteKeyMode.BlackKeysFixed:
                    WhiteKeyModeButton.Content = $"{blackCount}{Languages.Strings.CS_KeysBlackFixed}";
                    break;
            }
        }
        /*private void NoteLengthModeButton_Click(object sender, RoutedEventArgs e)
        {
            // 循环切换模式
            noteLengthMode = (NoteLengthMode)(((int)noteLengthMode + 1) % 3);
            UpdateNoteLengthModeUI();
            ReloadPreview();
        }
        private void UpdateNoteLengthModeUI()
        {
            switch (noteLengthMode)
            {
                case NoteLengthMode.Unlimited:
                    NoteLengthModeButton.Content = "不限制";
                    noteSplitLength.Visibility = Visibility.Collapsed;
                    break;
                case NoteLengthMode.FlowWithColor:
                    NoteLengthModeButton.Content = "随颜色流动";
                    noteSplitLength.Visibility = Visibility.Visible;
                    break;
                case NoteLengthMode.SplitToGrid:
                    NoteLengthModeButton.Content = "切成小方格";
                    noteSplitLength.Visibility = Visibility.Visible;
                    break;
            }
        }*/
        private void NoteLengthModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NoteLengthModeComboBox == null || noteSplitLength == null)
                return;

            if (NoteLengthModeComboBox.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag as string;
                switch (tag)
                {
                    case "Unlimited":
                        noteLengthMode = NoteLengthMode.Unlimited;
                        var fadeOut = (Storyboard)noteSplitLength.GetValue(FadeOutStoryboard);
                        if (fadeOut != null)
                        {
                            fadeOut.Completed -= FadeOut_Completed;
                            fadeOut.Completed += FadeOut_Completed;
                            fadeOut.Begin();
                        }
                        else
                        {
                            noteSplitLength.Visibility = Visibility.Collapsed;
                        }
                        break;
                    case "FlowWithColor":
                    case "SplitToGrid":
                        noteLengthMode = tag == "FlowWithColor" ? NoteLengthMode.FlowWithColor : NoteLengthMode.SplitToGrid;
                        if (noteSplitLength.Value <= 0) { 
                            noteSplitLength.Value = 1;
                            //输出提示信息
                            MessageBox.Show("分割长度不能小于等于0，已自动调整为1。");
                        } // 或者你喜欢的默认值
                        // 只有在控件原本是隐藏时才执行淡入动画
                        if (noteSplitLength.Visibility != Visibility.Visible)
                        {
                            noteSplitLength.Visibility = Visibility.Visible;
                            noteSplitLength.Opacity = 1;
                            var fadeIn = (Storyboard)noteSplitLength.GetValue(FadeInStoryboard);
                            fadeIn?.Begin();
                        }
                        // 如果已经可见，则不重复动画
                        break;
                }
                ReloadPreview();
            }
        }

        // 新增事件处理方法
        private void FadeOut_Completed(object sender, EventArgs e)
        {
            noteSplitLength.Visibility = Visibility.Collapsed;
            // 解绑，防止多次触发
            var fadeOut = (Storyboard)noteSplitLength.GetValue(FadeOutStoryboard);
            if (fadeOut != null)
                fadeOut.Completed -= FadeOut_Completed;
        }
        private BitmapSource ToGrayScale(BitmapSource src, Action<double> progress = null)
        {
            var format = PixelFormats.Bgra32;
            int width = src.PixelWidth;
            int height = src.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            src.CopyPixels(pixels, stride, 0);

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int i = rowStart + x * 4;
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                    pixels[i] = gray;
                    pixels[i + 1] = gray;
                    pixels[i + 2] = gray;
                }
                if (progress != null && (y % 8 == 0 || y == height - 1))
                    progress((y + 1) / (double)height);
            }

            var bmp = BitmapSource.Create(width, height, src.DpiX, src.DpiY, format, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }
        private async void GrayScaleCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            genImage.Source = null;
            saveMidi.IsEnabled = false;
            if (string.IsNullOrEmpty(openedImagePath) || !File.Exists(openedImagePath))
                return;

            string ext = System.IO.Path.GetExtension(openedImagePath).ToLowerInvariant();
            string[] vectorAllowed = { ".svg" };
            string[] gsVectorAllowed = { ".eps", ".ai", ".pdf" };

            // 仅在灰度缩略图未生成时生成
            if (grayScaleCheckBox.IsChecked == true)
            {
                if (landscapeGrayPreviewSrc == null && landscapeColorPreviewSrc != null)
                {
                    // 生成横向灰度缩略图
                    landscapeGrayPreviewSrc = ToGrayScale(landscapeColorPreviewSrc);
                    ExtractPixels(landscapeGrayPreviewSrc, out landscapeGrayPreviewPixels);
                }
                if (portraitGrayPreviewSrc == null && portraitColorPreviewSrc != null)
                {
                    // 生成纵向灰度缩略图
                    portraitGrayPreviewSrc = ToGrayScale(portraitColorPreviewSrc);
                    ExtractPixels(portraitGrayPreviewSrc, out portraitGrayPreviewPixels);
                }

                // 针对矢量图，优先用已有彩色缩略图转灰度
                if (vectorAllowed.Contains(ext))
                {
                    // 如果已经有彩色缩略图，直接转灰度，不再重新生成
                    if (landscapeColorPreviewSrc != null)
                    {
                        landscapeGrayPreviewSrc = ToGrayScale(landscapeColorPreviewSrc);
                        ExtractPixels(landscapeGrayPreviewSrc, out landscapeGrayPreviewPixels);
                    }
                    if (portraitColorPreviewSrc != null)
                    {
                        portraitGrayPreviewSrc = ToGrayScale(portraitColorPreviewSrc);
                        ExtractPixels(portraitGrayPreviewSrc, out portraitGrayPreviewPixels);
                    }
                    // 如果没有彩色缩略图，才重新生成
                    if (landscapeColorPreviewSrc == null || portraitColorPreviewSrc == null)
                    {
                        int keyWidth = (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1;
                        int targetHeight = GetTargetHeight();
                        var progress = new Progress<string>(msg => saveMidi.Content = msg);
                        await Task.Run(() => GenerateSVGThumbnails(openedImagePath, keyWidth, targetHeight, previewRotation, previewFlip, progress));
                        if (landscapeColorPreviewSrc != null)
                        {
                            landscapeGrayPreviewSrc = ToGrayScale(landscapeColorPreviewSrc);
                            ExtractPixels(landscapeGrayPreviewSrc, out landscapeGrayPreviewPixels);
                        }
                        if (portraitColorPreviewSrc != null)
                        {
                            portraitGrayPreviewSrc = ToGrayScale(portraitColorPreviewSrc);
                            ExtractPixels(portraitGrayPreviewSrc, out portraitGrayPreviewPixels);
                        }
                    }
                }
                else if (gsVectorAllowed.Contains(ext))
                {
                    // EPS/AI/PDF同理，优先用已有彩色缩略图转灰度
                    if (landscapeColorPreviewSrc != null)
                    {
                        landscapeGrayPreviewSrc = ToGrayScale(landscapeColorPreviewSrc);
                        ExtractPixels(landscapeGrayPreviewSrc, out landscapeGrayPreviewPixels);
                    }
                    else
                    {
                        int keyWidth = (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1;
                        int targetHeight = GetTargetHeight();
                        // 重新渲染矢量为位图
                        BitmapSource src = await Task.Run(() => RenderGsVectorToBitmapSource(openedImagePath, keyWidth, targetHeight));
                        if (src != null)
                        {
                            landscapeColorPreviewSrc = src;
                            ExtractPixels(src, out landscapeColorPreviewPixels);
                            landscapeGrayPreviewSrc = ToGrayScale(src);
                            ExtractPixels(landscapeGrayPreviewSrc, out landscapeGrayPreviewPixels);
                        }
                    }
                }
            }

            await UpdateOpenedImageByAngleAndFlip();
            await ReloadAutoPalette();
        }


        // 旋转、镜像按钮事件
        private async void RotateLeftButton_Click(object sender, RoutedEventArgs e)
        {
            previewRotation = (previewRotation + 270) % 360;
            if (previewRotation < 0) previewRotation += 360;
            openedImage.ImageRotation = previewRotation;
            openedImage.ImageFlip = previewFlip;

            string ext = System.IO.Path.GetExtension(openedImagePath ?? "").ToLowerInvariant();
            if (ext == ".svg")
            {
                await ReloadVectorBitmapAsync();
                // 不再重复调用
                // ReloadPreview();
                // CustomHeightNumberSelect.Value = GetTargetHeight();
            }
            else
            {
                await UpdateOpenedImageByAngleAndFlip();
                ReloadPreview();
                CustomHeightNumberSelect.Value = GetTargetHeight();
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private async void RotateRightButton_Click(object sender, RoutedEventArgs e)
        {
            previewRotation = (previewRotation + 90) % 360;
            if (previewRotation < 0) previewRotation += 360;
            openedImage.ImageRotation = previewRotation;
            openedImage.ImageFlip = previewFlip;

            string ext = System.IO.Path.GetExtension(openedImagePath ?? "").ToLowerInvariant();
            if (ext == ".svg")
            {
                await ReloadVectorBitmapAsync();
            }
            else
            {
                await UpdateOpenedImageByAngleAndFlip();
                ReloadPreview();
                CustomHeightNumberSelect.Value = GetTargetHeight();
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private async void FlipHorizontalButton_Click(object sender, RoutedEventArgs e)
        {
            previewFlip = !previewFlip;
            openedImage.ImageRotation = previewRotation;
            openedImage.ImageFlip = previewFlip;

            string ext = System.IO.Path.GetExtension(openedImagePath ?? "").ToLowerInvariant();
            if (ext == ".svg")
            {
                await ReloadVectorBitmapAsync();
            }
            else
            {
                await UpdateOpenedImageByAngleAndFlip();
                ReloadPreview();
            }
        }

        // 90度顺时针旋转
        private async Task<BitmapSource> Rotate90(BitmapSource src)
        {
            return await Task.Run(() =>
            {
                var tb = new TransformedBitmap(src, new RotateTransform(90));
                tb.Freeze();
                // 保证输出为BGRA32
                var converted = new FormatConvertedBitmap(tb, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                return converted;
            });
        }

        private async Task<BitmapSource> Rotate180(BitmapSource src)
        {
            return await Task.Run(() =>
            {
                var tb = new TransformedBitmap(src, new RotateTransform(180));
                tb.Freeze();
                var converted = new FormatConvertedBitmap(tb, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                return converted;
            });
        }

        private async Task<BitmapSource> Rotate270(BitmapSource src)
        {
            return await Task.Run(() =>
            {
                var tb = new TransformedBitmap(src, new RotateTransform(270));
                tb.Freeze();
                var converted = new FormatConvertedBitmap(tb, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                return converted;
            });
        }

        private async Task<BitmapSource> FlipHorizontal(BitmapSource src)
        {
            return await Task.Run(() =>
            {
                int w = src.PixelWidth, h = src.PixelHeight;
                int stride = w * 4;
                byte[] srcPixels = new byte[h * stride];
                src.CopyPixels(srcPixels, stride, 0);

                byte[] dstPixels = new byte[h * stride];
                Parallel.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int srcIdx = y * stride + x * 4;
                        int dstIdx = y * stride + (w - 1 - x) * 4;
                        Buffer.BlockCopy(srcPixels, srcIdx, dstPixels, dstIdx, 4);
                    }
                });
                var bmp = BitmapSource.Create(w, h, src.DpiX, src.DpiY, PixelFormats.Bgra32, null, dstPixels, stride);
                bmp.Freeze();
                return bmp;
            });
        }
        //这个函数是用来同步生成MIDI图片的
        private async Task UpdateOpenedImageByAngleAndFlip()
        {
            string ext = System.IO.Path.GetExtension(openedImagePath ?? "").ToLowerInvariant();
            bool isSvg = ext == ".svg";
            bool isGsVector = ext == ".eps" || ext == ".ai" || ext == ".pdf";
            bool isGray = grayScaleCheckBox.IsChecked == true;
            BitmapSource src = null;

            if (isSvg)
            {
                // SVG只保留一份缩略图，旋转/镜像直接在生成时处理
                src = landscapeColorPreviewSrc;
                // 灰度化
                if (isGray && src != null)
                {
                    src = ToGrayScale(src);
                }
                openedImageSrc = src;
                openedImageWidth = src?.PixelWidth ?? 0;
                openedImageHeight = src?.PixelHeight ?? 0;

                if (src != null)
                {
                    int stride = src.Format.BitsPerPixel / 8 * src.PixelWidth;
                    byte[] pixels = new byte[src.PixelHeight * stride];
                    src.CopyPixels(pixels, stride, 0);
                    openedImagePixels = pixels;
                }
                else
                {
                    openedImagePixels = null;
                }
                return;
            }

            if (isGsVector)
            {
                // EPS/AI/PDF渲染后支持旋转和镜像
                int keyWidth = (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1;
                int targetHeight = GetTargetHeight();
                src = await Task.Run(() => RenderGsVectorToBitmapSource(openedImagePath, keyWidth, targetHeight));

                // 旋转/镜像
                int rot = previewRotation;
                if (previewFlip)
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
                if (previewFlip && src != null)
                    src = await FlipHorizontal(src);

                // 灰度化
                if (isGray && src != null)
                {
                    src = ToGrayScale(src);
                }

                openedImageSrc = src;
                openedImageWidth = src?.PixelWidth ?? 0;
                openedImageHeight = src?.PixelHeight ?? 0;

                if (openedImageSrc != null)
                {
                    int stride = openedImageSrc.Format.BitsPerPixel / 8 * openedImageSrc.PixelWidth;
                    byte[] pixels = new byte[openedImageSrc.PixelHeight * stride];
                    openedImageSrc.CopyPixels(pixels, stride, 0);
                    openedImagePixels = pixels;
                }
                else
                {
                    openedImagePixels = null;
                }
                return;
            }

            // 位图图片的旋转/镜像处理（原有逻辑）
            int rot2 = previewRotation;
            if (previewFlip)
            {
                if (rot2 == 90) rot2 = 270;
                else if (rot2 == 270) rot2 = 90;
            }

            switch (rot2)
            {
                case 0:
                    src = isGray ? portraitGrayPreviewSrc : portraitColorPreviewSrc;
                    break;
                case 90:
                    src = isGray ? landscapeGrayPreviewSrc : landscapeColorPreviewSrc;
                    if (src != null) src = await Rotate90(src);
                    break;
                case 180:
                    src = isGray ? portraitGrayPreviewSrc : portraitColorPreviewSrc;
                    if (src != null) src = await Rotate180(src);
                    break;
                case 270:
                    src = isGray ? landscapeGrayPreviewSrc : landscapeColorPreviewSrc;
                    if (src != null) src = await Rotate270(src);
                    break;
            }

            if (previewFlip && src != null)
            {
                src = await FlipHorizontal(src);
            }

            openedImageSrc = src;
            openedImageWidth = openedImageSrc?.PixelWidth ?? 0;
            openedImageHeight = openedImageSrc?.PixelHeight ?? 0;

            if (openedImageSrc != null)
            {
                int stride = openedImageSrc.Format.BitsPerPixel / 8 * openedImageSrc.PixelWidth;
                byte[] pixels = new byte[openedImageSrc.PixelHeight * stride];
                openedImageSrc.CopyPixels(pixels, stride, 0);
                openedImagePixels = pixels;
            }
            else
            {
                openedImagePixels = null;
            }
        }
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
            var (processes, exportParams) = await PrepareBatchConversionProcesses(items, progress);
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
                string ext = System.IO.Path.GetExtension(imgPath).ToLowerInvariant();

                // 1. 位图格式
                string[] bitmapAllowed = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
                string[] svgAllowed = { ".svg" };
                string[] gsVectorAllowed = { ".eps", ".ai", ".pdf" };

                if (bitmapAllowed.Contains(ext))
                {
                    byte[] imageBytes = await Task.Run(() => File.ReadAllBytes(imgPath));
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
                byte[] pixels = new byte[height * stride];
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
                byte[] ditheredPixels;
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
            var exportParams = new BatchExportParams
            {
                TicksPerPixelValue = ticksPerPixelValue,
                MidiPPQValue = midiPPQValue,
                StartOffsetValue = startOffsetValue,
                MidiBPMValue = midiBPMValue,
                GenColorEvents = (bool)genColorEventsCheck.IsChecked
            };
            return (processList, exportParams);
        }
    }
}