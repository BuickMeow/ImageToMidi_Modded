using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImageToMidi
{
    static class ResizeImage
    {
        public static byte[] MakeResizedImage(byte[] imageData, int imageStride, int newWidth, int newHeight)
        {
            int originalWidth = imageStride / 4;
            int originalHeight = imageData.Length / imageStride;

            byte[] resizedImage = new byte[newWidth * newHeight * 4];

            double scaleX = (double)originalWidth / newWidth;
            double scaleY = (double)originalHeight / newHeight;

            Parallel.For(0, newWidth * newHeight, p =>
            {
                int x = p % newWidth;
                int y = p / newWidth;

                double srcX = x * scaleX;
                double srcY = y * scaleY;

                int srcX1 = (int)Math.Floor(srcX);
                int srcY1 = (int)Math.Floor(srcY);
                int srcX2 = Math.Min(srcX1 + 1, originalWidth - 1);
                int srcY2 = Math.Min(srcY1 + 1, originalHeight - 1);

                double weightX = srcX - srcX1;
                double weightY = srcY - srcY1;

                for (int channel = 0; channel < 4; channel++)
                {
                    double value =
                        (1 - weightX) * (1 - weightY) * imageData[srcY1 * imageStride + srcX1 * 4 + channel] +
                        weightX * (1 - weightY) * imageData[srcY1 * imageStride + srcX2 * 4 + channel] +
                        (1 - weightX) * weightY * imageData[srcY2 * imageStride + srcX1 * 4 + channel] +
                        weightX * weightY * imageData[srcY2 * imageStride + srcX2 * 4 + channel];

                    resizedImage[y * newWidth * 4 + x * 4 + channel] = (byte)value;
                }
            });

            return resizedImage;
        }
    }
}