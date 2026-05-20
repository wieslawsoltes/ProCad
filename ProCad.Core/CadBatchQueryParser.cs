using System.Collections.Generic;
using System.Text;

namespace ProCad.Core;

public static class CadBatchQueryParser
{
    public static CadBatchQuery Parse(string? text)
    {
        var terms = new List<CadBatchQueryTerm>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CadBatchQuery(terms);
        }

        foreach (var token in Tokenize(text))
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var separatorIndex = token.IndexOf(':');
            if (separatorIndex < 0)
            {
                separatorIndex = token.IndexOf('=');
            }

            if (separatorIndex > 0)
            {
                var key = token[..separatorIndex].Trim();
                var value = token[(separatorIndex + 1)..].Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                terms.Add(new CadBatchQueryTerm(key.Length == 0 ? null : key, value));
            }
            else
            {
                terms.Add(new CadBatchQueryTerm(null, token.Trim()));
            }
        }

        return new CadBatchQuery(terms);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in text)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (builder.Length > 0)
                {
                    tokens.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(ch);
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }
}
