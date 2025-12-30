using System;
using System.Drawing;
using System.Drawing.Imaging;
using AForge.Video;
using AForge.Video.DirectShow;

namespace ChuniVController
{
    /// <summary>
    /// Captures webcam frames and detects motion/hands in 6 horizontal IR zones using AForge.NET.
    /// Sends IR_BLOCKED/IR_UNBLOCKED messages when motion is detected in each zone.
    /// Compatible with .NET Framework 4.7.2.
    /// </summary>
    public class WebcamIrService : IDisposable
    {
        private readonly ChuniIO _io;
        private readonly int _deviceIndex;
        private readonly int _motionThreshold;
        private readonly double _bottomDeadzonePercent;
        private VideoCaptureDevice _videoSource;
        private Bitmap _previousFrame;
        private bool _running;
        
        // Track the state of each IR sensor (0-5)
        private bool[] _irBlocked = new bool[6];
        
        public WebcamIrService(ChuniIO io, int deviceIndex = 0, double motionThreshold = 20.0, double bottomDeadzonePercent = 15.0)
        {
            _io = io;
            _deviceIndex = deviceIndex;
            _motionThreshold = (int)motionThreshold;
            _bottomDeadzonePercent = bottomDeadzonePercent;
        }

        public bool Start()
        {
            if (_running) return false;

            try
            {
                // Get available video devices
                FilterInfoCollection videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                
                if (videoDevices.Count == 0)
                {
                    Console.WriteLine("No webcam devices found");
                    return false;
                }

                if (_deviceIndex >= videoDevices.Count)
                {
                    Console.WriteLine($"Device index {_deviceIndex} not found. Available devices: {videoDevices.Count}");
                    return false;
                }

                // Create video source
                _videoSource = new VideoCaptureDevice(videoDevices[_deviceIndex].MonikerString);
                _videoSource.NewFrame += OnNewFrame;
                _videoSource.Start();
                _running = true;

                Console.WriteLine($"Webcam IR service started on device {_deviceIndex}: {videoDevices[_deviceIndex].Name}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error starting webcam: " + ex.Message);
                return false;
            }
        }

        public void Stop()
        {
            if (!_running) return;

            _running = false;
            
            if (_videoSource != null && _videoSource.IsRunning)
            {
                _videoSource.SignalToStop();
                _videoSource.WaitForStop();
                _videoSource.NewFrame -= OnNewFrame;
                _videoSource = null;
            }

            _previousFrame?.Dispose();
            _previousFrame = null;

            Console.WriteLine("Webcam IR service stopped");
        }

        private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            if (!_running) return;

            try
            {
                // Clone the frame to avoid issues with disposal
                Bitmap currentFrame = (Bitmap)eventArgs.Frame.Clone();

                if (_previousFrame == null)
                {
                    _previousFrame = ConvertToGrayscale(currentFrame);
                    currentFrame.Dispose();
                    return;
                }

                // Convert current frame to grayscale
                Bitmap grayCurrent = ConvertToGrayscale(currentFrame);
                currentFrame.Dispose();

                // Detect motion in 6 horizontal zones
                DetectMotionInZones(grayCurrent);

                // Update previous frame
                _previousFrame?.Dispose();
                _previousFrame = grayCurrent;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Frame processing error: " + ex.Message);
            }
        }

        private Bitmap ConvertToGrayscale(Bitmap source)
        {
            Bitmap grayscale = new Bitmap(source.Width, source.Height);
            
            using (Graphics g = Graphics.FromImage(grayscale))
            {
                ColorMatrix colorMatrix = new ColorMatrix(
                    new float[][] 
                    {
                        new float[] {.3f, .3f, .3f, 0, 0},
                        new float[] {.59f, .59f, .59f, 0, 0},
                        new float[] {.11f, .11f, .11f, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0, 0, 0, 0, 1}
                    });

                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(colorMatrix);
                    g.DrawImage(source, 
                        new Rectangle(0, 0, source.Width, source.Height),
                        0, 0, source.Width, source.Height,
                        GraphicsUnit.Pixel, attributes);
                }
            }
            
            return grayscale;
        }

        private void DetectMotionInZones(Bitmap grayCurrent)
        {
            int width = grayCurrent.Width;
            int height = grayCurrent.Height;
            int zoneWidth = width / 6;
            
            // Calculate deadzone (bottom X% of frame)
            int deadzoneHeight = (int)(height * _bottomDeadzonePercent / 100.0);
            int activeHeight = height - deadzoneHeight;

            BitmapData prevData = _previousFrame.LockBits(
                new Rectangle(0, 0, _previousFrame.Width, _previousFrame.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            BitmapData currData = grayCurrent.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                int bytesPerPixel = 3;
                int stride = prevData.Stride;

                unsafe
                {
                    byte* prevPtr = (byte*)prevData.Scan0;
                    byte* currPtr = (byte*)currData.Scan0;

                    for (int zone = 0; zone < 6; zone++)
                    {
                        int startX = zone * zoneWidth;
                        int endX = (zone == 5) ? width : (zone + 1) * zoneWidth;
                        int motionPixels = 0;
                        int totalPixels = (endX - startX) * activeHeight;

                        // Only process pixels above the deadzone
                        for (int y = 0; y < activeHeight; y++)
                        {
                            for (int x = startX; x < endX; x++)
                            {
                                int offset = y * stride + x * bytesPerPixel;
                                
                                // Compare grayscale values (R, G, B are same in grayscale)
                                int diff = Math.Abs(currPtr[offset] - prevPtr[offset]);
                                
                                if (diff > _motionThreshold)
                                {
                                    motionPixels++;
                                }
                            }
                        }

                        double motionPercent = (double)motionPixels / totalPixels * 100.0;
                        bool shouldBlock = motionPercent > 1.5;

                        if (shouldBlock && !_irBlocked[zone])
                        {
                            _irBlocked[zone] = true;
                            SendIrMessage((byte)zone, ChuniMessageTypes.IrBlocked);
                        }
                        else if (!shouldBlock && _irBlocked[zone])
                        {
                            _irBlocked[zone] = false;
                            SendIrMessage((byte)zone, ChuniMessageTypes.IrUnblocked);
                        }
                    }
                }
            }
            finally
            {
                _previousFrame.UnlockBits(prevData);
                grayCurrent.UnlockBits(currData);
            }
        }

        private void SendIrMessage(byte target, ChuniMessageTypes type)
        {
            var message = new ChuniIoMessage
            {
                Source = (byte)ChuniMessageSources.Controller,
                Type = (byte)type,
                Target = target
            };
            _io.Send(message);
            Console.WriteLine($"IR {target} {(type == ChuniMessageTypes.IrBlocked ? "BLOCKED" : "UNBLOCKED")}");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
