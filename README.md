<p align="center"><img width="25%" src="https://raw.githubusercontent.com/UMapx/UMapx.Video.Windows/main/docs/umapxnet_big.png" /></p>
<p align="center">UMapx sub-library for video capturing and AVI processing on Windows</p>

# Installation
Install **UMapx.Video.Windows** to your project using [NuGet](https://www.nuget.org/packages/UMapx.Video.Windows/) package manager.

```csharp
using UMapx.Video;
using UMapx.Video.DirectShow;
using UMapx.Video.VFW;
```

See the [WPF camera example](https://github.com/UMapx/UMapx.Video.Windows/tree/main/examples).

# Video sources

| Source | Purpose |
| --- | --- |
| `VideoCaptureDevice` | DirectShow camera capture, settings and snapshots |
| `FileVideoSource`, `AVIFileVideoSource` | DirectShow and VFW file playback |
| `JPEGStream`, `MJPEGStream` | HTTP JPEG polling and multipart/raw MJPEG streams |
| `ScreenCaptureStream` | Capture a rectangular screen region |
| `VideoImageSource` | Generate frames from a bitmap |
| `VideoImageDepthSource` | Generate image and depth frames from a bitmap and depth array |
| `AsyncVideoSource` | Process incoming frames on a separate thread |

The shared API includes `IVideoSource`, `IVideoDepthSource`, `VideoCapabilities`,
frame/depth event arguments and video error/completion events.

# Platform support

The library targets **.NET Standard 2.0** but requires Windows: it uses DirectShow,
Video for Windows (VFW), COM and System.Drawing.Common. It is not supported on Linux or macOS.

Regression tests cover Windows with .NET 8, in x64 and x86 processes.
The example requires the .NET 8 Windows Desktop runtime; building the repository
requires the .NET 8 SDK.
Physical cameras and third-party codecs need separate validation with the intended
device, driver and process architecture. ARM64 and .NET Framework are not covered
by this test suite. A codec must be installed for the process architecture being used.

# Capturing and stopping

```csharp
var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
if (devices.Count == 0)
    throw new InvalidOperationException("No camera is available.");

using var source = new VideoCaptureDevice(devices[0].MonikerString);
source.NewFrame += (_, e) =>
{
    // The source disposes e.Frame after this handler returns.
    // Clone it if processing must outlive the handler.
    using var copy = (System.Drawing.Bitmap)e.Frame.Clone();
    // Process copy here.
};
source.VideoSourceError += (_, e) => Console.Error.WriteLine(e.Description);
source.Start();

// Later, outside a video event handler:
source.SignalToStop();
source.WaitForStop();
```

For DirectShow/VFW sources, `SignalToStop()` requests cooperative shutdown without waiting. `WaitForStop()`
waits for completion; `Dispose()` requests shutdown and waits before releasing
resources. Calling `Stop()` or `Dispose()` from a video callback requests shutdown
and returns so that the callback can finish. Call `WaitForStop()` from outside the
callback if you need to wait for full completion. A blocked driver or a handler
that never returns can still delay shutdown; `Thread.Abort()` is not used.

For `JPEGStream`, `MJPEGStream` and `ScreenCaptureStream`, use `SignalToStop()`
followed by `WaitForStop()`, or call `Dispose()`. Their legacy `Stop()` methods
are obsolete. Image and depth-image sources stop through `SignalToStop()` or
`Dispose()`; their obsolete `Stop()` and `WaitForStop()` methods are not implemented.
These image sources take ownership of the supplied bitmap and dispose it with the source.

The capture constructor accepts `Format24bppRgb` (default) and `Format32bppRgb`.
Other pixel formats throw `ArgumentException` before a device starts. Incoming RGB
frames are checked against their buffer length, and both DIB orientations are handled.
To receive `SnapshotFrame` events on supported devices, set `ProvideSnapshots = true`
before `Start()` and subscribe to `SnapshotFrame`. A `NewFrame` subscription is not required.

`AsyncVideoSource` owns its nested source and disposes it when the wrapper is
disposed. With `SkipFramesIfBusy = false`, acquisition waits while a frame is being
processed; setting it to `true` drops frames received while processing is busy.
Accepted frames are drained before `PlayingFinished` is raised on the processing
thread. Exceptions from `NewFrame` handlers are reported through `VideoSourceError`
and stop processing. Event handlers should return promptly and should not throw.

# Build and test

Run on Windows:

```powershell
dotnet build UMapx.Video.Windows.sln -c Release
dotnet build examples/UMapx.Video.Windows.Example.sln -c Release
dotnet test tests/UMapx.Video.Windows.Tests.csproj -c Release
```

Tests generate temporary video files and use local HTTP fixtures. They do not
open a camera or capture the desktop. They cover native
AVI/DirectShow playback, Microsoft Video 1 compression, frame bounds and orientation,
snapshot callbacks, JPEG/MJPEG parsing and streaming, large and partial frames,
stream timeouts, image/depth sources, startup failures, concurrent shutdown,
disposal and restarts.

To also run the built tests in an x86 process, install the corresponding x86 .NET
runtime and run, for example:

```powershell
dotnet vstest tests/bin/Release/net8.0-windows/UMapx.Video.Windows.Tests.dll '/Platform:x86'
```

# License
MIT
