using MIDIModificationFramework;
using MIDIModificationFramework.MIDI_Events;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
        bool cancelled = false;
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

        /*public enum HeightModeEnum
        {
            SameAsWidth,
            OriginalHeight,
            CustomHeight,
            OriginalAspectRatio // 新增的枚举值
        }*/

        //public HeightModeEnum HeightMode { get; private set; }
        //public int CustomHeight { get; private set; }
        //public bool ImageFullyGenerated { get; private set; }
        private ResizeAlgorithm resizeAlgorithm = ResizeAlgorithm.AreaResampling;
        // ConversionProcess.cs 新增字段
        private List<int> keyList = null;
        private bool useKeyList = false;
        private bool fixedWidth = false; // 是否等宽模式
        private bool whiteKeyClipped = false;
        private bool blackKeyClipped = false;
        private bool whiteKeyFixed = false;
        private bool blackKeyFixed = false;

        //private int originalImageWidth;
        //private int originalImageHeight;

        //private int previewRotation;
        private int targetHeight;
        private int[] noteCountPerColor;

        public ConversionProcess(
    BitmapPalette palette,
    byte[] imageData,
    int imgStride,
    int startKey,
    int endKey,
    bool measureFromStart,
    int maxNoteLength,
    int targetHeight, // 新增
    ResizeAlgorithm resizeAlgorithm,
    List<int> keyList,
    bool whiteKeyFixed = false,
    bool blackKeyFixed = false,
    bool whiteKeyClipped = false,
    bool blackKeyClipped = false)
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

            int tracks = Palette.Colors.Count;
            EventBuffers = new FastList<MIDIEvent>[tracks];
            for (int i = 0; i < tracks; i++)
                EventBuffers[i] = new FastList<MIDIEvent>();
        }


        public Task RunProcessAsync(Action callback, Action<double> progressCallback = null)
        {
            noteCountPerColor = new int[Palette.Colors.Count];
            return Task.Run(() =>
            {
                int targetWidth = EffectiveWidth; // 等效宽度
                int height = targetHeight; // 使用传入的
                //debug输出targetHeight
                //Debug.WriteLine("targetHeight: " + targetHeight);
                int width = targetWidth;

                resizedImage = ResizeImage.MakeResizedImage(imageData, imageStride, targetWidth, height, resizeAlgorithm);

                long[] lastTimes = new long[Palette.Colors.Count];
                long[] lastOnTimes = new long[width];
                int[] colors = new int[width];
                long time = 0;
                //debug输出resizedImage的宽高和比例
                //Console.WriteLine("resizedImage width: " + width + " height: " + height + " ratio: " + (double)width / height);

                for (int i = 0; i < width; i++) colors[i] = -1;

                // 主循环，逐行处理
                for (int i = height - 1; i >= 0 && !cancelled; i--)
                {
                    int rowOffset = i * width * 4;
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

                        int pixel = rowOffset + j * 4;
                        int c = colors[j];
                        int newc = GetColorID(resizedImage[pixel + 2], resizedImage[pixel + 1], resizedImage[pixel + 0]);
                        if (resizedImage[pixel + 3] < 128) newc = -2;
                        bool newNote = false;
                        if (useMaxNoteLength)
                        {
                            if (measureFromStart) newNote = (i % maxNoteLength == 0) && c != -1;
                            else newNote = (time - lastOnTimes[j]) >= maxNoteLength && c != -1;
                        }
                        if (newc != c || newNote)
                        {
                            if (c != -1 && c != -2)
                            {
                                EventBuffers[c].Add(new NoteOffEvent((uint)(time - lastTimes[c]), (byte)0, (byte)midiKey));
                                lastTimes[c] = time;
                            }
                            colors[j] = newc;
                            c = newc;
                            if (c != -2)
                            {
                                EventBuffers[c].Add(new NoteOnEvent((uint)(time - lastTimes[c]), (byte)0, (byte)midiKey, 1));
                                lastTimes[c] = time;
                                lastOnTimes[j] = time;
                                noteCountPerColor[c]++; // 统计
                            }
                        }
                    }
                    time++;

                    // 进度回调（每处理一定行数更新一次，防止UI过载）
                    if (progressCallback != null && (i % 32 == 0 || i == 0))
                    {
                        double progress = 1.0 - (double)i / height;
                        progressCallback(progress);
                    }

                    if ((i & 1023) == 0 && cancelled)
                        return;
                }
                if (cancelled) return;

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
                    if (c != -1 && c != -2)
                    {
                        EventBuffers[c].Add(new NoteOffEvent((uint)(time - lastTimes[c]), (byte)0, (byte)midiKey));
                        lastTimes[c] = time;
                    }
                }

                CountNotes();
                Image = GenerateImage();

                // 最终进度100%
                progressCallback?.Invoke(1.0);

                if (!cancelled && callback != null)
                    callback();
                if (resizedImage.Length != width * height * 4)
                {
                    throw new InvalidOperationException($"resizedImage 长度不匹配: {resizedImage.Length} != {width}*{height}*4");
                }
            });
        }

        int GetColorID(int r, int g, int b)
        {
            int smallest = 0;
            bool first = true;
            int id = 0;
            for (int i = 0; i < Palette.Colors.Count; i++)
            {
                var col = Palette.Colors[i];
                int _r = col.R - r;
                int _g = col.G - g;
                int _b = col.B - b;
                int dist = _r * _r + _g * _g + _b * _b;
                if (dist < smallest || first)
                {
                    first = false;
                    smallest = dist;
                    id = i;
                }
            }
            return id;
        }

        public void Cancel()
        {
            cancelled = true;
            try
            {
                if (Image != null)
                {
                    Image.Dispose();
                }
            }
            catch { }
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

        // 4. 修改GenerateImage，使用上述方法
        public Bitmap GenerateImage(Action<double> progressCallback = null)
        {
            int width = EffectiveWidth;
            int height = resizedImage.Length / 4 / width;
            int scale = 5;
            Bitmap img = new Bitmap(width * scale + 1, height * scale + 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics dg = Graphics.FromImage(img))
            {
                int totalTracks = EventBuffers.Length;
                foreach (var (track, n, keyIndex) in EnumerateDrawableNotes())
                {
                    var _c = GetNoteColor(track);
                    using (var brush = new System.Drawing.SolidBrush(_c))
                        dg.FillRectangle(brush, keyIndex * scale, height * scale - (int)n.End * scale, scale, (int)n.Length * scale);
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.Black))
                        dg.DrawRectangle(pen, keyIndex * scale, height * scale - (int)n.End * scale, scale, (int)n.Length * scale);
                    if (cancelled) break;
                }
                progressCallback?.Invoke(1.0);
            }
            this.Image = img;
            return img;
        }

        // 5. 修改GeneratePreviewWriteableBitmapAsync，使用上述方法
        public async Task<WriteableBitmap> GeneratePreviewWriteableBitmapAsync(int scale = 8, Action<double> progressCallback = null)
        {
            int width = EffectiveWidth;
            if (resizedImage == null || width <= 0)
                return null;
            int height = resizedImage.Length / 4 / width;

            // 根据图片高度动态调整scale
            if (height > 7680)
                scale = 4;
            else if (height > 2160)
                scale = 6;
            else
                scale = 8;

            int bmpWidth = width * scale + 1;
            int bmpHeight = height * scale + 1;
            var wb = new WriteableBitmap(bmpWidth, bmpHeight, 96, 96, PixelFormats.Bgra32, null);

            // 1. 收集所有音符（已包含keyIndex和track）
            var notes = new List<(int track, Note note, int keyIndex)>(EnumerateDrawableNotes());

            // 2. 区块参数
            int blockRows = 32 * scale;
            int blockCount = (bmpHeight + blockRows - 1) / blockRows;
            int[][] blockPixelsList = new int[blockCount][];
            int[] blockHeights = new int[blockCount];

            // 3. 区块级并行填充像素
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

            // 4. 主线程依次写入区块到WriteableBitmap
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

            // 统计总事件数
            int totalEvents = 0;
            foreach (var proc in processList)
            {
                for (int i = 0; i < tracks; i++)
                {
                    var eventBuffersField = typeof(ConversionProcess).GetField("EventBuffers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var eventBuffers = eventBuffersField.GetValue(proc) as FastList<MIDIEvent>[];
                    totalEvents += eventBuffers[i].Count();
                    if (useColorEvents) totalEvents++; // 每轨道每帧加一个ColorEvent
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
                    // 只在第一个轨道插入TempoEvent
                    if (i == 0)
                    {
                        writer.Write(new TempoEvent(0, tempo));
                        writtenEvents++;
                    }

                    // 1. 收集所有帧的事件，转换为绝对tick
                    var absEvents = new List<(ulong absTick, MIDIEvent e)>();
                    ulong globalTick = (ulong)startOffset;
                    for (int frameIdx = 0; frameIdx < processList.Count; frameIdx++)
                    {
                        var proc = processList[frameIdx];
                        var eventBuffersField = typeof(ConversionProcess).GetField("EventBuffers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var eventBuffers = eventBuffersField.GetValue(proc) as FastList<MIDIEvent>[];

                        // ColorEvent
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

                        // 推进globalTick：每帧的tick长度 = 图片高度
                        int frameHeight = proc.targetHeight; // targetHeight为每帧图片高度
                        globalTick += (ulong)(frameHeight * ticksPerPixel);
                    }

                    // 2. 按绝对tick排序
                    absEvents.Sort((a, b) => a.absTick.CompareTo(b.absTick));

                    // 3. 重新计算DeltaTime并写入
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

        int Clamp(int i)
        {
            if (i < 0) return 0;
            if (i > 255) return 255;
            return i;
        }
        // 修改CountNotes方法，统计每种颜色的音符数
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

        // 新增方法：获取每种颜色的音符数
        public int GetNoteCountForColor(int colorIndex)
        {
            if (noteCountPerColor == null || colorIndex < 0 || colorIndex >= noteCountPerColor.Length)
                return 0;
            //debug输出数组
            //Console.WriteLine($"NoteCountPerColor: {string.Join(", ", noteCountPerColor)}");

            return noteCountPerColor[colorIndex];
        }
    }
}