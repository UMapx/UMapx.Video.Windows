using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using UMapx.Video;
using UMapx.Video.DirectShow;
using UMapx.Video.DirectShow.Internals;
using Xunit;
using Vfw = UMapx.Video.VFW.Win32;

namespace UMapx.Video.Windows.Tests;

public class FrameBufferTests
{
    [Theory]
    [InlineData(PixelFormat.Format32bppArgb)]
    [InlineData(PixelFormat.Format32bppPArgb)]
    [InlineData(PixelFormat.Format8bppIndexed)]
    public void UnsupportedCaptureFormatFailsBeforeStarting(PixelFormat format)
    {
        Assert.Throws<ArgumentException>(() => new VideoCaptureDevice("unused", format));
    }

    [Theory]
    [InlineData(PixelFormat.Format24bppRgb)]
    [InlineData(PixelFormat.Format32bppRgb)]
    public void SupportedCaptureFormatsAreAccepted(PixelFormat format)
    {
        using var source = new VideoCaptureDevice("unused", format);
    }

    [Theory]
    [InlineData(2, PixelFormat.Format24bppRgb)]
    [InlineData(-2, PixelFormat.Format24bppRgb)]
    [InlineData(2, PixelFormat.Format32bppRgb)]
    [InlineData(-2, PixelFormat.Format32bppRgb)]
    public void CopiesPaddedRowsWithCorrectOrientation(int signedHeight, PixelFormat format)
    {
        int bytesPerPixel = Image.GetPixelFormatSize(format) / 8;
        int stride = BitmapFrame.GetStride(3, format);
        var bytes = new byte[stride * 2];
        for (int row = 0; row < 2; row++)
            for (int x = 0; x < 3; x++)
                bytes[row * stride + x * bytesPerPixel + (row == 0 ? 2 : 0)] = 255;
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            using var image = BitmapFrame.Copy(pointer, bytes.Length, 3, signedHeight, format);
            Assert.Equal((signedHeight < 0 ? Color.Red : Color.Blue).ToArgb(), image.GetPixel(2, 0).ToArgb());
            Assert.Equal((signedHeight < 0 ? Color.Blue : Color.Red).ToArgb(), image.GetPixel(2, 1).ToArgb());
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    [Fact]
    public void RejectsTruncatedNullAndOverflowingBuffersBeforeCopy()
    {
        // This sentinel must never be dereferenced: each input fails validation first.
        var sentinel = new IntPtr(1);
        Assert.Throws<ArgumentException>(() => BitmapFrame.Copy(sentinel, 23, 4, 2, PixelFormat.Format24bppRgb));
        Assert.Throws<ArgumentNullException>(() => BitmapFrame.Copy(IntPtr.Zero, 24, 4, 2, PixelFormat.Format24bppRgb));
        Assert.Throws<ArgumentOutOfRangeException>(() => BitmapFrame.Copy(sentinel, 24, 4, 0, PixelFormat.Format24bppRgb));
        Assert.Throws<ArgumentOutOfRangeException>(() => BitmapFrame.Copy(sentinel, 24, 4, int.MinValue, PixelFormat.Format24bppRgb));
        Assert.Throws<OverflowException>(() => BitmapFrame.Copy(sentinel, int.MaxValue, int.MaxValue, 2, PixelFormat.Format32bppRgb));
    }

    [Fact]
    public void CompressionOptionsMatchWindowsPointerLayout()
    {
        Assert.Equal(IntPtr.Size == 8 ? 56 : 44, Marshal.SizeOf<Vfw.AVICOMPRESSOPTIONS>());
        Assert.Equal(24, Marshal.OffsetOf<Vfw.AVICOMPRESSOPTIONS>(nameof(Vfw.AVICOMPRESSOPTIONS.format)).ToInt32());
        Assert.Equal(IntPtr.Size == 8 ? 40 : 32, Marshal.OffsetOf<Vfw.AVICOMPRESSOPTIONS>(nameof(Vfw.AVICOMPRESSOPTIONS.parameters)).ToInt32());
        Assert.Equal(IntPtr.Size == 8 ? 48 : 36, Marshal.OffsetOf<Vfw.AVICOMPRESSOPTIONS>(nameof(Vfw.AVICOMPRESSOPTIONS.parametersSize)).ToInt32());
    }

    [Fact]
    public void NegotiatedFormatMustMatchRequestedPixelDepth()
    {
        using var media = new AMMediaType
        {
            MajorType = MediaType.Video,
            SubType = MediaSubType.RGB24,
            FormatType = FormatType.VideoInfo,
            FormatSize = Marshal.SizeOf<VideoInfoHeader>(),
            FormatPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<VideoInfoHeader>())
        };
        var header = new VideoInfoHeader
        {
            BmiHeader = new BitmapInfoHeader { Size = 40, Width = 3, Height = -2, Planes = 1, BitCount = 24 }
        };
        Marshal.StructureToPtr(header, media.FormatPtr, false);
        Assert.Equal(-2, RgbVideoFormat.Read(media, PixelFormat.Format24bppRgb).Height);
        Assert.Throws<NotSupportedException>(() => RgbVideoFormat.Read(media, PixelFormat.Format32bppRgb));
        media.FormatSize = 1;
        Assert.Throws<NotSupportedException>(() => RgbVideoFormat.Read(media, PixelFormat.Format24bppRgb));
    }

    [Fact]
    public void SnapshotRequiresOnlySnapshotSubscriber()
    {
        using var source = new VideoCaptureDevice();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var worker = (VideoSourceWorker)typeof(VideoCaptureDevice).GetField("worker", flags)!.GetValue(source)!;
        worker.Start(() => { }, () => { while (!worker.WaitForStopSignal(100)) { } }, "snapshot test");
        typeof(VideoCaptureDevice).GetField("startTime", flags)!.SetValue(source, DateTime.Now.AddSeconds(-10));
        int snapshots = 0;
        string error = null;
        source.VideoSourceError += (_, e) => error = e.Description;
        source.SnapshotFrame += (_, e) => { Assert.Equal(2, e.Frame.Width); snapshots++; };
        var type = typeof(VideoCaptureDevice).GetNestedType("Grabber", BindingFlags.NonPublic)!;
        var grabber = Activator.CreateInstance(type, source, true, PixelFormat.Format24bppRgb);
        type.GetProperty("Width")!.SetValue(grabber, 2);
        type.GetProperty("Height")!.SetValue(grabber, 2);
        var buffer = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.Copy(new byte[16], 0, buffer, 16);
            type.GetMethod("BufferCB")!.Invoke(grabber, new object[] { 0d, buffer, 16 });
            Assert.Equal(1, snapshots);
            type.GetMethod("BufferCB")!.Invoke(grabber, new object[] { 0d, new IntPtr(1), 15 });
            Assert.Equal(1, snapshots);
            Assert.Contains("smaller than", error);
            Assert.True(worker.IsStopping);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
