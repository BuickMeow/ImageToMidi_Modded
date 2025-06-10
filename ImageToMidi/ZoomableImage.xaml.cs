using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    /// <summary>
    /// Interaction logic for ZoomableImage.xaml
    /// </summary>

    public delegate void ColorClickedEventHandler(object sender, Color clicked);


    public class RoutedColorClickedEventArgs : RoutedEventArgs
    {
        public RoutedColorClickedEventArgs(Color clickedColor, RoutedEvent e) : base(e)
        {
            ClickedColor = clickedColor;
        }

        protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
        {
            ((ColorClickedEventHandler)genericHandler)(genericTarget, ClickedColor);
        }

        public Color ClickedColor { get; }
    }

    public partial class ZoomableImage : UserControl
    {
        public int ImageRotation
        {
            get { return (int)GetValue(ImageRotationProperty); }
            set { SetValue(ImageRotationProperty, value); }
        }
        public static readonly DependencyProperty ImageRotationProperty =
            DependencyProperty.Register("ImageRotation", typeof(int), typeof(ZoomableImage), new PropertyMetadata(0, OnTransformChanged));

        public bool ImageFlip
        {
            get { return (bool)GetValue(ImageFlipProperty); }
            set { SetValue(ImageFlipProperty, value); }
        }
        public static readonly DependencyProperty ImageFlipProperty =
            DependencyProperty.Register("ImageFlip", typeof(bool), typeof(ZoomableImage), new PropertyMetadata(false, OnTransformChanged));

        private static void OnTransformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ZoomableImage)d).ApplyImageTransform();
        }

        // 修改 ApplyImageTransform 方法
        private void ApplyImageTransform()
        {
            var group = new TransformGroup();

            // 水平翻转
            if (ImageFlip)
            {
                group.Children.Add(new ScaleTransform(-1, 1, imageBorder.ActualWidth / 2, imageBorder.ActualHeight / 2));
            }

            // 旋转
            if (ImageRotation != 0)
            {
                group.Children.Add(new RotateTransform(ImageRotation, imageBorder.ActualWidth / 2, imageBorder.ActualHeight / 2));
            }

            imageBorder.RenderTransform = group;
        }
        public BitmapSource Source
        {
            get { return (BitmapSource)GetValue(SourceProperty); }
            set
            {
                SetValue(SourceProperty, value);
                shownImage.Source = value;
                RefreshView();

                if (value != null)
                {
                    // 先在UI线程获取必要信息
                    int width = 0, height = 0, stride = 0;
                    byte[] pixels = null;
                    Dispatcher.Invoke(() =>
                    {
                        width = value.PixelWidth;
                        height = value.PixelHeight;
                        stride = width * 4;
                        pixels = new byte[height * stride];
                        value.CopyPixels(pixels, stride, 0);
                    });

                    // 再在UI线程更新缓存
                    cachedWidth = width;
                    cachedHeight = height;
                    cachedStride = stride;
                    cachedPixels = pixels;
                }
                else
                {
                    cachedPixels = null;
                    cachedStride = 0;
                    cachedWidth = 0;
                    cachedHeight = 0;
                }
            }
        }

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register("Source", typeof(ImageSource), typeof(ZoomableImage), new PropertyMetadata(null));

        double targetZoom = 1;

        public double Zoom
        {
            get { return (double)GetValue(ZoomProperty); }
            set { SetValue(ZoomProperty, value); }
        }

        public static readonly DependencyProperty ZoomProperty =
            DependencyProperty.Register("Zoom", typeof(double), typeof(ZoomableImage), new PropertyMetadata(1.0, new PropertyChangedCallback(OnZoomChanged)));

        public Point Offset
        {
            get { return (Point)GetValue(OffsetProperty); }
            set { SetValue(OffsetProperty, value); }
        }

        public static readonly DependencyProperty OffsetProperty =
            DependencyProperty.Register("point", typeof(Point), typeof(ZoomableImage), new PropertyMetadata(new Point(0, 0)));


        public BitmapScalingMode ScalingMode
        {
            get { return (BitmapScalingMode)GetValue(ScalingModeProperty); }
            set { SetValue(ScalingModeProperty, value); }
        }

        public static readonly DependencyProperty ScalingModeProperty =
            DependencyProperty.Register("ScalingMode", typeof(BitmapScalingMode), typeof(ZoomableImage), new PropertyMetadata(BitmapScalingMode.Linear));




        public bool ClickableColors
        {
            get { return (bool)GetValue(ClickableColorsProperty); }
            set { SetValue(ClickableColorsProperty, value); }
        }

        public static readonly DependencyProperty ClickableColorsProperty =
            DependencyProperty.Register("ClickableColors", typeof(bool), typeof(ZoomableImage), new PropertyMetadata(false));



        public static readonly RoutedEvent ColorClickedEvent = EventManager.RegisterRoutedEvent(
            "Clicked", RoutingStrategy.Bubble,
            typeof(ColorClickedEventHandler), typeof(NumberSelect));

        public event ColorClickedEventHandler ColorClicked
        {
            add { AddHandler(ColorClickedEvent, value); }
            remove { RemoveHandler(ColorClickedEvent, value); }
        }


        VelocityDrivenAnimation smoothZoom;
        Storyboard smoothZoomStoryboard;

        public ZoomableImage()
        {
            InitializeComponent();
            DataContext = this;

            smoothZoom = new VelocityDrivenAnimation();
            smoothZoom.From = 1.0;
            smoothZoom.To = 1.0;
            smoothZoom.Duration = new Duration(TimeSpan.FromSeconds(0.1));

            smoothZoomStoryboard = new Storyboard();
            smoothZoomStoryboard.Children.Add(smoothZoom);
            smoothZoomStoryboard.SlipBehavior = SlipBehavior.Grow;
            Storyboard.SetTarget(smoothZoom, this);
            Storyboard.SetTargetProperty(smoothZoom, new PropertyPath(ZoomableImage.ZoomProperty));
        }

        private static void OnZoomChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            ((ZoomableImage)sender).UpdateZoomOffset((double)e.OldValue, (double)e.NewValue);
            ((ZoomableImage)sender).RefreshView();
        }

        //节能酱写的缓存字段 用于加快吸色
        private byte[] cachedPixels = null;
        private int cachedStride = 0;
        private int cachedWidth = 0;
        private int cachedHeight = 0;

        private void RefreshView()
        {
            if (Source == null) return;
            double aspect = Source.Width / Source.Height;
            double containerAspect = container.ActualWidth / container.ActualHeight;
            double width, height;
            if (aspect > containerAspect)
            {
                width = container.ActualWidth;
                height = container.ActualWidth / aspect;
            }
            else
            {
                width = container.ActualHeight * aspect;
                height = container.ActualHeight;
            }
            var zoom = Zoom;
            if (zoom < 1) zoom = 1;

            imageBorder.Width = width;
            imageBorder.Height = height;
            shownImage.Width = width;
            shownImage.Height = height;

            // 偏移量（Offset）转为像素
            double offsetX = Offset.X * width * zoom;
            double offsetY = Offset.Y * height * zoom;

            // 组合变换：缩放、平移、旋转/翻转
            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(zoom, zoom, width / 2, height / 2));
            group.Children.Add(new TranslateTransform(offsetX, offsetY));

            // 旋转和翻转
            if (ImageFlip)
                group.Children.Add(new ScaleTransform(-1, 1, width / 2, height / 2));
            if (ImageRotation != 0)
                group.Children.Add(new RotateTransform(ImageRotation, width / 2, height / 2));

            imageBorder.RenderTransform = group;
        }

        void UpdateZoomOffset(double oldval, double newval)
        {
            double scaleMult = newval / oldval;
            double aspect = Source.Width / Source.Height;
            double containerAspect = container.ActualWidth / container.ActualHeight;
            double width, height;
            if (aspect > containerAspect)
            {
                width = container.ActualWidth;
                height = container.ActualWidth / aspect;
            }
            else
            {
                width = container.ActualHeight * aspect;
                height = container.ActualHeight;
            }
            var pos = Mouse.GetPosition(container);
            pos = new Point(pos.X - (container.ActualWidth - width) / 2, pos.Y - (container.ActualHeight - height) / 2);
            pos = new Point(pos.X - width / 2, pos.Y - height / 2);
            pos = new Point(pos.X / width / Zoom + Offset.X, pos.Y / height / Zoom + Offset.Y);
            Offset = new Point((Offset.X - pos.X) * scaleMult + pos.X, (Offset.Y - pos.Y) * scaleMult + pos.Y);
            ClampOffset();
        }

        private void Container_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Source == null) return;
            double scaleMult = Math.Pow(2, e.Delta / 500.0);
            targetZoom *= scaleMult;

            if (targetZoom < 1)
            {
                targetZoom = 1;
                if (Zoom <= 1)
                {
                    scaleMult = scaleMult * scaleMult * scaleMult;
                    Offset = new Point(Offset.X * scaleMult, Offset.Y * scaleMult);
                    RefreshView();
                }
            }

            smoothZoom.From = Zoom;
            smoothZoom.To = targetZoom;
            smoothZoomStoryboard.Begin();

            ClampOffset();
        }

        private void Container_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshView();
        }

        bool mouseNotMoved = false;
        bool mouseIsDown = false;
        Point mouseMoveStart;
        Point offsetStart;
        private void Container_MouseDown(object sender, MouseButtonEventArgs e)
        {
            container.CaptureMouse();
            mouseIsDown = true;
            mouseNotMoved = true;
            mouseMoveStart = e.GetPosition(container);
            offsetStart = Offset;
        }

        void ClampOffset()
        {
            Offset = new Point(Offset.X > 0.5 ? 0.5 : Offset.X, Offset.Y > 0.5 ? 0.5 : Offset.Y);
            Offset = new Point(Offset.X < -0.5 ? -0.5 : Offset.X, Offset.Y < -0.5 ? -0.5 : Offset.Y);
        }

        private void Container_MouseMove(object sender, MouseEventArgs e)
        {
            if (mouseIsDown)
            {
                Point currentMousePos = e.GetPosition(container);
                Vector mouseOffset = currentMousePos - mouseMoveStart;

                if (mouseOffset.X != 0 || mouseOffset.Y != 0)
                {
                    // 旋转角度修正拖动方向
                    double angle = ImageRotation % 360;
                    if (angle < 0) angle += 360;
                    double radians = angle * Math.PI / 180.0;

                    // 逆时针旋转鼠标偏移向量
                    double cos = Math.Cos(-radians);
                    double sin = Math.Sin(-radians);
                    double dx = mouseOffset.X * cos - mouseOffset.Y * sin;
                    double dy = mouseOffset.X * sin + mouseOffset.Y * cos;

                    container.Cursor = Cursors.ScrollAll;
                    mouseNotMoved = false;

                    // 关键：拖动距离除以缩放比例
                    double zoom = Zoom;
                    if (zoom < 1) zoom = 1;

                    Offset = new Point(
                        Offset.X + dx / shownImage.ActualWidth / zoom,
                        Offset.Y + dy / shownImage.ActualHeight / zoom
                    );
                    ClampOffset();

                    mouseMoveStart = currentMousePos;
                    offsetStart = Offset;

                    RefreshView();
                }
            }
        }

        private void Container_MouseUp(object sender, MouseButtonEventArgs e)
        {
            container.ReleaseMouseCapture();
            if (!mouseIsDown) return;
            if (mouseNotMoved)
            {
                if (ClickableColors)
                {
                    // 获取鼠标在container内的坐标
                    var pos = e.GetPosition(container);

                    // 渲染container为位图
                    int width = (int)container.ActualWidth;
                    int height = (int)container.ActualHeight;
                    if (width > 0 && height > 0)
                    {
                        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                        rtb.Render(container);

                        // 读取像素
                        var pixels = new byte[width * height * 4];
                        rtb.CopyPixels(pixels, width * 4, 0);

                        int x = (int)pos.X;
                        int y = (int)pos.Y;
                        if (x >= 0 && x < width && y >= 0 && y < height)
                        {
                            int pixel = (y * width + x) * 4;
                            var c = Color.FromArgb(
                                pixels[pixel + 3],
                                pixels[pixel + 2],
                                pixels[pixel + 1],
                                pixels[pixel + 0]
                            );
                            RaiseEvent(new RoutedColorClickedEventArgs(c, ColorClickedEvent));
                        }
                    }
                }
            }
            else
            {
                container.Cursor = Cursor;
            }
            mouseIsDown = false;
        }
    }

    class VelocityDrivenAnimation : DoubleAnimationBase
    {


        public double From
        {
            get { return (double)GetValue(FromProperty); }
            set { SetValue(FromProperty, value); }
        }

        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register("From", typeof(double), typeof(VelocityDrivenAnimation), new PropertyMetadata(0.0));


        public double To
        {
            get { return (double)GetValue(ToProperty); }
            set { SetValue(ToProperty, value); }
        }

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register("To", typeof(double), typeof(VelocityDrivenAnimation), new PropertyMetadata(0.0));


        VelocityDrivenAnimation parent = null;
        double velocity = 0;

        public VelocityDrivenAnimation() { }

        protected override Freezable CreateInstanceCore()
        {
            double v = velocity;
            double s = From;
            double f = To;
            var instance = new VelocityDrivenAnimation()
            {
                parent = this,
                From = From,
                To = To,
                velocity = velocity
            };
            return instance;
        }

        double easeFunc(double x, double v) =>
            (-2 + 4 * x + v * (1 + 2 * x * (1 + x * (-5 - 2 * (x - 3) * x)))) /
            (4 + 8 * (x - 1) * x);
        double easeVelFunc(double x, double v) =>
            -((x - 1) * (2 * x + v * (x - 1) * (-1 + 4 * x * (1 + (x - 1) * x)))) /
            Math.Pow(1 + 2 * (x - 1) * x, 2);

        protected override double GetCurrentValueCore(double defaultOriginValue, double defaultDestinationValue, AnimationClock animationClock)
        {
            double s = From;
            double f = To;
            double dist = f - s;
            if (dist == 0)
            {
                parent.velocity = 0;
                return s;
            }
            double v = velocity / dist;
            double x = (double)animationClock.CurrentProgress;

            double ease = easeFunc(x, v) - easeFunc(0, v);
            double vel = easeVelFunc(x, v);

            parent.velocity = vel * dist;
            return ease * dist + s;
        }
    }
}
