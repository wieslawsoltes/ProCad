using System.Collections.Generic;
using System.Text;

namespace ProCad.Rendering;

public interface IUnicodeTextService
{
    string Normalize(string text, NormalizationForm form = NormalizationForm.FormC);
    TextFlowDirection GetBaseDirection(string text);
    IReadOnlyList<UnicodeTextRun> GetScriptRuns(string text, TextFlowDirection? baseDirection = null);
    IReadOnlyList<int> GetLineBreakOpportunities(string text);
    IEnumerable<TextElementSpan> EnumerateTextElements(string text);
}

public enum TextFlowDirection
{
    LeftToRight,
    RightToLeft
}

public readonly record struct UnicodeTextRun(
    int Start,
    int Length,
    string ScriptTag,
    TextFlowDirection Direction);

public readonly record struct TextElementSpan(int Start, int Length);
