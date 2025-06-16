using Microsoft.Win32;
using MIDIModificationFramework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        bool leftSelected = true; //左侧面板是否打开
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
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(Rectangle.OpacityProperty));

            e.SetValue(FadeInStoryboard, fadeInBoard);

            DoubleAnimation fadeOut = new DoubleAnimation();
            fadeOut.From = 1.0;
            fadeOut.To = 0.0;
            fadeOut.Duration = new Duration(TimeSpan.FromSeconds(0.2));

            Storyboard fadeOutBoard = new Storyboard();
            fadeOutBoard.Children.Add(fadeOut);
            Storyboard.SetTarget(fadeOut, e);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(Rectangle.OpacityProperty));

            e.SetValue(FadeOutStoryboard, fadeOutBoard);
        }

        void TriggerMenuTransition(bool left)
        {
            if (left)
            {
                ((Storyboard)selectedHighlightRight.GetValue(FadeOutStoryboard)).Begin();
                ((Storyboard)selectedHighlightLeft.GetValue(FadeInStoryboard)).Begin();
                tabSelect.SelectedIndex = 0;
            }
            else
            {
                ((Storyboard)selectedHighlightRight.GetValue(FadeInStoryboard)).Begin();
                ((Storyboard)selectedHighlightLeft.GetValue(FadeOutStoryboard)).Begin();
                tabSelect.SelectedIndex = 1;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            //Debug.WriteLine($"KMeansParamPanel: {KMeansParamPanel}"); // 验证是否为 null
            MakeFadeInOut(selectedHighlightLeft);
            MakeFadeInOut(selectedHighlightRight);
            MakeFadeInOut(colPickerOptions);
            MakeFadeInOut(openedImage);
            MakeFadeInOut(genImage);
            MakeFadeInOut(randomSeedBox);

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

            this.Closing += MainWindow_Closing;

            UpdateNoteLengthModeUI();            
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

        private void RawMidiSelect_Click(object sender, RoutedEventArgs e)
        {
            colPicker.CancelPick();
            if (!leftSelected)
                TriggerMenuTransition(true);
            leftSelected = true;
            ReloadPreview();
        }

        private void ColorEventsSelect_Click(object sender, RoutedEventArgs e)
        {
            colPicker.CancelPick();
            if (leftSelected)
                TriggerMenuTransition(false);
            leftSelected = false;
            ReloadPreview();
        }

        //private const int MaxPreviewWidth = 256; // 最大宽度限制

        /// <summary>
        /// 如果图片宽度大于MaxPreviewWidth，则缩放宽度到该值，高度保持原样
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

            var src32 = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            src32.Freeze();
            int srcStride = width * 4;
            byte[] srcPixels = new byte[height * srcStride];
            src32.CopyPixels(srcPixels, srcStride, 0);

            byte[] dstPixels = new byte[dstH * dstW * 4];

            Parallel.For(0, dstH, y =>
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
                // 进度回调
                progress?.Invoke((y + 1) / (double)dstH);
            });

            var bmp = BitmapSource.Create(dstW, dstH, src.DpiX, src.DpiY, PixelFormats.Bgra32, null, dstPixels, dstW * 4);
            bmp.Freeze();
            return bmp;
        }

        private async void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            colPicker.CancelPick();
            ((Storyboard)openedImage.GetValue(FadeInStoryboard)).Begin();
            await Task.Delay(100);

            OpenFileDialog open = new OpenFileDialog();
            open.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp";
            if (!(bool)open.ShowDialog()) return;
            openedImagePath = open.FileName;

            string ext = System.IO.Path.GetExtension(openedImagePath).ToLowerInvariant();
            string[] allowed = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            if (!allowed.Contains(ext))
            {
                MessageBox.Show("请选择有效的图片文件（png, jpg, jpeg, bmp）。", "文件类型不支持", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 清理所有缓存
            openedImageSrc = null;
            openedImagePixels = null;
            originalImageSrc = null;
            //originalImagePixels = null;
            ditheredImagePixels = null;
            chosenPalette = null;
            convert = null;
            openedImage.Source = null;
            genImage.Source = null;

            openedImageSrc = null;
            openedImagePixels = null;
            originalImageSrc = null;
            //originalImagePixels = null;
            ditheredImagePixels = null;
            chosenPalette = null;
            convert = null;
            openedImage.Source = null;
            genImage.Source = null;
            
            //openedImageWidth = 0;
            //openedImageHeight = 0;

            // 清理所有缩略图缓存
            landscapeColorPreviewSrc = null;
            landscapeColorPreviewPixels = null;
            landscapeGrayPreviewSrc = null;
            landscapeGrayPreviewPixels = null;
            portraitColorPreviewSrc = null;
            portraitColorPreviewPixels = null;
            portraitGrayPreviewSrc = null;
            portraitGrayPreviewPixels = null;

            // 可选：重置旋转和镜像状态
            previewRotation = 0;
            previewFlip = false;
            openedImage.ImageRotation = 0;
            openedImage.ImageFlip = false;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            saveMidi.IsEnabled = false;
            var progress = new Progress<string>(msg => saveMidi.Content = msg);

            // 1. 只解码一次原图
            var (src, previewSrc, pixels) = await LoadAndProcessImageAsync(openedImagePath, false, progress);
            originalImageSrc = src;
            int origWidth = src.PixelWidth;
            int origHeight = src.PixelHeight;
            int origStride = src.Format.BitsPerPixel / 8 * origWidth;
            //originalImagePixels = new byte[origHeight * origStride];
            //src.CopyPixels(originalImagePixels, origStride, 0);

            // 2. 直接预览原图
            openedImage.Source = src;

            // 3. 生成缩略图缓存(XXX已废弃，已在第一步中实现)
            //GenerateThumbnails(originalImageSrc);

            // 4. 切换到当前缩略图（同步 openedImageSrc 和 openedImagePixels）
            await UpdateOpenedImageByMode();
            openedImage.Source = originalImageSrc;
            CustomHeightNumberSelect.Value = GetTargetHeight();// 打开新图片，高度栏自动更新

            // 5. 更新UI和后续流程
            openedImageWidth = openedImageSrc.PixelWidth;
            openedImageHeight = openedImageSrc.PixelHeight;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            //UpdateCustomHeightNumberSelect();
            saveMidi.Content = "正在生成色板... 0%";
            await ReloadAutoPalette();

            // 在BrowseImage_Click加载图片成功后添加
            // 加载图片后，确保完整添加 BatchFileItem
            BatchFileList.Add(new BatchFileItem
            {
                Index = BatchFileList.Count + 1,
                Format = System.IO.Path.GetExtension(openedImagePath).TrimStart('.').ToUpperInvariant(),
                FileName = System.IO.Path.GetFileName(openedImagePath),
                FrameCount = 1,
                Resolution = $"{originalImageSrc.PixelWidth}x{originalImageSrc.PixelHeight}",
                FullPath = openedImagePath
            });
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

            // 清理所有缩略图缓存
            landscapeColorPreviewSrc = null;
            landscapeColorPreviewPixels = null;
            landscapeGrayPreviewSrc = null;
            landscapeGrayPreviewPixels = null;
            portraitColorPreviewSrc = null;
            portraitColorPreviewPixels = null;
            portraitGrayPreviewSrc = null;
            portraitGrayPreviewPixels = null;

            // 可选：重置旋转和镜像状态
            previewRotation = 0;
            previewFlip = false;
            openedImage.ImageRotation = 0;
            openedImage.ImageFlip = false;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            saveMidi.IsEnabled = false;
            var progress = new Progress<string>(msg => saveMidi.Content = msg);

            // 1. 只解码一次原图
            var (src, previewSrc, pixels) = await LoadAndProcessImageAsync(openedImagePath, false, progress);
            originalImageSrc = src;

            // 2. 直接预览原图
            openedImage.Source = src;

            // 4. 切换到当前缩略图（同步 openedImageSrc 和 openedImagePixels）
            await UpdateOpenedImageByMode();
            openedImage.Source = originalImageSrc;
            CustomHeightNumberSelect.Value = GetTargetHeight();

            // 5. 更新UI和后续流程
            openedImageWidth = openedImageSrc.PixelWidth;
            openedImageHeight = openedImageSrc.PixelHeight;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            saveMidi.Content = "正在生成色板... 0%";
            await ReloadAutoPalette();
        }
        private async Task<(BitmapSource original, BitmapSource preview, byte[] previewPixels)> LoadAndProcessImageAsync(
    string path, bool previewToGray, IProgress<string> progress = null)
        {
            // 1. 读取图片字节流
            byte[] imageBytes = null;
            GC.Collect();
            await Task.Run(() =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    progress?.Report("正在读取文件...");
                }));
                imageBytes = File.ReadAllBytes(openedImagePath);
            });

            // 2. 创建BitmapSource（原图）
            progress?.Report("正在解码图片...");
            BitmapSource src = null;
            GC.Collect();
            GC.Collect();
            await Dispatcher.InvokeAsync(() =>
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
            progress?.Report("图片解码完成");

            // 3. 串行生成全部4个缩略图并带进度
            BitmapSource landscapeColor = null, landscapeGray = null, portraitColor = null, portraitGray = null;
            byte[] landscapeColorPixels = null, landscapeGrayPixels = null, portraitColorPixels = null, portraitGrayPixels = null;

            await Task.Run(() =>
            {
                var srcCopy = src;

                // 横向彩色
                progress?.Report("正在生成缩略图... 0%");
                landscapeColor = Downsample(srcCopy, null, 256, p =>
                {
                    int percent = (int)(p * 100);
                    progress?.Report($"正在生成缩略图... {percent}%");
                });
                {
                    int w = landscapeColor.PixelWidth, h = landscapeColor.PixelHeight, stride = landscapeColor.Format.BitsPerPixel / 8 * w;
                    landscapeColorPixels = new byte[h * stride];
                    landscapeColor.CopyPixels(landscapeColorPixels, stride, 0);
                }

                // 纵向彩色
                //progress?.Report("正在生成纵向彩色缩略图... 0%");
                progress?.Report("即将完成...");
                portraitColor = Downsample(srcCopy, 256, null, p =>
                {
                    int percent = (int)(p * 100);
                    progress?.Report($"正在生成缩略图... {percent}%");
                });
                {
                    int w = portraitColor.PixelWidth, h = portraitColor.PixelHeight, stride = portraitColor.Format.BitsPerPixel / 8 * w;
                    portraitColorPixels = new byte[h * stride];
                    portraitColor.CopyPixels(portraitColorPixels, stride, 0);
                }

                // 横向灰度
                progress?.Report("正在生成横向灰度缩略图... 0%");
                landscapeGray = ToGrayScale(landscapeColor, p =>
                {
                    int percent = (int)(p * 100);
                    progress?.Report($"正在生成横向灰度缩略图... {percent}%");
                });
                {
                    // 直接用landscapeColor的宽高
                    int w = landscapeColor.PixelWidth, h = landscapeColor.PixelHeight, stride = landscapeGray.Format.BitsPerPixel / 8 * w;
                    landscapeGrayPixels = new byte[h * stride];
                    landscapeGray.CopyPixels(landscapeGrayPixels, stride, 0);
                }

                // 纵向灰度
                progress?.Report("正在生成纵向灰度缩略图... 0%");
                portraitGray = ToGrayScale(portraitColor, p =>
                {
                    int percent = (int)(p * 100);
                    progress?.Report($"正在生成纵向灰度缩略图... {percent}%");
                });
                {
                    // 直接用portraitColor的宽高
                    int w = portraitColor.PixelWidth, h = portraitColor.PixelHeight, stride = portraitGray.Format.BitsPerPixel / 8 * w;
                    portraitGrayPixels = new byte[h * stride];
                    portraitGray.CopyPixels(portraitGrayPixels, stride, 0);
                }
            });

            // 赋值到全局变量
            landscapeColorPreviewSrc = landscapeColor;
            landscapeColorPreviewPixels = landscapeColorPixels;
            landscapeGrayPreviewSrc = landscapeGray;
            landscapeGrayPreviewPixels = landscapeGrayPixels;
            portraitColorPreviewSrc = portraitColor;
            portraitColorPreviewPixels = portraitColorPixels;
            portraitGrayPreviewSrc = portraitGray;
            portraitGrayPreviewPixels = portraitGrayPixels;

            // 4. 返回主预览（兼容原有逻辑）
            BitmapSource previewSrc;
            byte[] previewPixels;
            if (previewToGray)
            {
                previewSrc = landscapeGray;
                previewPixels = landscapeGrayPixels;
            }
            else
            {
                previewSrc = landscapeColor;
                previewPixels = landscapeColorPixels;
            }
            progress?.Report("全部缩略图生成完成");

            return (src, previewSrc, previewPixels);
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
                saveMidi.Content = "正在生成色板... 0%";
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
                                    saveMidi.Content = $"正在生成色板... {percent}%";
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
                            Content = $"HEX: {hex}\n音符数: {noteCount}"
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
        private void ReloadPreview()
        {
            if (!IsInitialized) return;
            if (openedImagePixels == null) return;
            if (openedImageSrc == null) return;
            if (colPicker == null) return;
            if (convert != null) convert.Cancel();

            midiExported = false;
            // 获取当前聚类方法
            var selectedItem = ClusterMethodComboBox.SelectedItem as ComboBoxItem;
            string methodTag = selectedItem?.Tag as string ?? "";

            // 只在抖动算法下显示抖动像素
            byte[] previewPixels;
            if ((methodTag == "FloydSteinbergDither" || methodTag == "OrderedDither") && ditheredImagePixels != null)
                previewPixels = ditheredImagePixels;
            else
                previewPixels = openedImagePixels;

            if (previewPixels == null) return;

            var palette = chosenPalette;
            if (leftSelected) palette = colPicker.GetPalette();

            if (palette == null || palette.Colors == null || palette.Colors.Count == 0)
            {
                // 可选：弹窗提示
                // MessageBox.Show("色板未生成，请先生成色板。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int targetHeight = GetTargetHeight();
            var keyList = GetKeyList();
            bool whiteKeyFixed = (whiteKeyMode == WhiteKeyMode.WhiteKeysFixed);
            bool blackKeyFixed = (whiteKeyMode == WhiteKeyMode.BlackKeysFixed);
            bool whiteKeyClipped = (whiteKeyMode == WhiteKeyMode.WhiteKeysClipped);
            bool blackKeyClipped = (whiteKeyMode == WhiteKeyMode.BlackKeysClipped);

            if (whiteKeyMode == WhiteKeyMode.WhiteKeysFilled || whiteKeyMode == WhiteKeyMode.BlackKeysFilled)
            {
                whiteKeyFixed = blackKeyFixed = whiteKeyClipped = blackKeyClipped = false;
            }

            int effectiveWidth = GetEffectiveKeyWidth();

            //bool useNoteLengthChecked = useNoteLength.IsChecked == true;
            //int maxNoteLength = (int)noteSplitLength.Value;
            //bool measureFromStart = startOfImage.IsChecked == true;

            bool useNoteLengthChecked = noteLengthMode != NoteLengthMode.Unlimited;
            int maxNoteLength = (int)noteSplitLength.Value;
            bool measureFromStart = noteLengthMode == NoteLengthMode.SplitToGrid;

            //Debug.WriteLine($"[ReloadPreview] effectiveWidth={effectiveWidth}, targetHeight={targetHeight}");
            convert = new ConversionProcess(
                palette,
                previewPixels,
                openedImageWidth * 4,
                (int)firstKeyNumber.Value,
                (int)lastKeyNumber.Value + 1,
                measureFromStart,
                useNoteLengthChecked ? maxNoteLength : 0,
                targetHeight, // 直接传入
                currentResizeAlgorithm,
                keyList,
                whiteKeyClipped,
                blackKeyClipped,
                whiteKeyFixed,
                blackKeyFixed
            );

            convert.EffectiveWidth = effectiveWidth;

            if (!(bool)genColorEventsCheck.IsChecked)
            {
                convert.RandomColors = true;
                convert.RandomColorSeed = (int)randomColorSeed.Value;
            }

            saveMidi.IsEnabled = false;
            saveMidi.Content = "正在生成音符... 0%";
            double lastReport = 0;
            DateTime lastReportTime = DateTime.Now;

            genImage.Source = null;

            convert.RunProcessAsync(() =>
            {
                // 音符生成完毕后，立即切换到“正在生成图片...0%”
                Dispatcher.Invoke(() =>
                {
                    saveMidi.Content = "正在生成图片... 0%";
                });

                // 图片生成（带进度）
                var img = convert.GenerateImage(progress =>
                {
                    int percent = (int)(progress * 100);
                    Dispatcher.Invoke(() =>
                    {
                        saveMidi.Content = $"正在生成图片... {percent}%";
                    });
                });

                // 图片生成完毕
                Dispatcher.Invoke(() =>
                {
                    saveMidi.Content = "正在生成图片... 100%";
                    ReloadPalettePreview();
                    ShowPreview();
                    UpdateSaveMidiButton();
                });
            },
            progress =>
            {
                if (progress - lastReport >= 0.01 || (DateTime.Now - lastReportTime).TotalMilliseconds > 100 || progress == 1.0)
                {
                    lastReport = progress;
                    lastReportTime = DateTime.Now;
                    int percent = (int)(progress * 100);
                    Dispatcher.Invoke(() =>
                    {
                        saveMidi.Content = $"正在生成音符... {percent}%";
                    });
                }
            });
            genImage.Source = null;//  清空图片
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
                    if (originalImageSrc == null)
                    {
                        return 0;
                    }
                    if (previewRotation == 90 || previewRotation == 270)
                    {
                        double aspectRatio = (double)originalImageSrc.PixelWidth / originalImageSrc.PixelHeight;
                        return (int)(effectiveWidth * aspectRatio);
                    }
                    else
                    {
                        double aspectRatio = (double)originalImageSrc.PixelHeight / originalImageSrc.PixelWidth;
                        return (int)(effectiveWidth * aspectRatio);
                    }
                        default:
                    return originalImageSrc != null ? originalImageSrc.PixelHeight : openedImageHeight;
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
            UpdateSaveMidiButton();
        }

        private async void SaveMidi_Click(object sender, RoutedEventArgs e)
        {
            colPicker.CancelPick();
            SaveFileDialog save = new SaveFileDialog();
            save.Filter = "MIDI文件 (*.mid)|*.mid";
            if (!(bool)save.ShowDialog()) return;
            try
            {
                if (convert != null)
                {
                    bool colorEvents = (bool)genColorEventsCheck.IsChecked;
                    saveMidi.IsEnabled = false;
                    saveMidi.Content = "正在导出 MIDI... 0%";

                    // 先在UI线程读取所有需要的值
                    int ticksPerPixelValue = (int)ticksPerPixel.Value;
                    int midiPPQValue = (int)midiPPQ.Value;
                    int startOffsetValue = (int)startOffset.Value;
                    int midiBPMValue = (int)midiBPM.Value;

                    double lastReport = 0;
                    DateTime lastReportTime = DateTime.Now;

                    await Task.Run(() =>
                    {
                        ConversionProcess.WriteMidi(
    save.FileName,
    new[] { convert }, // 传递单个 ConversionProcess 的集合
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
                                        saveMidi.Content = $"正在导出 MIDI... {percent}%";
                                    }));
                                }
                            }
                        );
                    });
                    midiExported = true;

                    saveMidi.IsEnabled = true;

                    // 2秒后恢复按钮内容
                    await Task.Delay(100);
                    saveMidi.Content = "导出成功！";
                    midiExported = false;
                    //UpdateSaveMidiButton();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Image To MIDI被玩坏了！<LineBreak/>这肯定不是节能酱的问题！<LineBreak/>绝对不是！");
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

        private void FirstKeyNumber_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            lastKeyNumber.Minimum = firstKeyNumber.Value + 1;
            CustomHeightNumberSelect.Value = GetTargetHeight();
            UpdateWhiteKeyModeButtonContent(); // 新增
            ReloadPreview();
        }

        private void LastKeyNumber_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            firstKeyNumber.Maximum = lastKeyNumber.Value - 1;
            CustomHeightNumberSelect.Value = GetTargetHeight();
            UpdateWhiteKeyModeButtonContent(); // 新增
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
                    errorTextBlock.Text = "色号无效";
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
                errorTextBlock.Text = "色号无效";
                errorTextBlock.Visibility = Visibility.Visible; // 显示错误信息
                return;
            }
            ColPicker_PickStop();
            colPicker.SendColor(c);
        }

        private void CustomHeightNumberSelect_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            if (heightMode == HeightModeEnum.CustomHeight)
            {
                customHeight = (int)CustomHeightNumberSelect.Value; // 更新自定义高度
                ReloadPreview(); // 刷新图像
            }
        }
        private void HeightModeButton_Click(object sender, RoutedEventArgs e)
        {
            CustomHeightNumberSelect.IsEnabled = !CustomHeightNumberSelect.IsEnabled;
            // 轮换高度模式
            if (heightMode == HeightModeEnum.SameAsWidth)
            {
                heightMode = HeightModeEnum.OriginalHeight;
                HeightModeButton.Content = "原图高度";
                CustomHeightNumberSelect.IsEnabled = false;
                int openedImageHeight = GetTargetHeight();
                CustomHeightNumberSelect.Value = openedImageHeight; // 显示原图的真实高度
            }
            else if (heightMode == HeightModeEnum.OriginalHeight)
            {
                heightMode = HeightModeEnum.CustomHeight;
                HeightModeButton.Content = "自定高度";
                CustomHeightNumberSelect.IsEnabled = true;
                customHeight = (int)CustomHeightNumberSelect.Value; // 设置自定义高度
            }
            else if (heightMode == HeightModeEnum.CustomHeight)
            {
                heightMode = HeightModeEnum.OriginalAspectRatio;
                HeightModeButton.Content = "原图比例";
                CustomHeightNumberSelect.IsEnabled = false;
                int targetHeight = GetTargetHeight();
                CustomHeightNumberSelect.Value = targetHeight; // 显示原图比例计算后的高度
            }
            else if (heightMode == HeightModeEnum.OriginalAspectRatio)
            {
                heightMode = HeightModeEnum.SameAsWidth;
                HeightModeButton.Content = "宽高相等";
                CustomHeightNumberSelect.IsEnabled = false;
                CustomHeightNumberSelect.Value = (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1; // 音符宽度
            }
            ReloadPreview(); // 刷新图像
        }
        private void UpdateSaveMidiButton()
        {
            if (midiExported)
            {
                saveMidi.IsEnabled = true;
                saveMidi.Content = "导出成功";
                return;
            }
            if (convert != null && convert.NoteCount > 0)
            {
                saveMidi.IsEnabled = true;
                saveMidi.Content = $"导出 MIDI（音符数 {convert.NoteCount}）";
            }
            else
            {
                saveMidi.IsEnabled = false;
                saveMidi.Content = "请稍侯...";
            }
        }
        private void ClusterMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            lastClusterMethodSelectedItem = ClusterMethodComboBox.SelectedItem;
            UpdateAlgorithmParamPanel();
            _ = ReloadAutoPalette();
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
                    WhiteKeyModeButton.Content = $"{total}键";
                    break;
                case WhiteKeyMode.WhiteKeysFilled:
                    WhiteKeyModeButton.Content = $"{whiteCount}白键(填充)";
                    break;
                case WhiteKeyMode.WhiteKeysClipped:
                    WhiteKeyModeButton.Content = $"{whiteCount}白键(裁剪)";
                    break;
                case WhiteKeyMode.WhiteKeysFixed:
                    WhiteKeyModeButton.Content = $"{whiteCount}白键(等宽)";
                    break;
                case WhiteKeyMode.BlackKeysFilled:
                    WhiteKeyModeButton.Content = $"{blackCount}黑键(填充)";
                    break;
                case WhiteKeyMode.BlackKeysClipped:
                    WhiteKeyModeButton.Content = $"{blackCount}黑键(裁剪)";
                    break;
                case WhiteKeyMode.BlackKeysFixed:
                    WhiteKeyModeButton.Content = $"{blackCount}黑键(等宽)";
                    break;
            }
        }
        private void NoteLengthModeButton_Click(object sender, RoutedEventArgs e)
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
                    NoteLengthModeButton.Content = "不限制音符长度";
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
            if (string.IsNullOrEmpty(openedImagePath) || !File.Exists(openedImagePath))
                return;

            await UpdateOpenedImageByMode();

            
            await ReloadAutoPalette();
        }
        

        // 旋转、镜像按钮事件
        private async void RotateLeftButton_Click(object sender, RoutedEventArgs e)
        {
            previewRotation = (previewRotation + 270) % 360;
            if (previewRotation < 0) previewRotation += 360;
            //Debug.WriteLine($"RotateLeft: previewRotation={previewRotation}");
            openedImage.ImageRotation = previewRotation;
            openedImage.ImageFlip = previewFlip;
            await UpdateOpenedImageByMode(); // 新增
            //openedImage.Source = openedImageSrc; // 新增调试
            ReloadPreview();
            CustomHeightNumberSelect.Value = GetTargetHeight();
        }

        private async void RotateRightButton_Click(object sender, RoutedEventArgs e)
        {
            previewRotation = (previewRotation + 90) % 360;
            if (previewRotation < 0) previewRotation += 360;
            //Debug.WriteLine($"RotateRight: previewRotation={previewRotation}");
            openedImage.ImageRotation = previewRotation;
            openedImage.ImageFlip = previewFlip;
            await UpdateOpenedImageByMode(); // 新增
            //openedImage.Source = openedImageSrc; // 新增调试
            ReloadPreview();
            CustomHeightNumberSelect.Value = GetTargetHeight();
        }

        private async void FlipHorizontalButton_Click(object sender, RoutedEventArgs e)
        {
            previewFlip = !previewFlip;
            openedImage.ImageRotation = previewRotation;
            openedImage.ImageFlip = previewFlip;
            await UpdateOpenedImageByMode(); // 新增
            //openedImage.Source = openedImageSrc; // 新增调试
            ReloadPreview();
        }

        // 90度顺时针旋转
        // 90度顺时针旋转（并行优化）
        private async Task<BitmapSource> Rotate90(BitmapSource src)
        {
            return await Task.Run(() =>
            {
                int w = src.PixelWidth, h = src.PixelHeight;
                int stride = w * 4;
                byte[] srcPixels = new byte[h * stride];
                src.CopyPixels(srcPixels, stride, 0);

                byte[] dstPixels = new byte[w * h * 4];
                int dstStride = h * 4;
                Parallel.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int srcIdx = y * stride + x * 4;
                        int dstIdx = x * dstStride + (h - 1 - y) * 4;
                        Buffer.BlockCopy(srcPixels, srcIdx, dstPixels, dstIdx, 4);
                    }
                });
                var bmp = BitmapSource.Create(h, w, src.DpiX, src.DpiY, PixelFormats.Bgra32, null, dstPixels, dstStride);
                bmp.Freeze();
                return bmp;
            });
        }

        private async Task<BitmapSource> Rotate180(BitmapSource src)
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
                        int dstIdx = (h - 1 - y) * stride + (w - 1 - x) * 4;
                        Buffer.BlockCopy(srcPixels, srcIdx, dstPixels, dstIdx, 4);
                    }
                });
                var bmp = BitmapSource.Create(w, h, src.DpiX, src.DpiY, PixelFormats.Bgra32, null, dstPixels, stride);
                bmp.Freeze();
                return bmp;
            });
        }

        private async Task<BitmapSource> Rotate270(BitmapSource src)
        {
            return await Task.Run(() =>
            {
                int w = src.PixelWidth, h = src.PixelHeight;
                int stride = w * 4;
                byte[] srcPixels = new byte[h * stride];
                src.CopyPixels(srcPixels, stride, 0);

                byte[] dstPixels = new byte[w * h * 4];
                int dstStride = h * 4;
                Parallel.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int srcIdx = y * stride + x * 4;
                        int dstIdx = (w - 1 - x) * dstStride + y * 4;
                        Buffer.BlockCopy(srcPixels, srcIdx, dstPixels, dstIdx, 4);
                    }
                });
                var bmp = BitmapSource.Create(h, w, src.DpiX, src.DpiY, PixelFormats.Bgra32, null, dstPixels, dstStride);
                bmp.Freeze();
                return bmp;
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
        private async Task UpdateOpenedImageByMode()
        {
            bool isGray = grayScaleCheckBox.IsChecked == true;
            BitmapSource src = null;

            // 翻转时角度反向
            int rot = previewRotation;
            if (previewFlip)
            {
                // 0/180 不变，90<->270
                if (rot == 90) rot = 270;
                else if (rot == 270) rot = 90;
            }

            switch (rot)
            {
                case 0:
                    src = isGray ? portraitGrayPreviewSrc : portraitColorPreviewSrc;
                    break;
                case 90:
                    src = isGray ? landscapeGrayPreviewSrc : landscapeColorPreviewSrc;
                    src = await Rotate90(src);
                    break;
                case 180:
                    src = isGray ? portraitGrayPreviewSrc : portraitColorPreviewSrc;
                    src = await Rotate180(src);
                    break;
                case 270:
                    src = isGray ? landscapeGrayPreviewSrc : landscapeColorPreviewSrc;
                    src = await Rotate270(src);
                    break;
            }

            // 翻转
            if (previewFlip && src != null)
            {
                src = await FlipHorizontal(src);
            }

            openedImageSrc = src;
            openedImageWidth = openedImageSrc?.PixelWidth ?? 0;
            openedImageHeight = openedImageSrc?.PixelHeight ?? 0;

            // 重新提取像素（无论旋转/镜像状态）
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
            if (!batchWindow.IsVisible)
            {
                batchWindow.Show();
            }
            else
            {
                batchWindow.Activate();
            }
        }
        public async Task BatchExportMidiAsync(
    IEnumerable<BatchFileItem> items,
    string exportFolder,
    IProgress<(int current, int total, string fileName)> progress = null)
        {
            int success = 0, fail = 0;
            var failList = new List<string>();
            var itemList = items.ToList();
            int total = itemList.Count;

            // 先在UI线程读取所有需要的参数，避免跨线程访问控件
            int colorCount = (int)trackCount.Value;
            int ticksPerPixelValue = (int)ticksPerPixel.Value;
            int midiPPQValue = (int)midiPPQ.Value;
            int startOffsetValue = (int)startOffset.Value;
            int midiBPMValue = (int)midiBPM.Value;
            bool genColorEvents = (bool)genColorEventsCheck.IsChecked;
            int firstKey = (int)firstKeyNumber.Value;
            int lastKey = (int)lastKeyNumber.Value;
            int noteSplitLengthValue = (int)noteSplitLength.Value;
            int targetHeight = GetTargetHeight();
            ResizeAlgorithm resizeAlgorithmValue = currentResizeAlgorithm;
            var keyList = GetKeyList();
            var noteLengthModeValue = noteLengthMode;
            var whiteKeyModeValue = whiteKeyMode;
            int effectiveWidth = GetEffectiveKeyWidth();

            // 聚类算法相关参数
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

            for (int i = 0; i < total; i++)
            {
                var item = itemList[i];
                try
                {
                    // 进度回调
                    progress?.Report((i + 1, total, item.FileName));

                    string imgPath = item.FullPath ?? item.FileName;
                    if (!File.Exists(imgPath)) throw new FileNotFoundException("文件不存在", imgPath);

                    // 1. 读取图片字节流
                    byte[] imageBytes = await Task.Run(() => File.ReadAllBytes(imgPath));

                    // 2. 在UI线程创建BitmapImage并Freeze
                    BitmapSource src = null;
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

                    // 3. 旋转/翻转/灰度处理
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

                    // 4. 提取像素
                    int width = src.PixelWidth, height = src.PixelHeight, stride = width * 4;
                    byte[] pixels = new byte[height * stride];
                    src.CopyPixels(pixels, stride, 0);

                    // 5. 生成色板
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

                    // 6. 生成MIDI
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

                    // 7. 导出MIDI
                    string midiName = $"{item.Index:D2}_{System.IO.Path.GetFileNameWithoutExtension(item.FileName)}.mid";
                    string midiPath = System.IO.Path.Combine(exportFolder, midiName);
                    await Task.Run(() =>
                    {
                        ConversionProcess.WriteMidi(
    midiPath,
    new[] { convert },
    ticksPerPixelValue,
    midiPPQValue,
    startOffsetValue,
    midiBPMValue,
    genColorEvents
);
                    });

                    success++;
                }
                catch (Exception ex)
                {
                    fail++;
                    failList.Add($"{item.FileName}：{ex.Message}\n{ex.StackTrace}");
                }
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            //MessageBox.Show($"批量导出完成！成功：{success}，失败：{fail}\n{string.Join("\n", failList)}", "导出结果");
        }
        // 合并导出：所有图片音符拼接到一个轨道
        public async Task BatchExportMidiConcatAsync(
    IEnumerable<BatchFileItem> items,
    string outputMidiPath,
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

                // 1. 读取图片
                byte[] imageBytes = await Task.Run(() => File.ReadAllBytes(imgPath));
                BitmapSource src = null;
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
            bool useColorEvents = (bool)genColorEventsCheck.IsChecked;
            // 6. 写MIDI（多图片首尾相连，直接用新版WriteMidi）
            await Task.Run(() =>
            {
                ConversionProcess.WriteMidi(
                    outputMidiPath,
                    processList,
                    ticksPerPixelValue,
                    midiPPQValue,
                    startOffsetValue,
                    midiBPMValue,
                    useColorEvents
                );
            });
        }
    }
}