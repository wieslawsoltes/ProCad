using System.Threading;
using System.Threading.Tasks;

namespace ACadInspector.Core;

public interface ICadBatchExportService
{
    Task<CadBatchExportResult?> SaveExportAsync(CadBatchExportFormat format, string? suggestedFileName, CancellationToken cancellationToken);
}
