using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    public partial class MainWindow : Window
    {
        #region FFmpegForGif

        // 在MainWindow类中添加FFmpeg相关方法和字段

        // 添加临时目录管理
        private string currentGifTempDir = null;
        private readonly object gifTempDirLock = new object();

        /// <summary>
        /// 检查FFmpeg是否可用
        /// </summary>
        private bool IsFFmpegAvailable()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string ffmpegPath = System.IO.Path.Combine(exeDir, "ffmpeg.exe");
            return File.Exists(ffmpegPath);
        }

        /// <summary>
        /// 使用FFmpeg提取GIF/WEBP的所有帧为PNG位图，支持取消机制
        /// </summary>
        private async Task<List<BitmapSource>> ExtractAnimatedFramesWithFFmpegAsync(string imagePath, IProgress<string> progress = null, CancellationToken cancellationToken = default)
        {
            if (!IsFFmpegAvailable())
            {
                throw new InvalidOperationException("未检测到FFmpeg.exe，无法处理动画图像。\nFFmpeg.exe is required for animated images.");
            }

            progress?.Report("正在使用FFmpeg提取动画帧...");

            // 检查取消
            cancellationToken.ThrowIfCancellationRequested();

            // 创建临时目录
            string tempDir;
            lock (gifTempDirLock)
            {
                // 清理旧的临时目录
                if (!string.IsNullOrEmpty(currentGifTempDir) && Directory.Exists(currentGifTempDir))
                {
                    try { Directory.Delete(currentGifTempDir, true); } catch { }
                }

                currentGifTempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ImageToMidiGifFrames", Guid.NewGuid().ToString());
                tempDir = currentGifTempDir;
            }

            try
            {
                Directory.CreateDirectory(tempDir);

                // 使用FFmpeg提取所有帧为PNG格式，强制转换为完整位图
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string ffmpegPath = System.IO.Path.Combine(exeDir, "ffmpeg.exe");

                string args = $"-i \"{imagePath}\" " +
                              $"-vf \"scale=flags=neighbor\" " +  // 使用最近邻插值保持像素完整性
                              $"-pix_fmt rgba " +                 // 强制RGBA格式
                              $"-f image2 " +                     // 输出为独立图像序列
                              $"-vsync 0 " +                      // 关闭帧同步
                              $"-loglevel info " +                // 输出详细信息
                              $"-avoid_negative_ts make_zero " +  // 避免负时间戳
                              $"-fflags +genpts " +               // 生成时间戳
                              $"\"{System.IO.Path.Combine(tempDir, "frame_%05d.png")}\" -y";

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = System.IO.Path.GetTempPath()
                };

                // 限制FFmpeg内存使用
                psi.EnvironmentVariables["FFREPORT"] = "file=nul";
                psi.EnvironmentVariables["FFMPEG_MEMORY_LIMIT"] = "256M";

                progress?.Report("FFmpeg正在处理动画帧...");

                using (var proc = Process.Start(psi))
                {
                    // 使用取消令牌注册进程终止
                    using (cancellationToken.Register(() =>
                    {
                        try
                        {
                            if (!proc.HasExited)
                            {
                                proc.Kill();
                            }
                        }
                        catch { }
                    }))
                    {
                        // 异步读取输出，监控进度
                        var errorTask = Task.Run(async () =>
                        {
                            try
                            {
                                string line;
                                while ((line = await proc.StandardError.ReadLineAsync()) != null)
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                        break;

                                    if (line.Contains("frame=") && progress != null)
                                    {
                                        // 解析帧数进度
                                        var match = System.Text.RegularExpressions.Regex.Match(line, @"frame=\s*(\d+)");
                                        if (match.Success)
                                        {
                                            progress.Report($"FFmpeg处理中...帧 {match.Groups[1].Value}");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"FFmpeg stderr read error: {ex.Message}");
                            }
                        }, cancellationToken);

                        // 等待进程完成，设置3分钟超时
                        const int timeoutMinutes = 3;
                        var processTask = Task.Run(() => proc.WaitForExit(timeoutMinutes * 60 * 1000), cancellationToken);

                        await processTask;

                        if (!proc.HasExited)
                        {
                            progress?.Report("FFmpeg处理超时，正在终止...");
                            try
                            {
                                proc.Kill();
                                proc.WaitForExit(5000);
                            }
                            catch { }
                            throw new TimeoutException($"FFmpeg处理超时（{timeoutMinutes}分钟）");
                        }

                        await errorTask;

                        // 检查取消
                        cancellationToken.ThrowIfCancellationRequested();

                        if (proc.ExitCode != 0)
                        {
                            throw new Exception($"FFmpeg处理失败，退出代码: {proc.ExitCode}");
                        }
                    }
                }

                progress?.Report("正在加载提取的帧...");

                // 加载所有提取的PNG帧
                var frameFiles = Directory.GetFiles(tempDir, "frame_*.png")
                                         .OrderBy(f => f)
                                         .ToArray();

                if (frameFiles.Length == 0)
                {
                    throw new Exception("FFmpeg未能提取到任何帧");
                }

                var frames = new List<BitmapSource>();

                for (int i = 0; i < frameFiles.Length; i++)
                {
                    // 检查取消
                    cancellationToken.ThrowIfCancellationRequested();

                    progress?.Report($"加载帧 {i + 1}/{frameFiles.Length}");

                    try
                    {
                        // 使用内存流避免文件锁定
                        byte[] frameData = File.ReadAllBytes(frameFiles[i]);
                        using (var ms = new MemoryStream(frameData))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = ms;
                            bitmap.EndInit();
                            bitmap.Freeze();

                            // 确保转换为标准BGRA32格式
                            var convertedBitmap = new FormatConvertedBitmap(
                                bitmap,
                                PixelFormats.Bgra32,
                                null,
                                0);
                            convertedBitmap.Freeze();

                            frames.Add(convertedBitmap);
                        }

                        // 立即删除已处理的帧文件，释放磁盘空间
                        try { File.Delete(frameFiles[i]); } catch { }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"加载帧 {frameFiles[i]} 失败: {ex.Message}");
                        // 跳过损坏的帧，继续处理
                    }
                }

                if (frames.Count == 0)
                {
                    throw new Exception("所有帧都加载失败");
                }

                progress?.Report($"成功加载 {frames.Count} 帧");
                return frames;
            }
            finally
            {
                // 清理临时目录
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }

                // 强制垃圾回收
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        /// <summary>
        /// 使用FFmpeg获取动画图像信息
        /// </summary>
        private async Task<(int frameCount, string resolution)> GetAnimatedImageInfoWithFFmpegAsync(string imagePath)
        {
            if (!IsFFmpegAvailable())
            {
                return (0, "需要FFmpeg");
            }

            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string ffprobePath = System.IO.Path.Combine(exeDir, "ffprobe.exe");

                if (!File.Exists(ffprobePath))
                {
                    // 如果没有ffprobe，使用ffmpeg
                    ffprobePath = System.IO.Path.Combine(exeDir, "ffmpeg.exe");
                }

                string args = $"-v error -select_streams v:0 -show_entries stream=width,height,nb_frames -of default=noprint_wrappers=1:nokey=1 \"{imagePath}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    string output = await proc.StandardOutput.ReadToEndAsync();
                    await Task.Run(() => proc.WaitForExit());

                    var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length >= 3)
                    {
                        int width = int.TryParse(lines[0], out width) ? width : 0;
                        int height = int.TryParse(lines[1], out height) ? height : 0;
                        int frameCount = int.TryParse(lines[2], out frameCount) ? frameCount : 1;

                        return (frameCount, $"{width}x{height}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取动画信息失败: {ex.Message}");
            }

            return (1, "未知");
        }


        /// <summary>
        /// 原始的帧加载方法，作为FFmpeg的回退方案
        /// </summary>
        private List<BitmapSource> LoadAnimatedFramesFallback(string imagePath)
        {
            try
            {
                var decoder = BitmapDecoder.Create(
                    new Uri(imagePath),
                    BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.OnLoad);

                var processedFrames = new List<BitmapSource>();

                for (int i = 0; i < decoder.Frames.Count; i++)
                {
                    var frame = decoder.Frames[i];

                    // 强制转换为完整的位图格式
                    var convertedFrame = new FormatConvertedBitmap(
                        frame,
                        PixelFormats.Bgra32,
                        null,
                        0);
                    convertedFrame.Freeze();

                    processedFrames.Add(convertedFrame);
                }

                return processedFrames;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"回退方法加载帧失败: {ex.Message}");
                return new List<BitmapSource>();
            }
        }

        /// <summary>
        /// 清理临时文件的方法
        /// </summary>
        private void CleanupGifTempDirectory()
        {
            lock (gifTempDirLock)
            {
                if (!string.IsNullOrEmpty(currentGifTempDir) && Directory.Exists(currentGifTempDir))
                {
                    try
                    {
                        Directory.Delete(currentGifTempDir, true);
                        currentGifTempDir = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"清理临时目录失败: {ex.Message}");
                    }
                }
            }
        }

        #endregion
    }
}
