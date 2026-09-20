namespace UMapx.Video
{
    using System;
    using System.Threading;

    // Tracks callbacks, including DirectShow callbacks running outside our worker thread.
    internal static class VideoSourceCallbacks
    {
        [ThreadStatic]
        private static int depth;

        internal static bool IsActive => depth != 0;

        internal static void Invoke(Action callback)
        {
            depth++;
            try { callback(); }
            finally { depth--; }
        }
    }

    // Owns the complete lifetime of a native source's worker. Monitor-based signalling
    // avoids closing a wait handle while a frame callback or worker still uses it.
    internal sealed class VideoSourceWorker : IDisposable
    {
        private readonly object sync = new object();
        private Thread thread;
        private bool stopping = true;
        private bool disposed;

        internal bool IsRunning
        {
            get { lock (sync) return thread != null && thread.IsAlive; }
        }

        internal bool IsStopping
        {
            get { lock (sync) return stopping; }
        }

        internal void Start(Action initialize, Action action, string name)
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(name);
                if (thread != null && thread.IsAlive) return;
                initialize();
                stopping = false;
                thread = new Thread(() =>
                {
                    try { action(); }
                    finally
                    {
                        lock (sync)
                        {
                            stopping = true;
                            Monitor.PulseAll(sync);
                        }
                    }
                }) { IsBackground = true, Name = name };
                try { thread.Start(); }
                catch
                {
                    thread = null;
                    stopping = true;
                    throw;
                }
            }
        }

        internal void SignalToStop()
        {
            lock (sync)
            {
                stopping = true;
                Monitor.PulseAll(sync);
            }
        }

        internal bool WaitForStopSignal(int milliseconds)
        {
            lock (sync)
            {
                if (!stopping) Monitor.Wait(sync, milliseconds);
                return stopping;
            }
        }

        internal void WaitForStop()
        {
            Thread current;
            lock (sync) current = thread;
            // A DirectShow graph may be waiting for this very callback to return.
            if (current != null && current != Thread.CurrentThread && !VideoSourceCallbacks.IsActive)
                current.Join();
        }

        public void Dispose()
        {
            lock (sync)
            {
                disposed = true;
                stopping = true;
                Monitor.PulseAll(sync);
            }
            WaitForStop();
        }
    }
}
