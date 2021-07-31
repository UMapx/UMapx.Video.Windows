using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UMapx.Video.DirectShow;

namespace UMapx.Video.Windows.Example
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        #region Fields

        private readonly IVideoSource _videoSource;
        private static readonly object _locker = new object();
        private Bitmap _frame;

        #endregion

        #region Launcher

        /// <summary>
        /// Constructor.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;

            // main camera
            _videoSource = GetVideoDevice(0, 0);
            _videoSource.NewFrame += OnNewFrame;
            _videoSource.Start();
        }

        /// <summary>
        /// Window closing.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event args</param>
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _videoSource.SignalToStop();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Get frame and dispose previous.
        /// </summary>
        Bitmap Frame
        {
            get
            {
                if (_frame is null)
                    return null;

                Bitmap frame;

                lock (_locker)
                {
                    frame = (Bitmap)_frame.Clone();
                }

                return frame;
            }
            set
            {
                lock (_locker)
                {
                    if (_frame is object)
                    {
                        _frame.Dispose();
                        _frame = null;
                    }

                    _frame = value;
                }
            }
        }

        #endregion

        #region Handling events

        /// <summary>
        /// Frame handling on event call.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="eventArgs">event arguments</param>
        private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            Frame = (Bitmap)eventArgs.Frame.Clone();
            InvokeDrawing();
        }

        #endregion

        #region Private voids

        /// <summary>
        /// Draw calculated <see cref="BitmapImage"/> based on <see cref="RealSenseVideoSource"/> bitmap converted frames
        /// in <see cref="Window"/> Image element
        /// </summary>
        private void InvokeDrawing()
        {
            try
            {
                // color drawing
                var printColor = Frame;

                if (printColor is object)
                {
                    var bitmapColor = ToBitmapImage(printColor);
                    bitmapColor.Freeze();
                    Dispatcher.BeginInvoke(new ThreadStart(delegate { imgColor.Source = bitmapColor; }));
                }
            }
            catch { }
        }

        /// <summary>
        /// Converts a <see cref="Bitmap"/> to <see cref="BitmapImage"/>.
        /// </summary>
        /// <param name="bitmap">Bitmap</param>
        /// <returns>BitmapImage</returns>
        private BitmapImage ToBitmapImage(Bitmap bitmap)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Bmp);
            ms.Seek(0, SeekOrigin.Begin);
            bi.StreamSource = ms;
            bi.EndInit();
            return bi;
        }

        /// <summary>
        /// Returns configured camera device.
        /// </summary>
        /// <param name="camIndex">Camera index</param>
        /// <param name="resIndex">Resolution index</param>
        /// <returns>VideoCapabilities</returns>
        public static IVideoSource GetVideoDevice(int camIndex, int resIndex)
        {
            var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            var videoDevice = new VideoCaptureDevice(videoDevices[camIndex].MonikerString);
            var videoCapabilities = videoDevice.VideoCapabilities;
            videoDevice.VideoResolution = videoCapabilities[resIndex];
            return videoDevice;
        }

        #endregion
    }
}
