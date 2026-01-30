using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ACadInspector.Core;

public interface ICadBatchFileDialogService
{
    Task<IReadOnlyList<CadOpenFileResult>> OpenCadFilesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CadOpenFileResult>> OpenCadFolderAsync(CancellationToken cancellationToken);
}
