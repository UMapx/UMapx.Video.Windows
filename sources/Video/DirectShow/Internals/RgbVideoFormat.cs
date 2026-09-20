namespace UMapx.Video.DirectShow.Internals
{
    using System;
    using System.Drawing.Imaging;
    using System.Runtime.InteropServices;

    internal static class RgbVideoFormat
    {
        internal static BitmapInfoHeader Read(AMMediaType mediaType, PixelFormat format)
        {
            if (mediaType.MajorType != MediaType.Video || mediaType.SubType != MediaSubType.ConvertFrom(format) ||
                mediaType.FormatPtr == IntPtr.Zero)
                throw new NotSupportedException("The graph did not negotiate the requested RGB video format.");

            BitmapInfoHeader header;
            if (mediaType.FormatType == FormatType.VideoInfo && mediaType.FormatSize >= Marshal.SizeOf<VideoInfoHeader>())
                header = Marshal.PtrToStructure<VideoInfoHeader>(mediaType.FormatPtr).BmiHeader;
            else if (mediaType.FormatType == FormatType.VideoInfo2 && mediaType.FormatSize >= Marshal.SizeOf<VideoInfoHeader2>())
                header = Marshal.PtrToStructure<VideoInfoHeader2>(mediaType.FormatPtr).BmiHeader;
            else
                throw new NotSupportedException("The graph returned an invalid video format block.");

            if (header.BitCount != System.Drawing.Image.GetPixelFormatSize(format) || header.Compression != 0 ||
                header.Planes != 1 || header.Height == 0 || header.Height == int.MinValue)
                throw new NotSupportedException("The graph returned an unsupported RGB bitmap layout.");
            _ = checked(BitmapFrame.GetStride(header.Width, format) * Math.Abs(header.Height));
            return header;
        }
    }
}
