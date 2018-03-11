using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.AI.MachineLearning.Preview;
using Windows.Media;
using Windows.Storage;

namespace FERPlusEmotionRecognition
{
    public class EmotionRecognizer
    {
        public class Results
        {
            public float Neutral { get; set; }
            public float Happiness { get; set; }
            public float Surprise { get; set; }
            public float Sadness { get; set; }
            public float Anger { get; set; }
            public float Disgust { get; set; }
            public float Fear { get; set; }
            public float Contempt { get; set; }
        }

        // model and in/out tensor descriptions
        private LearningModelPreview model = null;
        private ImageVariableDescriptorPreview inputImageDescription;
        private TensorVariableDescriptorPreview outputTensorDescription;

        public async Task LoadModelAsync()
        {
            try
            {
                // Load Model
                var modelFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri($"ms-appx:///Assets/FERPlus.onnx"));
                model = await LearningModelPreview.LoadModelFromStorageFileAsync(modelFile);

                // Retrieve model input and output variable descriptions (we already know
                // the model takes an image in and outputs a tensor)
                var inputFeatures = model.Description.InputFeatures.ToList();
                var outputFeatures = model.Description.OutputFeatures.ToList();

                inputImageDescription =
                    inputFeatures.FirstOrDefault(
                        feature => feature.ModelFeatureKind == LearningModelFeatureKindPreview.Image)
                    as ImageVariableDescriptorPreview;

                outputTensorDescription =
                    outputFeatures.FirstOrDefault(
                        feature => feature.ModelFeatureKind == LearningModelFeatureKindPreview.Tensor)
                    as TensorVariableDescriptorPreview;
            }
            catch (Exception)
            {
                model = null;
                throw;
            }
        }

        public async Task<Results> EvaluateAsync(VideoFrame inputFrame)
        {
            if (model != null) {
                // Create bindings for the input and output buffer
                var binding = new LearningModelBindingPreview(model as LearningModelPreview);
                var outputVariableList = new List<float>();
                binding.Bind(inputImageDescription.Name, inputFrame);
                binding.Bind(outputTensorDescription.Name, outputVariableList);

                // Process the frame through the model
                var results = await model.EvaluateAsync(binding, String.Empty);
                var resultProbabilities = results.Outputs[outputTensorDescription.Name] as List<float>;

                // return the result
                return new Results()
                {
                    Neutral = resultProbabilities[0],
                    Happiness = resultProbabilities[1],
                    Surprise = resultProbabilities[2],
                    Sadness = resultProbabilities[3],
                    Anger = resultProbabilities[4],
                    Disgust = resultProbabilities[5],
                    Fear = resultProbabilities[6],
                    Contempt = resultProbabilities[7]
                };
            }

            return null;
        }
    }
}
