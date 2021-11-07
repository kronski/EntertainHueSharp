using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MMALSharp;
using MMALSharp.Common;
using MMALSharp.Common.Utility;
using MMALSharp.Components;
using MMALSharp.Config;
using MMALSharp.Handlers;
using MMALSharp.Native;
using MMALSharp.Ports;

public class PiCameraCaptureHandler : InMemoryCaptureHandler, IVideoCaptureHandler
{
    private int frames = 0;
    private long bytes = 0;
    private byte[] lastFrame = null;
    private readonly bool storeLastFrame;

    public int Frames => frames;
    public long Bytes => bytes;

    public byte[] LastFrame => lastFrame;
    public PiCameraCaptureHandler(bool storeLastFrame)
    {
        this.storeLastFrame = storeLastFrame;
    }
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
            if (storeLastFrame)
                lastFrame = this.WorkingData?.ToArray();
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

    private SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1);

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
        await semaphoreSlim.WaitAsync();
        try
        {
            MMALCameraConfig.VideoResolution = new Resolution(640, 480); // Set to 640 x 480. Default is 1280 x 720.
            MMALCameraConfig.VideoFramerate = new MMAL_RATIONAL_T(500, 1); // Set to 20fps. Default is 30fps.
            MMALCameraConfig.ShutterSpeed = 0; // Set to 2s exposure time. Default is 0 (auto).
            MMALCameraConfig.ISO = 0; // Set ISO to 400. Default is 0 (auto).
            MMALCameraConfig.VideoEncoding = MMALEncoding.BGR24;
            MMALCameraConfig.VideoSubformat = MMALEncoding.BGR24;

            using (var myCaptureHandler = new PiCameraCaptureHandler(storeLastFrame: false))
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
        finally
        {
            semaphoreSlim.Release();
        }
    }

    public async Task<bool> TakePicture()
    {
        await semaphoreSlim.WaitAsync();
        try
        {
            // Singleton initialized lazily. Reference once in your application.
            MMALCamera cam = MMALCamera.Instance;
            MMALCameraConfig.Flips = MMALSharp.Native.MMAL_PARAM_MIRROR_T.MMAL_PARAM_MIRROR_BOTH;

            var o = Options.Current;
            if (o.X.HasValue ||
                o.Y.HasValue ||
                o.Width.HasValue ||
                o.Height.HasValue)
                MMALCameraConfig.ROI = new Zoom(o.X ?? 0, o.Y ?? 0, o.Width ?? 1, o.Height ?? 1);

            using (var imgCaptureHandler = new ImageStreamCaptureHandler("/home/pi/images/", "jpg"))
            {
                await cam.TakePicture(imgCaptureHandler, MMALEncoding.JPEG, MMALEncoding.I420);
            }

            // Cleanup disposes all unmanaged resources and unloads Broadcom library. To be called when no more processing is to be done
            // on the camera.
            cam.Cleanup();
            return true;
        }
        finally
        {
            semaphoreSlim.Release();
        }
    }

    public async Task<Stream> GetJpeg()
    {
        await semaphoreSlim.WaitAsync();
        try
        {
            MMALCameraConfig.StillResolution = new Resolution(640, 480); // Set to 640 x 480. Default is 1280 x 720.
            MMALCameraConfig.Flips = MMALSharp.Native.MMAL_PARAM_MIRROR_T.MMAL_PARAM_MIRROR_BOTH;

            using var myCaptureHandler = new PiCameraCaptureHandler(storeLastFrame: true);
            using var imgEncoder = new MMALImageEncoder();
            using var renderer = new MMALNullSinkComponent();

            cam.ConfigureCameraSettings();

            var portConfig = new MMALPortConfig(MMALEncoding.JPEG, MMALEncoding.I420, 90);

            imgEncoder.ConfigureOutputPort(portConfig, myCaptureHandler);

            // Create our component pipeline.
            cam.Camera.StillPort.ConnectTo(imgEncoder);
            // Create our component pipeline.
            cam.Camera.PreviewPort.ConnectTo(renderer);

            CancellationTokenSource cancel = new CancellationTokenSource(2000);
            // Camera warm up time
            await cam.ProcessAsync(cam.Camera.StillPort, cancel.Token).ConfigureAwait(false);

            if (myCaptureHandler.LastFrame is null) return null;
            return new MemoryStream(myCaptureHandler.LastFrame);

        }
        finally
        {
            semaphoreSlim.Release();
        }
    }


    public void Dispose()
    {
        cam.Cleanup();
    }
}