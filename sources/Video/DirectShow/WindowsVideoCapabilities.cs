namespace UMapx.Video.DirectShow
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Runtime.InteropServices;
    using UMapx.Video.DirectShow.Internals;

    /// <summary>
    /// Capabilities of video Windows device such as frame size and frame rate.
    /// </summary>
    public class WindowsVideoCapabilities : VideoCapabilities
    {

        // Retrieve capabilities of a video device
        static internal WindowsVideoCapabilities[] FromStreamConfig(IAMStreamConfig videoStreamConfig)
        {
            if (videoStreamConfig == null)
                throw new ArgumentNullException("videoStreamConfig");

            // ensure this device reports capabilities
            int hr = videoStreamConfig.GetNumberOfCapabilities(out int count, out int size);

            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);

            if (count <= 0)
                throw new NotSupportedException("This video device does not report capabilities.");

            if (size > Marshal.SizeOf(typeof(VideoStreamConfigCaps)))
                throw new NotSupportedException("Unable to retrieve video device capabilities. This video device requires a larger VideoStreamConfigCaps structure.");

            // group capabilities with similar parameters
            Dictionary<ulong, WindowsVideoCapabilities> videocapsList = new Dictionary<ulong, WindowsVideoCapabilities>();

            for (int i = 0; i < count; i++)
            {
                try
                {
                    WindowsVideoCapabilities vc = new WindowsVideoCapabilities(videoStreamConfig, i);

                    ulong key = (((uint)vc.AverageFrameRate) << 48) |
                               (((uint)vc.FrameSize.Height) << 32) |
                               (((uint)vc.FrameSize.Width) << 16);

                    if (!videocapsList.ContainsKey(key))
                    {
                        videocapsList.Add(key, vc);
                    }
                    else
                    {
                        if (vc.BitCount > videocapsList[key].BitCount)
                        {
                            videocapsList[key] = vc;
                        }
                    }
                }
                catch
                {
                }
            }

            WindowsVideoCapabilities[] videocaps = new WindowsVideoCapabilities[videocapsList.Count];
            videocapsList.Values.CopyTo(videocaps, 0);

            return videocaps;
        }

        // Retrieve capabilities of a video device
        internal WindowsVideoCapabilities(IAMStreamConfig videoStreamConfig, int index) : base()
        {
            AMMediaType mediaType = null;
            VideoStreamConfigCaps caps = new VideoStreamConfigCaps();

            try
            {
                // retrieve capabilities struct at the specified index
                int hr = videoStreamConfig.GetStreamCaps(index, out mediaType, caps);

                if (hr != 0)
                    Marshal.ThrowExceptionForHR(hr);

                if (mediaType.FormatType == FormatType.VideoInfo)
                {
                    VideoInfoHeader videoInfo = (VideoInfoHeader)Marshal.PtrToStructure(mediaType.FormatPtr, typeof(VideoInfoHeader));

                    FrameSize = new Size(videoInfo.BmiHeader.Width, videoInfo.BmiHeader.Height);
                    BitCount = videoInfo.BmiHeader.BitCount;
                    AverageFrameRate = (int)(10000000 / videoInfo.AverageTimePerFrame);
                    MaximumFrameRate = (int)(10000000 / caps.MinFrameInterval);
                }
                else if (mediaType.FormatType == FormatType.VideoInfo2)
                {
                    VideoInfoHeader2 videoInfo = (VideoInfoHeader2)Marshal.PtrToStructure(mediaType.FormatPtr, typeof(VideoInfoHeader2));

                    FrameSize = new Size(videoInfo.BmiHeader.Width, videoInfo.BmiHeader.Height);
                    BitCount = videoInfo.BmiHeader.BitCount;
                    AverageFrameRate = (int)(10000000 / videoInfo.AverageTimePerFrame);
                    MaximumFrameRate = (int)(10000000 / caps.MinFrameInterval);
                }
                else
                {
                    throw new ApplicationException("Unsupported format found.");
                }

                // ignore 12 bpp formats for now, since it was noticed they cause issues on Windows 8
                // TODO: proper fix needs to be done so ICaptureGraphBuilder2::RenderStream() does not fail
                // on such formats
                //if (BitCount <= 12)
                //{
                //    throw new ApplicationException("Unsupported format found.");
                //}
            }
            finally
            {
                if (mediaType != null)
                    mediaType.Dispose();
            }
        }
    }
}
