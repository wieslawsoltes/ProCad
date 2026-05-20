using System.Collections.Generic;

namespace ProCad.Core;

public sealed class DxfTextDiffResult
{
    public DxfTextDiffResult(IReadOnlyList<DxfTextDiffLine> lines, bool isApproximate, string? warning)
    {
        Lines = lines;
        IsApproximate = isApproximate;
        Warning = warning;

        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case DxfTextDiffKind.Added:
                    AddedCount++;
                    break;
                case DxfTextDiffKind.Removed:
                    RemovedCount++;
                    break;
                case DxfTextDiffKind.Modified:
                    ModifiedCount++;
                    break;
                case DxfTextDiffKind.Unchanged:
                    UnchangedCount++;
                    break;
            }
        }
    }

    public IReadOnlyList<DxfTextDiffLine> Lines { get; }

    public bool IsApproximate { get; }

    public string? Warning { get; }

    public int AddedCount { get; }

    public int RemovedCount { get; }

    public int ModifiedCount { get; }

    public int UnchangedCount { get; }
}
