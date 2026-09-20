namespace UMapx.Video
{
    using System;
    using System.Drawing;
    using System.Threading;

    /// <summary>Processes frames from a nested video source on a separate thread.</summary>
    /// <remarks>
    /// Clone a frame to retain it after its handler returns. SignalToStop never waits.
    /// Stop, WaitForStop and Dispose wait outside video callbacks; inside a callback,
    /// shutdown completes after it returns. Disposal also disposes the nested source.
    /// Frame-handler exceptions are reported through VideoSourceError and stop processing.
    /// </remarks>
    public class AsyncVideoSource : IVideoSource
    {
        private readonly IVideoSource nestedVideoSource;
        private readonly object sync = new object();
        private ProcessingSession session;
        private volatile bool skipFramesIfBusy;
        private bool disposed;
        private int nestedDisposed;
        private int framesProcessed;

        /// <summary>Raised on the processing thread for each accepted frame.</summary>
        public event NewFrameEventHandler NewFrame;
        /// <summary>Reports acquisition and frame-processing errors.</summary>
        public event VideoSourceErrorEventHandler VideoSourceError;
        /// <summary>Raised after acquisition stops and accepted frames are processed.</summary>
        public event PlayingFinishedEventHandler PlayingFinished;
        /// <summary>The wrapped video source.</summary>
        public IVideoSource NestedVideoSource => nestedVideoSource;
        /// <summary>Whether to drop frames while processing is busy. Default: false.</summary>
        public bool SkipFramesIfBusy
        {
            get => skipFramesIfBusy;
            set => skipFramesIfBusy = value;
        }
        /// <summary>The nested source's identifier.</summary>
        public string Source => nestedVideoSource.Source;
        /// <summary>Frames received by the nested source since the last access.</summary>
        public int FramesReceived => nestedVideoSource.FramesReceived;
        /// <summary>Bytes received by the nested source since the last access.</summary>
        public long BytesReceived => nestedVideoSource.BytesReceived;
        /// <summary>Frames processed since the last access.</summary>
        public int FramesProcessed => Interlocked.Exchange(ref framesProcessed, 0);
        /// <summary>Whether acquisition or processing is still running.</summary>
        public bool IsRunning
        {
            get { lock (sync) return session != null && session.Thread.IsAlive; }
        }

        /// <summary>Creates a wrapper for a nested source.</summary>
        /// <param name="nestedVideoSource">The source to process and eventually dispose.</param>
        public AsyncVideoSource(IVideoSource nestedVideoSource) : this(nestedVideoSource, false) { }

        /// <summary>Creates a wrapper with the specified frame-dropping policy.</summary>
        /// <param name="nestedVideoSource">The source to process and eventually dispose.</param>
        /// <param name="skipFramesIfBusy">Whether to drop frames while a handler is running.</param>
        public AsyncVideoSource(IVideoSource nestedVideoSource, bool skipFramesIfBusy)
        {
            this.nestedVideoSource = nestedVideoSource ?? throw new ArgumentNullException(nameof(nestedVideoSource));
            this.skipFramesIfBusy = skipFramesIfBusy;
        }

        /// <summary>Starts acquisition and asynchronous processing.</summary>
        public void Start()
        {
            ProcessingSession current;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(AsyncVideoSource));
                if (session != null && session.Thread.IsAlive) return;
                if (nestedVideoSource.IsRunning) return;
                framesProcessed = 0;
                current = new ProcessingSession(this);
                session = current;
                try
                {
                    current.Subscribe();
                    current.Thread.Start();
                }
                catch
                {
                    current.Unsubscribe();
                    session = null;
                    throw;
                }
            }

            // Do not hold the lifecycle lock across user code or synchronous frame delivery.
            try { nestedVideoSource.Start(); }
            catch
            {
                current.RequestStop();
                current.CompleteStart(false);
                current.Thread.Join();
                throw;
            }
            current.CompleteStart(true);
        }

        /// <summary>Requests shutdown without waiting for frame handlers.</summary>
        public void SignalToStop()
        {
            ProcessingSession current;
            lock (sync) current = session;
            if (current == null || !current.Thread.IsAlive) return;
            current.RequestStop();
            if (Volatile.Read(ref nestedDisposed) == 0) nestedVideoSource.SignalToStop();
        }

        /// <summary>Waits for acquisition, processing and resource cleanup to complete.</summary>
        /// <remarks>Does not block inside video callbacks, which must return before shutdown can finish.</remarks>
        public void WaitForStop()
        {
            ProcessingSession current;
            lock (sync) current = session;
            if (current != null && current.Thread != Thread.CurrentThread && !VideoSourceCallbacks.IsActive)
                current.Thread.Join();
        }

        /// <summary>Requests shutdown and waits unless called from a video callback.</summary>
        public void Stop()
        {
            SignalToStop();
            WaitForStop();
        }

        /// <summary>Stops processing and disposes the nested source.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Releases resources after processing has stopped.</summary>
        /// <param name="disposing">Whether disposal was requested explicitly.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            lock (sync) disposed = true;
            SignalToStop();
            WaitForStop();
            if (!IsRunning) DisposeNestedIfRequested();
        }

        private void DisposeNestedIfRequested()
        {
            bool requested;
            lock (sync) requested = disposed;
            if (requested && Interlocked.Exchange(ref nestedDisposed, 1) == 0)
                nestedVideoSource.Dispose();
        }

        private void ReportError(object sender, VideoSourceErrorEventArgs error)
        {
            VideoSourceCallbacks.Invoke(() => VideoSourceError?.Invoke(sender, error));
        }

        // Each start owns its queue and subscriptions. Late events from an old run cannot
        // publish into a restarted source. Shutdown also wakes producers waiting for a slot.
        private sealed class ProcessingSession
        {
            private readonly AsyncVideoSource owner;
            private readonly object queue = new object();
            private Bitmap pending;
            private bool processing;
            private bool stopping;
            private bool finished;
            private bool startCompleted;
            private bool startSucceeded;
            private ReasonToFinishPlaying reason = ReasonToFinishPlaying.StoppedByUser;
            internal readonly Thread Thread;

            internal ProcessingSession(AsyncVideoSource owner)
            {
                this.owner = owner;
                Thread = new Thread(Process) { IsBackground = true, Name = "AsyncVideoSource" };
            }

            internal bool StopRequested { get { lock (queue) return stopping; } }

            internal void Subscribe()
            {
                owner.nestedVideoSource.NewFrame += ReceiveFrame;
                owner.nestedVideoSource.VideoSourceError += ReceiveError;
                owner.nestedVideoSource.PlayingFinished += ReceiveFinished;
            }

            internal void Unsubscribe()
            {
                owner.nestedVideoSource.NewFrame -= ReceiveFrame;
                owner.nestedVideoSource.VideoSourceError -= ReceiveError;
                owner.nestedVideoSource.PlayingFinished -= ReceiveFinished;
            }

            internal void CompleteStart(bool succeeded)
            {
                // Shutdown cannot dispose the nested source until this handshake finishes.
                if (succeeded && StopRequested) owner.nestedVideoSource.SignalToStop();
                lock (queue)
                {
                    startSucceeded = succeeded;
                    startCompleted = true;
                    Monitor.PulseAll(queue);
                }
            }

            internal void RequestStop()
            {
                lock (queue)
                {
                    stopping = true;
                    Monitor.PulseAll(queue);
                }
            }

            private void ReceiveFrame(object sender, NewFrameEventArgs args)
            {
                if (owner.NewFrame == null) return;
                lock (queue)
                {
                    while ((processing || pending != null) && !stopping && !finished)
                    {
                        if (owner.skipFramesIfBusy) return;
                        Monitor.Wait(queue);
                    }
                    if (stopping || finished) return;
                    pending = (Bitmap)args.Frame.Clone();
                    Monitor.PulseAll(queue);
                }
            }

            private void ReceiveError(object sender, VideoSourceErrorEventArgs args) => owner.ReportError(sender, args);

            private void ReceiveFinished(object sender, ReasonToFinishPlaying finishReason)
            {
                lock (queue)
                {
                    if (reason != ReasonToFinishPlaying.VideoSourceError) reason = finishReason;
                    finished = true;
                    Monitor.PulseAll(queue);
                }
            }

            private void Process()
            {
                try
                {
                    while (true)
                    {
                        Bitmap frame;
                        lock (queue)
                        {
                            while (pending == null && !stopping && !finished) Monitor.Wait(queue);
                            if (pending == null) break;
                            frame = pending;
                            pending = null;
                            processing = true;
                        }
                        try
                        {
                            using (frame)
                                VideoSourceCallbacks.Invoke(() => owner.NewFrame?.Invoke(owner, new NewFrameEventArgs(frame)));
                            Interlocked.Increment(ref owner.framesProcessed);
                        }
                        finally
                        {
                            lock (queue)
                            {
                                processing = false;
                                Monitor.PulseAll(queue);
                            }
                        }
                    }
                }
                catch (Exception error)
                {
                    lock (queue) reason = ReasonToFinishPlaying.VideoSourceError;
                    RequestStop();
                    owner.ReportError(owner, new VideoSourceErrorEventArgs(error.Message));
                }
                finally
                {
                    RequestStop();
                    try
                    {
                        lock (queue)
                            while (!startCompleted) Monitor.Wait(queue);
                        owner.nestedVideoSource.SignalToStop();
                        owner.nestedVideoSource.WaitForStop();
                    }
                    finally
                    {
                        Unsubscribe();
                        lock (queue)
                        {
                            pending?.Dispose();
                            pending = null;
                            Monitor.PulseAll(queue);
                        }
                        try
                        {
                            if (startSucceeded)
                                VideoSourceCallbacks.Invoke(() => owner.PlayingFinished?.Invoke(owner, reason));
                        }
                        finally { owner.DisposeNestedIfRequested(); }
                    }
                }
            }
        }
    }
}
