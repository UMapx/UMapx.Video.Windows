using System.Drawing;
using UMapx.Video;
using Xunit;

namespace UMapx.Video.Windows.Tests;

public class AsyncVideoSourceTests
{
    internal static TaskCompletionSource<bool> Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static Task Within(Task task) => task.WaitAsync(TimeSpan.FromSeconds(5));

    [Fact]
    public void RejectsNullSource() => Assert.Throws<ArgumentNullException>(() => new AsyncVideoSource(null));

    [Fact]
    public async Task DisposeStopsIdleWorkerAndUnsubscribes()
    {
        var nested = new TestSource();
        var source = new AsyncVideoSource(nested);
        source.Start();
        await Within(Task.Run(source.Dispose));
        Assert.False(source.IsRunning);
        Assert.Equal(0, nested.Subscribers);
        Assert.Equal(1, nested.DisposeCount);
        source.Dispose();
        Assert.Equal(1, nested.DisposeCount);
        Assert.Throws<ObjectDisposedException>(source.Start);
    }

    [Fact]
    public async Task FailedStartUnsubscribesAndCanRetry()
    {
        var nested = new TestSource { StartAction = () => throw new ArgumentException("bad source") };
        using var source = new AsyncVideoSource(nested);
        await Within(Task.Run(() => Assert.Throws<ArgumentException>(source.Start)));
        Assert.False(source.IsRunning);
        Assert.Equal(0, nested.Subscribers);
        nested.StartAction = null;
        source.Start();
        source.SignalToStop();
        await Within(Task.Run(source.WaitForStop));
        Assert.False(source.IsRunning);
    }

    [Theory]
    [InlineData("signal")]
    [InlineData("stop")]
    [InlineData("dispose")]
    public async Task ShutdownFromFrameCallbackDoesNotDeadlock(string method)
    {
        var nested = new TestSource();
        using var source = new AsyncVideoSource(nested);
        var returned = Completion();
        source.NewFrame += (_, _) =>
        {
            if (method == "signal") source.SignalToStop();
            else if (method == "stop") source.Stop();
            else source.Dispose();
            returned.TrySetResult(true);
        };
        source.Start();
        nested.Emit();
        await Within(returned.Task);
        await Within(Task.Run(source.WaitForStop));
        Assert.False(source.IsRunning);
        Assert.Equal(0, nested.Subscribers);
        if (method == "dispose") Assert.Equal(1, nested.DisposeCount);
    }

    [Fact]
    public async Task DisposeWaitsForActiveFrameBeforeDisposingNestedSource()
    {
        var nested = new TestSource();
        var source = new AsyncVideoSource(nested);
        using var release = new ManualResetEventSlim();
        var entered = Completion();
        source.NewFrame += (_, _) => { entered.TrySetResult(true); release.Wait(); };
        source.Start();
        nested.Emit();
        await Within(entered.Task);
        var disposing = Task.Run(source.Dispose);
        try
        {
            await Task.Delay(50);
            Assert.False(disposing.IsCompleted);
            Assert.Equal(0, nested.DisposeCount);
        }
        finally { release.Set(); }
        await Within(disposing);
        Assert.False(source.IsRunning);
        Assert.Equal(1, nested.DisposeCount);
    }

    [Fact]
    public async Task SignalWakesBlockedProducer()
    {
        var nested = new TestSource();
        using var source = new AsyncVideoSource(nested);
        using var release = new ManualResetEventSlim();
        var entered = Completion();
        source.NewFrame += (_, _) => { entered.TrySetResult(true); release.Wait(); };
        source.Start();
        nested.Emit();
        await Within(entered.Task);
        var producer = Task.Run(nested.Emit);
        try
        {
            source.SignalToStop();
            await Within(producer);
        }
        finally { release.Set(); }
        await Within(Task.Run(source.WaitForStop));
    }

    [Fact]
    public async Task SkipFramesDropsBusyFrame()
    {
        var nested = new TestSource();
        using var source = new AsyncVideoSource(nested, true);
        using var release = new ManualResetEventSlim();
        var entered = Completion();
        int count = 0;
        source.NewFrame += (_, _) => { Interlocked.Increment(ref count); entered.TrySetResult(true); release.Wait(); };
        source.Start();
        nested.Emit();
        await Within(entered.Task);
        try { await Within(Task.Run(nested.Emit)); }
        finally { release.Set(); }
        source.Stop();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task NaturalCompletionDrainsAcceptedFrameAndAllowsRestart()
    {
        var nested = new TestSource();
        using var source = new AsyncVideoSource(nested);
        int count = 0;
        ReasonToFinishPlaying? reason = null;
        source.NewFrame += (_, _) => Interlocked.Increment(ref count);
        source.PlayingFinished += (_, value) => reason = value;
        for (int i = 0; i < 3; i++)
        {
            source.Start();
            nested.Emit();
            nested.Finish();
            await Within(Task.Run(source.WaitForStop));
            Assert.False(source.IsRunning);
            Assert.Equal(0, nested.Subscribers);
            Assert.Equal(1, source.FramesProcessed);
        }
        Assert.Equal(3, count);
        Assert.Equal(0, source.FramesProcessed);
        Assert.Equal(ReasonToFinishPlaying.EndOfStreamReached, reason);
    }

    [Fact]
    public async Task FrameExceptionReportsErrorAndCleansUp()
    {
        var nested = new TestSource();
        using var source = new AsyncVideoSource(nested);
        var error = Completion();
        source.NewFrame += (_, _) => throw new InvalidOperationException("handler failure");
        source.VideoSourceError += (_, e) => error.TrySetResult(e.Description == "handler failure");
        source.Start();
        nested.Emit();
        Assert.True(await error.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Within(Task.Run(source.WaitForStop));
        Assert.Equal(0, nested.Subscribers);
    }

    [Fact]
    public async Task DisposeDuringStartWaitsAndStopsSourceAfterStartup()
    {
        using var release = new ManualResetEventSlim();
        var entered = Completion();
        var nested = new TestSource { StartAction = () => { entered.TrySetResult(true); release.Wait(); } };
        var source = new AsyncVideoSource(nested);
        var starting = Task.Run(source.Start);
        await Within(entered.Task);
        var disposing = Task.Run(source.Dispose);
        try
        {
            await Task.Delay(50);
            Assert.False(disposing.IsCompleted);
        }
        finally { release.Set(); }
        await Within(Task.WhenAll(starting, disposing));
        Assert.False(nested.IsRunning);
        Assert.False(source.IsRunning);
        Assert.Equal(1, nested.DisposeCount);
    }

    [Fact]
    public async Task DisposeFromFinishedCallbackDisposesNestedSource()
    {
        var nested = new TestSource();
        var source = new AsyncVideoSource(nested);
        source.PlayingFinished += (_, _) => source.Dispose();
        source.Start();
        nested.Finish();
        await Within(Task.Run(source.WaitForStop));
        Assert.Equal(1, nested.DisposeCount);
    }

    [Fact]
    public async Task SynchronousFramesDuringStartCanRequestDisposal()
    {
        var nested = new TestSource();
        var returned = Completion();
        var source = new AsyncVideoSource(nested);
        nested.StartAction = () => { nested.Emit(); nested.Emit(); };
        source.NewFrame += (_, _) => { source.Dispose(); returned.TrySetResult(true); };
        await Within(Task.Run(source.Start));
        await Within(returned.Task);
        await Within(Task.Run(source.WaitForStop));
        Assert.False(source.IsRunning);
        Assert.False(nested.IsRunning);
        Assert.Equal(0, nested.Subscribers);
        Assert.Equal(1, nested.DisposeCount);
    }

    [Fact]
    public async Task RepeatedConcurrentShutdownLeavesNoSubscribers()
    {
        for (int iteration = 0; iteration < 25; iteration++)
        {
            var nested = new TestSource();
            var source = new AsyncVideoSource(nested);
            source.Start();
            await Within(Task.WhenAll(Task.Run(source.Stop), Task.Run(source.Dispose), Task.Run(source.Dispose)));
            Assert.False(source.IsRunning);
            Assert.Equal(0, nested.Subscribers);
            Assert.Equal(1, nested.DisposeCount);
        }
    }

    private sealed class TestSource : IVideoSource
    {
        private int running;
        public Action StartAction;
        public int DisposeCount;
        public int Subscribers => (NewFrame?.GetInvocationList().Length ?? 0) +
            (VideoSourceError?.GetInvocationList().Length ?? 0) + (PlayingFinished?.GetInvocationList().Length ?? 0);
        public event NewFrameEventHandler NewFrame;
        public event VideoSourceErrorEventHandler VideoSourceError;
        public event PlayingFinishedEventHandler PlayingFinished;
        public string Source => "test";
        public int FramesReceived => 0;
        public long BytesReceived => 0;
        public bool IsRunning => Volatile.Read(ref running) != 0;
        public void Start() { StartAction?.Invoke(); Volatile.Write(ref running, 1); }
        public void SignalToStop() { if (Interlocked.Exchange(ref running, 0) != 0) PlayingFinished?.Invoke(this, ReasonToFinishPlaying.StoppedByUser); }
        public void WaitForStop() { }
        public void Stop() => SignalToStop();
        public void Dispose() { SignalToStop(); Interlocked.Increment(ref DisposeCount); }
        public void Emit() { using var frame = new Bitmap(4, 4); NewFrame?.Invoke(this, new NewFrameEventArgs(frame)); }
        public void Finish() { Volatile.Write(ref running, 0); PlayingFinished?.Invoke(this, ReasonToFinishPlaying.EndOfStreamReached); }
    }
}
