using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Archery.Shared.Models;

namespace Archery.Shared.Services;

/// <summary>
/// Service for running YOLO inference using ONNX Runtime.
/// Handles model loading, inference, post-processing, and NMS filtering.
/// </summary>
public class YoloInferenceService : IYoloInferenceService
{
    private InferenceSession? _session;
    private readonly YoloConfig _config;

    public YoloInferenceService(string modelPath, YoloConfig config)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"YOLO model not found at: {modelPath}");
        }

        _config = config;

        try
        {
            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_VERBOSE,
            };
            _session = new InferenceSession(modelPath, sessionOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load YOLO model: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Runs YOLO inference on an image and returns detections.
    /// </summary>
    public List<YoloDetection> Predict(byte[] imageBytes)
    {
        if (_session == null)
        {
            throw new InvalidOperationException("Inference session not initialized");
        }

        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image bytes cannot be null or empty", nameof(imageBytes));
        }

        try
        {
            // 1. Preprocess image
            System.Diagnostics.Debug.WriteLine("[YoloInferenceService] Starting inference...");
            var (inputTensor, origWidth, origHeight, scaleX, scaleY) =
                YoloPreprocessingUtility.PreprocessImage(imageBytes, _config.InputSize);
            
            System.Diagnostics.Debug.WriteLine(
                $"[YoloInferenceService] Image preprocessed: {origWidth}x{origHeight} -> " +
                $"tensor shape [{string.Join(", ", inputTensor.Dimensions.ToArray())}]");

            // 2. Create inference input
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("images", inputTensor)
            };

            // 3. Run inference
            System.Diagnostics.Debug.WriteLine("[YoloInferenceService] Running YOLO inference...");
            using var results = _session.Run(inputs);
            
            System.Diagnostics.Debug.WriteLine($"[YoloInferenceService] Inference returned {results.Count} outputs");
            foreach (var result in results)
            {
                System.Diagnostics.Debug.WriteLine($"[YoloInferenceService] Output: {result.Name}");
            }

            // 4. Post-process outputs
            var detections = PostProcessResults(results, origWidth, origHeight, scaleX, scaleY);

            System.Diagnostics.Debug.WriteLine($"[YoloInferenceService] ✓ Inference complete: {detections.Count} detections");

            // 5. Apply NMS (Non-Maximum Suppression)
            var filteredDetections = ApplyNms(detections, _config.NmsThreshold);

            System.Diagnostics.Debug.WriteLine(
                $"[YoloInferenceService] ✓ After NMS: {filteredDetections.Count} detections");

            return filteredDetections;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[YoloInferenceService] ✗ Inference failed: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[YoloInferenceService] Stack: {ex.StackTrace}");
            throw new InvalidOperationException($"YOLO inference failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Post-processes YOLO model outputs into detection objects.
    /// YOLO outputs shape: [1, 25, 8400] or similar
    /// Each detection: [x, y, w, h, confidence, class_scores...]
    /// </summary>
    private List<YoloDetection> PostProcessResults(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        int originalWidth,
        int originalHeight,
        float scaleX,
        float scaleY)
    {
        var detections = new List<YoloDetection>();

        // Get the output tensor (typically named "output0" or similar)
        var output = results.FirstOrDefault();
        if (output == null)
        {
            System.Diagnostics.Debug.WriteLine("[YoloInferenceService] ✗ No output tensor found");
            return detections;
        }

        System.Diagnostics.Debug.WriteLine($"[YoloInferenceService] Output name: {output.Name}");

        var outputTensor = output.Value as DenseTensor<float>;
        if (outputTensor == null)
        {
            System.Diagnostics.Debug.WriteLine("[YoloInferenceService] ✗ Output tensor is not DenseTensor<float>");
            return detections;
        }

        // Get dimensions: [1, num_classes+5, num_predictions]
        var dimensions = outputTensor.Dimensions.ToArray();
        System.Diagnostics.Debug.WriteLine($"[YoloInferenceService] Output tensor dimensions: [{string.Join(", ", dimensions)}]");
        
        if (dimensions.Length < 3)
        {
            System.Diagnostics.Debug.WriteLine($"[YoloInferenceService] ✗ Unexpected tensor dimensions: {dimensions.Length}");
            return detections;
        }

        int numClasses = dimensions[1] - 5;
        int numPredictions = dimensions[2];
        
        System.Diagnostics.Debug.WriteLine($"[YoloInferenceService] Classes: {numClasses}, Predictions: {numPredictions}");

        int detectedCount = 0;
        int filteredCount = 0;
        var topPredictions = new List<(int Index, float Conf, float ClassScore, float Final, int ClassId)>();

        // Process each prediction
        for (int i = 0; i < numPredictions; i++)
        {
            // Extract raw values
            float x = outputTensor[0, 0, i];
            float y = outputTensor[0, 1, i];
            float w = outputTensor[0, 2, i];
            float h = outputTensor[0, 3, i];
            float confidence = outputTensor[0, 4, i];

            // Get class scores
            float maxClassScore = 0;
            int classId = 0;

            for (int c = 0; c < numClasses; c++)
            {
                float classScore = outputTensor[0, 5 + c, i];
                if (classScore > maxClassScore)
                {
                    maxClassScore = classScore;
                    classId = c;
                }
            }

            // FIX: The model has unreliable objectness scores. The class score is a more reliable
            // indicator of confidence. We use the max class score directly as the confidence value.
            float finalConfidence = maxClassScore;

            // Track top predictions for logging
            if (topPredictions.Count < 10 || finalConfidence > topPredictions.Min(p => p.Final))
            {
                topPredictions.Add((i, confidence, maxClassScore, finalConfidence, classId));
                topPredictions = topPredictions.OrderByDescending(p => p.Final).Take(10).ToList();
            }

            // Filter by confidence threshold
            // Since objectness is 0, use class confidence directly for threshold
            float threshold = _config.ConfidenceThreshold;
            
            if (finalConfidence < threshold)
            {
                filteredCount++;
                continue;
            }

            detectedCount++;

            // Scale coordinates back to original image size
            float scaledX = x * scaleX;
            float scaledY = y * scaleY;
            float scaledW = w * scaleX;
            float scaledH = h * scaleY;

            // Create detection object
            var detection = new YoloDetection
            {
                ClassId = classId,
                ClassName = _config.ClassLabels.TryGetValue(classId, out var name) ? name : $"class_{classId}",
                Confidence = finalConfidence,
                X = scaledX,
                Y = scaledY,
                Width = scaledW,
                Height = scaledH
            };

            System.Diagnostics.Debug.WriteLine(
                $"[YoloInferenceService] ✓ Added detection: {detection.ClassName} " +
                $"({detection.Confidence:P}) at ({detection.X:F1}, {detection.Y:F1}) [ClassId: {classId}]");

            detections.Add(detection);
        }

        // Log top 10 predictions
        System.Diagnostics.Debug.WriteLine("[YoloInferenceService] TOP 10 PREDICTIONS (all, regardless of threshold):");
        foreach (var pred in topPredictions)
        {
            var className = _config.ClassLabels.TryGetValue(pred.ClassId, out var name) ? name : $"class_{pred.ClassId}";
            System.Diagnostics.Debug.WriteLine(
                $"  [{pred.Index}] {className}: objConf={pred.Conf:P2}, " +
                $"classConf={pred.ClassScore:P2}, final={pred.Final:P2}");
        }

        System.Diagnostics.Debug.WriteLine(
            $"[YoloInferenceService] Post-processing complete: " +
            $"detected={detectedCount}, filtered={filteredCount}, total={detections.Count}");

        return detections;
    }

    /// <summary>
    /// Applies Non-Maximum Suppression to remove overlapping detections.
    /// </summary>
    private List<YoloDetection> ApplyNms(List<YoloDetection> detections, float nmsThreshold)
    {
        if (detections.Count == 0)
        {
            return detections;
        }

        // Sort by confidence descending
        var sorted = detections.OrderByDescending(d => d.Confidence).ToList();
        var kept = new List<YoloDetection>();

        while (sorted.Count > 0)
        {
            // Take the detection with highest confidence
            var best = sorted[0];
            kept.Add(best);
            sorted.RemoveAt(0);

            // Remove detections with high IoU (Intersection over Union)
            sorted = sorted.Where(d =>
            {
                float iou = CalculateIou(best, d);
                return iou < nmsThreshold;
            }).ToList();
        }

        return kept;
    }

    /// <summary>
    /// Calculates Intersection over Union between two detections.
    /// </summary>
    private float CalculateIou(YoloDetection det1, YoloDetection det2)
    {
        // Convert center coordinates to corner coordinates
        float x1_min = det1.X - det1.Width / 2;
        float y1_min = det1.Y - det1.Height / 2;
        float x1_max = det1.X + det1.Width / 2;
        float y1_max = det1.Y + det1.Height / 2;

        float x2_min = det2.X - det2.Width / 2;
        float y2_min = det2.Y - det2.Height / 2;
        float x2_max = det2.X + det2.Width / 2;
        float y2_max = det2.Y + det2.Height / 2;

        // Calculate intersection
        float inter_x_min = Math.Max(x1_min, x2_min);
        float inter_y_min = Math.Max(y1_min, y2_min);
        float inter_x_max = Math.Min(x1_max, x2_max);
        float inter_y_max = Math.Min(y1_max, y2_max);

        float inter_width = Math.Max(0, inter_x_max - inter_x_min);
        float inter_height = Math.Max(0, inter_y_max - inter_y_min);
        float inter_area = inter_width * inter_height;

        // Calculate union
        float area1 = det1.Width * det1.Height;
        float area2 = det2.Width * det2.Height;
        float union_area = area1 + area2 - inter_area;

        // Calculate IoU
        return union_area > 0 ? inter_area / union_area : 0;
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}