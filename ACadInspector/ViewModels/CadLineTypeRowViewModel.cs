using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ACadSharp.Tables;
using Avalonia.Media.Imaging;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadLineTypeRowViewModel : ViewModelBase
{
    public LineType LineType { get; }

    [Reactive]
    public partial string Name { get; set; } = string.Empty;

    [Reactive]
    public partial string Handle { get; set; } = string.Empty;

    [Reactive]
    public partial string Description { get; set; } = string.Empty;

    [Reactive]
    public partial string PatternLength { get; set; } = string.Empty;

    [Reactive]
    public partial string SegmentCount { get; set; } = string.Empty;

    [Reactive]
    public partial bool IsComplex { get; set; }

    [Reactive]
    public partial bool HasShapes { get; set; }

    [Reactive]
    public partial bool IsCurrent { get; set; }

    [Reactive]
    public partial string SegmentSummary { get; set; } = string.Empty;

    [Reactive]
    public partial Bitmap? Preview { get; set; }

    public CadLineTypeRowViewModel(LineType lineType)
    {
        LineType = lineType;
        RefreshFromLineType(currentLineTypeName: null);
    }

    public void RefreshFromLineType(string? currentLineTypeName)
    {
        Name = LineType.Name;
        Handle = LineType.Handle == 0 ? string.Empty : LineType.Handle.ToString("X", CultureInfo.InvariantCulture);
        Description = LineType.Description ?? string.Empty;
        PatternLength = LineType.PatternLength.ToString("0.###", CultureInfo.InvariantCulture);

        var segments = LineType.Segments.ToList();
        SegmentCount = segments.Count.ToString(CultureInfo.InvariantCulture);
        IsComplex = LineType.IsComplex;
        HasShapes = LineType.HasShapes;
        IsCurrent = !string.IsNullOrWhiteSpace(currentLineTypeName) &&
                    string.Equals(currentLineTypeName, LineType.Name, StringComparison.OrdinalIgnoreCase);
        SegmentSummary = BuildSegmentSummary(segments);
    }

    private static string BuildSegmentSummary(IReadOnlyList<LineType.Segment> segments)
    {
        if (segments.Count == 0)
        {
            return "Continuous";
        }

        var tokens = new List<string>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment.IsPoint)
            {
                tokens.Add("0");
                continue;
            }

            var prefix = segment.IsSpace ? "-" : string.Empty;
            var magnitude = Math.Abs(segment.Length).ToString("0.###", CultureInfo.InvariantCulture);
            var token = $"{prefix}{magnitude}";

            if (segment.IsText && !string.IsNullOrWhiteSpace(segment.Text))
            {
                token += $"[\"{segment.Text}\"]";
            }
            else if (segment.IsShape)
            {
                token += $"[#{segment.ShapeNumber}]";
            }

            tokens.Add(token);
        }

        return string.Join(", ", tokens);
    }
}
