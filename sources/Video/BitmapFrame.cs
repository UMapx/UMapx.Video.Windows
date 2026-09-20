namespace UMapx.Video
{
    using System;
    using System.Drawing;
    using System.Drawing.Imaging;

    internal static class BitmapFrame
    {
        internal static int GetStride(int width, PixelFormat format)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (format != PixelFormat.Format24bppRgb && format != PixelFormat.Format32bppRgb)
                throw new ArgumentException("Only Format24bppRgb and Format32bppRgb are supported.", nameof(format));
            return checked((checked(width * (Image.GetPixelFormatSize(format) / 8)) + 3) & ~3);
        }

        // A positive DIB height means bottom-up; a negative height means top-down.
        internal static Bitmap Copy(IntPtr buffer, int bufferLength, int width, int signedHeight, PixelFormat format)
        {
            int stride = GetStride(width, format);
            if (signedHeight == 0 || signedHeight == int.MinValue)
                throw new ArgumentOutOfRangeException(nameof(signedHeight));
            int height = Math.Abs(signedHeight);
            int required = checked(stride * height);
            if (buffer == IntPtr.Zero) throw new ArgumentNullException(nameof(buffer));
            if (bufferLength < required)
                throw new ArgumentException("The video buffer is smaller than the negotiated frame.", nameof(bufferLength));

            var image = new Bitmap(width, height, format);
            try
            {
                var data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, format);
                try
                {
                    int rowBytes = checked(width * (Image.GetPixelFormatSize(format) / 8));
                    for (int y = 0; y < height; y++)
                    {
                        int row = signedHeight > 0 ? height - 1 - y : y;
                        SystemTools.CopyUnmanagedMemory(IntPtr.Add(data.Scan0, checked(row * data.Stride)),
                            IntPtr.Add(buffer, checked(y * stride)), rowBytes);
                    }
                }
                finally { image.UnlockBits(data); }
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }
    }
}
