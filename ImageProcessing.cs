/*using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ImageToMidi
{
    /// <summary>
    /// 图像处理管理器，负责所有图像加载、处理和转换相关功能
    /// </summary>
    public class ImageProcessing
    {
        private readonly MainWindow mainWindow;

        // 图像数据缓存
        public BitmapSource OpenedImageSrc { get; set; }
        public byte[] OpenedImagePixels { get; set; }
        public BitmapSource OriginalImageSrc { get; set; }
        public BitmapSource LandscapeColorPreviewSrc { get; set; }
        public byte[] LandscapeColorPreviewPixels { get; set; }
        public BitmapSource PortraitColorPreviewSrc { get; set; }
        public byte[] PortraitColorPreviewPixels { get; set; }
        public BitmapSource LandscapeGrayPreviewSrc { get; set; }
        public byte[] LandscapeGrayPreviewPixels { get; set; }
        public BitmapSource PortraitGrayPreviewSrc { get; set; }
        public byte[] PortraitGrayPreviewPixels { get; set; }
        public byte[] DitheredImagePixels { get; set; }

        // 图像属性
        public int OpenedImageWidth { get; set; }
        public int OpenedImageHeight { get; set; }
        public string OpenedImagePath { get; set; } = "";
        public int OriginalImageWidth { get; set; }
        public int OriginalImageHeight { get; set; }

        // 预览旋转角度和镜像状态
        public int PreviewRotation { get; set; } = 0; // 0, 90, 180, 270
        public bool PreviewFlip { get; set; } = false;

        // 动画帧相关
        private List<BitmapFrame> animatedFrames = null;
        private int currentFrameIndex = 0;
        private int totalFrameCount = 0;
        private bool isAnimatedImage = false;

        public ImageProcessing(MainWindow window)
        {
            mainWindow = window ?? throw new ArgumentNullException(nameof(window));
        }

        /// <summary>
        /// 支持的文件扩展名定义
        /// </summary>
        public static class SupportedExtensions
        {
            public static readonly string[] BitmapAllowed = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            public static readonly string[] VectorAllowed = { ".svg" };
            public static readonly string[] GsVectorAllowed = { ".eps", ".ai", ".pdf" };
            public static readonly string[] AllSupported = BitmapAllowed.Concat(VectorAllowed).Concat(GsVectorAllowed).ToArray();
        }

        /// <summary>
        /// 判断文件是否为受支持的图片格式
        /// </summary>
        public static bool IsSupportedImageFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return SupportedExtensions.AllSupported.Contains(ext);
        }

        /// <summary>
        /// 获取文件类型
        /// </summary>
        public static string GetImageFileType(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "unknown";

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (SupportedExtensions.BitmapAllowed.Contains(ext))
                return "bitmap";
            else if (SupportedExtensions.VectorAllowed.Contains(ext))
                return "svg";
            else if (SupportedExtensions.GsVectorAllowed.Contains(ext))
                return "gsvector";
            else
                return "unknown";
        }

        /// <summary>
        /// 清理所有图像缓存数据
        /// </summary>
        public void ClearImageCache()
        {
            // 清理ZoomableImage资源
            if (mainWindow.openedImage != null)
            {
                mainWindow.openedImage.Source = null;
                mainWindow.openedImage.SetSKBitmap(null);
            }

            // 阶段1：清理最大的像素数组
            OpenedImagePixels = null;
            DitheredImagePixels = null;
            ForceGarbageCollection(); // 立即回收大数组

            // 阶段2：清理预览像素数组
            LandscapeColorPreviewPixels = null;
            LandscapeGrayPreviewPixels = null;
            PortraitColorPreviewPixels = null;
            PortraitGrayPreviewPixels = null;
            ForceGarbageCollection(); // 立即回收预览数组

            // 阶段3：清理BitmapSource对象
            OpenedImageSrc = null;
            OriginalImageSrc = null;
            LandscapeColorPreviewSrc = null;
            LandscapeGrayPreviewSrc = null;
            PortraitColorPreviewSrc = null;
            PortraitGrayPreviewSrc = null;

            // 阶段4：清理其他对象
            mainWindow.chosenPalette = null;
            if (mainWindow.convert != null)
            {
                mainWindow.convert.Cancel();
                mainWindow.convert = null;
            }

            if (mainWindow.genImage != null)
                mainWindow.genImage.Source = null;

            // 重置属性
            OpenedImageWidth = 0;
            OpenedImageHeight = 0;
            OpenedImagePath = "";
            OriginalImageWidth = 0;
            OriginalImageHeight = 0;
            PreviewRotation = 0;
            PreviewFlip = false;

            if (mainWindow.openedImage != null)
            {
                mainWindow.openedImage.ImageRotation = 0;
                mainWindow.openedImage.ImageFlip = false;
            }

            // 清理调色板
            mainWindow.colPicker?.ClearPalette();

            // 最终强制垃圾回收
            ForceGarbageCollection();
        }

        /// <summary>
        /// 强制垃圾回收
        /// </summary>
        public static void ForceGarbageCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// 通用的图像加载和处理方法
        /// </summary>
        public async Task LoadImageCommonAsync(string imagePath, bool isFromBrowse)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return;

            // 设置路径
            OpenedImagePath = imagePath;

            // 清理所有缓存
            ClearImageCache();

            // 禁用保存按钮
            mainWindow.saveMidi.IsEnabled = false;
            var progress = new Progress<string>(msg => mainWindow.saveMidi.Content = msg);

            try
            {
                // 创建处理配置
                var config = new ImageProcessingConfig
                {
                    KeyWidth = (int)mainWindow.lastKeyNumber.Value - (int)mainWindow.firstKeyNumber.Value + 1,
                    TargetHeight = GetTargetHeight(),
                    PreviewRotation = PreviewRotation,
                    PreviewFlip = PreviewFlip,
                    HighResPreviewWidth = mainWindow.highResPreviewWidth,
                    IsForPreview = !isFromBrowse
                };

                // 加载和处理图像
                var result = await LoadAndProcessImageAsync(imagePath, config, progress);

                // 应用结果到主窗口
                await ApplyImageLoadResult(result, isFromBrowse, progress);

                // 后续处理
                await PostLoadProcessing(result, isFromBrowse);
            }
            catch (Exception ex)
            {
                await HandleLoadError(ex, imagePath);
                return;
            }

            // 验证文件格式支持
            if (!IsSupportedImageFile(imagePath))
            {
                MessageBox.Show("请选择有效的图片或矢量图文件（png, jpg, jpeg, bmp, gif, webp, svg, eps, ai, pdf）。", "文件类型不支持", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 最终处理
            mainWindow.CustomHeightNumberSelect.Value = GetTargetHeight();
            mainWindow.saveMidi.Content = $"{Languages.Strings.CS_GeneratingPalette} 0%";
            await mainWindow.ReloadAutoPalette();

            // 处理灰度模式
            await HandleGrayScaleMode();

            // 垃圾回收
            ForceGarbageCollection();
        }

        /// <summary>
        /// 图像处理的配置参数
        /// </summary>
        public class ImageProcessingConfig
        {
            public int KeyWidth { get; set; }
            public int TargetHeight { get; set; }
            public int PreviewRotation { get; set; } = 0;
            public bool PreviewFlip { get; set; } = false;
            public int HighResPreviewWidth { get; set; } = 4096;
            public bool IsForPreview { get; set; } = false;
        }

        /// <summary>
        /// 图像加载的结果
        /// </summary>
        public class ImageLoadResult
        {
            public BitmapSource OriginalSource { get; set; }
            public BitmapSource OpenedImageSource { get; set; }
            public byte[] OpenedImagePixels { get; set; }
            public int ImageWidth { get; set; }
            public int ImageHeight { get; set; }
            public int OriginalImageWidth { get; set; }
            public int OriginalImageHeight { get; set; }
            public BitmapSource LandscapeColorPreviewSrc { get; set; }
            public byte[] LandscapeColorPreviewPixels { get; set; }
            public BitmapSource PortraitColorPreviewSrc { get; set; }
            public byte[] PortraitColorPreviewPixels { get; set; }
        }

        /// <summary>
        /// 加载和处理图像的核心方法
        /// </summary>
        public async Task<ImageLoadResult> LoadAndProcessImageAsync(string imagePath, ImageProcessingConfig config, IProgress<string> progress = null)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                throw new FileNotFoundException($"图像文件不存在: {imagePath}");

            var result = new ImageLoadResult();
            string fileType = GetImageFileType(imagePath);
            BitmapSource src = null;

            progress?.Report("正在加载图像...");

            switch (fileType)
            {
                case "bitmap":
                    src = await LoadBitmapImageAsync(imagePath, progress);
                    result.OriginalSource = src;

                    // 生成缩略图
                    await GenerateThumbnailsAsync(src, result, progress);
                    break;

                case "svg":
                    src = await LoadSvgImageAsync(imagePath, config, progress);
                    result.LandscapeColorPreviewSrc = src;
                    // 修复：创建临时变量
                    byte[] tempPixels;
                    ExtractPixels(src, out tempPixels);
                    result.LandscapeColorPreviewPixels = tempPixels;
                    break;

                case "gsvector":
                    if (!IsGhostscriptAvailable())
                        throw new InvalidOperationException("Ghostscript不可用，无法处理EPS/AI/PDF文件");

                    src = await LoadGsVectorImageAsync(imagePath, config, progress);
                    break;

                default:
                    throw new NotSupportedException($"不支持的文件格式: {Path.GetExtension(imagePath)}");
            }

            // 设置基本属性
            if (src != null)
            {
                result.ImageWidth = src.PixelWidth;
                result.ImageHeight = src.PixelHeight;
                result.OriginalImageWidth = src.PixelWidth;
                result.OriginalImageHeight = src.PixelHeight;
                result.OpenedImageSource = src;
                // 修复：创建临时变量
                byte[] tempPixels2;
                ExtractPixels(src, out tempPixels2);
                result.OpenedImagePixels = tempPixels2;
            }

            progress?.Report("图像加载完成");
            return result;
        }

        /// <summary>
        /// 加载位图图像
        /// </summary>
        private async Task<BitmapSource> LoadBitmapImageAsync(string path, IProgress<string> progress = null)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            // 特殊处理GIF和WEBP文件
            if (ext == ".gif" || ext == ".webp")
            {
                return await LoadAnimatedImageWithComposition(path, progress);
            }
            else
            {
                return await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = fs;
                        bmp.EndInit();
                        bmp.Freeze();
                        return (BitmapSource)bmp;
                    }
                });
            }
        }

        /// <summary>
        /// 加载SVG图像
        /// </summary>
        private async Task<BitmapSource> LoadSvgImageAsync(string path, ImageProcessingConfig config, IProgress<string> progress = null)
        {
            return await Task.Run(() =>
            {
                progress?.Report("正在渲染SVG...");

                var svgDocument = Svg.SvgDocument.Open(path);
                if (svgDocument == null)
                    throw new Exception("SVG文件解析失败");

                var viewBox = svgDocument.ViewBox;
                float viewBoxX = viewBox.MinX;
                float viewBoxY = viewBox.MinY;
                float viewBoxWidth = viewBox.Width > 0 ? viewBox.Width : 1f;
                float viewBoxHeight = viewBox.Height > 0 ? viewBox.Height : 1f;

                using (var bitmap = new Bitmap(config.KeyWidth, config.TargetHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    // 设置高质量渲染选项
                    graphics.SmoothingMode = SmoothingMode.None;
                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphics.PixelOffsetMode = PixelOffsetMode.None;
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixel;

                    // 应用变换
                    graphics.TranslateTransform(-viewBoxX, -viewBoxY);
                    graphics.ScaleTransform((float)config.KeyWidth / viewBoxWidth, (float)config.TargetHeight / viewBoxHeight);

                    // 应用旋转和镜像
                    if (config.PreviewRotation != 0 || config.PreviewFlip)
                    {
                        graphics.TranslateTransform(viewBoxWidth / 2f, viewBoxHeight / 2f);
                        if (config.PreviewRotation != 0)
                            graphics.RotateTransform(config.PreviewRotation);
                        if (config.PreviewFlip)
                            graphics.ScaleTransform(-1, 1);
                        graphics.TranslateTransform(-viewBoxWidth / 2f, -viewBoxHeight / 2f);
                    }

                    // 渲染SVG
                    svgDocument.Draw(graphics);

                    // 转换为WPF BitmapSource
                    var bitmapSource = ConvertBitmapToBitmapSource(bitmap);
                    bitmapSource.Freeze();
                    return bitmapSource;
                }
            });
        }

        /// <summary>
        /// 加载Ghostscript矢量图像
        /// </summary>
        private async Task<BitmapSource> LoadGsVectorImageAsync(string path, ImageProcessingConfig config, IProgress<string> progress = null)
        {
            return await Task.Run(() =>
            {
                progress?.Report("正在使用Ghostscript渲染...");
                return RenderGsVectorToBitmapSource(path, config.KeyWidth, config.TargetHeight, progress);
            });
        }

        /// <summary>
        /// 将图像加载结果应用到主窗口
        /// </summary>
        private async Task ApplyImageLoadResult(ImageLoadResult result, bool isFromBrowse, IProgress<string> progress)
        {
            if (result?.OpenedImageSource == null)
                return;

            // 直接更新 ImageProcessing 中的数据，MainWindow 通过属性访问器自动获取
            OriginalImageSrc = result.OriginalSource;
            OpenedImageSrc = result.OpenedImageSource;
            OpenedImagePixels = result.OpenedImagePixels;
            OpenedImageWidth = result.ImageWidth;
            OpenedImageHeight = result.ImageHeight;
            OriginalImageWidth = result.OriginalImageWidth;
            OriginalImageHeight = result.OriginalImageHeight;

            // 更新预览缓存
            LandscapeColorPreviewSrc = result.LandscapeColorPreviewSrc;
            LandscapeColorPreviewPixels = result.LandscapeColorPreviewPixels;
            PortraitColorPreviewSrc = result.PortraitColorPreviewSrc;
            PortraitColorPreviewPixels = result.PortraitColorPreviewPixels;

            // ============ 删除重复的同步代码 ============
            // 以下代码全部删除，因为 MainWindow 现在通过属性访问器直接访问 ImageProcessing 的数据
            /*
            mainWindow.openedImageSrc = OpenedImageSrc;
            mainWindow.openedImagePixels = OpenedImagePixels;
            mainWindow.originalImageSrc = OriginalImageSrc;
            mainWindow.openedImageWidth = OpenedImageWidth;
            mainWindow.openedImageHeight = OpenedImageHeight;
            mainWindow.originalImageWidth = OriginalImageWidth;
            mainWindow.originalImageHeight = OriginalImageHeight;
            mainWindow.landscapeColorPreviewSrc = LandscapeColorPreviewSrc;
            mainWindow.landscapeColorPreviewPixels = LandscapeColorPreviewPixels;
            mainWindow.portraitColorPreviewSrc = PortraitColorPreviewSrc;
            mainWindow.portraitColorPreviewPixels = PortraitColorPreviewPixels;
            

            // 特殊处理不同的图像类型
            string fileType = GetImageFileType(OpenedImagePath);

            switch (fileType)
            {
                case "bitmap":
                    await HandleBitmapResult(result, isFromBrowse);
                    break;
                case "svg":
                    await HandleSvgResult(result, isFromBrowse);
                    break;
                case "gsvector":
                    await HandleGsVectorResult(result, isFromBrowse, progress);
                    break;
            }
        }

        /// <summary>
        /// 处理位图加载结果
        /// </summary>
        private async Task HandleBitmapResult(ImageLoadResult result, bool isFromBrowse)
        {
            if (isFromBrowse && OriginalImageSrc != null)
            {
                mainWindow.openedImage.Opacity = 0;
                var skBitmap = OriginalImageSrc.ToSKBitmap();
                OriginalImageSrc = null; // 释放原始引用
                mainWindow.openedImage.SetSKBitmap(skBitmap);

                var fadeIn = (Storyboard)mainWindow.openedImage.GetValue(MainWindow.FadeInStoryboard);
                fadeIn?.Begin();

                // 检测并加载动图帧
                LoadAnimatedFrames(OpenedImagePath);

                await mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ForceGarbageCollection();
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
            else if (!isFromBrowse)
            {
                await UpdateOpenedImageByAngleAndFlip();
                mainWindow.openedImage.Source = OriginalImageSrc;
                LoadAnimatedFrames(OpenedImagePath);
            }
        }

        /// <summary>
        /// 处理SVG加载结果
        /// </summary>
        private async Task HandleSvgResult(ImageLoadResult result, bool isFromBrowse)
        {
            if (isFromBrowse)
            {
                var settings = new SharpVectors.Renderers.Wpf.WpfDrawingSettings();
                var reader = new SharpVectors.Converters.FileSvgReader(settings);
                var drawing = reader.Read(OpenedImagePath);

                if (drawing != null)
                {
                    mainWindow.openedImage.SvgDrawing = drawing;
                }
                else
                {
                    MessageBox.Show("SVG加载失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (OpenedImageSrc != null)
                {
                    var skBitmap = OpenedImageSrc.ToSKBitmap();
                    mainWindow.openedImage.SetSKBitmap(skBitmap);
                }
            }
            else
            {
                mainWindow.openedImage.Source = OpenedImageSrc;
            }
        }

        /// <summary>
        /// 处理Ghostscript矢量图加载结果
        /// </summary>
        private async Task HandleGsVectorResult(ImageLoadResult result, bool isFromBrowse, IProgress<string> progress)
        {
            if (isFromBrowse)
            {
                try
                {
                    var highResBitmap = await RenderGsVectorHighResPreview(OpenedImagePath, OpenedImageSrc, mainWindow.highResPreviewWidth, progress);
                    var skBitmap = highResBitmap.ToSKBitmap();
                    mainWindow.openedImage.SetSKBitmap(skBitmap);
                }
                catch
                {
                    if (OpenedImageSrc != null)
                    {
                        var skBitmap = OpenedImageSrc.ToSKBitmap();
                        mainWindow.openedImage.SetSKBitmap(skBitmap);
                    }
                }
            }
            else
            {
                try
                {
                    var highResBitmap = await RenderGsVectorHighResPreview(OpenedImagePath, OpenedImageSrc, mainWindow.highResPreviewWidth, progress);
                    mainWindow.openedImage.Source = highResBitmap;
                }
                catch
                {
                    mainWindow.openedImage.Source = OpenedImageSrc;
                }
            }
        }

        /// <summary>
        /// 加载后的后续处理
        /// </summary>
        private async Task PostLoadProcessing(ImageLoadResult result, bool isFromBrowse)
        {
            // 如果是从浏览按钮加载，添加到批量列表
            if (isFromBrowse && (OriginalImageSrc != null || OpenedImageSrc != null))
            {
                var eitherSrc = OriginalImageSrc ?? OpenedImageSrc;
                mainWindow.BatchFileList.Add(new BatchFileItem
                {
                    Index = mainWindow.BatchFileList.Count + 1,
                    Format = Path.GetExtension(OpenedImagePath).TrimStart('.').ToUpperInvariant(),
                    FileName = Path.GetFileName(OpenedImagePath),
                    FrameCount = 1,
                    Resolution = $"{eitherSrc.PixelWidth}x{eitherSrc.PixelHeight}",
                    FullPath = OpenedImagePath
                });
            }
            else if (isFromBrowse)
            {
                MessageBox.Show("图片加载失败，无法添加到批量列表。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 处理加载错误
        /// </summary>
        private async Task HandleLoadError(Exception ex, string imagePath)
        {
            string fileType = GetImageFileType(imagePath);
            if (fileType == "gsvector" && !IsGhostscriptAvailable())
            {
                ShowGhostscriptDownloadDialog();
            }
            else
            {
                string ext = Path.GetExtension(imagePath).ToUpperInvariant();
                MessageBox.Show($"{ext}加载失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 处理灰度模式
        /// </summary>
        public async Task HandleGrayScaleMode()
        {
            if (mainWindow.grayScaleCheckBox.IsChecked == true)
            {
                if (LandscapeColorPreviewSrc != null && LandscapeGrayPreviewSrc == null)
                {
                    LandscapeGrayPreviewSrc = ToGrayScale(LandscapeColorPreviewSrc);
                    ExtractPixels(LandscapeGrayPreviewSrc, out var pixels);
                    LandscapeGrayPreviewPixels = pixels;
                    // 删除重复同步代码：mainWindow.landscapeGrayPreviewSrc = LandscapeGrayPreviewSrc;
                }
                if (PortraitColorPreviewSrc != null && PortraitGrayPreviewSrc == null)
                {
                    PortraitGrayPreviewSrc = ToGrayScale(PortraitColorPreviewSrc);
                    ExtractPixels(PortraitGrayPreviewSrc, out var pixels);
                    PortraitGrayPreviewPixels = pixels;
                    // 删除重复同步代码：mainWindow.portraitGrayPreviewSrc = PortraitGrayPreviewSrc;
                }
                await UpdateOpenedImageByAngleAndFlip();
            }
        }

        // 获取目标高度
        public int GetTargetHeight()
        {
            int effectiveWidth = mainWindow.GetEffectiveKeyWidth();
            switch (mainWindow.heightMode)
            {
                case MainWindow.HeightModeEnum.SameAsWidth:
                    return effectiveWidth;
                case MainWindow.HeightModeEnum.OriginalHeight:
                    return OpenedImageHeight;
                case MainWindow.HeightModeEnum.CustomHeight:
                    return mainWindow.customHeight;
                case MainWindow.HeightModeEnum.OriginalAspectRatio:
                    if (OpenedImageSrc == null)
                    {
                        return 0;
                    }
                    if (PreviewRotation == 90 || PreviewRotation == 270)
                    {
                        double aspectRatio = (double)OriginalImageWidth / OriginalImageHeight;
                        return (int)(effectiveWidth * aspectRatio);
                    }
                    else
                    {
                        double aspectRatio = (double)OriginalImageHeight / OriginalImageWidth;
                        return (int)(effectiveWidth * aspectRatio);
                    }
                default:
                    return OriginalImageSrc != null ? OriginalImageHeight : OpenedImageHeight;
            }
        }

        // ======== 以下是辅助方法，从原MainWindow中移动过来的 ========

        /// <summary>
        /// 生成缩略图
        /// </summary>
        private async Task GenerateThumbnailsAsync(BitmapSource src, ImageLoadResult result, IProgress<string> progress = null)
        {
            await Task.Run(() =>
            {
                progress?.Report("正在生成横向缩略图...");
                var landscapeColor = Downsample(src, null, 256);
                ExtractPixels(landscapeColor, out var landscapeColorPixels);

                progress?.Report("正在生成纵向缩略图...");
                var portraitColor = Downsample(src, 256, null);
                ExtractPixels(portraitColor, out var portraitColorPixels);

                result.LandscapeColorPreviewSrc = landscapeColor;
                result.LandscapeColorPreviewPixels = landscapeColorPixels;
                result.PortraitColorPreviewSrc = portraitColor;
                result.PortraitColorPreviewPixels = portraitColorPixels;
            });
        }

        /// <summary>
        /// 图像缩放方法
        /// </summary>
        public BitmapSource Downsample(BitmapSource src, int? maxWidth = null, int? maxHeight = null, Action<double> progress = null)
        {
            int width = src.PixelWidth;
            int height = src.PixelHeight;

            double scaleX = 1.0, scaleY = 1.0;

            if (maxWidth.HasValue && width > maxWidth.Value)
                scaleX = (double)maxWidth.Value / width;
            if (maxHeight.HasValue && height > maxHeight.Value)
                scaleY = (double)maxHeight.Value / height;

            if (scaleX == 1.0 && scaleY == 1.0)
                return src;

            int dstW = (int)Math.Round(width * scaleX);
            int dstH = (int)Math.Round(height * scaleY);

            var src32 = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            src32.Freeze();

            int srcStride = width * 4;
            int srcSize = height * srcStride;
            int dstSize = dstH * dstW * 4;

            byte[] srcPixels = new byte[srcSize];
            byte[] dstPixels = new byte[dstSize];
            Array.Clear(dstPixels, 0, dstSize);

            src32.CopyPixels(srcPixels, srcStride, 0);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
            };

            System.Threading.Tasks.Parallel.For(0, dstH, parallelOptions, y =>
            {
                ProcessRow(y, dstW, dstH, width, height, srcPixels, dstPixels, srcStride, progress);
            });

            var bmp = BitmapSource.Create(dstW, dstH, src.DpiX, src.DpiY, PixelFormats.Bgra32, null, dstPixels, dstW * 4);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// 处理缩放的单行像素
        /// </summary>
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

            if (progress != null && (y % 8 == 0 || y == dstH - 1))
            {
                progress((y + 1) / (double)dstH);
            }
        }

        /// <summary>
        /// 提取像素数据
        /// </summary>
        public static void ExtractPixels(BitmapSource source, out byte[] pixels)
        {
            int w = source.PixelWidth;
            int h = source.PixelHeight;
            int stride = source.Format.BitsPerPixel / 8 * w;
            pixels = new byte[h * stride];
            source.CopyPixels(pixels, stride, 0);
        }

        /// <summary>
        /// 转换为灰度图像
        /// </summary>
        public static BitmapSource ToGrayScale(BitmapSource src, IProgress<double> progress = null)
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
                    progress.Report((y + 1) / (double)height); // 使用 Report 方法
            }

            var bmp = BitmapSource.Create(width, height, src.DpiX, src.DpiY, format, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// 处理动画图像的帧合成
        /// </summary>
        private async Task<BitmapSource> LoadAnimatedImageWithComposition(string path, IProgress<string> progress = null)
        {
            return await Task.Run(() =>
            {
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        progress?.Report("正在解码动画帧...");

                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            var decoder = BitmapDecoder.Create(fs,
                                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                                BitmapCacheOption.OnLoad);

                            if (decoder.Frames.Count == 0)
                                return LoadWithDefaultMethod(path);

                            var firstFrame = decoder.Frames[0];

                            // 修复：对于动画图像，只返回第一帧，不进行多帧合成
                            // 这样可以确保总是显示第一帧而不是第10帧
                            progress?.Report($"检测到{decoder.Frames.Count}帧，使用第一帧");
                            return ProcessFrameFormat(firstFrame);
                        }
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"动画加载失败：{ex.Message}，使用默认方式");
                        return LoadWithDefaultMethod(path);
                    }
                });
            });
        }

        // 继续添加其他辅助方法...
        // (由于字符限制,我将在下一个代码块中继续)

        /// <summary>
        /// 处理单帧格式
        /// </summary>
        private static BitmapSource ProcessFrameFormat(BitmapSource frame)
        {
            try
            {
                var originalFormat = frame.Format;

                if (originalFormat == PixelFormats.Indexed8 ||
                    originalFormat == PixelFormats.Indexed4 ||
                    originalFormat == PixelFormats.Indexed2 ||
                    originalFormat == PixelFormats.Indexed1)
                {
                    return PreserveIndexedColors(frame);
                }
                else
                {
                    var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                    converted.Freeze();
                    return converted;
                }
            }
            catch
            {
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                return converted;
            }
        }

        /// <summary>
        /// 保持索引颜色的精度
        /// </summary>
        private static BitmapSource PreserveIndexedColors(BitmapSource indexedFrame)
        {
            try
            {
                int width = indexedFrame.PixelWidth;
                int height = indexedFrame.PixelHeight;
                var palette = indexedFrame.Palette;

                if (palette == null || palette.Colors.Count == 0)
                {
                    var converted = new FormatConvertedBitmap(indexedFrame, PixelFormats.Bgra32, null, 0);
                    converted.Freeze();
                    return converted;
                }

                int stride = (width * indexedFrame.Format.BitsPerPixel + 7) / 8;
                byte[] indexedPixels = new byte[height * stride];
                indexedFrame.CopyPixels(indexedPixels, stride, 0);

                int outputStride = width * 4;
                byte[] outputPixels = new byte[height * outputStride];
                int bitsPerPixel = indexedFrame.Format.BitsPerPixel;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int colorIndex = GetColorIndex(indexedPixels, x, y, width, bitsPerPixel, stride);

                        if (colorIndex < palette.Colors.Count)
                        {
                            var color = palette.Colors[colorIndex];
                            int outputIndex = y * outputStride + x * 4;

                            outputPixels[outputIndex] = color.B;
                            outputPixels[outputIndex + 1] = color.G;
                            outputPixels[outputIndex + 2] = color.R;
                            outputPixels[outputIndex + 3] = color.A;
                        }
                    }
                }

                var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, outputPixels, outputStride);
                result.Freeze();
                return result;
            }
            catch
            {
                var converted = new FormatConvertedBitmap(indexedFrame, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                return converted;
            }
        }

        /// <summary>
        /// 从索引像素数据中提取颜色索引
        /// </summary>
        private static int GetColorIndex(byte[] pixels, int x, int y, int width, int bitsPerPixel, int stride)
        {
            switch (bitsPerPixel)
            {
                case 8:
                    return pixels[y * stride + x];
                case 4:
                    {
                        int byteIndex = y * stride + x / 2;
                        int bitOffset = (x % 2) * 4;
                        return (pixels[byteIndex] >> bitOffset) & 0x0F;
                    }
                case 2:
                    {
                        int byteIndex = y * stride + x / 4;
                        int bitOffset = (x % 4) * 2;
                        return (pixels[byteIndex] >> bitOffset) & 0x03;
                    }
                case 1:
                    {
                        int byteIndex = y * stride + x / 8;
                        int bitOffset = x % 8;
                        return (pixels[byteIndex] >> bitOffset) & 0x01;
                    }
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 帧合成算法
        /// </summary>
        private static void CompositeFrames(byte[] basePixels, byte[] overlayPixels, int width, int height)
        {
            int pixelCount = width * height;

            for (int i = 0; i < pixelCount; i++)
            {
                int pixelIndex = i * 4;
                byte overlayAlpha = overlayPixels[pixelIndex + 3];

                if (overlayAlpha > 0)
                {
                    if (overlayAlpha == 255)
                    {
                        // 完全不透明：直接替换
                        basePixels[pixelIndex] = overlayPixels[pixelIndex];
                        basePixels[pixelIndex + 1] = overlayPixels[pixelIndex + 1];
                        basePixels[pixelIndex + 2] = overlayPixels[pixelIndex + 2];
                        basePixels[pixelIndex + 3] = overlayPixels[pixelIndex + 3];
                    }
                    else
                    {
                        // 半透明：进行Alpha混合
                        float alpha = overlayAlpha / 255.0f;
                        float invAlpha = 1.0f - alpha;

                        basePixels[pixelIndex] = (byte)(overlayPixels[pixelIndex] * alpha + basePixels[pixelIndex] * invAlpha);
                        basePixels[pixelIndex + 1] = (byte)(overlayPixels[pixelIndex + 1] * alpha + basePixels[pixelIndex + 1] * invAlpha);
                        basePixels[pixelIndex + 2] = (byte)(overlayPixels[pixelIndex + 2] * alpha + basePixels[pixelIndex + 2] * invAlpha);
                        basePixels[pixelIndex + 3] = (byte)Math.Min(255, basePixels[pixelIndex + 3] + overlayAlpha);
                    }
                }
            }
        }

        /// <summary>
        /// 默认加载方法
        /// </summary>
        private static BitmapSource LoadWithDefaultMethod(string path)
        {
            return Application.Current.Dispatcher.Invoke(() =>
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
                    bmp.StreamSource = fs;
                    bmp.EndInit();
                    bmp.Freeze();
                    return (BitmapSource)bmp;
                }
            });
        }

        /// <summary>
        /// System.Drawing.Bitmap转WPF BitmapSource
        /// </summary>
        public static BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bitmapData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                int stride = bitmapData.Stride;
                int bytes = stride * bitmap.Height;
                byte[] pixelData = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, pixelData, 0, bytes);

                // Alpha通道处理
                for (int i = 0; i < pixelData.Length; i += 4)
                {
                    byte a = pixelData[i + 3];
                    if (a >= 1)
                        pixelData[i + 3] = 255;
                    else
                        pixelData[i + 3] = 0;
                }

                var bitmapSource = BitmapSource.Create(
                    bitmap.Width, bitmap.Height,
                    bitmap.HorizontalResolution, bitmap.VerticalResolution,
                    PixelFormats.Bgra32, null, pixelData, stride);

                bitmapSource.Freeze();
                return bitmapSource;
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        /// <summary>
        /// 使用Ghostscript渲染矢量图到BitmapSource
        /// </summary>
        public static BitmapSource RenderGsVectorToBitmapSource(string filePath, int targetWidth, int targetHeight, IProgress<string> progress = null)
        {
            progress?.Report("正在分析边界框...");
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
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

            progress?.Report("正在渲染...");

            // 限制最小渲染尺寸
            int minRenderSize = 16;
            int renderWidth = Math.Max(targetWidth, minRenderSize);
            int renderHeight = Math.Max(targetHeight, minRenderSize);

            string tempPng = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");

            // 计算DPI，避免极端值
            double xDpi = (double)renderWidth / Math.Max(rawW, 1) * 72.0;
            double yDpi = (double)renderHeight / Math.Max(rawH, 1) * 72.0;
            xDpi = Math.Max(10, Math.Min(xDpi, 1200));
            yDpi = Math.Max(10, Math.Min(yDpi, 1200));

            string gsArgs = $"-dSAFER -dBATCH -dNOPAUSE -sDEVICE=pngalpha " +
                $"-dDEVICEWIDTHPOINTS={rawW} -dDEVICEHEIGHTPOINTS={rawH} " +
                $"-dFIXEDMEDIA -dPDFFitPage " +
                $"-r{xDpi.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}x{yDpi.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} " +
                $"-dGraphicsAlphaBits=1 -dTextAlphaBits=1 -dDownScaleFactor=1 " +
                $"-dColorConversionStrategy=/LeaveColorUnchanged " +
                $"-dAutoFilterColorImages=false -dAutoFilterGrayImages=false " +
                $"-dColorImageFilter=/FlateEncode -dGrayImageFilter=/FlateEncode " +
                $"-dMonoImageFilter=/CCITTFaxEncode -dOptimize=false " +
                $"-dUseCropBox=false -dEPSCrop=true " +
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

            progress?.Report("正在读取结果...");
            BitmapSource bmp;
            using (var fs = new FileStream(tempPng, FileMode.Open, FileAccess.Read))
            {
                using (var bitmap = new Bitmap(fs))
                {
                    bmp = ConvertBitmapToBitmapSource(bitmap);
                }
            }

            try { File.Delete(tempPng); } catch { }
            bmp.Freeze();

            // 如果目标尺寸比渲染尺寸小，WPF再缩放
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

        /// <summary>
        /// 生成 EPS/AI/PDF 文件的高分辨率预览图
        /// </summary>
        public async Task<BitmapSource> RenderGsVectorHighResPreview(string filePath, BitmapSource lowResSrc, int highResWidth, IProgress<string> progress = null)
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
        /// 检查Ghostscript是否可用
        /// </summary>
        public static bool IsGhostscriptAvailable()
        {
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
                        if (!proc.WaitForExit(1000))
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

        /// <summary>
        /// 显示Ghostscript下载对话框
        /// </summary>
        public static void ShowGhostscriptDownloadDialog()
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

        // ======== 动画帧相关方法 ========

        /// <summary>
        /// 检测是否为多帧的GIF或WEBP文件
        /// </summary>
        private bool IsMultiFrameImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return false;

            string ext = Path.GetExtension(imagePath).ToLowerInvariant();
            if (ext != ".gif" && ext != ".webp")
                return false;

            try
            {
                var decoder = BitmapDecoder.Create(new Uri(imagePath), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                return decoder.Frames.Count > 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 加载动图的所有帧
        /// </summary>
        public void LoadAnimatedFrames(string imagePath)
        {
            try
            {
                if (!IsMultiFrameImage(imagePath))
                {
                    HideFrameNavigationPanel();
                    return;
                }

                var decoder = BitmapDecoder.Create(new Uri(imagePath), BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
                animatedFrames = decoder.Frames.ToList();
                totalFrameCount = animatedFrames.Count;
                currentFrameIndex = 0;
                isAnimatedImage = totalFrameCount > 1;

                Debug.WriteLine($"检测到动图: {imagePath}, 帧数: {totalFrameCount}");

                if (isAnimatedImage)
                {
                    ShowFrameNavigationPanel();
                }
                else
                {
                    HideFrameNavigationPanel();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载动图帧失败: {ex.Message}");
                HideFrameNavigationPanel();
            }
        }

        /// <summary>
        /// 显示帧导航面板
        /// </summary>
        private void ShowFrameNavigationPanel()
        {
            mainWindow.frameNavigationPanel.Visibility = Visibility.Visible;
            UpdateFrameCountText();
        }

        /// <summary>
        /// 隐藏帧导航面板
        /// </summary>
        private void HideFrameNavigationPanel()
        {
            mainWindow.frameNavigationPanel.Visibility = Visibility.Collapsed;
            isAnimatedImage = false;
            animatedFrames = null;
            totalFrameCount = 0;
            currentFrameIndex = 0;
        }

        /// <summary>
        /// 更新帧计数显示
        /// </summary>
        public void UpdateFrameCountText()
        {
            mainWindow.frameCountText.Text = $"{currentFrameIndex + 1}/{totalFrameCount}";
        }

        // ======== 图像变换相关方法 ========

        /// <summary>
        /// 90度顺时针旋转
        /// </summary>
        public async Task<BitmapSource> Rotate90(BitmapSource src)
        {
            return await Task.Run(() =>
            {
                var tb = new TransformedBitmap(src, new RotateTransform(90));
                tb.Freeze();
                var converted = new FormatConvertedBitmap(tb, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                return converted;
            });
        }

        /// <summary>
        /// 180度旋转
        /// </summary>
        public async Task<BitmapSource> Rotate180(BitmapSource src)
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

        /// <summary>
        /// 270度顺时针旋转
        /// </summary>
        public static async Task<BitmapSource> Rotate270(BitmapSource src)
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

        /// <summary>
        /// 水平镜像
        /// </summary>
        public static async Task<BitmapSource> FlipHorizontal(BitmapSource src)
        {
            return await Task.Run(() =>
            {
                int w = src.PixelWidth, h = src.PixelHeight;
                int stride = w * 4;
                byte[] srcPixels = new byte[h * stride];
                src.CopyPixels(srcPixels, stride, 0);

                byte[] dstPixels = new byte[h * stride];
                System.Threading.Tasks.Parallel.For(0, h, y =>
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
        public async Task UpdateOpenedImageByAngleAndFlip()
        {
            // 调用 MainWindow 的公共方法
            await mainWindow.UpdateOpenedImageByAngleAndFlip();
        }
        /// <summary>
        /// 合成到指定帧索引
        /// </summary>
        public async Task<BitmapSource> ComposeFramesToIndex(int targetIndex)
        {
            return await Task.Run(() =>
            {
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (animatedFrames == null || targetIndex >= animatedFrames.Count)
                            return null;

                        var firstFrame = animatedFrames[0];
                        int width = firstFrame.PixelWidth;
                        int height = firstFrame.PixelHeight;
                        int stride = width * 4;

                        // 创建合成画布
                        byte[] compositePixels = new byte[width * height * 4];

                        // 处理第一帧
                        var processedFirstFrame = ProcessFrameFormat(firstFrame);
                        processedFirstFrame.CopyPixels(compositePixels, stride, 0);

                        // 逐帧合成直到目标帧
                        for (int frameIndex = 1; frameIndex <= targetIndex; frameIndex++)
                        {
                            var currentFrame = animatedFrames[frameIndex];
                            var processedFrame = ProcessFrameFormat(currentFrame);

                            // 提取当前帧像素
                            byte[] framePixels = new byte[width * height * 4];
                            processedFrame.CopyPixels(framePixels, stride, 0);

                            // 合成帧
                            CompositeFrames(compositePixels, framePixels, width, height);
                        }

                        // 创建最终的合成图像
                        var compositeImage = BitmapSource.Create(width, height, 96, 96,
                            PixelFormats.Bgra32, null, compositePixels, stride);
                        compositeImage.Freeze();

                        return compositeImage;
                    }
                    catch
                    {
                        return null;
                    }
                });
            });
        }
        // 在 ImageProcessing 类中添加以下方法

        /// <summary>
        /// 设置当前帧索引（从 MainWindow 同步）
        /// </summary>
        public void SetCurrentFrameIndex(int frameIndex)
        {
            if (isAnimatedImage && animatedFrames != null && frameIndex >= 0 && frameIndex < totalFrameCount)
            {
                currentFrameIndex = frameIndex;
            }
        }

        /// <summary>
        /// 获取当前帧索引
        /// </summary>
        public int GetCurrentFrameIndex()
        {
            return currentFrameIndex;
        }

        /// <summary>
        /// 获取总帧数
        /// </summary>
        public int GetTotalFrameCount()
        {
            return totalFrameCount;
        }

        /// <summary>
        /// 是否为动画图像
        /// </summary>
        public bool IsAnimatedImage()
        {
            return isAnimatedImage;
        }

        /// <summary>
        /// 获取动画帧列表（只读）
        /// </summary>
        public IReadOnlyList<BitmapFrame> GetAnimatedFrames()
        {
            return animatedFrames?.AsReadOnly();
        }
        /// <summary>
        /// 生成SVG缩略图
        /// </summary>
        public void GenerateSVGThumbnails(string vectorPath, int targetWidth, int targetHeight, int angle = 0, bool flip = false, IProgress<string> progress = null)
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
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
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

                LandscapeColorPreviewSrc = bitmapSource;
                byte[] tempPixels;
                ExtractPixels(bitmapSource, out tempPixels);
                LandscapeColorPreviewPixels = tempPixels;
            }

            progress?.Report($"{Languages.Strings.CS_SVGCompleted}");
        }
    }
}*/