using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Windows.AI.MachineLearning.Preview;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace FERPlusEmotionRecognition
{
    public sealed partial class MainPage : Page
    {
        // our high level recognizer
        private EmotionRecognizer recognizer = null;

        // for capturing the device's camera
        private MediaCapture mediaCapture = null;

        public MainPage()
        {
            this.InitializeComponent();

            this.recognizer = new EmotionRecognizer();
        }

        /// <summary>
        /// Initialize the model and the camera
        /// </summary>
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Load model
                await recognizer.LoadModelAsync();

                // Using Windows.Media.Capture.MediaCapture APIs
                // to stream from webcam
                mediaCapture = new MediaCapture();
                await mediaCapture.InitializeAsync();

                // Start capture preview
                PreviewControl.Source = mediaCapture;
                PreviewControl.FlowDirection = FlowDirection.RightToLeft;
                await mediaCapture.StartPreviewAsync();
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusBlock.Text = $"error: {ex.Message}");
            }
        }

        /// <summary>
        /// Trigger take photo and recognize the emotion present in the image
        /// </summary>
        private async void ButtonRun_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Prepare and capture photo
                var lowLagCapture = await mediaCapture.PrepareLowLagPhotoCaptureAsync(ImageEncodingProperties.CreateUncompressed(MediaPixelFormat.Bgra8));

                var capturedPhoto = await lowLagCapture.CaptureAsync();
                var softwareBitmap = capturedPhoto.Frame.SoftwareBitmap;

                await lowLagCapture.FinishAsync();

                // Display the captured image
                var imageSource = new SoftwareBitmapSource();
                var displayableImage = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                await imageSource.SetBitmapAsync(displayableImage);
                PreviewImage.Source = imageSource;

                // Crop, Resize and Convert to gray scale the image (FERPlus model expects a grayscale image of 64x64 pixels)
                var rgbaImage = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied);
                var mindim = Math.Min(rgbaImage.PixelWidth, rgbaImage.PixelHeight);
                var croppedBitmap = CropSoftwareBitmap(rgbaImage,
                    rgbaImage.PixelWidth / 2 - mindim / 2,
                    rgbaImage.PixelHeight / 2 - mindim / 2,
                    mindim, mindim);
                var scaledBitmap = ResizeSoftwareBitmap(croppedBitmap, 64, 64);
                var grayscaleBitmap = SoftwareBitmap.Convert(scaledBitmap, BitmapPixelFormat.Gray8, BitmapAlphaMode.Ignore);

                // Finally, Predict the dominat emotion present in the image (assuming that the image predominatly shows a face)
                VideoFrame inputImage = VideoFrame.CreateWithSoftwareBitmap(grayscaleBitmap);
                await Task.Run(async () =>
                {
                    // Evaluate the image
                    await EvaluateVideoFrameAsync(inputImage);
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusBlock.Text = $"error: {ex.Message}");
            }
        }

        /// <summary>
        /// Predict the emotion based on the VideoFrame passed in as arg
        /// </summary>
        private async Task EvaluateVideoFrameAsync(VideoFrame inputFrame)
        {
            if (inputFrame != null)
            {
                try
                {
                    var result = await this.recognizer.EvaluateAsync(inputFrame);

                    // Display the result
                    string message = "The emotions recognized are:";
                    message += $"\nNeutral with confidence of {result.Neutral:0.000}";
                    message += $"\nHappiness with confidence of {result.Happiness:0.000}";
                    message += $"\nSurprise with confidence of {result.Surprise:0.000}";
                    message += $"\nSadness with confidence of {result.Sadness:0.000}";
                    message += $"\nAnger with confidence of {result.Anger:0.000}";
                    message += $"\nDisgust with confidence of {result.Disgust:0.000}";
                    message += $"\nFear with confidence of {result.Fear:0.000}";
                    message += $"\nContempt with confidence of {result.Contempt:0.000}";

                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusBlock.Text = message);
                }
                catch (Exception ex)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusBlock.Text = $"error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Helper metohd that resize a SoftwareBitmap given a new width and height
        /// </summary>
        private SoftwareBitmap ResizeSoftwareBitmap(SoftwareBitmap softwareBitmap, float newWidth, float newHeight)
        {
            using (var resourceCreator = CanvasDevice.GetSharedDevice())
            using (var canvasBitmap = CanvasBitmap.CreateFromSoftwareBitmap(resourceCreator, softwareBitmap))
            using (var canvasRenderTarget = new CanvasRenderTarget(resourceCreator, newWidth, newHeight, canvasBitmap.Dpi))
            using (var drawingSession = canvasRenderTarget.CreateDrawingSession())
            using (var scaleEffect = new ScaleEffect())
            {
                scaleEffect.Source = canvasBitmap;
                scaleEffect.InterpolationMode = CanvasImageInterpolation.HighQualityCubic;
                scaleEffect.Scale = new System.Numerics.Vector2(newWidth / softwareBitmap.PixelWidth, newHeight / softwareBitmap.PixelHeight);
                drawingSession.DrawImage(scaleEffect);
                drawingSession.Flush();
                return SoftwareBitmap.CreateCopyFromBuffer(canvasRenderTarget.GetPixelBytes().AsBuffer(), BitmapPixelFormat.Bgra8, (int)newWidth, (int)newHeight, BitmapAlphaMode.Premultiplied);
            }
        }

        /// <summary>
        /// Helper metohd that crop a SoftwareBitmap given a new bounding box
        /// </summary>
        private SoftwareBitmap CropSoftwareBitmap(SoftwareBitmap softwareBitmap, float x, float y, float width, float height)
        {
            using (var resourceCreator = CanvasDevice.GetSharedDevice())
            using (var canvasBitmap = CanvasBitmap.CreateFromSoftwareBitmap(resourceCreator, softwareBitmap))
            using (var canvasRenderTarget = new CanvasRenderTarget(resourceCreator, width, height, canvasBitmap.Dpi))
            using (var drawingSession = canvasRenderTarget.CreateDrawingSession())
            using (var cropEffect = new CropEffect())
            {
                cropEffect.Source = canvasBitmap;
                drawingSession.DrawImage(
                    cropEffect,
                    new Rect(0.0, 0.0, width, height),
                    new Rect(x, y, width, height),
                    (float) 1.0,
                    CanvasImageInterpolation.HighQualityCubic);
                drawingSession.Flush();
                return SoftwareBitmap.CreateCopyFromBuffer(canvasRenderTarget.GetPixelBytes().AsBuffer(), BitmapPixelFormat.Rgba8, (int)width, (int)height, BitmapAlphaMode.Premultiplied);
            }
        }
    }
}
