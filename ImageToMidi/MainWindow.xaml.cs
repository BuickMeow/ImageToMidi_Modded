using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
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

        bool leftSelected = true;

        BitmapSource openedImageSrc = null;
        byte[] openedImagePixels = null;
        int openedImageWidth = 0;
        int openedImageHeight = 0;
        string openedImagePath = "";
        BitmapPalette chosenPalette = null;

        ConversionProcess convert = null;


        bool colorPick = false;

        public enum HeightModeEnum
        {
            SameAsWidth,
            OriginalHeight,
            CustomHeight,
            OriginalAspectRatio // 新增的枚举值
        }

        private HeightModeEnum heightMode = HeightModeEnum.SameAsWidth;
        private int customHeight = 0;

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

            MakeFadeInOut(selectedHighlightLeft);
            MakeFadeInOut(selectedHighlightRight);
            MakeFadeInOut(colPickerOptions);
            MakeFadeInOut(openedImage);
            MakeFadeInOut(genImage);
            MakeFadeInOut(randomSeedBox);

            colPicker.PickStart += ColPicker_PickStart;
            colPicker.PickStop += ColPicker_PickStop;
            colPicker.PaletteChanged += ReloadPreview;

            UpdateHeightForSameAsWidth();
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

        private void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            colPicker.CancelPick();
            OpenFileDialog open = new OpenFileDialog();
            open.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp";
            if (!(bool)open.ShowDialog()) return;
            openedImagePath = open.FileName;
            BitmapImage src = new BitmapImage();
            src.BeginInit();
            src.UriSource = new Uri(openedImagePath);
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.EndInit();
            openedImageWidth = src.PixelWidth;
            openedImageHeight = src.PixelHeight;
            int stride = src.PixelWidth * 4;
            int size = src.PixelHeight * stride;
            openedImagePixels = new byte[size];
            src.CopyPixels(openedImagePixels, stride, 0);
            openedImage.Source = src;
            openedImageSrc = src;
            ReloadAutoPalette();
            ((Storyboard)openedImage.GetValue(FadeInStoryboard)).Begin();

            // 更新自定义高度NumberSelect显示的数据
            UpdateCustomHeightNumberSelect();
        }

        private void UpdateCustomHeightNumberSelect()
        {
            switch (heightMode)
            {
                case HeightModeEnum.SameAsWidth:
                    CustomHeightNumberSelect.Value = (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1;
                    break;
                case HeightModeEnum.OriginalHeight:
                    CustomHeightNumberSelect.Value = openedImageHeight;
                    break;
                case HeightModeEnum.CustomHeight:
                    CustomHeightNumberSelect.Value = customHeight;
                    break;
                case HeightModeEnum.OriginalAspectRatio:
                    double aspectRatio = (double)openedImageHeight / openedImageWidth;
                    CustomHeightNumberSelect.Value = (int)((double)(lastKeyNumber.Value - firstKeyNumber.Value + 1) * aspectRatio);
                    break;
            }
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

        private void TrackCount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            ReloadAutoPalette();
        }

        void ReloadAutoPalette()
        {
            if (openedImageSrc == null) return;
            int tracks = (int)trackCount.Value;
            chosenPalette = new BitmapPalette(openedImageSrc, tracks * 16);
            ReloadPalettePreview();
            ReloadPreview();
        }

        void ReloadPalettePreview()
        {
            autoPaletteBox.Children.Clear();
            int tracks = (chosenPalette.Colors.Count + 15 - ((chosenPalette.Colors.Count + 15) % 16)) / 16;
            for (int i = 0; i < tracks; i++)
            {
                var dock = new Grid();
                for (int j = 0; j < 16; j++)
                    dock.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                autoPaletteBox.Children.Add(dock);
                DockPanel.SetDock(dock, Dock.Top);
                for (int j = 0; j < 16; j++)
                {
                    if (i * 16 + j < chosenPalette.Colors.Count)
                    {
                        var box = new Viewbox()
                        {
                            Stretch = Stretch.Uniform,
                            Child =
                                new Rectangle()
                                {
                                    Width = 40,
                                    Height = 40,
                                    Fill = new SolidColorBrush(chosenPalette.Colors[i * 16 + j])
                                }
                        };
                        Grid.SetColumn(box, j);
                        dock.Children.Add(box);
                    }
                }
            }
        }

        private void ReloadPreview()
        {
            if (!IsInitialized) return;
            if (openedImagePixels == null) return;
            if (convert != null) convert.Cancel();

            var palette = chosenPalette;
            if (leftSelected) palette = colPicker.GetPalette();

            // 获取目标高度
            int targetHeight = GetTargetHeight();

            // 根据用户选择的高度模式和输入的自定义高度值初始化 ConversionProcess 对象
            if ((bool)useNoteLength.IsChecked)
            {
                convert = new ConversionProcess(
                    palette,
                    openedImagePixels,
                    openedImageWidth * 4,
                    (int)firstKeyNumber.Value,
                    (int)lastKeyNumber.Value + 1,
                    (bool)startOfImage.IsChecked,
                    (int)noteSplitLength.Value,
                    (ConversionProcess.HeightModeEnum)heightMode, // Fix: Cast to ConversionProcess.HeightModeEnum
                    targetHeight
                );
            }
            else
            {
                convert = new ConversionProcess(
                    palette,
                    openedImagePixels,
                    openedImageWidth * 4,
                    (int)firstKeyNumber.Value,
                    (int)lastKeyNumber.Value + 1,
                    (ConversionProcess.HeightModeEnum)heightMode, // Fix: Cast to ConversionProcess.HeightModeEnum
                    targetHeight
                );
            }

            if (!(bool)genColorEventsCheck.IsChecked)
            {
                convert.RandomColors = true;
                convert.RandomColorSeed = (int)randomColorSeed.Value;
            }

            convert.RunProcessAsync(() =>
            {
                ShowPreview();
            });
            genImage.Source = null;
            saveMidi.IsEnabled = false;
        }

        private int GetTargetHeight()
        {
            switch (heightMode)
            {
                case HeightModeEnum.SameAsWidth:
                    return (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1;
                case HeightModeEnum.OriginalHeight:
                    return openedImageHeight;
                case HeightModeEnum.CustomHeight:
                    return customHeight;
                case HeightModeEnum.OriginalAspectRatio:
                    // 计算保持原图比例的高度
                    double aspectRatio = (double)openedImageHeight / openedImageWidth;
                    return (int)((double)(lastKeyNumber.Value - firstKeyNumber.Value + 1) * aspectRatio);
                default:
                    return openedImageHeight; // 默认使用原图高度
            }
        }

        BitmapImage BitmapToImageSource(System.Drawing.Bitmap bitmap)
        {
            try
            {
                using (MemoryStream memory = new MemoryStream())
                {
                    bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                    memory.Position = 0;
                    BitmapImage bitmapimage = new BitmapImage();
                    bitmapimage.BeginInit();
                    bitmapimage.StreamSource = memory;
                    bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapimage.EndInit();

                    return bitmapimage;
                }
            }
            catch { return null; }
        }

        void ShowPreview()
        {
            try
            {
                var src = convert.Image;
                Dispatcher.Invoke(() =>
                {
                    var bmp = BitmapToImageSource(src);
                    if (bmp != null)
                    {
                        genImage.Source = bmp;
                        saveMidi.IsEnabled = true;
                        ((Storyboard)genImage.GetValue(FadeInStoryboard)).Begin();

                        // 更新导出 MIDI 按钮的文本
                        saveMidi.Content = $"导出 MIDI（音符数 {convert.NoteCount}）";
                    }
                });
            }
            catch { }
        }

        private void SaveMidi_Click(object sender, RoutedEventArgs e)
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
                    convert.WriteMidi(save.FileName, (int)ticksPerPixel.Value, (int)midiPPQ.Value, (int)startOffset.Value, colorEvents);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Image To MIDI被玩坏了！<LineBreak/>这肯定不是节能酱的问题！<LineBreak/>绝对不是！");
            }
        }


        private void NoteSplitLength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            ReloadPreview();
        }

        private void StartOfImage_Checked(object sender, RoutedEventArgs e)
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
        }

        private void ResetPalette_Click(object sender, RoutedEventArgs e)
        {
            ReloadAutoPalette();
        }

        private void ClusterisePalette_Click(object sender, RoutedEventArgs e)
        {
            if (chosenPalette == null || openedImagePixels == null) return;
            chosenPalette =
                Clusterisation.Clusterise(
                    chosenPalette,
                    ResizeImage.MakeResizedImage(openedImagePixels, openedImageWidth * 4, 128, openedImageHeight),
                    10
                );
            ReloadPalettePreview();
            ReloadPreview();
        }

        private void OpenedImage_ColorClicked(object sender, Color c)
        {
            if (colorPick)
                colPicker.SendColor(c);
        }

        // 新增方法：更新“宽高相等”模式下的高度数值
        private void UpdateHeightForSameAsWidth()
        {
            if (heightMode == HeightModeEnum.SameAsWidth)
            {
                CustomHeightNumberSelect.Value = (int)lastKeyNumber.Value - (int)firstKeyNumber.Value + 1;
            }
            else if (heightMode == HeightModeEnum.OriginalAspectRatio)
            {
                double aspectRatio = (double)openedImageHeight / openedImageWidth;
                CustomHeightNumberSelect.Value = (int)((double)(lastKeyNumber.Value - firstKeyNumber.Value + 1) * aspectRatio);
            }
        }

        private void FirstKeyNumber_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            lastKeyNumber.Minimum = firstKeyNumber.Value + 1;
            UpdateHeightForSameAsWidth(); // 调用更新高度方法
            ReloadPreview();
        }

        private void LastKeyNumber_ValueChanged(object sender, RoutedPropertyChangedEventArgs<decimal> e)
        {
            firstKeyNumber.Maximum = lastKeyNumber.Value - 1;
            UpdateHeightForSameAsWidth(); // 调用更新高度方法
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
    }
}
