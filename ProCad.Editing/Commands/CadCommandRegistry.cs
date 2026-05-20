using ProCad.Editing.Sessions;
using System.Text;

namespace ProCad.Editing.Commands;

public interface ICadCommandRegistry
{
    IReadOnlyList<string> GetRegisteredCommands();
    IReadOnlyList<CadCommandDescriptor> GetCommandDescriptors();
    IReadOnlyList<CadCommandCompletionItem> GetCompletions(string input, int cursorIndex, bool includeKeywords = true);
    bool TryGetDescriptor(string token, out CadCommandDescriptor descriptor);
    void Register(ICadCommandHandler handler);
    bool TryResolve(string token, out ICadCommandHandler handler);
    bool TryParse(string input, out string? command, out IReadOnlyList<string> arguments);
    ValueTask<CadCommandResult> ExecuteAsync(string input, ICadEditorSession? session, CancellationToken cancellationToken = default);
}

public sealed class CadCommandRegistry : ICadCommandRegistry
{
    private readonly Dictionary<string, ICadCommandHandler> _nameMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliasMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CadCommandDescriptor> _descriptorMap = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> GetRegisteredCommands()
    {
        return _nameMap.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<CadCommandDescriptor> GetCommandDescriptors()
    {
        return _descriptorMap.Values
            .OrderBy(static descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Register(ICadCommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _nameMap[handler.Name] = handler;
        if (handler is ICadDescribedCommandHandler described)
        {
            _descriptorMap[handler.Name] = described.Descriptor;
        }
        else if (CadCommandDescriptorCatalog.TryCreate(handler, out var catalogDescriptor))
        {
            _descriptorMap[handler.Name] = catalogDescriptor;
        }
        else
        {
            _descriptorMap[handler.Name] = CadCommandDescriptor.CreateDefault(handler);
        }

        _aliasMap[handler.Name] = handler.Name;
        foreach (var alias in handler.Aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            _aliasMap[alias] = handler.Name;
        }
    }

    public bool TryResolve(string token, out ICadCommandHandler handler)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            handler = null!;
            return false;
        }

        if (_aliasMap.TryGetValue(token.Trim(), out var resolved) && _nameMap.TryGetValue(resolved, out handler!))
        {
            return true;
        }

        handler = null!;
        return false;
    }

    public bool TryGetDescriptor(string token, out CadCommandDescriptor descriptor)
    {
        descriptor = null!;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (!_aliasMap.TryGetValue(token.Trim(), out var resolved))
        {
            return false;
        }

        return _descriptorMap.TryGetValue(resolved, out descriptor!);
    }

    public bool TryParse(string input, out string? command, out IReadOnlyList<string> arguments)
    {
        var parsed = Parse(input);
        command = parsed.Command;
        arguments = parsed.Arguments;
        return command is not null;
    }

    public IReadOnlyList<CadCommandCompletionItem> GetCompletions(string input, int cursorIndex, bool includeKeywords = true)
    {
        input ??= string.Empty;
        var normalizedCursor = Math.Clamp(cursorIndex, 0, input.Length);
        var prefix = input[..normalizedCursor];
        var atTokenBoundary = prefix.Length == 0 || char.IsWhiteSpace(prefix[^1]);
        var tokens = Tokenize(prefix);

        if (tokens.Count == 0)
        {
            return GetCommandCompletions(string.Empty);
        }

        var currentToken = atTokenBoundary ? string.Empty : tokens[^1];
        if (tokens.Count == 1 && !TryResolve(tokens[0], out _))
        {
            return GetCommandCompletions(currentToken);
        }

        if (!TryResolve(tokens[0], out var resolved))
        {
            return Array.Empty<CadCommandCompletionItem>();
        }

        if (tokens.Count == 1 && !atTokenBoundary)
        {
            // Once a command token is fully resolved, surface argument keywords immediately
            // without requiring an extra trailing whitespace.
            currentToken = string.Empty;
        }

        if (!includeKeywords || !TryGetDescriptor(resolved.Name, out var descriptor))
        {
            return Array.Empty<CadCommandCompletionItem>();
        }

        var argumentCount = Math.Max(0, tokens.Count - 1);
        return GetKeywordCompletions(descriptor, currentToken, argumentCount);
    }

    public async ValueTask<CadCommandResult> ExecuteAsync(
        string input,
        ICadEditorSession? session,
        CancellationToken cancellationToken = default)
    {
        var parsed = Parse(input);
        if (parsed.Command is null)
        {
            return CadCommandResult.Fail("Command is empty.");
        }

        if (!TryResolve(parsed.Command, out var handler))
        {
            return CadCommandResult.Fail($"Unknown command '{parsed.Command}'.");
        }

        var context = new CadCommandContext(
            session,
            input,
            parsed.Command,
            parsed.Arguments,
            cancellationToken);

        if (!handler.CanExecute(context))
        {
            return CadCommandResult.Fail($"Command '{handler.Name}' is not available in the current context.");
        }

        return await handler.ExecuteAsync(context);
    }

    private IReadOnlyList<CadCommandCompletionItem> GetCommandCompletions(string prefix)
    {
        var prefixValue = prefix?.Trim() ?? string.Empty;
        var result = new List<CadCommandCompletionItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in GetCommandDescriptors())
        {
            if (descriptor.Name.StartsWith(prefixValue, StringComparison.OrdinalIgnoreCase) &&
                seen.Add(descriptor.Name))
            {
                result.Add(new CadCommandCompletionItem(
                    descriptor.Name,
                    descriptor.Name,
                    Kind: "Command",
                    descriptor.Description));
            }

            foreach (var alias in descriptor.Aliases)
            {
                if (!alias.StartsWith(prefixValue, StringComparison.OrdinalIgnoreCase) ||
                    !seen.Add(alias))
                {
                    continue;
                }

                result.Add(new CadCommandCompletionItem(
                    alias,
                    $"{alias} ({descriptor.Name})",
                    Kind: "Alias",
                    descriptor.Description));
            }
        }

        return result;
    }

    private static IReadOnlyList<CadCommandCompletionItem> GetKeywordCompletions(
        CadCommandDescriptor descriptor,
        string prefix,
        int argumentCount)
    {
        var syntaxes = ResolveKeywordSyntaxes(descriptor, argumentCount);
        if (syntaxes.Count == 0)
        {
            return Array.Empty<CadCommandCompletionItem>();
        }

        var prefixValue = prefix?.Trim() ?? string.Empty;
        var result = new List<CadCommandCompletionItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var syntax in syntaxes)
        {
            foreach (var keyword in syntax.Keywords)
            {
                if (!keyword.Keyword.StartsWith(prefixValue, StringComparison.OrdinalIgnoreCase) ||
                    !seen.Add(keyword.Keyword))
                {
                    continue;
                }

                result.Add(new CadCommandCompletionItem(
                    keyword.Keyword,
                    keyword.Keyword,
                    Kind: "Keyword",
                    keyword.Description));
            }
        }

        return result;
    }

    private static IReadOnlyList<CadCommandSyntax> ResolveKeywordSyntaxes(
        CadCommandDescriptor descriptor,
        int argumentCount)
    {
        if (descriptor.Syntaxes.Count == 0)
        {
            return Array.Empty<CadCommandSyntax>();
        }

        if (descriptor.TryResolveSyntaxByArgumentCount(argumentCount, out _, out var syntax) &&
            syntax.Keywords.Count > 0)
        {
            return [syntax];
        }

        return descriptor.Syntaxes
            .Where(static candidate => candidate.Keywords.Count > 0)
            .ToArray();
    }

    private static (string? Command, IReadOnlyList<string> Arguments) Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (null, Array.Empty<string>());
        }

        var tokens = Tokenize(input);
        if (tokens.Count == 0)
        {
            return (null, Array.Empty<string>());
        }

        var command = tokens[0];
        if (tokens.Count == 1)
        {
            return (command, Array.Empty<string>());
        }

        var args = new string[tokens.Count - 1];
        for (var index = 0; index < args.Length; index++)
        {
            args[index] = tokens[index + 1];
        }

        return (command, args);
    }

    private static IReadOnlyList<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
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

    private static void FlushCurrent(ICollection<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }
}
