using System.Threading;
using System.Threading.Tasks;

namespace ACadInspector.Core;

public interface ICadFileDialogService
{
    Task<CadOpenFileResult?> OpenCadFileAsync(CadFileFormat? preferredFormat, CancellationToken cancellationToken);
    Task<CadSaveFileResult?> SaveCadFileAsync(CadFileFormat format, string? suggestedFileName, CancellationToken cancellationToken);
}
