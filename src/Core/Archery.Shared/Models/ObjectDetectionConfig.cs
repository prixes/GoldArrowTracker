// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

using System.Text.Json;
using System.Reflection;

namespace Archery.Shared.Models;

/// <summary>
/// Configuration for Object Detection inference.
/// </summary>
public class ObjectDetectionConfig
{
    /// <summary>
    /// Gets or sets the model input size (assumed square: 640x640).
    /// </summary>
    public int InputSize { get; set; }

    /// <summary>
    /// Gets or sets the confidence threshold for detections.
    /// Note: This model uses class confidence directly (not objectness), 
    /// so threshold should be 0.3-0.5 for reasonable detection.
    /// </summary>
    public float ConfidenceThreshold { get; set; }

    /// <summary>
    /// Gets or sets the Non-Maximum Suppression (NMS) threshold (default 0.45).
    /// </summary>
    public float NmsThreshold { get; set; }

    /// <summary>
    /// Gets or sets the class labels mapping.
    /// Model has 12 classes total, with 11 score rings (0-10) and 'target' class.
    /// Classes represent archery scoring rings: 0=miss, 1-10=rings, 'target'=target face
    /// </summary>
    public Dictionary<int, string> ClassLabels { get; set; } = new();

    /// <summary>
    /// Loads configuration from an embedded JSON resource.
    /// </summary>
    /// <param name="resourceName">The full name of the embedded JSON resource (e.g., "YourAssembly.Configurations.object_detection_config.json").</param>
    /// <returns>A ObjectDetectionConfig instance populated with data from the JSON.</returns>
    public static async Task<ObjectDetectionConfig> LoadFromJsonAsync(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        }

        // Copy to MemoryStream to ensure seekability if the original stream doesn't support it
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var config = await JsonSerializer.DeserializeAsync<ObjectDetectionConfig>(memoryStream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true // Allow matching "InputSize" to "inputsize" etc.
        });

        if (config == null)
        {
            throw new InvalidOperationException($"Failed to deserialize ObjectDetectionConfig from embedded resource '{resourceName}'.");
        }

        return config;
    }
}
