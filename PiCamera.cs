using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMALSharp;
using MMALSharp.Common;
using MMALSharp.Common.Utility;
using MMALSharp.Components;
using MMALSharp.Config;
using MMALSharp.Handlers;
using MMALSharp.Native;

public class PiCameraCaptureHandler : InMemoryCaptureHandler, IVideoCaptureHandler
{
    private int frames = 0;
    private long bytes = 0;
    public int Frames => frames;
    public long Bytes => bytes;
    public override void Process(ImageContext context)
    {
        // The InMemoryCaptureHandler parent class has a property called "WorkingData". 
        // It is your responsibility to look after the clearing of this property.

        // The "eos" parameter indicates whether the MMAL buffer has an EOS parameter, if so, the data that's currently
        // stored in the "WorkingData" property plus the data found in the "data" parameter indicates you have a full image frame.

        // The call to base.Process will add the data to the WorkingData list.
        base.Process(context);

        if (context.Eos)
        {
            //Console.WriteLine(frames.ToString());
            frames++;
            bytes += this.WorkingData.Count;
            this.WorkingData.Clear();
        }
    }

    public void Split()
    {

    }
}




public class PiCamera : IDisposable
{
    private static PiCamera instance;
    private MMALCamera cam;

    public static PiCamera Instance
    {
        get
        {
            if (instance is null)
                instance = new PiCamera();
            return instance;
        }
    }

    private PiCamera()
    {
        cam = MMALCamera.Instance;
    }

    public async Task<(int Frames, long Bytes, Stopwatch Time)> SampleCameraFrameRate(TimeSpan time)
    {
        MMALCameraConfig.VideoResolution = new Resolution(640, 480); // Set to 640 x 480. Default is 1280 x 720.
        MMALCameraConfig.VideoFramerate = new MMAL_RATIONAL_T(500, 1); // Set to 20fps. Default is 30fps.
        MMALCameraConfig.ShutterSpeed = 0; // Set to 2s exposure time. Default is 0 (auto).
        MMALCameraConfig.ISO = 0; // Set ISO to 400. Default is 0 (auto).
        MMALCameraConfig.VideoEncoding = MMALEncoding.BGR24;
        MMALCameraConfig.VideoSubformat = MMALEncoding.BGR24;

        using (var myCaptureHandler = new PiCameraCaptureHandler())
        using (var nullSink = new MMALNullSinkComponent())
        {
            cam.ConfigureCameraSettings(null, myCaptureHandler);
            cam.Camera.PreviewPort.ConnectTo(nullSink);

            // Camera warm up time
            await Task.Delay(2000);

            CancellationTokenSource cts = new CancellationTokenSource(time);
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            // Process images for 15 seconds.        
            await cam.ProcessAsync(cam.Camera.VideoPort, cts.Token);

            stopWatch.Stop();
            return (myCaptureHandler.Frames, myCaptureHandler.Bytes, stopWatch);
        }
    }

    public async Task<bool> TakePicture(double? X, double? Y, double? Width, double? Height)
    {
        // Singleton initialized lazily. Reference once in your application.
        MMALCamera cam = MMALCamera.Instance;
        MMALCameraConfig.Flips = MMALSharp.Native.MMAL_PARAM_MIRROR_T.MMAL_PARAM_MIRROR_BOTH;
        if (X.HasValue ||
            Y.HasValue ||
            Width.HasValue ||
            Height.HasValue)
            MMALCameraConfig.ROI = new Zoom(X ?? 0, Y ?? 0, Width ?? 1, Height ?? 1);

        using (var imgCaptureHandler = new ImageStreamCaptureHandler("/home/pi/images/", "jpg"))
        {
            await cam.TakePicture(imgCaptureHandler, MMALEncoding.JPEG, MMALEncoding.I420);
        }

        // Cleanup disposes all unmanaged resources and unloads Broadcom library. To be called when no more processing is to be done
        // on the camera.
        cam.Cleanup();
        return true;
    }


    public void Dispose()
    {
        cam.Cleanup();
    }
}