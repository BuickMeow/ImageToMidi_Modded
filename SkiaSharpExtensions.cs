using SkiaSharp;
using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ImageToMidi
{
    public static class SkiaSharpExtensions
    {
        public static async Task<SKBitmap> ToSKBitmapAsync(this BitmapSource bitmapSource, CancellationToken cancellationToken = default)
        {
            if (bitmapSource == null) return null;

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return ConvertDirectPixelCopyParallel(bitmapSource, cancellationToken);
                }
                catch
                {
                    return ConvertViaEncoding(bitmapSource);
                }
            }, cancellationToken);
        }

        public static SKBitmap ToSKBitmap(this BitmapSource bitmapSource)
        {
            if (bitmapSource == null) return null;

            try
            {
                return ConvertDirectPixelCopyParallel(bitmapSource, CancellationToken.None);
            }
            catch
            {
                return ConvertViaEncoding(bitmapSource);
            }
        }

        private static SKBitmap ConvertDirectPixelCopyParallel(BitmapSource bitmapSource, CancellationToken cancellationToken)
        {
            var width = bitmapSource.PixelWidth;
            var height = bitmapSource.PixelHeight;
            var pixelSize = width * height * 4;

            var convertedSource = bitmapSource.Format != System.Windows.Media.PixelFormats.Bgra32
                ? new FormatConvertedBitmap(bitmapSource, System.Windows.Media.PixelFormats.Bgra32, null, 0)
                : bitmapSource;

            SKBitmap skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

            byte[] pixels = pixelSize >= 85000
                ? new byte[pixelSize]
                : ArrayPool<byte>.Shared.Rent(pixelSize);

            try
            {
                int stride = width * 4;
                convertedSource.CopyPixels(pixels, stride, 0);

                IntPtr dstPtr = skBitmap.GetPixels();
                unsafe
                {
                    byte* dst = (byte*)dstPtr.ToPointer();
                    Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, y =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Buffer.MemoryCopy(
                            source: Unsafe.AsPointer(ref pixels[y * stride]),
                            destination: dst + y * stride,
                            destinationSizeInBytes: stride,
                            sourceBytesToCopy: stride
                        );
                    });
                }
            }
            finally
            {
                if (pixelSize < 85000)
                    ArrayPool<byte>.Shared.Return(pixels);
            }

            return skBitmap;
        }

        private static SKBitmap ConvertViaEncoding(BitmapSource bitmapSource)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                stream.Position = 0;
                return SKBitmap.Decode(stream);
            }
        }

        public static async Task<(SKBitmap bitmap, SKImage image)> LoadImageAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var bitmap = SKBitmap.Decode(filePath);
                    var image = SKImage.FromBitmap(bitmap);
                    return (bitmap, image);
                }
                catch
                {
                    return (null, null);
                }
            });
        }
    }
}