using System.Threading;
using System.Threading.Tasks;

namespace ProCad.Core;

public interface ICadBatchExportService
{
    Task<CadBatchExportResult?> SaveExportAsync(CadBatchExportFormat format, string? suggestedFileName, CancellationToken cancellationToken);
}
