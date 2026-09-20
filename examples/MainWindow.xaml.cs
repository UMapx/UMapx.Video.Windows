using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using UMapx.Video.DirectShow;

namespace UMapx.Video.Windows.Example
{
    /// <summary>Displays frames from the first available camera.</summary>
    public partial class MainWindow : System.Windows.Window
    {
        private IVideoSource videoSource;
        private volatile bool closing;
        private int drawingPending;

        /// <summary>Creates the camera preview window.</summary>
        public MainWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => StartCamera();
            Closing += (_, _) => CloseCamera();
        }

        private void StartCamera()
        {
            if (closing || videoSource != null) return;
            try
            {
                videoSource = GetVideoDevice(0, 0);
                videoSource.NewFrame += OnNewFrame;
                videoSource.VideoSourceError += OnVideoSourceError;
                videoSource.Start();
            }
            catch (Exception error)
            {
                videoSource?.Dispose();
                videoSource = null;
                statusText.Text = error.Message;
            }
        }

        private void CloseCamera()
        {
            closing = true;
            if (videoSource == null) return;
            videoSource.NewFrame -= OnNewFrame;
            videoSource.VideoSourceError -= OnVideoSourceError;
            videoSource.SignalToStop();
            videoSource.WaitForStop();
            videoSource.Dispose();
            videoSource = null;
        }

        private void OnVideoSourceError(object sender, VideoSourceErrorEventArgs args)
        {
            if (closing) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (closing) return;
                statusText.Text = args.Description;
                statusText.Visibility = Visibility.Visible;
            }));
        }

        private void OnNewFrame(object sender, NewFrameEventArgs args)
        {
            // Bound the dispatcher queue; the source owns the bitmap until this method returns.
            if (closing || Interlocked.CompareExchange(ref drawingPending, 1, 0) != 0) return;
            try
            {
                BitmapImage frame = ToBitmapImage(args.Frame);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (closing) return;
                        imgColor.Source = frame;
                        statusText.Visibility = Visibility.Collapsed;
                    }
                    finally { Interlocked.Exchange(ref drawingPending, 0); }
                }));
            }
            catch (Exception error)
            {
                Interlocked.Exchange(ref drawingPending, 0);
                OnVideoSourceError(sender, new VideoSourceErrorEventArgs(error.Message));
            }
        }

        private static BitmapImage ToBitmapImage(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Bmp);
                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        /// <summary>Returns a configured camera, or throws an explanatory error.</summary>
        /// <param name="camIndex">Index of the camera.</param>
        /// <param name="resIndex">Index of the resolution.</param>
        /// <returns>A camera ready to start.</returns>
        public static IVideoSource GetVideoDevice(int camIndex, int resIndex)
        {
            var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (devices.Count == 0) throw new InvalidOperationException("No camera is available.");
            if (camIndex < 0 || camIndex >= devices.Count) throw new ArgumentOutOfRangeException(nameof(camIndex));
            var device = new VideoCaptureDevice(devices[camIndex].MonikerString);
            try
            {
                var capabilities = device.VideoCapabilities;
                if (capabilities.Length == 0) throw new InvalidOperationException("The camera reports no supported video modes.");
                if (resIndex < 0 || resIndex >= capabilities.Length) throw new ArgumentOutOfRangeException(nameof(resIndex));
                device.VideoResolution = capabilities[resIndex];
                return device;
            }
            catch
            {
                device.Dispose();
                throw;
            }
        }
    }
}
