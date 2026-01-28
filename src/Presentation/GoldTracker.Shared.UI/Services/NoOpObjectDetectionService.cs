using Archery.Shared.Models;
using Archery.Shared.Services;
using System;
using System.Collections.Generic;

namespace GoldTracker.Shared.UI.Services
{
    /// <summary>
    /// A no-op implementation of IObjectDetectionService.
    /// Used when local inference is not available or desired (e.g. Browser/WASM or tests).
    /// </summary>
    public class NoOpObjectDetectionService : IObjectDetectionService
    {
        public ObjectDetectionConfig Config { get; }

        public NoOpObjectDetectionService(ObjectDetectionConfig config)
        {
            Config = config;
        }

        public List<ObjectDetectionResult> Predict(byte[] imageBytes, string? filePath = null)
        {
            // Do nothing
            return new List<ObjectDetectionResult>();
        }

        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}
