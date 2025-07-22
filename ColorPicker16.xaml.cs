using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    public partial class ColorPicker16 : UserControl
    {
        const int MaxColors = 256;
        const int RowSize = 16;

        Color[] colors = new Color[MaxColors];
        bool[] set = new bool[MaxColors];

        public event Action PickStart;
        public event Action PickStop;
        public event Action PaletteChanged;

        int colorPickerButton = -1;
        int lastPicker = -1;

        public ColorPicker16()
        {
            InitializeComponent();
            InitButtons(RowSize); // 初始只显示一行
        }

        private void InitButtons(int count)
        {
            buttonGrid.Children.Clear();
            buttonGrid.Rows = (count + RowSize - 1) / RowSize;
            buttonGrid.Columns = RowSize;

            for (int i = 0; i < count; i++)
            {
                var btn = CreateColorButton(i);
                buttonGrid.Children.Add(btn);
            }
        }

        private Button CreateColorButton(int i)
        {
            var btn = new Button
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                Tag = i,
                Content = new PackIcon
                {
                    Kind = PackIconKind.Eyedropper,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Height = 23,
                    Width = 38
                }
            };
            btn.Click += Button_Click;
            btn.LostFocus += Button_LostFocus;
            // 新增：悬停时刷新ToolTip
            btn.MouseEnter += (s, e) =>
            {
                btn.ToolTip = GetColorHexTooltip(i);
            };
            return btn;
        }
        public Func<int, int> GetNoteCountForColor { get; set; }
        private string GetColorHexTooltip(int i)
        {
            if (set[i])
            {
                var c = colors[i];
                int noteCount = GetNoteCountForColor != null ? GetNoteCountForColor(i) : 0;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}\nR:{c.R}\nG:{c.G}\nB:{c.B}\nA:{c.A}\n{Languages.Strings.CS_TrackNoteCount} {noteCount}";
            }
            else
            {
                return $"{Languages.Strings.CP_NoColorSelected}";
            }
        }

        private Button GetButton(int i)
        {
            return (Button)buttonGrid.Children[i];
        }

        private int GetButtonID(Button b)
        {
            return (int)b.Tag;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var b = (Button)sender;
            int id = GetButtonID(b);
            if (set[id])
            {
                b.Background = Brushes.Transparent;
                ((PackIcon)b.Content).Kind = PackIconKind.Eyedropper;
                set[id] = false;
                PaletteChanged?.Invoke();
            }
            else
            {
                // 如果当前按钮已经是吸色模式，则取消吸色模式
                if (colorPickerButton != -1 && colorPickerButton != id)
                {
                    ((PackIcon)GetButton(colorPickerButton).Content).Kind = PackIconKind.Eyedropper;
                }

                // 设置当前按钮为吸色模式
                ((PackIcon)b.Content).Kind = PackIconKind.Vanish;
                colorPickerButton = id;
                lastPicker = id;
                PickStart?.Invoke();
            }

            // 检查是否需要显示新的一行或减少行数
            ShowNextRowIfNeeded();
        }



        private void ShowNextRowIfNeeded()
        {
            int filled = set.Count(s => s);
            int visible = buttonGrid.Children.Count;
            if (filled == visible && visible < MaxColors)
            {
                int toShow = Math.Min(visible + RowSize, MaxColors);
                InitButtons(toShow);
                // 恢复已设置的颜色
                for (int i = 0; i < toShow; i++)
                {
                    if (set[i])
                    {
                        GetButton(i).Background = new SolidColorBrush(colors[i]);
                        ((PackIcon)GetButton(i).Content).Kind = PackIconKind.Tick;
                    }
                }
            }
            else if (filled < visible && visible > RowSize)
            {
                // 检查是否所有颜色按键都被手动删除
                bool allCleared = true;
                for (int i = visible - RowSize; i < visible; i++)
                {
                    if (set[i])
                    {
                        allCleared = false;
                        break;
                    }
                }
                // 检查其他行是否至少有一个空位置
                bool otherRowsHaveSpace = false;
                for (int i = 0; i < visible - RowSize; i++)
                {
                    if (!set[i])
                    {
                        otherRowsHaveSpace = true;
                        break;
                    }
                }
                if (allCleared && otherRowsHaveSpace)
                {
                    int toShow = Math.Max(visible - RowSize, RowSize);
                    InitButtons(toShow);
                    // 恢复已设置的颜色
                    for (int i = 0; i < toShow; i++)
                    {
                        if (set[i])
                        {
                            GetButton(i).Background = new SolidColorBrush(colors[i]);
                            ((PackIcon)GetButton(i).Content).Kind = PackIconKind.Tick;
                        }
                    }
                }
            }
        }




        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 可根据需要调整高度
        }

        private void Button_LostFocus(object sender, RoutedEventArgs e)
        {
            // 保持原有逻辑
        }

        public void CancelPick()
        {
            if (colorPickerButton != -1)
            {
                ((PackIcon)GetButton(colorPickerButton).Content).Kind = PackIconKind.Eyedropper;
                colorPickerButton = -1;
                PickStop?.Invoke();
            }
        }

        public void SendColor(Color c)
        {
            // 确保显示足够的颜色按键
            ShowNextRowIfNeeded();

            GetButton(lastPicker).Background = new SolidColorBrush(c);
            colors[lastPicker] = c;
            set[lastPicker] = true;

            ((PackIcon)GetButton(colorPickerButton).Content).Kind = PackIconKind.Tick;
            colorPickerButton = -1;
            PickStop?.Invoke();
            PaletteChanged?.Invoke();

            // 检查是否需要显示新的一行
            ShowNextRowIfNeeded();
        }


        public BitmapPalette GetPalette()
        {
            List<Color> c = new List<Color>();
            for (int i = 0; i < MaxColors; i++)
                if (set[i])
                    c.Add(colors[i]);
            if (c.Count == 0) c.Add(Colors.Black);
            return new BitmapPalette(c);
        }
        /// <summary>
        /// 清空调色板
        /// </summary>
        public void ClearPalette()
        {
            for (int i = 0; i < MaxColors; i++)
            {
                colors[i] = Colors.Transparent;
                set[i] = false;
                if (buttonGrid.Children.Count > i)
                {
                    var btn = GetButton(i);
                    btn.Background = Brushes.Transparent;
                    if (btn.Content is PackIcon icon)
                        icon.Kind = PackIconKind.Eyedropper;
                }
            }
            colorPickerButton = -1;
            lastPicker = -1;
            PaletteChanged?.Invoke();
        }
    }
}