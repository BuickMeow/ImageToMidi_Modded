using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    public partial class MainWindow : Window
    {
        #region GifProcessing

        // GIF/WEBP 帧导航相关字段和属性
        private List<BitmapSource> animatedFrames = null;  // 改为 BitmapSource
        private int currentFrameIndex = 0;
        private int totalFrameCount = 0;
        private bool isAnimatedImage = false;

        // 添加取消令牌源来处理异步操作竞态条件
        private CancellationTokenSource loadAnimatedFramesCts = null;
        private readonly object loadAnimatedFramesLock = new object();

        /// <summary>
        /// 检测是否为多帧的GIF或WEBP文件
        /// </summary>
        private bool IsMultiFrameImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return false;

            string ext = System.IO.Path.GetExtension(imagePath).ToLowerInvariant();
            if (ext != ".gif" && ext != ".webp")
                return false;

            try
            {
                var decoder = BitmapDecoder.Create(
                    new Uri(imagePath),
                    BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.None);
                return decoder.Frames.Count > 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 加载动图的所有帧
        /// 修改后的加载动画帧方法，优先使用FFmpeg，支持取消机制
        /// </summary>
        private async void LoadAnimatedFrames(string imagePath)
        {
            // 创建新的取消令牌并取消之前的操作
            CancellationTokenSource currentCts;
            lock (loadAnimatedFramesLock)
            {
                // 取消之前的操作
                loadAnimatedFramesCts?.Cancel();
                loadAnimatedFramesCts?.Dispose();

                // 创建新的取消令牌源
                loadAnimatedFramesCts = new CancellationTokenSource();
                currentCts = loadAnimatedFramesCts;
            }

            var cancellationToken = currentCts.Token;

            try
            {
                // 检查取消
                cancellationToken.ThrowIfCancellationRequested();

                string ext = System.IO.Path.GetExtension(imagePath).ToLowerInvariant();
                bool isAnimated = false;

                // 首先检测是否为多帧图像
                if (ext == ".gif" || ext == ".webp")
                {
                    if (IsFFmpegAvailable())
                    {
                        // 使用FFmpeg检测帧数
                        var info = await GetAnimatedImageInfoWithFFmpegAsync(imagePath);
                        cancellationToken.ThrowIfCancellationRequested();
                        isAnimated = info.frameCount > 1;
                    }
                    else
                    {
                        // 回退到原始方法
                        isAnimated = IsMultiFrameImage(imagePath);
                    }
                }

                if (!isAnimated)
                {
                    // 确保这是最新的操作
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    HideFrameNavigationPanel();
                    return;
                }

                // 显示进度
                var progress = new Progress<string>(msg =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                        saveMidi.Content = msg;
                });

                List<BitmapSource> frames = null;

                if (IsFFmpegAvailable())
                {
                    // 优先使用FFmpeg处理
                    try
                    {
                        frames = await ExtractAnimatedFramesWithFFmpegAsync(imagePath, progress, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // 操作被取消，直接返回
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"FFmpeg处理失败，回退到原始方法: {ex.Message}");

                        // 检查取消
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        MessageBox.Show($"FFmpeg处理失败，将使用内置解码器：{ex.Message}",
                                       "警告", MessageBoxButton.OK, MessageBoxImage.Warning);

                        // 回退到原始方法
                        frames = LoadAnimatedFramesFallback(imagePath);
                    }
                }
                else
                {
                    // 使用原始方法
                    frames = LoadAnimatedFramesFallback(imagePath);
                }

                // 最终检查取消
                cancellationToken.ThrowIfCancellationRequested();

                if (frames == null || frames.Count == 0)
                {
                    HideFrameNavigationPanel();
                    return;
                }

                // 确保这是最新的操作再更新UI
                lock (loadAnimatedFramesLock)
                {
                    if (loadAnimatedFramesCts != currentCts || cancellationToken.IsCancellationRequested)
                    {
                        // 这不是最新的操作，忽略结果
                        return;
                    }
                }

                animatedFrames = frames;
                totalFrameCount = animatedFrames.Count;
                currentFrameIndex = 0;
                isAnimatedImage = totalFrameCount > 1;

                Debug.WriteLine($"成功加载动画: {imagePath}, 帧数: {totalFrameCount}");

                if (isAnimatedImage)
                {
                    ShowFrameNavigationPanel();
                    // 自动显示第一帧
                    UpdateFrameDisplay();
                }
                else
                {
                    HideFrameNavigationPanel();
                }
            }
            catch (OperationCanceledException)
            {
                // 操作被取消，不需要处理
                Debug.WriteLine($"LoadAnimatedFrames 被取消: {imagePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载动画帧失败: {ex.Message}");
                if (!cancellationToken.IsCancellationRequested)
                {
                    MessageBox.Show($"加载动画帧失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    HideFrameNavigationPanel();
                }
            }
        }

        /// <summary>
        /// 显示帧导航面板
        /// </summary>
        private void ShowFrameNavigationPanel()
        {
            frameNavigationPanel.Visibility = Visibility.Visible;
            UpdateFrameCountText();
        }

        /// <summary>
        /// 隐藏帧导航面板
        /// </summary>
        private void HideFrameNavigationPanel()
        {
            frameNavigationPanel.Visibility = Visibility.Collapsed;
            isAnimatedImage = false;
            animatedFrames = null;
            totalFrameCount = 0;
            currentFrameIndex = 0;
        }

        /// <summary>
        /// 更新帧计数显示
        /// </summary>
        private void UpdateFrameCountText()
        {
            frameCountText.Text = $"{currentFrameIndex + 1}/{totalFrameCount}";
        }

        /// <summary>
        /// 更新当前帧显示
        /// </summary>
        private async void UpdateFrameDisplay()
        {
            if (!isAnimatedImage || animatedFrames == null || currentFrameIndex >= animatedFrames.Count)
                return;

            try
            {
                // 获取当前帧
                var currentFrame = animatedFrames[currentFrameIndex];

                // 清理之前的缓存
                landscapeColorPreviewSrc = null;
                landscapeColorPreviewPixels = null;
                portraitColorPreviewSrc = null;
                portraitColorPreviewPixels = null;
                landscapeGrayPreviewSrc = null;
                landscapeGrayPreviewPixels = null;
                portraitGrayPreviewSrc = null;
                portraitGrayPreviewPixels = null;

                // 更新原始图像数据
                originalImageSrc = currentFrame;
                originalImageWidth = currentFrame.PixelWidth;
                originalImageHeight = currentFrame.PixelHeight;

                // 生成缩略图（模拟 GenerateThumbnailsOptimized）
                var landscapeColor = Downsample(currentFrame, null, 256);
                ExtractPixels(landscapeColor, out var landscapeColorPixels);
                var portraitColor = Downsample(currentFrame, 256, null);
                ExtractPixels(portraitColor, out var portraitColorPixels);

                landscapeColorPreviewSrc = landscapeColor;
                landscapeColorPreviewPixels = landscapeColorPixels;
                portraitColorPreviewSrc = portraitColor;
                portraitColorPreviewPixels = portraitColorPixels;

                // 处理灰度（如果需要）
                if (grayScaleCheckBox.IsChecked == true)
                {
                    landscapeGrayPreviewSrc = ToGrayScale(landscapeColorPreviewSrc);
                    ExtractPixels(landscapeGrayPreviewSrc, out landscapeGrayPreviewPixels);
                    portraitGrayPreviewSrc = ToGrayScale(portraitColorPreviewSrc);
                    ExtractPixels(portraitGrayPreviewSrc, out portraitGrayPreviewPixels);
                }

                // 更新显示的图像数据
                await UpdateOpenedImageByAngleAndFlip();

                // 更新ZoomableImage显示
                var skBitmap = originalImageSrc.ToSKBitmap();
                openedImage.SetSKBitmap(skBitmap);

                // 更新帧计数显示
                UpdateFrameCountText();

                // 重新生成调色板和预览
                await ReloadAutoPalette();

                // 更新批处理列表中的帧数信息
                UpdateBatchFileFrameCount();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新帧显示失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新批处理列表中的帧数信息
        /// </summary>
        private void UpdateBatchFileFrameCount()
        {
            if (BatchFileList.Count > 0)
            {
                var lastItem = BatchFileList.LastOrDefault();
                if (lastItem != null && lastItem.FullPath == openedImagePath)
                {
                    lastItem.FrameCount = totalFrameCount;
                }
            }
        }

        /// <summary>
        /// 上一帧按钮点击事件
        /// </summary>
        private void PrevFrameButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isAnimatedImage || animatedFrames == null)
                return;

            currentFrameIndex--;
            if (currentFrameIndex < 0)
                currentFrameIndex = totalFrameCount - 1; // 循环到最后一帧

            UpdateFrameDisplay();
        }

        /// <summary>
        /// 下一帧按钮点击事件
        /// </summary>
        private void NextFrameButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isAnimatedImage || animatedFrames == null)
                return;

            currentFrameIndex++;
            if (currentFrameIndex >= totalFrameCount)
                currentFrameIndex = 0; // 循环到第一帧

            UpdateFrameDisplay();
        }

        #endregion
    }
}