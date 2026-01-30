using Archery.Shared.Models;

namespace GoldTracker.Shared.UI.Services.Abstractions
{
    public interface IDatasetExportService
    {
        Task ExportDatasetAsync(byte[] originalImageBytes, List<DatasetAnnotation> annotations, string? filePath = null);
    }
}
