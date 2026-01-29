
using Archery.Shared.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GoldTracker.Shared.UI.Services.Abstractions
{
    public interface IDatasetExportService
    {
        Task ExportDatasetAsync(byte[] originalImageBytes, List<DatasetAnnotation> annotations, string? filePath = null);
    }
}
