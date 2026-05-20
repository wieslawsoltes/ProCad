using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace ProCad.Rendering;

/// <summary>
/// Provides Unicode-aware helpers for normalization, run segmentation, and line breaks.
/// </summary>
public sealed class UnicodeTextService : IUnicodeTextService
{
    public string Normalize(string text, NormalizationForm form = NormalizationForm.FormC)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Normalize(form);
    }

    public TextFlowDirection GetBaseDirection(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return TextFlowDirection.LeftToRight;
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsStrongRtl(rune))
            {
                return TextFlowDirection.RightToLeft;
            }

            if (IsStrongLtr(rune))
            {
                return TextFlowDirection.LeftToRight;
            }
        }

        return TextFlowDirection.LeftToRight;
    }

    public IReadOnlyList<UnicodeTextRun> GetScriptRuns(string text, TextFlowDirection? baseDirection = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<UnicodeTextRun>();
        }

        var direction = baseDirection ?? GetBaseDirection(text);
        var runs = new List<UnicodeTextRun>();

        var runStart = 0;
        var runLength = 0;
        var currentScript = string.Empty;
        var currentDirection = direction;
        var index = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var script = ResolveScriptTag(rune, currentScript);
            var runeDirection = ResolveDirection(rune, currentDirection);

            if (runLength == 0)
            {
                runStart = index;
                currentScript = script;
                currentDirection = runeDirection;
            }
            else if (!string.Equals(script, currentScript, StringComparison.Ordinal) || runeDirection != currentDirection)
            {
                runs.Add(new UnicodeTextRun(runStart, runLength, currentScript, currentDirection));
                runStart = index;
                runLength = 0;
                currentScript = script;
                currentDirection = runeDirection;
            }

            runLength += rune.Utf16SequenceLength;
            index += rune.Utf16SequenceLength;
        }

        if (runLength > 0)
        {
            runs.Add(new UnicodeTextRun(runStart, runLength, currentScript, currentDirection));
        }

        return runs;
    }

    public IReadOnlyList<int> GetLineBreakOpportunities(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<int>();
        }

        var breaks = new List<int>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var index = enumerator.ElementIndex;
            var nextIndex = index + element.Length;
            if (IsBreakAfterElement(element))
            {
                breaks.Add(nextIndex);
            }
        }

        return breaks;
    }

    public IEnumerable<TextElementSpan> EnumerateTextElements(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var index = enumerator.ElementIndex;
            var element = enumerator.GetTextElement();
            yield return new TextElementSpan(index, element.Length);
        }
    }

    private static bool IsBreakAfterElement(string element)
    {
        if (string.IsNullOrEmpty(element))
        {
            return false;
        }

        var last = element[^1];
        if (char.IsWhiteSpace(last))
        {
            return true;
        }

        return last is '-' or '/' or '\\' or ',' or ';' or ':';
    }

    private static TextFlowDirection ResolveDirection(Rune rune, TextFlowDirection fallback)
    {
        if (IsStrongRtl(rune))
        {
            return TextFlowDirection.RightToLeft;
        }

        if (IsStrongLtr(rune))
        {
            return TextFlowDirection.LeftToRight;
        }

        return fallback;
    }

    private static bool IsStrongRtl(Rune rune)
    {
        var value = rune.Value;
        return (value >= 0x0590 && value <= 0x08FF) || // Hebrew + Arabic blocks
               (value >= 0xFB1D && value <= 0xFEFC);   // Presentation forms
    }

    private static bool IsStrongLtr(Rune rune)
    {
        var value = rune.Value;
        return (value >= 0x0041 && value <= 0x007A) ||
               (value >= 0x00C0 && value <= 0x02AF) || // Latin/IPA
               (value >= 0x0370 && value <= 0x052F);   // Greek/Cyrillic
    }

    private static string ResolveScriptTag(Rune rune, string currentScript)
    {
        var value = rune.Value;
        if (value <= 0x0020)
        {
            return string.IsNullOrEmpty(currentScript) ? "Zyyy" : currentScript;
        }

        foreach (var mapping in ScriptMappings)
        {
            if (IsInRange(mapping.Range, rune))
            {
                return mapping.Tag;
            }
        }

        return string.IsNullOrEmpty(currentScript) ? "Zyyy" : currentScript;
    }

    private readonly record struct ScriptMapping(UnicodeRange Range, string Tag);

    private static bool IsInRange(UnicodeRange range, Rune rune)
    {
        var start = range.FirstCodePoint;
        var end = start + range.Length;
        var value = rune.Value;
        return value >= start && value < end;
    }

    private static readonly ScriptMapping[] ScriptMappings =
    [
        new(UnicodeRanges.BasicLatin, "Latn"),
        new(UnicodeRanges.Latin1Supplement, "Latn"),
        new(UnicodeRanges.LatinExtendedA, "Latn"),
        new(UnicodeRanges.LatinExtendedB, "Latn"),
        new(UnicodeRanges.GreekandCoptic, "Grek"),
        new(UnicodeRanges.GreekExtended, "Grek"),
        new(UnicodeRanges.Cyrillic, "Cyrl"),
        new(UnicodeRanges.CyrillicSupplement, "Cyrl"),
        new(UnicodeRanges.Arabic, "Arab"),
        new(UnicodeRanges.ArabicSupplement, "Arab"),
        new(UnicodeRanges.ArabicExtendedA, "Arab"),
        new(UnicodeRanges.Hebrew, "Hebr"),
        new(UnicodeRanges.Hiragana, "Hira"),
        new(UnicodeRanges.Katakana, "Kana"),
        new(UnicodeRanges.KatakanaPhoneticExtensions, "Kana"),
        new(UnicodeRanges.HangulSyllables, "Hang"),
        new(UnicodeRanges.HangulJamo, "Hang"),
        new(UnicodeRanges.HangulCompatibilityJamo, "Hang"),
        new(UnicodeRanges.CjkUnifiedIdeographs, "Hani"),
        new(UnicodeRanges.CjkUnifiedIdeographsExtensionA, "Hani"),
        new(UnicodeRanges.CjkSymbolsandPunctuation, "Hani"),
        new(UnicodeRanges.Thai, "Thai"),
        new(UnicodeRanges.Devanagari, "Deva")
    ];
}
