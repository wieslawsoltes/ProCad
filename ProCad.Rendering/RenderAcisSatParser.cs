using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProCad.Rendering;

internal sealed class RenderAcisSatDocument
{
    public IReadOnlyList<RenderAcisSatRecord> Records { get; }
    public IReadOnlyList<RenderAcisSatSubtype> Subtypes { get; }

    public RenderAcisSatDocument(
        IReadOnlyList<RenderAcisSatRecord> records,
        IReadOnlyList<RenderAcisSatSubtype> subtypes)
    {
        Records = records;
        Subtypes = subtypes;
    }
}

internal sealed class RenderAcisSatRecord
{
    public int Id { get; }
    public int SequenceId { get; }
    public string Type { get; }
    public IReadOnlyList<string> Tokens { get; }
    public IReadOnlyList<int> References { get; }
    public IReadOnlyList<int> SubtypeReferences { get; }
    public IReadOnlyList<double> Numbers { get; }

    public RenderAcisSatRecord(
        int id,
        int sequenceId,
        string type,
        IReadOnlyList<string> tokens,
        IReadOnlyList<int> references,
        IReadOnlyList<int> subtypeReferences,
        IReadOnlyList<double> numbers)
    {
        Id = id;
        SequenceId = sequenceId;
        Type = type;
        Tokens = tokens;
        References = references;
        SubtypeReferences = subtypeReferences;
        Numbers = numbers;
    }
}

internal sealed class RenderAcisSatSubtype
{
    public int Index { get; }
    public IReadOnlyList<string> Tokens { get; }
    public IReadOnlyList<int> References { get; }
    public IReadOnlyList<int> SubtypeReferences { get; }
    public IReadOnlyList<double> Numbers { get; }

    public RenderAcisSatSubtype(
        int index,
        IReadOnlyList<string> tokens,
        IReadOnlyList<int> references,
        IReadOnlyList<int> subtypeReferences,
        IReadOnlyList<double> numbers)
    {
        Index = index;
        Tokens = tokens;
        References = references;
        SubtypeReferences = subtypeReferences;
        Numbers = numbers;
    }
}

internal static class RenderAcisSatParser
{
    public static bool TryParse(string? text, out RenderAcisSatDocument document)
    {
        document = new RenderAcisSatDocument(
            Array.Empty<RenderAcisSatRecord>(),
            Array.Empty<RenderAcisSatSubtype>());
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (RenderAcisSabDecoder.TryDecode(text, out var decoded))
        {
            text = decoded;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!text.Contains('#'))
        {
            return TryParseLegacy(text, out document);
        }

        var tokens = Tokenize(text);
        var records = ParseRecords(tokens, requireNegativeSequence: true, out var subtypes);
        if (records.Count == 0)
        {
            records = ParseRecords(tokens, requireNegativeSequence: false, out subtypes);
        }

        if (records.Count == 0)
        {
            return false;
        }

        document = new RenderAcisSatDocument(records, subtypes);
        return true;
    }

    private static bool TryParseLegacy(string text, out RenderAcisSatDocument document)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var records = new List<RenderAcisSatRecord>();
        var subtypes = new List<RenderAcisSatSubtype>();
        var autoId = 0;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || IsHeaderLine(line))
            {
                continue;
            }

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            if (IsNumericLine(tokens))
            {
                continue;
            }

            var record = CreateRecord(tokens, ref autoId, subtypes);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        document = new RenderAcisSatDocument(records, subtypes);
        return records.Count > 0;
    }

    private static List<RenderAcisSatRecord> ParseRecords(
        IReadOnlyList<string> tokens,
        bool requireNegativeSequence,
        out List<RenderAcisSatSubtype> subtypes)
    {
        var records = new List<RenderAcisSatRecord>();
        subtypes = new List<RenderAcisSatSubtype>();
        var autoId = 0;
        var current = new List<string>();
        var collecting = false;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token == "#")
            {
                if (current.Count > 0)
                {
                    var record = CreateRecord(current, ref autoId, subtypes);
                    if (record is not null)
                    {
                        records.Add(record);
                    }
                }

                current.Clear();
                collecting = false;
                continue;
            }

            if (!collecting)
            {
                var nextToken = i + 1 < tokens.Count ? tokens[i + 1] : string.Empty;
                if (!IsRecordStart(token, nextToken, requireNegativeSequence))
                {
                    continue;
                }

                collecting = true;
                current.Clear();
            }

            if (collecting)
            {
                current.Add(token);
            }
        }

        return records;
    }

    private static RenderAcisSatRecord? CreateRecord(
        IReadOnlyList<string> tokens,
        ref int autoId,
        List<RenderAcisSatSubtype> subtypes)
    {
        if (tokens.Count == 0)
        {
            return null;
        }

        var sequenceId = 0;
        var tokenStart = 0;
        if (tokens.Count > 1 && TryParseInt(tokens[0], out var seq) && !IsNumericToken(tokens[1]))
        {
            sequenceId = seq;
            tokenStart = 1;
        }

        if (tokens.Count <= tokenStart)
        {
            return null;
        }

        var type = tokens[tokenStart];
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var id = sequenceId != 0 ? Math.Abs(sequenceId) : ++autoId;
        var recordTokens = new List<string>(tokens.Count - tokenStart);
        for (var i = tokenStart; i < tokens.Count; i++)
        {
            recordTokens.Add(tokens[i]);
        }

        CaptureSubtypes(recordTokens, subtypes);

        var references = new List<int>();
        var subtypeReferences = new List<int>();
        var numbers = new List<double>();
        ExtractValues(recordTokens, 1, references, subtypeReferences, numbers);

        return new RenderAcisSatRecord(
            id,
            sequenceId,
            type,
            recordTokens,
            references,
            subtypeReferences,
            numbers);
    }

    private static void CaptureSubtypes(IReadOnlyList<string> tokens, List<RenderAcisSatSubtype> subtypes)
    {
        var stack = new Stack<SubtypeBuilder>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token == "{")
            {
                var builder = new SubtypeBuilder(subtypes.Count);
                subtypes.Add(new RenderAcisSatSubtype(
                    builder.Index,
                    Array.Empty<string>(),
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    Array.Empty<double>()));
                stack.Push(builder);
                continue;
            }

            if (token == "}")
            {
                if (stack.Count == 0)
                {
                    continue;
                }

                var builder = stack.Pop();
                subtypes[builder.Index] = BuildSubtype(builder);
                continue;
            }

            if (stack.Count > 0)
            {
                stack.Peek().Tokens.Add(token);
            }
        }
    }

    private static RenderAcisSatSubtype BuildSubtype(SubtypeBuilder builder)
    {
        var references = new List<int>();
        var subtypeReferences = new List<int>();
        var numbers = new List<double>();
        ExtractValues(builder.Tokens, 0, references, subtypeReferences, numbers);
        return new RenderAcisSatSubtype(builder.Index, builder.Tokens, references, subtypeReferences, numbers);
    }

    private static void ExtractValues(
        IReadOnlyList<string> tokens,
        int startIndex,
        List<int> references,
        List<int> subtypeReferences,
        List<double> numbers)
    {
        for (var i = startIndex; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token == "{" || token == "}")
            {
                continue;
            }

            if (string.Equals(token, "ref", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
            {
                if (TryParseInt(tokens[i + 1], out var subtypeIndex))
                {
                    subtypeReferences.Add(subtypeIndex);
                }

                i++;
                continue;
            }

            if (TryParseReference(token, out var reference))
            {
                if (reference > 0)
                {
                    references.Add(reference);
                }
                continue;
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                numbers.Add(number);
            }
        }
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var buffer = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                FlushToken(tokens, buffer);
                continue;
            }

            if (ch is '#' or '{' or '}')
            {
                FlushToken(tokens, buffer);
                tokens.Add(ch.ToString(CultureInfo.InvariantCulture));
                continue;
            }

            buffer.Append(ch);
        }

        FlushToken(tokens, buffer);
        return tokens;
    }

    private static void FlushToken(List<string> tokens, StringBuilder buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        tokens.Add(buffer.ToString());
        buffer.Clear();
    }

    private static bool IsRecordStart(string token, string nextToken, bool requireNegativeSequence)
    {
        if (!TryParseInt(token, out var sequenceId))
        {
            return false;
        }

        if (requireNegativeSequence && sequenceId >= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(nextToken) || nextToken is "#" or "{" or "}")
        {
            return false;
        }

        return !IsNumericToken(nextToken);
    }

    private static bool IsHeaderLine(string line)
    {
        if (line.StartsWith("ACIS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (line.StartsWith("ASM", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (line.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("END", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsNumericLine(string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return true;
        }

        for (var i = 0; i < tokens.Length; i++)
        {
            if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumericToken(string token)
    {
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static bool TryParseInt(string token, out int value)
    {
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseReference(string token, out int reference)
    {
        reference = 0;
        if (token.Length < 2 || token[0] != '$')
        {
            return false;
        }

        if (!int.TryParse(token.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (value <= 0)
        {
            return true;
        }

        reference = value;
        return true;
    }

    private sealed class SubtypeBuilder
    {
        public int Index { get; }
        public List<string> Tokens { get; }

        public SubtypeBuilder(int index)
        {
            Index = index;
            Tokens = new List<string>();
        }
    }

}
