using System.Collections.Concurrent;
using System.Drawing;
using UMapx.Video;
using UMapx.Video.DirectShow;
using UMapx.Video.VFW;
using Xunit;
using static UMapx.Video.Windows.Tests.AsyncVideoSourceTests;

namespace UMapx.Video.Windows.Tests;

// Exercise real Windows VFW/DirectShow without accessing a physical camera.
public class NativeVideoSourceTests
{
    [Fact]
    public void Video1CompressionRoundTrip()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".avi");
        try
        {
            using (var writer = new AVIWriter("MSVC"))
            {
                writer.Open(path, 16, 16);
                using var frame = new Bitmap(16, 16);
                using var graphics = Graphics.FromImage(frame);
                graphics.Clear(Color.Red);
                writer.AddFrame(frame);
            }
            using var reader = new AVIReader();
            reader.Open(path);
            using var decoded = reader.GetNextFrame();
            Assert.InRange(decoded.GetPixel(8, 8).R, 240, 255);
            Assert.InRange(decoded.GetPixel(8, 8).B, 0, 15);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AviRoundTripPreservesFramesAndOrientation()
    {
        using var fixture = new AviFixture();
        using var reader = new AVIReader();
        reader.Open(fixture.Path);
        Assert.Equal(8, reader.Width);
        Assert.Equal(6, reader.Height);
        Assert.Equal(3, reader.Length);
        Assert.Equal(25, reader.FrameRate);
        using var frame = reader.GetNextFrame();
        Assert.Equal(Color.Red.ToArgb(), frame.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), frame.GetPixel(0, 5).ToArgb());
        reader.Dispose();
        reader.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reader.Open(fixture.Path));
    }

    [Fact]
    public void WriterRejectsInvalidSizesAndUseAfterDisposal()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".avi");
        using var writer = new AVIWriter();
        Assert.Throws<ArgumentException>(() => writer.Open(path, -2, 6));
        Assert.Throws<ArgumentException>(() => writer.Open(path, 0, 6));
        Assert.Throws<OverflowException>(() => writer.Open(path, int.MaxValue - 1, 6));
        writer.Dispose();
        writer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => writer.Open(path, 8, 6));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSourceCompletesAndRestarts(bool directShow)
    {
        using var fixture = new AviFixture();
        using var source = CreateSource(fixture.Path, directShow);
        var errors = new ConcurrentQueue<string>();
        var reasons = new ConcurrentQueue<ReasonToFinishPlaying>();
        int frames = 0;
        source.NewFrame += (_, _) => Interlocked.Increment(ref frames);
        source.VideoSourceError += (_, e) => errors.Enqueue(e.Description);
        source.PlayingFinished += (_, reason) => reasons.Enqueue(reason);
        for (int i = 0; i < 3; i++)
        {
            source.Start();
            await Within(Task.Run(source.WaitForStop));
            Assert.False(source.IsRunning);
        }
        Assert.Empty(errors);
        Assert.Equal(9, frames);
        Assert.Equal(3, reasons.Count);
        Assert.All(reasons, reason => Assert.Equal(ReasonToFinishPlaying.EndOfStreamReached, reason));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task NativeShutdownFromCallbackCompletes(bool directShow, bool dispose)
    {
        using var fixture = new AviFixture();
        using var source = CreateSource(fixture.Path, directShow);
        var returned = Completion();
        var errors = new ConcurrentQueue<string>();
        source.VideoSourceError += (_, e) => errors.Enqueue(e.Description);
        source.NewFrame += (_, _) =>
        {
            if (dispose) source.Dispose();
            else source.Stop();
            returned.TrySetResult(true);
        };
        source.Start();
        await Within(returned.Task);
        await Within(Task.Run(source.WaitForStop));
        Assert.False(source.IsRunning);
        Assert.Empty(errors);
        if (dispose) Assert.Throws<ObjectDisposedException>(source.Start);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeDuringCallbackWaitsWithoutClosingSynchronization(bool directShow)
    {
        using var fixture = new AviFixture();
        using var source = CreateSource(fixture.Path, directShow);
        using var release = new ManualResetEventSlim();
        var entered = Completion();
        var errors = new ConcurrentQueue<string>();
        source.VideoSourceError += (_, e) => errors.Enqueue(e.Description);
        source.NewFrame += (_, _) => { entered.TrySetResult(true); release.Wait(); };
        source.Start();
        await Within(entered.Task);
        var disposing = Task.Run(source.Dispose);
        try
        {
            await Task.Delay(50);
            Assert.False(disposing.IsCompleted);
        }
        finally { release.Set(); }
        await Within(disposing);
        Assert.Empty(errors);
        Assert.False(source.IsRunning);
        source.SignalToStop(); // idempotent, even after disposal
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncWrapperDrainsRealNativeSource(bool directShow)
    {
        using var fixture = new AviFixture();
        using var source = new AsyncVideoSource(CreateSource(fixture.Path, directShow));
        int count = 0;
        var errors = new ConcurrentQueue<string>();
        source.NewFrame += (_, _) => Interlocked.Increment(ref count);
        source.VideoSourceError += (_, e) => errors.Enqueue(e.Description);
        source.Start();
        await Within(Task.Run(source.WaitForStop));
        Assert.Equal(3, count);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ConcurrentNativeShutdownIsIdempotent()
    {
        using var fixture = new AviFixture();
        var source = new AVIFileVideoSource(fixture.Path);
        source.Start();
        await Within(Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() =>
        {
            if (i % 3 == 0) source.Stop();
            else if (i % 3 == 1) source.Dispose();
            else { source.SignalToStop(); source.WaitForStop(); }
        }))));
        Assert.False(source.IsRunning);
        Assert.Throws<ObjectDisposedException>(source.Start);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrameHandlerFailureStopsNativeSourceAndReportsError(bool directShow)
    {
        using var fixture = new AviFixture();
        using var source = CreateSource(fixture.Path, directShow);
        string error = null;
        ReasonToFinishPlaying? finished = null;
        source.NewFrame += (_, _) => throw new InvalidOperationException("frame handler failed");
        source.VideoSourceError += (_, e) => error = e.Description;
        source.PlayingFinished += (_, reason) => finished = reason;
        source.Start();
        await Within(Task.Run(source.WaitForStop));
        Assert.Equal("frame handler failed", error);
        Assert.Equal(ReasonToFinishPlaying.VideoSourceError, finished);
    }

    private static IVideoSource CreateSource(string path, bool directShow) =>
        directShow ? new FileVideoSource(path) : new AVIFileVideoSource(path);

    private sealed class AviFixture : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".avi");
        internal AviFixture()
        {
            using var writer = new AVIWriter();
            writer.Open(Path, 8, 6);
            using var frame = new Bitmap(8, 6);
            using var graphics = Graphics.FromImage(frame);
            graphics.Clear(Color.Red);
            graphics.FillRectangle(Brushes.Blue, 0, 3, 8, 3);
            for (int i = 0; i < 3; i++) writer.AddFrame(frame);
        }
        public void Dispose() => File.Delete(Path);
    }
}
