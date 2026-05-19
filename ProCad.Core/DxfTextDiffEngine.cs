using System;
using System.Collections.Generic;

namespace ProCad.Core;

public sealed class DxfTextDiffEngine
{
    private const int MaxMyersLines = 12000;
    private const int MaxTraceCells = 8_000_000;

    public DxfTextDiffResult Compare(string leftText, string rightText)
    {
        var leftLines = SplitLines(leftText);
        var rightLines = SplitLines(rightText);
        return Compare(leftLines, rightLines);
    }

    public DxfTextDiffResult Compare(IReadOnlyList<string> leftLines, IReadOnlyList<string> rightLines)
    {
        if (leftLines.Count == 0 && rightLines.Count == 0)
        {
            return new DxfTextDiffResult(Array.Empty<DxfTextDiffLine>(), false, null);
        }

        if (leftLines.Count + rightLines.Count > MaxMyersLines)
        {
            var approximate = BuildApproximateDiff(leftLines, rightLines, "DXF text diff used an approximate algorithm due to file size.");
            return approximate;
        }

        if (!TryBuildMyersEdits(leftLines, rightLines, out var edits, out var warning))
        {
            var approximate = BuildApproximateDiff(leftLines, rightLines, warning ?? "DXF text diff used an approximate algorithm due to size limits.");
            return approximate;
        }

        var lines = BuildLinesFromEdits(edits, leftLines, rightLines);
        return new DxfTextDiffResult(lines, false, null);
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\r')
            {
                var length = i - start;
                lines.Add(length > 0 ? text.Substring(start, length) : string.Empty);
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }
            else if (ch == '\n')
            {
                var length = i - start;
                lines.Add(length > 0 ? text.Substring(start, length) : string.Empty);
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text.Substring(start));
        }

        return lines;
    }

    private static bool TryBuildMyersEdits(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right,
        out List<DiffEditKind> edits,
        out string? warning)
    {
        var n = left.Count;
        var m = right.Count;
        var max = n + m;
        var size = 2 * max + 1;
        var offset = max;
        var v = new int[size];
        var trace = new List<int[]>();
        warning = null;

        var found = false;
        var foundDepth = 0;

        for (var d = 0; d <= max; d++)
        {
            for (var k = -d; k <= d; k += 2)
            {
                var index = k + offset;
                int x;
                if (k == -d || (k != d && v[index - 1] < v[index + 1]))
                {
                    x = v[index + 1];
                }
                else
                {
                    x = v[index - 1] + 1;
                }

                var y = x - k;
                while (x < n && y < m && string.Equals(left[x], right[y], StringComparison.Ordinal))
                {
                    x++;
                    y++;
                }

                v[index] = x;
                if (x >= n && y >= m)
                {
                    found = true;
                    foundDepth = d;
                }
            }

            var snapshot = new int[size];
            Array.Copy(v, snapshot, size);
            trace.Add(snapshot);

            if ((long)trace.Count * size > MaxTraceCells)
            {
                warning = "DXF text diff exceeded memory limits and switched to an approximate algorithm.";
                edits = new List<DiffEditKind>();
                return false;
            }

            if (found)
            {
                break;
            }
        }

        if (!found)
        {
            edits = new List<DiffEditKind>();
            warning = "DXF text diff exceeded the edit distance limit and switched to an approximate algorithm.";
            return false;
        }

        edits = BuildEditsFromTrace(trace, left, right, foundDepth, offset);
        return true;
    }

    private static List<DiffEditKind> BuildEditsFromTrace(
        List<int[]> trace,
        IReadOnlyList<string> left,
        IReadOnlyList<string> right,
        int depth,
        int offset)
    {
        var edits = new List<DiffEditKind>();
        var x = left.Count;
        var y = right.Count;

        for (var d = depth; d >= 0; d--)
        {
            var v = trace[d];
            var k = x - y;
            var index = k + offset;
            int prevK;
            if (k == -d || (k != d && v[index - 1] < v[index + 1]))
            {
                prevK = k + 1;
            }
            else
            {
                prevK = k - 1;
            }

            var prevX = v[prevK + offset];
            var prevY = prevX - prevK;

            while (x > prevX && y > prevY)
            {
                edits.Add(DiffEditKind.Equal);
                x--;
                y--;
            }

            if (d == 0)
            {
                break;
            }

            if (x == prevX)
            {
                edits.Add(DiffEditKind.Insert);
                y--;
            }
            else
            {
                edits.Add(DiffEditKind.Delete);
                x--;
            }
        }

        edits.Reverse();
        return edits;
    }

    private static List<DxfTextDiffLine> BuildLinesFromEdits(
        IReadOnlyList<DiffEditKind> edits,
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        var raw = new List<DxfTextDiffLine>(edits.Count);
        var leftIndex = 0;
        var rightIndex = 0;

        foreach (var edit in edits)
        {
            switch (edit)
            {
                case DiffEditKind.Equal:
                    raw.Add(new DxfTextDiffLine(
                        leftIndex + 1,
                        rightIndex + 1,
                        left[leftIndex],
                        right[rightIndex],
                        DxfTextDiffKind.Unchanged));
                    leftIndex++;
                    rightIndex++;
                    break;
                case DiffEditKind.Delete:
                    raw.Add(new DxfTextDiffLine(
                        leftIndex + 1,
                        null,
                        left[leftIndex],
                        string.Empty,
                        DxfTextDiffKind.Removed));
                    leftIndex++;
                    break;
                case DiffEditKind.Insert:
                    raw.Add(new DxfTextDiffLine(
                        null,
                        rightIndex + 1,
                        string.Empty,
                        right[rightIndex],
                        DxfTextDiffKind.Added));
                    rightIndex++;
                    break;
            }
        }

        return MergeModifiedPairs(raw);
    }

    private static List<DxfTextDiffLine> MergeModifiedPairs(IReadOnlyList<DxfTextDiffLine> raw)
    {
        var merged = new List<DxfTextDiffLine>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var current = raw[i];
            if (current.Kind == DxfTextDiffKind.Removed && i + 1 < raw.Count)
            {
                var next = raw[i + 1];
                if (next.Kind == DxfTextDiffKind.Added)
                {
                    merged.Add(new DxfTextDiffLine(
                        current.LeftLineNumber,
                        next.RightLineNumber,
                        current.LeftText,
                        next.RightText,
                        DxfTextDiffKind.Modified));
                    i++;
                    continue;
                }
            }

            merged.Add(current);
        }

        return merged;
    }

    private static DxfTextDiffResult BuildApproximateDiff(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right,
        string warning)
    {
        var lines = new List<DxfTextDiffLine>();
        var leftCount = left.Count;
        var rightCount = right.Count;

        var prefix = 0;
        while (prefix < leftCount && prefix < rightCount &&
               string.Equals(left[prefix], right[prefix], StringComparison.Ordinal))
        {
            lines.Add(new DxfTextDiffLine(prefix + 1, prefix + 1, left[prefix], right[prefix], DxfTextDiffKind.Unchanged));
            prefix++;
        }

        var leftSuffix = leftCount - 1;
        var rightSuffix = rightCount - 1;
        while (leftSuffix >= prefix && rightSuffix >= prefix &&
               string.Equals(left[leftSuffix], right[rightSuffix], StringComparison.Ordinal))
        {
            leftSuffix--;
            rightSuffix--;
        }

        var leftMiddleCount = leftSuffix - prefix + 1;
        var rightMiddleCount = rightSuffix - prefix + 1;
        var paired = Math.Min(Math.Max(leftMiddleCount, 0), Math.Max(rightMiddleCount, 0));

        for (var i = 0; i < paired; i++)
        {
            var leftIndex = prefix + i;
            var rightIndex = prefix + i;
            var leftText = leftIndex <= leftSuffix ? left[leftIndex] : string.Empty;
            var rightText = rightIndex <= rightSuffix ? right[rightIndex] : string.Empty;

            if (leftIndex <= leftSuffix && rightIndex <= rightSuffix)
            {
                var kind = string.Equals(leftText, rightText, StringComparison.Ordinal)
                    ? DxfTextDiffKind.Unchanged
                    : DxfTextDiffKind.Modified;
                lines.Add(new DxfTextDiffLine(leftIndex + 1, rightIndex + 1, leftText, rightText, kind));
            }
            else if (leftIndex <= leftSuffix)
            {
                lines.Add(new DxfTextDiffLine(leftIndex + 1, null, leftText, string.Empty, DxfTextDiffKind.Removed));
            }
            else if (rightIndex <= rightSuffix)
            {
                lines.Add(new DxfTextDiffLine(null, rightIndex + 1, string.Empty, rightText, DxfTextDiffKind.Added));
            }
        }

        for (var i = prefix + paired; i <= leftSuffix; i++)
        {
            lines.Add(new DxfTextDiffLine(i + 1, null, left[i], string.Empty, DxfTextDiffKind.Removed));
        }

        for (var i = prefix + paired; i <= rightSuffix; i++)
        {
            lines.Add(new DxfTextDiffLine(null, i + 1, string.Empty, right[i], DxfTextDiffKind.Added));
        }

        var suffixLength = leftCount - (leftSuffix + 1);
        for (var i = 0; i < suffixLength; i++)
        {
            var leftIndex = leftSuffix + 1 + i;
            var rightIndex = rightSuffix + 1 + i;
            lines.Add(new DxfTextDiffLine(leftIndex + 1, rightIndex + 1, left[leftIndex], right[rightIndex], DxfTextDiffKind.Unchanged));
        }

        return new DxfTextDiffResult(lines, true, warning);
    }

    private enum DiffEditKind
    {
        Equal,
        Insert,
        Delete
    }
}
