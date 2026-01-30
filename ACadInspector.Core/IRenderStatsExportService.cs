using System.Threading;
using System.Threading.Tasks;

namespace ACadInspector.Core;

public interface IRenderStatsExportService
{
    Task<RenderStatsExportResult?> SaveStatsAsync(string? suggestedFileName, CancellationToken cancellationToken);
}
