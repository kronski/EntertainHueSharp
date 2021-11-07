using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace EntertainHue
{
    class EntertainHue
    {

        public async Task<int> Run(string[] args)
        {
            if (Options.Parse(args) is null)
                return -1;

            ConsoleEx.SetVerboseOption(Options.Current.Verbose);

            if (Options.Current.Picture)
            {
                await ConsoleEx.Verbose("Taking picture");
                await PiCamera.Instance.TakePicture();
                return 0;
            }

            if (Options.Current.CameraFrames)
            {
                var (frames, bytes, time) = await PiCamera.Instance.SampleCameraFrameRate(TimeSpan.FromSeconds(15));

                await ConsoleEx.Verbose(frames.ToString(), " frames, ", (frames * 1000 / time.ElapsedMilliseconds).ToString(), " fps");
                await ConsoleEx.Verbose((bytes / 1000).ToString(), " kb, ", (bytes / time.ElapsedMilliseconds).ToString(), " kbps");
                await ConsoleEx.Verbose((bytes / frames).ToString(), " average frame size (bytes).");

                return 0;
            }


            {
                int result = await HueService.Current.FindAndInit();
                if (result != 0)
                    return result;
            }
            {
                int result = await HueService.Current.InitEntertainmentGroup();
                if (result != 0)
                    return result;
            }


            var cancellationTokenSource = new CancellationTokenSource();
            List<Task> tasks = new List<Task>();
            try
            {
                if (Options.Current.RandomLight || Options.Current.Calibrate)
                {
                    var streamTask = HueService.Current.RunStreaming(cancellationTokenSource.Token);
                    tasks.Add(streamTask);
                }


                if (Options.Current.Calibrate)
                {
                    var webServerTask = WebServer.Run(cancellationTokenSource.Token);
                    tasks.Add(webServerTask);
                }


                if (Options.Current.RandomLight)
                {
                    var randomLightTask = HueService.Current.RunRandomLight(cancellationTokenSource.Token);
                    tasks.Add(randomLightTask);
                }

                if (tasks.Count > 0)
                {
                    var clickTask = Task.Run(() =>
                        {
                            Console.ReadKey(true);
                            cancellationTokenSource.Cancel();
                        });
                    tasks.Add(clickTask);
                }

                await Task.WhenAll(tasks);
            }
            finally
            {
                HueService.Current.CleanUp();
            }



            return 0;
        }
    }
}