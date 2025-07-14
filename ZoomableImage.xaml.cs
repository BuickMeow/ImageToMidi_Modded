using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
//using SkiaSharp.Views.WPF;
//using SkiaSharp.Extended.Svg;

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
        // SkiaSharp 相关字段
        private SKBitmap _bitmap;
        private SKImage _skImage;
        private SKPaint _highQualityPaint;
        private SKMatrix _transformMatrix = SKMatrix.Identity;

        // 缓存 SKSamplingOptions，避免重复创建
        private SKSamplingOptions _nearestSampling;
        private SKSamplingOptions _linearSampling;

        // 缓存计算结果，避免重复计算
        private double _cachedContainerWidth;
        private double _cachedContainerHeight;
        private bool _needsMatrixUpdate = true;

        // 缓存上次的 ScalingMode，避免重复设置
        private BitmapScalingMode _lastScalingMode = BitmapScalingMode.Unspecified;

        #region Dependency Properties

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

        // 支持 SVG 绘制
        public DrawingGroup SvgDrawing
        {
            get { return (DrawingGroup)GetValue(SvgDrawingProperty); }
            set { SetValue(SvgDrawingProperty, value); }
        }
        public static readonly DependencyProperty SvgDrawingProperty =
            DependencyProperty.Register("SvgDrawing", typeof(DrawingGroup), typeof(ZoomableImage), new PropertyMetadata(null));

        // 高性能的 Source 属性，支持 BitmapSource
        public BitmapSource Source
        {
            get { return (BitmapSource)GetValue(SourceProperty); }
            set
            {
                SetValue(SourceProperty, value);
                SetBitmapSource(value);
            }
        }
        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register("Source", typeof(ImageSource), typeof(ZoomableImage), new PropertyMetadata(null));

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

        #endregion

        #region Events

        public static readonly RoutedEvent ColorClickedEvent = EventManager.RegisterRoutedEvent(
            "Clicked", RoutingStrategy.Bubble,
            typeof(ColorClickedEventHandler), typeof(ZoomableImage));

        public event ColorClickedEventHandler ColorClicked
        {
            add { AddHandler(ColorClickedEvent, value); }
            remove { RemoveHandler(ColorClickedEvent, value); }
        }

        #endregion

        #region Private Fields

        double targetZoom = 1;
        VelocityDrivenAnimation smoothZoom;
        Storyboard smoothZoomStoryboard;

        bool mouseNotMoved = false;
        bool mouseIsDown = false;
        Point mouseMoveStart;
        Point offsetStart;

        #endregion

        #region Constructor and Initialization

        public ZoomableImage()
        {
            InitializeComponent();
            DataContext = this;

            // 资源清理事件
            this.Unloaded += ZoomableImage_Unloaded;
            // 初始化 SkiaSharp 相关资源
            InitializeSkiaSharp();

            // 初始化平滑缩放动画
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

        private void InitializeSkiaSharp()
        {
            // 初始化高质量绘制画笔
            _highQualityPaint = new SKPaint
            {
                IsAntialias = false
            };

            // 预创建 SKSamplingOptions，避免每次绘制时创建
            _nearestSampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
            _linearSampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        }

        #endregion

        #region Resource Management

        private void ZoomableImage_Unloaded(object sender, RoutedEventArgs e)
        {
            // 完整的资源清理
            _bitmap?.Dispose();
            _bitmap = null;
            _skImage?.Dispose();
            _skImage = null;
            _highQualityPaint?.Dispose();
            _highQualityPaint = null;
        }

        #endregion

        #region Image Loading and Processing

        // 修改 SetSKBitmap 方法，增加参数验证和更好的资源管理
        public void SetSKBitmap(SKBitmap bitmap)
        {
            // 释放旧的资源
            _bitmap?.Dispose();
            _bitmap = null;
            _skImage?.Dispose();
            _skImage = null;

            if (bitmap != null)
            {
                _bitmap = bitmap; // 直接使用，不复制
                if (_bitmap != null)
                {
                    _skImage = SKImage.FromBitmap(_bitmap);
                }
            }

            _needsMatrixUpdate = true;
            RefreshView();
        }

        // 增加清理方法
        public void ClearImage()
        {
            SetSKBitmap(null);
            Source = null; // 确保 WPF Source 也被清理
        }

        // 修改 SetBitmapSource 为私有方法
        private void SetBitmapSource(BitmapSource source)
        {
            // 释放旧的资源
            _bitmap?.Dispose();
            _bitmap = null;
            _skImage?.Dispose();
            _skImage = null;

            if (source != null)
            {
                // 转换为 SKBitmap
                _bitmap = source.ToSKBitmap();

                // 立即创建 SKImage 并缓存
                if (_bitmap != null)
                {
                    _skImage = SKImage.FromBitmap(_bitmap);
                }
            }

            _needsMatrixUpdate = true;
            RefreshView();
        }

        #endregion

        #region Event Handlers

        private static void OnTransformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ZoomableImage)d;
            control._needsMatrixUpdate = true;
            control.RefreshView();
        }

        private static void OnZoomChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            var control = (ZoomableImage)sender;
            control.UpdateZoomOffset((double)e.OldValue, (double)e.NewValue);
            control._needsMatrixUpdate = true;
            control.RefreshView();
        }

        #endregion

        #region SkiaSharp Rendering

        // 优化后的 OnPaintSurface 方法
        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            // 使用缓存的 SKImage，避免在 _skImage 为 null 时继续执行
            if (_skImage == null) return;

            // 只在 ScalingMode 真正改变时才更新画笔设置
            if (_lastScalingMode != ScalingMode)
            {
                switch (ScalingMode)
                {
                    case BitmapScalingMode.NearestNeighbor:
                        _highQualityPaint.IsAntialias = false;
                        break;
                    case BitmapScalingMode.Unspecified:
                    default:
                        _highQualityPaint.IsAntialias = true;
                        break;
                }
                _lastScalingMode = ScalingMode;
            }

            // 更新变换矩阵（仅在需要时）
            if (_needsMatrixUpdate ||
                Math.Abs(_cachedContainerWidth - container.ActualWidth) > 0.1 ||
                Math.Abs(_cachedContainerHeight - container.ActualHeight) > 0.1)
            {
                UpdateTransformMatrix();
                _cachedContainerWidth = container.ActualWidth;
                _cachedContainerHeight = container.ActualHeight;
                _needsMatrixUpdate = false;
            }

            canvas.Save();
            canvas.SetMatrix(in _transformMatrix);

            // 使用缓存的 SKSamplingOptions
            SKSamplingOptions samplingOptions = ScalingMode == BitmapScalingMode.NearestNeighbor
                ? _nearestSampling
                : _linearSampling;

            // 直接使用缓存的 SKImage，避免每次都从 SKBitmap 创建
            var destRect = new SKRect(0, 0, _skImage.Width, _skImage.Height);
            canvas.DrawImage(_skImage, destRect, samplingOptions, _highQualityPaint);

            canvas.Restore();
        }

        private void RefreshView()
        {
            skiaElement.InvalidateVisual();
        }

        #endregion

        #region Transform Matrix

        // UpdateTransformMatrix 方法改用 _skImage
        private void UpdateTransformMatrix()
        {
            if (_skImage == null || container.ActualWidth <= 0 || container.ActualHeight <= 0)
            {
                _transformMatrix = SKMatrix.Identity;
                return;
            }

            float canvasWidth = (float)container.ActualWidth;
            float canvasHeight = (float)container.ActualHeight;
            float imageWidth = _skImage.Width;
            float imageHeight = _skImage.Height;

            // 1. 计算适应容器的缩放比例（保持宽高比）
            float imageAspect = imageWidth / imageHeight;
            float canvasAspect = canvasWidth / canvasHeight;
            float initialScale = imageAspect > canvasAspect ?
                canvasWidth / imageWidth :
                canvasHeight / imageHeight;

            // 2. 计算缩放后的图像尺寸
            float scaledWidth = imageWidth * initialScale;
            float scaledHeight = imageHeight * initialScale;

            // 3. 计算画布中心和图像中心的偏移量
            float canvasCenterX = canvasWidth / 2;
            float canvasCenterY = canvasHeight / 2;
            float imageCenterX = imageWidth / 2;
            float imageCenterY = imageHeight / 2;

            // 4. 构建变换矩阵：先缩放，后平移到中心
            var matrix = SKMatrix.CreateScale(initialScale, initialScale);
            matrix = matrix.PostConcat(SKMatrix.CreateTranslation(
                canvasCenterX - imageCenterX * initialScale,
                canvasCenterY - imageCenterY * initialScale
            ));

            // 5. 应用用户缩放（以画布中心为缩放中心）
            float userZoom = Math.Max(1.0f, (float)Zoom);
            matrix = matrix.PostConcat(SKMatrix.CreateScale(userZoom, userZoom, canvasCenterX, canvasCenterY));

            // 6. 应用用户平移
            float offsetX = (float)Offset.X * scaledWidth * userZoom;
            float offsetY = (float)Offset.Y * scaledHeight * userZoom;
            matrix = matrix.PostConcat(SKMatrix.CreateTranslation(offsetX, offsetY));

            // 7. 应用旋转和翻转（以画布中心为变换中心）
            if (ImageFlip)
            {
                matrix = matrix.PostConcat(SKMatrix.CreateScale(-1, 1, canvasCenterX, canvasCenterY));
            }
            if (ImageRotation != 0)
            {
                matrix = matrix.PostConcat(SKMatrix.CreateRotationDegrees(ImageRotation, canvasCenterX, canvasCenterY));
            }

            _transformMatrix = matrix;
        }

        #endregion

        #region Zoom and Pan

        void UpdateZoomOffset(double oldval, double newval)
        {
            if (_skImage == null || container.ActualWidth <= 0 || container.ActualHeight <= 0) return;

            double scaleMult = newval / oldval;
            double aspect = _skImage.Width / (double)_skImage.Height;
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
            pos = new Point(pos.X / width / Math.Max(1.0, oldval) + Offset.X, pos.Y / height / Math.Max(1.0, oldval) + Offset.Y);

            Offset = new Point((Offset.X - pos.X) * scaleMult + pos.X, (Offset.Y - pos.Y) * scaleMult + pos.Y);
            ClampOffset();
        }

        void ClampOffset()
        {
            Offset = new Point(
                Math.Max(-0.5, Math.Min(0.5, Offset.X)),
                Math.Max(-0.5, Math.Min(0.5, Offset.Y))
            );
        }

        #endregion

        #region Mouse Events

        private void Container_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_skImage == null) return;

            double scaleMult = Math.Pow(2, e.Delta / 500.0);
            targetZoom *= scaleMult;

            if (targetZoom < 1)
            {
                targetZoom = 1;
                if (Zoom <= 1)
                {
                    scaleMult = scaleMult * scaleMult * scaleMult;
                    Offset = new Point(Offset.X * scaleMult, Offset.Y * scaleMult);
                    _needsMatrixUpdate = true;
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
            _needsMatrixUpdate = true;
            RefreshView();
        }

        private void Container_MouseDown(object sender, MouseButtonEventArgs e)
        {
            container.CaptureMouse();
            mouseIsDown = true;
            mouseNotMoved = true;
            mouseMoveStart = e.GetPosition(container);
            offsetStart = Offset;
        }

        private void Container_MouseMove(object sender, MouseEventArgs e)
        {
            if (_skImage == null) return;
            if (!mouseIsDown) return;

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

                // 根据当前缩放级别调整拖动敏感度
                double zoom = Math.Max(1.0, Zoom);
                double sensitivity = 1.0 / zoom;

                // 使用 SKImage 的尺寸
                double aspect = _skImage.Width / (double)_skImage.Height;
                double containerAspect = container.ActualWidth / container.ActualHeight;
                double imageWidth = aspect > containerAspect ? container.ActualWidth : container.ActualHeight * aspect;
                double imageHeight = aspect > containerAspect ? container.ActualWidth / aspect : container.ActualHeight;

                Offset = new Point(
                    Offset.X + dx * sensitivity / imageWidth,
                    Offset.Y + dy * sensitivity / imageHeight
                );
                ClampOffset();

                mouseMoveStart = currentMousePos;
                _needsMatrixUpdate = true;
                RefreshView();
            }
        }

        private void Container_MouseUp(object sender, MouseButtonEventArgs e)
        {
            container.ReleaseMouseCapture();
            if (!mouseIsDown) return;

            if (mouseNotMoved && ClickableColors && _bitmap != null)
            {
                // 颜色拾取仍然需要使用 SKBitmap
                var viewPoint = e.GetPosition(skiaElement);
                var color = GetColorAtPoint(viewPoint);
                if (color.HasValue)
                {
                    var wpfColor = Color.FromArgb(color.Value.Alpha, color.Value.Red, color.Value.Green, color.Value.Blue);
                    RaiseEvent(new RoutedColorClickedEventArgs(wpfColor, ColorClickedEvent));
                }
            }
            else
            {
                container.Cursor = Cursor;
            }
            mouseIsDown = false;
        }

        #endregion

        #region Color Picking

        // 高性能颜色拾取方法
        private SKColor? GetColorAtPoint(Point viewPoint)
        {
            if (_bitmap == null) return null;

            try
            {
                // 将视图坐标转换为图像坐标
                if (_transformMatrix.TryInvert(out var invertedMatrix))
                {
                    var imagePoint = invertedMatrix.MapPoint(new SKPoint((float)viewPoint.X, (float)viewPoint.Y));

                    int x = (int)Math.Round(imagePoint.X);
                    int y = (int)Math.Round(imagePoint.Y);

                    // 边界检查
                    if (x >= 0 && x < _bitmap.Width && y >= 0 && y < _bitmap.Height)
                    {
                        return _bitmap.GetPixel(x, y);
                    }
                }
            }
            catch
            {
                // 在异常情况下返回 null
            }

            return null;
        }

        #endregion
    }

    #region VelocityDrivenAnimation

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
            var instance = new VelocityDrivenAnimation()
            {
                parent = this,
                From = From,
                To = To,
                velocity = velocity
            };
            return instance;
        }

        double EaseFunc(double x, double v) =>
            (-2 + 4 * x + v * (1 + 2 * x * (1 + x * (-5 - 2 * (x - 3) * x)))) /
            (4 + 8 * (x - 1) * x);

        double EaseVelFunc(double x, double v) =>
            -((x - 1) * (2 * x + v * (x - 1) * (-1 + 4 * x * (1 + (x - 1) * x)))) /
            Math.Pow(1 + 2 * (x - 1) * x, 2);

        protected override double GetCurrentValueCore(double defaultOriginValue, double defaultDestinationValue, AnimationClock animationClock)
        {
            double s = From;
            double f = To;
            double dist = f - s;
            if (dist == 0)
            {
                if (parent != null) parent.velocity = 0;
                return s;
            }
            double v = velocity / dist;
            double x = (double)animationClock.CurrentProgress;

            double ease = EaseFunc(x, v) - EaseFunc(0, v);
            double vel = EaseVelFunc(x, v);

            if (parent != null) parent.velocity = vel * dist;
            return ease * dist + s;
        }
    }

    #endregion
}