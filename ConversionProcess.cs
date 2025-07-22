using MIDIModificationFramework;
using MIDIModificationFramework.MIDI_Events;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    class ConversionProcess
    {
        BitmapPalette Palette;
        byte[] imageData;
        int imageStride;
        private volatile bool cancelled = false;
        private readonly object cancellationLock = new object();
        private Task currentTask;
        private CancellationTokenSource cancellationTokenSource;

        int maxNoteLength;
        bool measureFromStart;
        bool useMaxNoteLength = false;

        public bool RandomColors = false;
        public int RandomColorSeed = 0;

        int startKey;
        int endKey;

        byte[] resizedImage;

        public int NoteCount { get; private set; }

        public Bitmap Image { get; private set; }

        public int EffectiveWidth { get; set; }

        private readonly FastList<MIDIEvent>[] EventBuffers;

        private ResizeAlgorithm resizeAlgorithm = ResizeAlgorithm.AreaResampling;
        private List<int> keyList = null;
        private bool useKeyList = false;
        private bool fixedWidth = false;
        private bool whiteKeyClipped = false;
        private bool blackKeyClipped = false;
        private bool whiteKeyFixed = false;
        private bool blackKeyFixed = false;

        private int targetHeight;
        private int[] noteCountPerColor;

        private ImageToMidi.GetColorID.PaletteLabCache paletteLabCache;

        // 新增：完成状态标志
        public bool IsCompleted { get; private set; } = false;

        // 新增：保护锁，确保不会被中途取消
        private bool isProtected = false;
        private readonly object protectionLock = new object();

        public ConversionProcess(
    BitmapPalette palette,
    byte[] imageData,
    int imgStride,
    int startKey,
    int endKey,
    bool measureFromStart,
    int maxNoteLength,
    int targetHeight,
    ResizeAlgorithm resizeAlgorithm,
    List<int> keyList,
    bool whiteKeyFixed = false,
    bool blackKeyFixed = false,
    bool whiteKeyClipped = false,
    bool blackKeyClipped = false,
            ColorIdMethod colorIdMethod = ColorIdMethod.RGB)
        {
            if (palette == null)
                throw new ArgumentNullException(nameof(palette), "Palette 不能为空");
            if (palette.Colors == null)
                throw new ArgumentNullException(nameof(palette.Colors), "Palette.Colors 不能为空");
            if (palette.Colors.Count == 0)
                throw new ArgumentException("Palette.Colors 不能为0");

            this.Palette = palette;
            this.imageData = imageData;
            this.imageStride = imgStride;
            this.startKey = startKey;
            this.endKey = endKey;
            this.measureFromStart = measureFromStart;
            this.maxNoteLength = maxNoteLength;
            this.useMaxNoteLength = maxNoteLength > 0;
            this.targetHeight = targetHeight;
            this.resizeAlgorithm = resizeAlgorithm;
            this.keyList = keyList;
            this.useKeyList = keyList != null;
            this.whiteKeyClipped = whiteKeyClipped;
            this.blackKeyClipped = blackKeyClipped;
            this.whiteKeyFixed = whiteKeyFixed;
            this.blackKeyFixed = blackKeyFixed;
            this.fixedWidth = whiteKeyFixed || blackKeyFixed;
            this.colorIdMethod = colorIdMethod;

            int tracks = Palette.Colors.Count;
            EventBuffers = new FastList<MIDIEvent>[tracks];
            for (int i = 0; i < tracks; i++)
                EventBuffers[i] = new FastList<MIDIEvent>();

            var drawingPalette = new List<System.Drawing.Color>(Palette.Colors.Count);
            foreach (var c in Palette.Colors)
                drawingPalette.Add(System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B));
            paletteLabCache = new ImageToMidi.GetColorID.PaletteLabCache(drawingPalette);
        }

        public Task RunProcessAsync(Action callback, Action<double> progressCallback = null, bool enableProtection = true)
        {
            if (useMaxNoteLength && maxNoteLength <= 0)
                throw new ArgumentException("maxNoteLength 必须为正整数");

            if (keyList != null && keyList.Count < EffectiveWidth)
                throw new ArgumentException("keyList 长度不足");

            lock (cancellationLock)
            {
                // 取消之前的任务
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = new CancellationTokenSource();

                cancelled = false;
                IsCompleted = false;

                lock (protectionLock)
                {
                    isProtected = enableProtection;
                }
            }

            noteCountPerColor = new int[Palette.Colors.Count];

            var token = cancellationTokenSource.Token;
            currentTask = Task.Run(() =>
            {
                try
                {
                    // 检查是否在开始时就被取消
                    if (token.IsCancellationRequested && !isProtected)
                        return;

                    int targetWidth = EffectiveWidth;
                    int height = targetHeight;
                    int width = targetWidth;

                    resizedImage = ResizeImage.MakeResizedImage(imageData, imageStride, targetWidth, height, resizeAlgorithm);

                    // 进入保护区域 - 一旦开始实际转换就不能被中断
                    lock (protectionLock)
                    {
                        if (enableProtection)
                            isProtected = true;
                    }

                    long[] lastTimes = new long[Palette.Colors.Count];
                    long[] lastOnTimes = new long[width];
                    int[] colors = new int[width];
                    long time = 0;

                    for (int i = 0; i < width; i++)
                    {
                        colors[i] = -2;
                        lastOnTimes[i] = useMaxNoteLength ? -maxNoteLength - 1 : 0;
                    }

                    int[,] colorIndices = new int[height, width];

                    // 并行处理颜色索引
                    Parallel.For(0, height, i =>
                    {
                        if (token.IsCancellationRequested && !isProtected)
                            return;

                        int rowOffset = i * width * 4;
                        for (int j = 0; j < width; j++)
                        {
                            int pixel = rowOffset + j * 4;
                            int r = resizedImage[pixel + 2];
                            int g = resizedImage[pixel + 1];
                            int b = resizedImage[pixel + 0];
                            int a = resizedImage[pixel + 3];
                            if (a < 128)
                                colorIndices[i, j] = -2;
                            else
                            {
                                int id = GetColorID(r, g, b);
                                if (id < 0 || id >= Palette.Colors.Count)
                                    colorIndices[i, j] = -2;
                                else
                                    colorIndices[i, j] = id;
                            }
                        }
                    });

                    // 核心转换逻辑 - 在保护模式下不可中断
                    for (int i = height - 1; i >= 0; i--)
                    {
                        // 只有在非保护模式下才检查取消
                        if (!isProtected && token.IsCancellationRequested)
                            return;

                        for (int j = 0; j < width; j++)
                        {
                            int midiKey;
                            if (fixedWidth)
                            {
                                midiKey = startKey + j;
                                if (whiteKeyFixed && !MainWindow.IsWhiteKey(midiKey)) { colors[j] = -2; continue; }
                                if (blackKeyFixed && MainWindow.IsWhiteKey(midiKey)) { colors[j] = -2; continue; }
                            }
                            else
                            {
                                if (keyList == null || j >= keyList.Count)
                                {
                                    colors[j] = -2;
                                    continue;
                                }
                                midiKey = keyList[j];
                                if (whiteKeyClipped && !MainWindow.IsWhiteKey(midiKey)) { colors[j] = -2; continue; }
                                if (blackKeyClipped && MainWindow.IsWhiteKey(midiKey)) { colors[j] = -2; continue; }
                            }

                            int c = colors[j];
                            int newc = colorIndices[i, j];
                            bool colorChanged = (newc != c);
                            bool newNote = false;

                            if (useMaxNoteLength)
                            {
                                if (measureFromStart)
                                {
                                    long rowFromBottom = height - 1 - i;
                                    newNote = (rowFromBottom > 0) && (rowFromBottom % maxNoteLength == 0);
                                }
                                else
                                {
                                    long timeSinceLastOn = time - lastOnTimes[j];
                                    newNote = timeSinceLastOn >= maxNoteLength;
                                }
                            }

                            if (colorChanged || newNote)
                            {
                                if (c >= 0 && c < EventBuffers.Length)
                                {
                                    EventBuffers[c].Add(new NoteOffEvent((uint)(time - lastTimes[c]), (byte)0, (byte)midiKey));
                                    lastTimes[c] = time;
                                }

                                if (newc >= 0 && newc < EventBuffers.Length)
                                {
                                    EventBuffers[newc].Add(new NoteOnEvent((uint)(time - lastTimes[newc]), (byte)0, (byte)midiKey, 1));
                                    lastTimes[newc] = time;
                                    noteCountPerColor[newc]++;
                                }

                                colors[j] = newc;
                                lastOnTimes[j] = time;
                            }
                        }
                        time++;

                        if (progressCallback != null && (i % 32 == 0 || i == 0))
                        {
                            double progress = 1.0 - (double)i / height;
                            progressCallback(progress);
                        }

                        // 在保护模式下减少取消检查频率
                        if (!isProtected && (i & 1023) == 0 && token.IsCancellationRequested)
                            return;
                    }

                    // 处理最后一行的NoteOff
                    for (int j = 0; j < width; j++)
                    {
                        int c = colors[j];
                        int midiKey;
                        if (fixedWidth)
                        {
                            midiKey = startKey + j;
                            if (whiteKeyFixed && !MainWindow.IsWhiteKey(midiKey)) continue;
                            if (blackKeyFixed && MainWindow.IsWhiteKey(midiKey)) continue;
                        }
                        else
                        {
                            if (keyList == null || j >= keyList.Count)
                                continue;
                            midiKey = keyList[j];
                        }
                        if (c >= 0 && c < EventBuffers.Length)
                        {
                            EventBuffers[c].Add(new NoteOffEvent((uint)(time - lastTimes[c]), (byte)0, (byte)midiKey));
                            lastTimes[c] = time;
                        }
                    }

                    CountNotes();
                    progressCallback?.Invoke(1.0);

                    // 标记为已完成
                    IsCompleted = true;

                    // 清除保护状态
                    lock (protectionLock)
                    {
                        isProtected = false;
                    }

                    if (!token.IsCancellationRequested && callback != null)
                        callback();
                }
                catch (OperationCanceledException)
                {
                    // 被取消时清理状态
                    lock (protectionLock)
                    {
                        isProtected = false;
                    }
                    IsCompleted = false;
                }
                catch (Exception ex)
                {
                    // 错误时清理状态
                    lock (protectionLock)
                    {
                        isProtected = false;
                    }
                    IsCompleted = false;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            $"RunProcessAsync 发生异常：\n{ex.Message}\n\n{ex.StackTrace}",
                            "ImageToMidi 错误定位",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    });
                }
            }, token);

            return currentTask;
        }

        private ColorIdMethod colorIdMethod = ColorIdMethod.RGB;
        int GetColorID(int r, int g, int b)
        {
            return ImageToMidi.GetColorID.FindColorID(colorIdMethod, r, g, b, paletteLabCache);
        }

        public void Cancel()
        {
            lock (protectionLock)
            {
                // 如果在保护模式下，不允许取消
                if (isProtected)
                    return;
            }

            lock (cancellationLock)
            {
                cancelled = true;
                cancellationTokenSource?.Cancel();
            }

            try
            {
                if (Image != null)
                {
                    Image.Dispose();
                }
            }
            catch { }
        }

        // 新增：强制取消方法（仅在程序关闭时使用）
        public void ForceCancel()
        {
            lock (protectionLock)
            {
                isProtected = false;
            }

            lock (cancellationLock)
            {
                cancelled = true;
                cancellationTokenSource?.Cancel();
            }

            try
            {
                if (Image != null)
                {
                    Image.Dispose();
                }
            }
            catch { }
        }

        // 新增：等待当前任务完成
        public async Task WaitForCompletionAsync(int timeoutMs = 5000)
        {
            if (currentTask != null)
            {
                try
                {
                    await Task.WhenAny(currentTask, Task.Delay(timeoutMs));
                }
                catch
                {
                    // 忽略取消异常
                }
            }
        }

        // 1. 新增：音符遍历与keyIndex计算的复用方法
        private IEnumerable<(int track, Note note, int keyIndex)> EnumerateDrawableNotes()
        {
            int width = EffectiveWidth;
            for (int i = 0; i < EventBuffers.Length; i++)
            {
                foreach (Note n in new ExtractNotes(EventBuffers[i]))
                {
                    int keyIndex = -1;
                    if (fixedWidth)
                    {
                        keyIndex = n.Key - startKey;
                        if (keyIndex < 0 || keyIndex >= width) continue;
                        if (whiteKeyFixed && !MainWindow.IsWhiteKey(n.Key)) continue;
                        if (blackKeyFixed && MainWindow.IsWhiteKey(n.Key)) continue;
                    }
                    else
                    {
                        if (keyList != null)
                        {
                            keyIndex = keyList.IndexOf(n.Key);
                            if (keyIndex < 0 || keyIndex >= width) continue;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    yield return (i, n, keyIndex);
                }
            }
        }

        // 2. 新增：获取颜色的复用方法
        private System.Drawing.Color GetNoteColor(int track)
        {
            if (RandomColors)
            {
                int r, g, b;
                Random rand = new Random(track + RandomColorSeed * 256);
                HsvToRgb(rand.NextDouble() * 360, 1, 0.5, out r, out g, out b);
                return System.Drawing.Color.FromArgb(255, r, g, b);
            }
            else
            {
                var c = Palette.Colors[track];
                return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
            }
        }

        // 3. 新增：获取WPF颜色的复用方法
        private int GetNoteColorArgb(int track)
        {
            if (RandomColors)
            {
                int r, g, b;
                Random rand = new Random(track + RandomColorSeed * 256);
                HsvToRgb(rand.NextDouble() * 360, 1, 0.5, out r, out g, out b);
                return (255 << 24) | (r << 16) | (g << 8) | b;
            }
            else
            {
                var c = Palette.Colors[track];
                return (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
            }
        }

        // 5. 修改GeneratePreviewWriteableBitmapAsync，使用上述方法
        public async Task<WriteableBitmap> GeneratePreviewWriteableBitmapAsync(int scale = 8, Action<double> progressCallback = null)
        {
            // 只有在转换完成后才生成预览
            if (!IsCompleted)
                return null;

            int width = EffectiveWidth;
            if (resizedImage == null || width <= 0)
                return null;
            int height = resizedImage.Length / 4 / width;

            if (height > 7680)
                scale = 4;
            else if (height > 2160)
                scale = 6;
            else
                scale = 8;

            int bmpWidth = width * scale + 1;
            int bmpHeight = height * scale + 1;

            WriteableBitmap wb = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                wb = new WriteableBitmap(bmpWidth, bmpHeight, 96, 96, PixelFormats.Bgra32, null);
            });

            var notes = new List<(int track, Note note, int keyIndex)>(EnumerateDrawableNotes());

            int blockRows = 32 * scale;
            int blockCount = (bmpHeight + blockRows - 1) / blockRows;
            int[][] blockPixelsList = new int[blockCount][];
            int[] blockHeights = new int[blockCount];

            Parallel.For(0, blockCount, block =>
            {
                int yBlockStart = block * blockRows;
                int yBlockEnd = Math.Min(yBlockStart + blockRows, bmpHeight);
                int blockHeight = yBlockEnd - yBlockStart;
                blockHeights[block] = blockHeight;
                int[] blockPixels = new int[bmpWidth * blockHeight];

                foreach (var (track, n, keyIndex) in notes)
                {
                    int color = GetNoteColorArgb(track);

                    int x0 = keyIndex * scale;
                    int x1 = x0 + scale;
                    int y0 = bmpHeight - (int)n.End * scale;
                    int y1 = y0 + (int)n.Length * scale;

                    int yy0 = Math.Max(y0, yBlockStart);
                    int yy1 = Math.Min(y1, yBlockEnd);
                    if (yy0 >= yy1) continue;

                    for (int y = yy0; y < yy1; y++)
                    {
                        int rowStart = (y - yBlockStart) * bmpWidth;
                        for (int x = x0; x < x1; x++)
                        {
                            if (x >= 0 && x < bmpWidth)
                                blockPixels[rowStart + x] = color;
                        }
                    }

                    int black = unchecked((int)0xFF000000);
                    for (int x = x0; x < x1; x++)
                    {
                        if (yy0 >= 0 && yy0 < bmpHeight && yy0 == y0)
                            blockPixels[(yy0 - yBlockStart) * bmpWidth + x] = black;
                        if (yy1 - 1 >= 0 && yy1 - 1 < bmpHeight && yy1 - 1 == y1 - 1)
                            blockPixels[(yy1 - 1 - yBlockStart) * bmpWidth + x] = black;
                    }
                    for (int y = yy0; y < yy1; y++)
                    {
                        if (x0 >= 0 && x0 < bmpWidth)
                            blockPixels[(y - yBlockStart) * bmpWidth + x0] = black;
                        if (x1 - 1 >= 0 && x1 - 1 < bmpWidth)
                            blockPixels[(y - yBlockStart) * bmpWidth + (x1 - 1)] = black;
                    }
                }

                blockPixelsList[block] = blockPixels;
            });

            for (int block = 0; block < blockCount; block++)
            {
                int yBlockStart = block * blockRows;
                int blockHeight = blockHeights[block];
                int[] blockPixels = blockPixelsList[block];

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    wb.WritePixels(
                        new Int32Rect(0, yBlockStart, bmpWidth, blockHeight),
                        blockPixels, bmpWidth * 4, 0);
                }, System.Windows.Threading.DispatcherPriority.Background);

                progressCallback?.Invoke((double)(Math.Min(yBlockStart + blockHeight, bmpHeight)) / bmpHeight);
                await Task.Yield();
            }

            return wb;
        }

        public static void WriteMidi(
    string filename,
    IEnumerable<ConversionProcess> processes,
    int ticksPerPixel,
    int ppq,
    int startOffset,
    int midiBPM,
    bool useColorEvents,
    Action<double> reportProgress = null)
        {
            var processList = processes.ToList();
            if (processList.Count == 0) return;

            int tracks = processList[0].Palette.Colors.Count;
            var palette = processList[0].Palette;

            int totalEvents = 0;
            foreach (var proc in processList)
            {
                for (int i = 0; i < tracks; i++)
                {
                    var eventBuffersField = typeof(ConversionProcess).GetField("EventBuffers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var eventBuffers = eventBuffersField.GetValue(proc) as FastList<MIDIEvent>[];
                    totalEvents += eventBuffers[i].Count();
                    if (useColorEvents) totalEvents++;
                }
            }
            if (totalEvents == 0) totalEvents = 1;

            int writtenEvents = 0;

            using (var stream = new BufferedStream(File.Open(filename, FileMode.Create)))
            {
                MidiWriter writer = new MidiWriter(stream);
                writer.Init();
                writer.WriteFormat(1);
                writer.WritePPQ((ushort)ppq);
                writer.WriteNtrks((ushort)tracks);

                int tempo = 60000000 / midiBPM;

                for (int i = 0; i < tracks; i++)
                {
                    writer.InitTrack();
                    if (i == 0)
                    {
                        writer.Write(new TempoEvent(0, tempo));
                        writtenEvents++;
                    }

                    var absEvents = new List<(ulong absTick, MIDIEvent e)>();
                    ulong globalTick = (ulong)startOffset;
                    for (int frameIdx = 0; frameIdx < processList.Count; frameIdx++)
                    {
                        var proc = processList[frameIdx];
                        var eventBuffersField = typeof(ConversionProcess).GetField("EventBuffers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var eventBuffers = eventBuffersField.GetValue(proc) as FastList<MIDIEvent>[];

                        if (useColorEvents)
                        {
                            var c = palette.Colors[i];
                            absEvents.Add((globalTick, new ColorEvent(0, 0, c.R, c.G, c.B, c.A)));
                        }

                        ulong tick = globalTick;
                        foreach (MIDIEvent e in eventBuffers[i])
                        {
                            tick += e.DeltaTime * (ulong)ticksPerPixel;
                            absEvents.Add((tick, e.Clone()));
                        }

                        int frameHeight = proc.targetHeight;
                        globalTick += (ulong)(frameHeight * ticksPerPixel);
                    }

                    absEvents.Sort((a, b) => a.absTick.CompareTo(b.absTick));

                    ulong lastTick = 0;
                    foreach (var (absTick, e) in absEvents)
                    {
                        e.DeltaTime = (uint)(absTick - lastTick);
                        writer.Write(e);
                        lastTick = absTick;
                        writtenEvents++;
                        if ((writtenEvents & 0x3F) == 0 || writtenEvents == totalEvents)
                            reportProgress?.Invoke((double)writtenEvents / totalEvents);
                    }

                    writer.EndTrack();
                }
                writer.Close();
            }
            reportProgress?.Invoke(1.0);
        }

        void HsvToRgb(double h, double S, double V, out int r, out int g, out int b)
        {
            double H = h;
            while (H < 0) { H += 360; }
            ;
            while (H >= 360) { H -= 360; }
            ;
            double R, G, B;
            if (V <= 0)
            { R = G = B = 0; }
            else if (S <= 0)
            {
                R = G = B = V;
            }
            else
            {
                double hf = H / 60.0;
                int i = (int)Math.Floor(hf);
                double f = hf - i;
                double pv = V * (1 - S);
                double qv = V * (1 - S * f);
                double tv = V * (1 - S * (1 - f));
                switch (i)
                {
                    case 0:
                        R = V;
                        G = tv;
                        B = pv;
                        break;

                    case 1:
                        R = qv;
                        G = V;
                        B = pv;
                        break;
                    case 2:
                        R = pv;
                        G = V;
                        B = tv;
                        break;

                    case 3:
                        R = pv;
                        G = qv;
                        B = V;
                        break;
                    case 4:
                        R = tv;
                        G = pv;
                        B = V;
                        break;

                    case 5:
                        R = V;
                        G = pv;
                        B = qv;
                        break;

                    case 6:
                        R = V;
                        G = tv;
                        B = pv;
                        break;
                    case -1:
                        R = V;
                        G = pv;
                        B = qv;
                        break;

                    default:
                        R = G = B = V;
                        break;
                }
            }
            r = Clamp((int)(R * 255.0));
            g = Clamp((int)(G * 255.0));
            b = Clamp((int)(B * 255.0));
        }
        void RgbToHsv(int r, int g, int b, out double h, out double s, out double v)
        {
            double R = r / 255.0, G = g / 255.0, B = b / 255.0;
            double max = Math.Max(R, Math.Max(G, B));
            double min = Math.Min(R, Math.Min(G, B));
            v = max;

            double delta = max - min;
            if (max == 0)
                s = 0;
            else
                s = delta / max;

            if (delta == 0)
            {
                h = 0;
            }
            else if (max == R)
            {
                h = 60 * (((G - B) / delta) % 6);
            }
            else if (max == G)
            {
                h = 60 * (((B - R) / delta) + 2);
            }
            else // max == B
            {
                h = 60 * (((R - G) / delta) + 4);
            }
            if (h < 0) h += 360;
        }
        int Clamp(int i)
        {
            if (i < 0) return 0;
            if (i > 255) return 255;
            return i;
        }

        private void CountNotes()
        {
            NoteCount = 0;
            for (int i = 0; i < EventBuffers.Length; i++)
            {
                foreach (Note n in new ExtractNotes(EventBuffers[i]))
                {
                    NoteCount++;
                }
            }
        }

        public int GetNoteCountForColor(int colorIndex)
        {
            if (noteCountPerColor == null || colorIndex < 0 || colorIndex >= noteCountPerColor.Length)
                return 0;
            return noteCountPerColor[colorIndex];
        }
    }
}