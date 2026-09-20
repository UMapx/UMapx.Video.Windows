namespace UMapx.Video.VFW
{
    using System;
    using System.Drawing;
    using System.Threading;
    using UMapx.Video;

    /// <summary>
    /// AVI file video source.
    /// </summary>
    /// 
    /// <remarks><para>The video source reads AVI files using Video for Windows.</para>
    /// 
    /// <para>Sample usage:</para>
    /// <code>
    /// // create AVI file video source
    /// AVIFileVideoSource source = new AVIFileVideoSource( "some file" );
    /// // set event handlers
    /// source.NewFrame += new NewFrameEventHandler( video_NewFrame );
    /// // start the video source
    /// source.Start( );
    /// // ...
    /// // signal to stop
    /// source.SignalToStop( );
    /// 
    /// // New frame event handler, which is invoked on each new available video frame
    /// private void video_NewFrame( object sender, NewFrameEventArgs eventArgs )
    /// {
    ///     // get new frame
    ///     Bitmap bitmap = eventArgs.Frame;
    ///     // process the frame
    /// }
    /// </code>
    /// </remarks>
    /// 
    public class AVIFileVideoSource : IVideoSource
	{
        // video file name
		private string source;
        // received frames count
		private int framesReceived;
        // recieved byte count
        private long bytesReceived;
        // frame interval in milliseconds
        private int frameInterval = 0;
        // get frame interval from source or use manually specified
        private bool frameIntervalFromSource = true;

		private readonly VideoSourceWorker worker = new VideoSourceWorker();

        /// <summary>
        /// New frame event.
        /// </summary>
        /// 
        /// <remarks><para>Notifies clients about new available frame from video source.</para>
        /// 
        /// <para><note>Since video source may have multiple clients, each client is responsible for
        /// making a copy (cloning) of the passed video frame, because the video source disposes its
        /// own original copy after notifying of clients.</note></para>
        /// </remarks>
        /// 
        public event NewFrameEventHandler NewFrame;

        /// <summary>
        /// Video source error event.
        /// </summary>
        /// 
        /// <remarks>This event is used to notify clients about any type of errors occurred in
        /// video source object, for example internal exceptions.</remarks>
        /// 
        public event VideoSourceErrorEventHandler VideoSourceError;

        /// <summary>
        /// Video playing finished event.
        /// </summary>
        /// 
        /// <remarks><para>This event is used to notify clients that the video playing has finished.</para>
        /// </remarks>
        /// 
        public event PlayingFinishedEventHandler PlayingFinished;

        /// <summary>
        /// Frame interval.
        /// </summary>
        /// 
        /// <remarks><para>The property sets the interval in milliseconds between frames. If the property is
        /// set to 100, then the desired frame rate will be 10 frames per second.</para>
        /// 
        /// <para><note>Setting this property to 0 leads to no delay between video frames - frames
        /// are read as fast as possible.</note></para>
        /// 
        /// <para>Default value is set to <b>0</b>.</para>
        /// </remarks>
        /// 
        public int FrameInterval
        {
            get { return frameInterval; }
            set { frameInterval = value; }
        }

        /// <summary>
        /// Get frame interval from source or use manually specified.
        /// </summary>
        /// 
        /// <remarks><para>The property specifies which frame rate to use for video playing.
        /// If the property is set to <see langword="true"/>, then video is played
        /// with original frame rate, which is set in source AVI file. If the property is
        /// set to <see langword="false"/>, then custom frame rate is used, which is
        /// calculated based on the manually specified <see cref="FrameInterval">frame interval</see>.</para>
        /// 
        /// <para>Default value is set to <see langword="true"/>.</para>
        /// </remarks>
        /// 
        public bool FrameIntervalFromSource
        {
            get { return frameIntervalFromSource; }
            set { frameIntervalFromSource = value; }
        }

        /// <summary>
        /// Video source.
        /// </summary>
        /// 
        /// <remarks><para>Video file name to play.</para></remarks>
        /// 
        public virtual string Source
		{
			get { return source; }
			set { source = value; }
		}

        /// <summary>
        /// Received frames count.
        /// </summary>
        /// 
        /// <remarks>Number of frames the video source provided from the moment of the last
        /// access to the property.
        /// </remarks>
        /// 
        public int FramesReceived
		{
			get
			{
				return Interlocked.Exchange(ref framesReceived, 0);
			}
		}

        /// <summary>
        /// Received bytes count.
        /// </summary>
        /// 
        /// <remarks>Number of bytes the video source provided from the moment of the last
        /// access to the property.
        /// </remarks>
        /// 
        public long BytesReceived
		{
            get
            {
                return Interlocked.Exchange(ref bytesReceived, 0);
            }
		}

        /// <summary>
        /// State of the video source.
        /// </summary>
        /// 
        /// <remarks>Current state of video source object - running or not.</remarks>
        /// 
        public bool IsRunning => worker.IsRunning;

        /// <summary>
        /// Initializes a new instance of the <see cref="AVIFileVideoSource"/> class.
        /// </summary>
        /// 
		public AVIFileVideoSource( ) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="AVIFileVideoSource"/> class.
        /// </summary>
        /// 
        /// <param name="source">Video file name</param>
        /// 
        public AVIFileVideoSource( string source )
        {
            this.source = source;
        }

        /// <summary>
        /// Start video source.
        /// </summary>
        /// 
        /// <remarks>Starts video source and return execution to caller. Video source
        /// object creates background thread and notifies about new frames with the
        /// help of <see cref="NewFrame"/> event.</remarks>
        /// 
        /// <exception cref="ArgumentException">Video source is not specified.</exception>
        /// 
        public void Start()
        {
            worker.Start(() =>
            {
                if (string.IsNullOrEmpty(source))
                    throw new ArgumentException("Video source is not specified");
                framesReceived = 0;
                bytesReceived = 0;
            }, WorkerThread, source);
        }

        /// <summary>
        /// Signal video source to stop its work.
        /// </summary>
        /// 
        /// <remarks>Signals video source to stop its background thread, stop to
        /// provide new frames and free resources.</remarks>
        /// 
        public void SignalToStop()
        {
            worker.SignalToStop();
        }

        /// <summary>
        /// Wait for video source has stopped.
        /// </summary>
        /// 
        /// <remarks>Waits for source stopping after it was signalled to stop using
        /// <see cref="SignalToStop"/> method.</remarks>
        /// 
        public void WaitForStop()
        {
            worker.WaitForStop();
        }

        /// <summary>
        /// Stop video source.
        /// </summary>
        /// 
        /// <remarks>Signals the source to stop and waits for completion. From a source callback,
        /// only the stop is requested; the worker completes after the callback returns.</remarks>
        [Obsolete("Use SignalToStop followed by WaitForStop.")]
        public void Stop()
        {
            SignalToStop();
            WaitForStop();
        }

        /// <summary>
        /// Worker thread.
        /// </summary>
        /// 
        private void WorkerThread()
        {
            ReasonToFinishPlaying reasonToStop = ReasonToFinishPlaying.StoppedByUser;
            try
            {
                using (var reader = new AVIReader())
                {
                    reader.Open(source);
                    int stopPosition = checked(reader.Start + reader.Length);
                    int interval = frameIntervalFromSource ? (int)(1000 / reader.FrameRate) : frameInterval;
                    while (!worker.IsStopping)
                    {
                        var elapsed = System.Diagnostics.Stopwatch.StartNew();
                        using (Bitmap frame = reader.GetNextFrame())
                        {
                            Interlocked.Increment(ref framesReceived);
                            Interlocked.Add(ref bytesReceived, (long)frame.Width * frame.Height * (Bitmap.GetPixelFormatSize(frame.PixelFormat) / 8));
                            VideoSourceCallbacks.Invoke(() => NewFrame?.Invoke(this, new NewFrameEventArgs(frame)));
                        }
                        if (reader.Position >= stopPosition)
                        {
                            reasonToStop = ReasonToFinishPlaying.EndOfStreamReached;
                            break;
                        }
                        int remaining = interval - (int)Math.Min(int.MaxValue, elapsed.ElapsedMilliseconds);
                        if (remaining > 0 && worker.WaitForStopSignal(remaining)) break;
                    }
                }
            }
            catch (Exception error)
            {
                reasonToStop = ReasonToFinishPlaying.VideoSourceError;
                VideoSourceCallbacks.Invoke(() => VideoSourceError?.Invoke(this, new VideoSourceErrorEventArgs(error.Message)));
            }
            VideoSourceCallbacks.Invoke(() => PlayingFinished?.Invoke(this, reasonToStop));
        }

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing) worker.Dispose();
        }

        /// <inheritdoc/>
        ~AVIFileVideoSource()
        {
            Dispose(false);
        }

        #endregion
    }
}
