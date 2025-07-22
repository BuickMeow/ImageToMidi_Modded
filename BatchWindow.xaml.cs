using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;


namespace ImageToMidi
{

    public class BatchFileItem : INotifyPropertyChanged
    {
        private int _index;
        public int Index
        {
            get => _index;
            set
            {
                if (_index != value)
                {
                    _index = value;
                    OnPropertyChanged(nameof(Index));
                }
            }
        }
        public string FileName { get; set; }
        public string Format { get; set; }
        public int FrameCount { get; set; }
        public string Resolution { get; set; }
        public string FullPath { get; set; }
        public bool IsFolder { get; set; }
        public bool IsBack { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    public partial class BatchWindow : Window
    {


        private bool allowClose = false;
        private double _initWidth;
        private double _initHeight;
        private Stack<string> folderStack = new Stack<string>(); // 路径栈
        private List<BatchFileItem> rootItems = new List<BatchFileItem>(); // 主列表缓存

        // 1. 扩展支持的文件类型
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tiff" };
        private static readonly string[] DynamicImageExtensions = { ".gif", ".mp4", ".mov", ".avi", ".mkv", ".webm" };
        private static readonly string[] VectorExtensions = { ".svg", ".eps", ".ai", ".pdf" }; // 新增
        private static readonly string[] AllSupportedExtensions = ImageExtensions.Concat(DynamicImageExtensions).Concat(VectorExtensions).ToArray(); // 修改

        string tempDir = Path.Combine(Path.GetTempPath(), "ImageToMidiFrames", Guid.NewGuid().ToString());



        public ObservableCollection<BatchFileItem> FileList { get; set; }

        public BatchWindow(ObservableCollection<BatchFileItem> fileList)
        {
            InitializeComponent();
            FileList = fileList;
            BatchDataGrid.ItemsSource = FileList;
            _initWidth = this.Width;
            _initHeight = this.Height;

            BatchDataGrid.PreviewKeyDown += BatchDataGrid_PreviewKeyDown;
            SetExportButtonsEnabled(true);
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!allowClose)
            {
                e.Cancel = true;
                this.Hide();
                // 隐藏后再恢复宽高
                this.Width = _initWidth;
                this.Height = _initHeight;
            }
            else
            {
                base.OnClosing(e);
            }
        }

        public void ForceClose()
        {
            allowClose = true;
            this.Close();
        }
        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                Close();
                return;
            }
            double start = this.ActualWidth;
            this.Width = start;
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
            // 先隐藏，再恢复宽高，避免闪烁
            this.Hide();
            this.Width = _initWidth;
            this.Height = _initHeight;
        }
        private void MinimiseButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Minimized;
                return;
            }
            double start = batchWindow.ActualHeight;
            double startpos = Top;
            batchWindow.Height = start;
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
            batchWindow.Height = double.NaN;
            Height = start;
            Top = startpos;
        }
        // 新增：通用的文件批量导入方法
        private async Task ImportFilesAsync(IEnumerable<string> files)
        {
            bool ffmpegChecked = false;
            bool ffmpegAvailable = false;
            bool ghostscriptChecked = false;
            bool ghostscriptAvailable = false;

            foreach (var file in files)
            {
                try
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    string format = ext.TrimStart('.').ToUpperInvariant();
                    int frameCount = 1;
                    string resolution = "";

                    if (DynamicImageExtensions.Contains(ext))
                    {
                        // 检查FFmpeg
                        if (!ffmpegChecked)
                        {
                            ffmpegAvailable = IsFFmpegAvailable();
                            ffmpegChecked = true;
                        }
                        if (!ffmpegAvailable)
                        {
                            MessageBox.Show("未检测到FFmpeg.exe，无法读取动态图/视频文件。\nFFmpeg.exe is required.", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            continue;
                        }

                        // 获取帧数和分辨率
                        var videoInfo = await GetVideoInfoAsync(file);
                        frameCount = videoInfo.frameCount;
                        resolution = videoInfo.resolution;

                        FileList.Add(new BatchFileItem
                        {
                            FileName = Path.GetFileName(file),
                            Format = format,
                            FrameCount = frameCount,
                            Resolution = resolution,
                            FullPath = file
                        });
                    }
                    else if (ImageExtensions.Contains(ext))
                    {
                        var bitmap = new BitmapImage(new Uri(file));
                        int width = bitmap.PixelWidth;
                        int height = bitmap.PixelHeight;
                        resolution = $"{width}x{height}";

                        FileList.Add(new BatchFileItem
                        {
                            FileName = Path.GetFileName(file),
                            Format = format,
                            FrameCount = 1,
                            Resolution = resolution,
                            FullPath = file
                        });
                    }
                    else if (VectorExtensions.Contains(ext)) // 新增矢量图处理
                    {
                        // 检查SVG支持或Ghostscript
                        if ((ext == ".eps" || ext == ".ai" || ext == ".pdf"))
                        {
                            if (!ghostscriptChecked)
                            {
                                ghostscriptAvailable = IsGhostscriptAvailable();
                                ghostscriptChecked = true;
                            }
                            if (!ghostscriptAvailable)
                            {
                                MessageBox.Show($"未检测到Ghostscript，无法读取{format}文件。\n请前往 www.ghostscript.com 下载并安装 Ghostscript。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                continue;
                            }
                        }

                        // 获取矢量图信息
                        var vectorInfo = await GetVectorInfoAsync(file);
                        frameCount = vectorInfo.frameCount;
                        resolution = vectorInfo.resolution;

                        FileList.Add(new BatchFileItem
                        {
                            FileName = Path.GetFileName(file),
                            Format = format,
                            FrameCount = frameCount,
                            Resolution = resolution,
                            FullPath = file
                        });
                    }
                    else
                    {
                        MessageBox.Show($"不支持的文件格式: {file}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"无法读取文件: {file}\n{ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            ReIndexFileList();
        }
        // 新增：获取视频帧数和分辨率
        private static async Task<(int frameCount, string resolution)> GetVideoInfoAsync(string videoPath)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string ffprobePath = Path.Combine(exeDir, "ffprobe.exe");
            if (!File.Exists(ffprobePath))
                throw new FileNotFoundException("未找到ffprobe.exe\nffprobe.exe is required.");

            int frameCount = 0;
            string resolution = "";

            // 获取分辨率和帧数
            string args = $"-v error -select_streams v:0 -show_entries stream=width,height,nb_frames -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"";
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
                    resolution = $"{lines[0]}x{lines[1]}";
                    int.TryParse(lines[2], out frameCount);
                }
            }
            return (frameCount, resolution);
        }
        private async void AddImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = $"{Languages.Strings.B_FileFormat}|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.gif;*.mp4;*.mov;*.avi;*.mkv;*.webm;*.svg;*.eps;*.ai;*.pdf|{Languages.Strings.B_AllFiles}|*.*", // 修改
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                await ImportFilesAsync(dlg.FileNames);
                if (dlg.FileNames.Length > 0)
                {
                    Properties.Settings.Default.LastImportFolder = Path.GetDirectoryName(dlg.FileNames[0]);
                    Properties.Settings.Default.Save();
                }
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new VistaFolderBrowserDialog();
            if (dlg.ShowDialog() == true)
            {
                string folderPath = dlg.SelectedPath;

                // 只添加文件夹项，不递归导入文件
                FileList.Add(new BatchFileItem
                {
                    FileName = Path.GetFileName(folderPath),
                    Format = $"{Languages.Strings.B_FolderFormat}",
                    FrameCount = GetImageCountRecursive(folderPath),
                    Resolution = "",
                    FullPath = folderPath,
                    IsFolder = true
                });

                Properties.Settings.Default.LastImportFolder = folderPath;
                Properties.Settings.Default.Save();
                rootItems = FileList.ToList();
                ReIndexFileList();
            }
        }
        private int GetImageCountRecursive(string folder)
        {
            int count = 0;
            try
            {
                count += Directory.GetFiles(folder)
                    .Count(f => AllSupportedExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()));
                foreach (var sub in Directory.GetDirectories(folder))
                {
                    count += GetImageCountRecursive(sub);
                }
            }
            catch { }
            return count;
        }

        private void BatchDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (BatchDataGrid.SelectedItem is BatchFileItem item)
            {
                if (item.IsBack)
                {
                    // 回到上一级
                    if (folderStack.Count == 0)
                    {
                        FileList.Clear();
                        foreach (var it in rootItems)
                            FileList.Add(it);
                    }
                    else
                    {
                        folderStack.Pop();
                        ShowFolder(folderStack.Count > 0 ? folderStack.Peek() : null);
                    }
                    return;
                }
                if (item.IsFolder)
                {
                    folderStack.Push(item.FullPath);
                    ShowFolder(item.FullPath);
                    return;
                }

                // 普通文件项
                var mainWin = Application.Current.MainWindow as MainWindow;
                if (mainWin != null)
                {
                    string filePath = item.FullPath ?? item.FileName;
                    var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

                    // 扩展支持的格式
                    if (ImageExtensions.Contains(ext) || VectorExtensions.Contains(ext))
                    {
                        // 调用主窗口的图片加载方法
                        mainWin.LoadImageForPreview(filePath);
                        // 可选：自动切换到主窗口
                        mainWin.Activate();
                    }
                    else
                    {
                        MessageBox.Show("仅支持图片和矢量图格式: png, jpg, jpeg, bmp, tiff, svg, eps, ai, pdf\nOther Formats are not supported.", "不支持的格式", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void ShowFolder(string folder)
        {
            FileList.Clear();

            // 只有在子目录时才显示"回到上一级"
            if (!string.IsNullOrEmpty(folder))
            {
                FileList.Add(new BatchFileItem
                {
                    Index = 0,
                    FileName = $"{Languages.Strings.B_Back}",
                    Format = "",
                    FrameCount = 0,
                    Resolution = "",
                    FullPath = "",
                    IsBack = true
                });
            }

            if (string.IsNullOrEmpty(folder))
            {
                // 回到主列表
                foreach (var it in rootItems)
                    FileList.Add(it);
            }
            else
            {
                // 文件夹下的子文件夹
                var dirs = Directory.GetDirectories(folder);
                foreach (var dir in dirs)
                {
                    int count = GetImageCountRecursive(dir);
                    FileList.Add(new BatchFileItem
                    {
                        FileName = System.IO.Path.GetFileName(dir),
                        Format = $"{Languages.Strings.B_FolderFormat}",
                        FrameCount = count,
                        Resolution = "",
                        FullPath = dir,
                        IsFolder = true
                    });
                }

                // 文件夹下的图片、视频和矢量图
                var files = Directory.GetFiles(folder)
                    .Where(f => AllSupportedExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()));
                foreach (var file in files)
                {
                    try
                    {
                        var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                        string format = ext.TrimStart('.').ToUpperInvariant();

                        if (ImageExtensions.Contains(ext))
                        {
                            var bitmap = new System.Windows.Media.Imaging.BitmapImage(new Uri(file));
                            int width = bitmap.PixelWidth;
                            int height = bitmap.PixelHeight;
                            string resolution = $"{width}x{height}";
                            FileList.Add(new BatchFileItem
                            {
                                FileName = System.IO.Path.GetFileName(file),
                                Format = format,
                                FrameCount = 1,
                                Resolution = resolution,
                                FullPath = file
                            });
                        }
                        else if (DynamicImageExtensions.Contains(ext))
                        {
                            var videoInfo = GetVideoInfoAsync(file).GetAwaiter().GetResult();
                            FileList.Add(new BatchFileItem
                            {
                                FileName = System.IO.Path.GetFileName(file),
                                Format = format,
                                FrameCount = videoInfo.frameCount,
                                Resolution = videoInfo.resolution,
                                FullPath = file
                            });
                        }
                        else if (VectorExtensions.Contains(ext)) // 新增矢量图处理
                        {
                            var vectorInfo = GetVectorInfoAsync(file).GetAwaiter().GetResult();
                            FileList.Add(new BatchFileItem
                            {
                                FileName = System.IO.Path.GetFileName(file),
                                Format = format,
                                FrameCount = vectorInfo.frameCount,
                                Resolution = vectorInfo.resolution,
                                FullPath = file
                            });
                        }
                    }
                    catch { }
                }
            }
            ReIndexFileList();
        }
        private void RemoveImage_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedItems();
        }
        private void RemoveSelectedItems()
        {
            var selectedItems = BatchDataGrid.SelectedItems.Cast<BatchFileItem>().Where(i => !i.IsBack).ToList();
            if (selectedItems.Count > 0)
            {
                int idx = BatchDataGrid.SelectedIndex;
                foreach (var item in selectedItems)
                {
                    FileList.Remove(item);
                }
                ReIndexFileList();

                // 自动选中下一个
                if (FileList.Count > 0)
                {
                    if (idx >= FileList.Count)
                        idx = FileList.Count - 1;
                    BatchDataGrid.SelectedIndex = idx;
                }
                // 保证DataGrid有焦点，方便连续Delete
                BatchDataGrid.Focus();
                if (BatchDataGrid.SelectedIndex >= 0)
                {
                    var row = BatchDataGrid.ItemContainerGenerator.ContainerFromIndex(BatchDataGrid.SelectedIndex) as System.Windows.Controls.DataGridRow;
                    if (row != null)
                        row.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
                }
            }
        }
        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            var selected = BatchDataGrid.SelectedItems.Cast<BatchFileItem>()
                .Where(i => !i.IsBack)
                .ToList();
            if (selected.Count == 0) return;

            int startIdx = FileList[0].IsBack ? 1 : 0;
            // 从前往后遍历
            for (int i = startIdx + 1; i < FileList.Count; i++)
            {
                var curr = FileList[i];
                var prev = FileList[i - 1];
                if (selected.Contains(curr) && !selected.Contains(prev))
                {
                    FileList.Move(i, i - 1);
                    // 交换后i-1是curr，i是prev，i++跳过刚刚交换过的prev
                }
            }
            ReIndexFileList();
            // 重新选中
            BatchDataGrid.SelectedItems.Clear();
            foreach (var item in selected)
                BatchDataGrid.SelectedItems.Add(item);
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            var selected = BatchDataGrid.SelectedItems.Cast<BatchFileItem>()
                .Where(i => !i.IsBack)
                .ToList();
            if (selected.Count == 0) return;

            int endIdx = FileList.Count - 1;
            if (FileList[endIdx].IsBack) endIdx--;

            // 从后往前遍历
            for (int i = endIdx - 1; i >= 0; i--)
            {
                var curr = FileList[i];
                var next = FileList[i + 1];
                if (selected.Contains(curr) && !selected.Contains(next))
                {
                    FileList.Move(i, i + 1);
                    // 交换后i+1是curr，i是next
                }
            }
            ReIndexFileList();
            // 重新选中
            BatchDataGrid.SelectedItems.Clear();
            foreach (var item in selected)
                BatchDataGrid.SelectedItems.Add(item);
        }

        private void MoveTop_Click(object sender, RoutedEventArgs e)
        {
            var selected = BatchDataGrid.SelectedItems.Cast<BatchFileItem>()
                .Where(i => !i.IsBack)
                .OrderBy(i => FileList.IndexOf(i))
                .ToList();
            if (selected.Count == 0) return;

            int startIdx = FileList[0].IsBack ? 1 : 0;
            foreach (var item in selected)
                FileList.Remove(item);
            for (int i = 0; i < selected.Count; i++)
                FileList.Insert(startIdx + i, selected[i]);
            ReIndexFileList();
            BatchDataGrid.SelectedItems.Clear();
            foreach (var item in selected)
                BatchDataGrid.SelectedItems.Add(item);
        }

        private void MoveEnd_Click(object sender, RoutedEventArgs e)
        {
            var selected = BatchDataGrid.SelectedItems.Cast<BatchFileItem>()
                .Where(i => !i.IsBack)
                .OrderBy(i => FileList.IndexOf(i))
                .ToList();
            if (selected.Count == 0) return;

            int endIdx = FileList.Count - 1;
            if (FileList[endIdx].IsBack) endIdx--;
            foreach (var item in selected)
                FileList.Remove(item);
            for (int i = 0; i < selected.Count; i++)
                FileList.Insert(endIdx - selected.Count + 1 + i, selected[i]);
            ReIndexFileList();
            BatchDataGrid.SelectedItems.Clear();
            foreach (var item in selected)
                BatchDataGrid.SelectedItems.Add(item);
        }

        // 重新编号方法
        private void ReIndexFileList()
        {
            int idx = 1;
            foreach (var item in FileList)
            {
                if (!item.IsBack)
                    item.Index = idx++;
                else
                    item.Index = 0;
            }
            SetExportButtonsEnabled(true);
        }
        private void BatchDataGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Delete 删除
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                RemoveSelectedItems();
                e.Handled = true;
            }
        }
        private List<(string filePath, string relativePath)> GetAllImageFiles(IEnumerable<BatchFileItem> items)
        {
            var result = new List<(string, string)>();
            foreach (var item in items)
            {
                if (item.IsFolder && !string.IsNullOrEmpty(item.FullPath))
                {
                    var baseFolder = item.FullPath;
                    foreach (var file in Directory.GetFiles(baseFolder, "*.*", SearchOption.AllDirectories)
                        .Where(f => AllSupportedExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))) // 使用更新后的AllSupportedExtensions
                    {
                        var rel = GetRelativePath(baseFolder, file);
                        result.Add((file, Path.Combine(item.FileName, rel)));
                    }
                }
                else if (!item.IsBack && !string.IsNullOrEmpty(item.FullPath))
                {
                    // 支持所有格式的文件
                    result.Add((item.FullPath, item.FileName));
                }
            }
            return result;
        }
        private static string GetRelativePath(string basePath, string fullPath)
        {
            // 兼容不同分隔符
            basePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            fullPath = Path.GetFullPath(fullPath);

            Uri baseUri = new Uri(basePath, UriKind.Absolute);
            Uri fileUri = new Uri(fullPath, UriKind.Absolute);

            Uri relativeUri = baseUri.MakeRelativeUri(fileUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            // Uri返回的是/分隔，转为本地分隔符
            return relativePath.Replace('/', Path.DirectorySeparatorChar);
        }
        // 在BatchWindow类中添加辅助方法
        private void SetExportButtonsEnabled(bool enabled)
        {
            // 如果没有可导出的文件，则始终禁用按钮
            var hasFiles = GetAllImageFiles(FileList.Where(f => !f.IsBack)).Count > 0;
            exportAllMidi.IsEnabled = enabled && hasFiles;
            mergeMidi.IsEnabled = enabled && hasFiles;
        }

        // 修改ExportAllMidi_Click
        private async void ExportAllMidi_Click(object sender, RoutedEventArgs e)
        {
            SetExportButtonsEnabled(false); // 禁用按钮
            try
            {
                var mainWin = Application.Current.MainWindow as MainWindow;
                if (mainWin == null)
                {
                    MessageBox.Show("主窗口未找到，无法导出MIDI。");
                    return;
                }

                var allFiles = GetAllImageFiles(FileList.Where(f => !f.IsBack));
                if (allFiles.Count == 0)
                {
                    SetExportButtonsEnabled(false); // 没有文件时禁用按钮
                    MessageBox.Show("没有可导出的文件。\nNo file to export.");
                    return;
                }
                var dlg = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
                if (dlg.ShowDialog() != true)
                    return;
                string exportFolder = dlg.SelectedPath;
                Properties.Settings.Default.LastExportFolder = exportFolder;
                Properties.Settings.Default.Save();

                int total = allFiles.Count;
                int success = 0, fail = 0;
                var failList = new List<string>();

                // 进度回调
                IProgress<string> progress = new Progress<string>(msg =>
                {
                    mergeMidi.Content = msg;
                });

                for (int i = 0; i < total; i++)
                {
                    var (filePath, relativePath) = allFiles[i];
                    try
                    {
                        string midiRelPath = Path.ChangeExtension(relativePath, ".mid");
                        string midiFullPath = Path.Combine(exportFolder, midiRelPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(midiFullPath));

                        exportAllMidi.Content = $"[{i + 1}/{total}] 处理: {Path.GetFileName(filePath)}";

                        if (DynamicImageExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant()))
                        {
                            var videoInfo = await GetVideoInfoAsync(filePath);
                            int frameCount = videoInfo.frameCount;
                            if (frameCount <= 0)
                            {
                                MessageBox.Show($"视频帧数为0，无法导出: {filePath}\nVideo frame count is 0, cannot export.");
                                continue;
                            }

                            int sampleInterval = 1;
                            string tempDir = Path.Combine(Path.GetTempPath(), "ImageToMidiFrames", Guid.NewGuid().ToString());

                            // 1. 批量采样导出帧，带进度
                            await ExtractSampledFramesAsync(filePath, tempDir, sampleInterval, progress);

                            // 2. 遍历采样帧图片
                            var frameFiles = Directory.GetFiles(tempDir, "frame_*.png").OrderBy(f => f).ToArray();
                            for (int frameIndex = 0; frameIndex < frameFiles.Length; frameIndex++)
                            {
                                string framePath = frameFiles[frameIndex];
                                string midiName = $"{Path.GetFileNameWithoutExtension(filePath)}_frame_{frameIndex * sampleInterval:D4}.mid";
                                string midiPath = Path.Combine(Path.GetDirectoryName(midiFullPath), midiName);

                                exportAllMidi.Content = $"[{i + 1}/{total}] {Languages.Strings.B_ConvertingMidi} {Path.GetFileName(framePath)} ({frameIndex + 1}/{frameFiles.Length})";

                                await mainWin.BatchExportMidiAsync(
                                    new[] { new BatchFileItem { FullPath = framePath, FileName = $"frame_{frameIndex * sampleInterval}.png", Index = frameIndex + 1 } },
                                    Path.GetDirectoryName(midiPath),
                                    null
                                );

                                try { File.Delete(framePath); } catch { }
                            }

                            try { Directory.Delete(tempDir, true); } catch { }
                        }
                        else
                        {
                            exportAllMidi.Content = $"[{i + 1}/{total}] {Languages.Strings.B_ConvertingMidi} {Path.GetFileName(filePath)}";
                            await mainWin.BatchExportMidiAsync(
                                new[] { new BatchFileItem { FullPath = filePath, FileName = Path.GetFileName(filePath), Index = i + 1 } },
                                Path.GetDirectoryName(midiFullPath),
                                null
                            );
                        }
                        success++;
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        failList.Add($"{filePath}：{ex.Message}");
                        MessageBox.Show($"导出失败: {filePath}\n{ex}");
                    }
                }

                exportAllMidi.Content = $"批量导出MIDI完成，成功{success}，失败{fail}";
            }
            finally
            {
                SetExportButtonsEnabled(true); // 恢复按钮
            }
        }

        // 修改MergeMidi_Click
        private async void MergeMidi_Click(object sender, RoutedEventArgs e)
        {
            SetExportButtonsEnabled(false); // 禁用按钮
            try
            {
                var mainWin = Application.Current.MainWindow as MainWindow;
                if (mainWin == null)
                {
                    MessageBox.Show("主窗口未找到，无法导出MIDI。");
                    return;
                }
                var allFiles = GetAllImageFiles(FileList.Where(f => !f.IsBack));
                if (allFiles.Count == 0)
                {
                    SetExportButtonsEnabled(false); // 没有文件时禁用按钮
                    MessageBox.Show("没有可导出的文件。\nNo file to export.");
                    return;
                }
                SaveFileDialog save = new SaveFileDialog();
                save.Filter = $"{Languages.Strings.CS_MidiFile} (*.mid)|*.mid";
                save.FileName = "Merged.mid"; // 设置默认文件名
                if (!(bool)save.ShowDialog()) return;
                string outputMidiPath = save.FileName;

                // 进度回调
                IProgress<string> progress = new Progress<string>(msg =>
                {
                    mergeMidi.Content = msg;
                });

                var concatItems = new List<BatchFileItem>();
                int sampleInterval = 1; // 可根据需要调整采样间隔
                int total = allFiles.Count;
                int current = 0;

                foreach (var (filePath, relativePath) in allFiles)
                {
                    current++;
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (DynamicImageExtensions.Contains(ext))
                    {
                        var videoInfo = await GetVideoInfoAsync(filePath);
                        int frameCount = videoInfo.frameCount;
                        if (frameCount <= 0)
                        {
                            MessageBox.Show($"视频帧数为0，无法导出: {filePath}");
                            continue;
                        }
                        string tempDir = Path.Combine(Path.GetTempPath(), "ImageToMidiFrames", Guid.NewGuid().ToString());
                        progress.Report($"[{current}/{total}] {Languages.Strings.B_ExtractingVideoFrames} {Path.GetFileName(filePath)}");
                        await ExtractSampledFramesAsync(filePath, tempDir, sampleInterval, progress);

                        var frameFiles = Directory.GetFiles(tempDir, "frame_*.png").OrderBy(f => f).ToArray();
                        for (int frameIndex = 0; frameIndex < frameFiles.Length; frameIndex++)
                        {
                            string framePath = frameFiles[frameIndex];
                            string frameName = $"{Path.GetFileNameWithoutExtension(filePath)}_frame_{frameIndex * sampleInterval:D4}.png";
                            concatItems.Add(new BatchFileItem
                            {
                                FullPath = framePath,
                                FileName = frameName,
                                Index = concatItems.Count + 1
                            });
                        }
                    }
                    else
                    {
                        progress.Report($"[{current}/{total}] {Languages.Strings.B_AddImage} {Path.GetFileName(filePath)}");
                        concatItems.Add(new BatchFileItem
                        {
                            FullPath = filePath,
                            FileName = relativePath,
                            Index = concatItems.Count + 1
                        });
                    }
                }

                // 合并MIDI时进度
                var mergeProgress = new Progress<(int, int, string)>(tuple =>
                {
                    mergeMidi.Content = $"[{tuple.Item1}/{tuple.Item2}] {tuple.Item3}";
                });

                await mainWin.BatchExportMidiConcatAsync(
                    concatItems,
                    outputMidiPath,
                    mergeProgress
                );
                mergeMidi.Content = $"{Languages.Strings.B_MergeCompleted}";

                // 清理所有临时帧目录
                foreach (var item in concatItems)
                {
                    if (item.FullPath != null && item.FullPath.Contains("ImageToMidiFrames"))
                    {
                        try { File.Delete(item.FullPath); } catch { }
                    }
                }
                // 递归删除空目录
                var tempRoot = Path.Combine(Path.GetTempPath(), "ImageToMidiFrames");
                try
                {
                    if (Directory.Exists(tempRoot))
                        Directory.Delete(tempRoot, true);
                }
                catch { }
            }
            finally
            {
                SetExportButtonsEnabled(true); // 恢复按钮
            }
        }
        private static bool IsFFmpegAvailable()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string ffmpegPath = Path.Combine(exeDir, "ffmpeg.exe");
            return File.Exists(ffmpegPath);
        }

        private async Task ExtractSampledFramesAsync(
    string videoPath, string outputDir, int sampleInterval, IProgress<string> progress = null)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string ffmpegPath = Path.Combine(exeDir, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
                throw new FileNotFoundException("未找到FFmpeg.exe");

            Directory.CreateDirectory(outputDir);
            progress?.Report($"{Languages.Strings.B_StartingFFmpeg}");

            // 添加内存优化参数
            string args = $"-i \"{videoPath}\" " +
                          $"-vf \"select=not(mod(n\\,{sampleInterval}))\" " +
                          $"-vsync 0 " +
                          $"-f image2 " +
                          $"-pix_fmt rgba " +
                          $"-threads 1 " +                           // 限制线程数
                          $"-loglevel error " +                      // 减少日志输出
                          $"-avoid_negative_ts make_zero " +         // 避免负时间戳
                          $"-fflags +genpts " +                      // 生成时间戳
                          $"-movflags +faststart " +                 // 快速启动
                          $"\"{Path.Combine(outputDir, "frame_%05d.png")}\" -y";

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                // 限制进程内存使用
                WorkingDirectory = Path.GetTempPath()
            };

            // 添加环境变量来限制 FFmpeg 内存使用
            psi.EnvironmentVariables["FFREPORT"] = "file=nul";  // 禁用报告文件

            using (var proc = Process.Start(psi))
            {
                // 异步读取错误输出，避免缓冲区溢出
                var errorTask = Task.Run(async () =>
                {
                    try
                    {
                        string line;
                        while ((line = await proc.StandardError.ReadLineAsync()) != null)
                        {
                            // 只报告关键进度信息，减少UI更新频率
                            if (line.Contains("frame=") || line.Contains("time="))
                            {
                                progress?.Report(line);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // 忽略读取错误，但记录日志
                        System.Diagnostics.Debug.WriteLine($"FFmpeg stderr read error: {ex.Message}");
                    }
                });

                // 异步读取标准输出
                var outputTask = Task.Run(async () =>
                {
                    try
                    {
                        string line;
                        while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
                        {
                            // 处理标准输出
                            System.Diagnostics.Debug.WriteLine($"FFmpeg stdout: {line}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"FFmpeg stdout read error: {ex.Message}");
                    }
                });

                // 等待进程完成，但设置超时
                const int timeoutMinutes = 3000; // 3000分钟超时
                var processTask = Task.Run(() => proc.WaitForExit(timeoutMinutes * 60 * 1000));

                try
                {
                    await processTask;

                    if (!proc.HasExited)
                    {
                        progress?.Report("FFmpeg 处理超时，正在终止进程...");
                        try
                        {
                            proc.Kill();
                            proc.WaitForExit(5000); // 等待5秒钟清理
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Kill FFmpeg process error: {ex.Message}");
                        }
                        throw new TimeoutException($"视频处理超时（{timeoutMinutes}分钟）");
                    }

                    // 等待输出读取完成
                    await Task.WhenAll(errorTask, outputTask);

                    if (proc.ExitCode != 0)
                    {
                        throw new Exception($"FFmpeg 处理失败，退出代码: {proc.ExitCode}");
                    }
                }
                finally
                {
                    // 确保进程被正确清理
                    try
                    {
                        if (!proc.HasExited)
                        {
                            proc.Kill();
                            proc.WaitForExit(1000);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Process cleanup error: {ex.Message}");
                    }
                }
            }

            progress?.Report($"{Languages.Strings.B_FFmpegCompleted}");

            // 强制垃圾回收，释放内存
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        // 新增：检查Ghostscript是否可用
        private static bool IsGhostscriptAvailable()
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

        // 新增：获取矢量图信息
        private static async Task<(int frameCount, string resolution)> GetVectorInfoAsync(string vectorPath)
        {
            string ext = Path.GetExtension(vectorPath).ToLowerInvariant();

            if (ext == ".svg")
            {
                try
                {
                    var svgDocument = Svg.SvgDocument.Open(vectorPath);
                    if (svgDocument != null)
                    {
                        var viewBox = svgDocument.ViewBox;
                        int width = (int)(viewBox.Width > 0 ? viewBox.Width : (svgDocument.Width.Value > 0 ? svgDocument.Width.Value : 100));
                        int height = (int)(viewBox.Height > 0 ? viewBox.Height : (svgDocument.Height.Value > 0 ? svgDocument.Height.Value : 100));
                        return (1, $"{width}x{height}");
                    }
                }
                catch
                {
                    return (1, "未知");
                }
            }
            else if (ext == ".eps" || ext == ".ai" || ext == ".pdf")
            {
                if (!IsGhostscriptAvailable())
                {
                    return (1, "需要Ghostscript");
                }

                try
                {
                    // 使用Ghostscript获取文档信息
                    string args = $"-dNODISPLAY -dBATCH -dNOPAUSE -q -c \"({vectorPath}) (r) file runpdfbegin pdfpagecount = quit\"";
                    var psi = new ProcessStartInfo
                    {
                        FileName = "gswin64c.exe",
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

                        // 简单解析，实际可能需要更复杂的逻辑
                        return (1, "矢量图");
                    }
                }
                catch
                {
                    return (1, "解析失败");
                }
            }

            return (1, "未知");
        }
        private void Info_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "您可在主页面进行参数设置\n" +
                "You can set parameters on the main window\n\n" +
                "如果需要导入gif或视频文件的话，需要FFmpeg.exe与FFprobe.exe\n" +
                "To import GIF or video files, FFmpeg.exe and FFprobe.exe are required\n\n" +
                "BPM设置参考：\n" +
                "BPM Reference:\n" +
                "BPM=设定高度*每像素时值*视频帧率*60秒/MIDI分辨率\n" +
                "BPM = Set Height * Gate Per Pixel * Video Frame Rate * 60 seconds / MIDI Resolution\n" +
                "例如: 384px * 10Gate * 30fps * 60s / 960ppq = 7200BPM\n" +
                "Example: 384px * 10Gate * 30fps * 60s / 960ppq = 7200BPM\n" +
                "BPM上限为60,000,000\n" +
                "Maximum BPM is 60,000,000",
                "参数设置技巧 | Parameter Setting Tips",
                MessageBoxButton.OK, MessageBoxImage.Information
            );
        }
    }
}