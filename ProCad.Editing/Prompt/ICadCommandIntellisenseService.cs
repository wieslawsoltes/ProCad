using ProCad.Editing.Commands;

namespace ProCad.Editing.Prompt;

public interface ICadCommandIntellisenseService
{
    IReadOnlyList<CadCommandCompletionItem> GetCompletions(string input, int cursorIndex);
    string? GetParameterHelp(string input, int cursorIndex);
}

public sealed class CadCommandIntellisenseService : ICadCommandIntellisenseService
{
    private readonly ICadCommandRegistry _registry;

    public CadCommandIntellisenseService(ICadCommandRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public IReadOnlyList<CadCommandCompletionItem> GetCompletions(string input, int cursorIndex)
    {
        return _registry.GetCompletions(input ?? string.Empty, cursorIndex, includeKeywords: true);
    }

    public string? GetParameterHelp(string input, int cursorIndex)
    {
        input ??= string.Empty;
        var normalizedCursor = Math.Clamp(cursorIndex, 0, input.Length);
        var prefix = input[..normalizedCursor];
        var atTokenBoundary = prefix.Length == 0 || char.IsWhiteSpace(prefix[^1]);
        var tokens = Tokenize(prefix);
        if (tokens.Count == 0)
        {
            return "Type a command. Use Tab/Shift+Tab to cycle completions.";
        }

        var commandToken = tokens[0];
        if (!_registry.TryGetDescriptor(commandToken, out var descriptor))
        {
            return $"Unknown command '{commandToken}'.";
        }

        var activeParameterIndex = atTokenBoundary
            ? Math.Max(0, tokens.Count - 1)
            : Math.Max(0, tokens.Count - 2);

        var argumentCount = Math.Max(0, tokens.Count - 1);
        if (!descriptor.TryResolveSyntaxByArgumentCount(argumentCount, out var syntaxIndex, out _))
        {
            syntaxIndex = 0;
        }

        return descriptor.BuildParameterHelp(activeParameterIndex, syntaxIndex);
    }

    private static IReadOnlyList<string> Tokenize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        char? quote = null;

        for (var index = 0; index < input.Length; index++)
        {
            var ch = input[index];
            if (quote is not null)
            {
                if (ch == quote.Value)
                {
                    quote = null;
                    continue;
                }

                if (ch == '\\' && index + 1 < input.Length && input[index + 1] == quote.Value)
                {
                    current.Append(quote.Value);
                    index++;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                FlushCurrent(tokens, current);
                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                continue;
            }

            current.Append(ch);
        }

        FlushCurrent(tokens, current);
        return tokens;
    }

    private static void FlushCurrent(ICollection<string> tokens, System.Text.StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }
}
