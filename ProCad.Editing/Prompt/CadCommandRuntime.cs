using System.Globalization;
using ProCad.Editing.Commands;
using ProCad.Editing.Operations;
using ProCad.Editing.Sessions;
using ProCad.Editing.Undo;

namespace ProCad.Editing.Prompt;

public enum CadPromptTokenType
{
    Raw = 0,
    Coordinate,
    Keyword,
    Number,
    Text,
    Handle
}

public readonly record struct CadPromptToken(CadPromptTokenType Type, string Value);

public sealed record CadPromptState(
    string Prompt,
    string? ActiveCommand,
    bool IsActive,
    int ActiveParameterIndex,
    string? ParameterHelp,
    IReadOnlyList<CadCommandCompletionItem> Completions,
    string? LastMessage)
{
    public static readonly CadPromptState Idle = new(
        Prompt: "Command",
        ActiveCommand: null,
        IsActive: false,
        ActiveParameterIndex: 0,
        ParameterHelp: "Type a command. Use Tab/Shift+Tab to cycle completions.",
        Completions: Array.Empty<CadCommandCompletionItem>(),
        LastMessage: null);
}

public sealed record CadPromptResolution(
    bool Handled,
    CadCommandResult? Result,
    CadPromptState State);

public sealed record CadCommandExecutedEventArgs(
    string Input,
    string? CommandName,
    CadCommandResult Result,
    CadUndoSource Source,
    bool IsTransparent,
    DateTimeOffset TimestampUtc);

public interface ICadCommandRuntime
{
    CadPromptState State { get; }
    string? LastCommandInput { get; }
    event EventHandler<CadPromptState>? StateChanged;
    event EventHandler<CadCommandExecutedEventArgs>? CommandExecuted;

    void BeginCommand(string commandName);
    void Cancel();
    CadPromptState Preview(string input, int cursorIndex);
    ValueTask<CadPromptResolution> SubmitAsync(string input, ICadEditorSession? session, CancellationToken cancellationToken = default);
    ValueTask<CadPromptResolution> SubmitTokenAsync(CadPromptToken token, ICadEditorSession? session, bool commit = false, CancellationToken cancellationToken = default);
}

public sealed class CadCommandRuntime : ICadCommandRuntime
{
    private readonly ICadCommandRegistry _registry;
    private readonly ICadCommandIntellisenseService _intellisense;
    private string? _activeCommand;
    private string? _activeInput;
    private PromptTokenSession? _activeTokenSession;

    public CadPromptState State { get; private set; } = CadPromptState.Idle;
    public string? LastCommandInput { get; private set; }
    public event EventHandler<CadPromptState>? StateChanged;
    public event EventHandler<CadCommandExecutedEventArgs>? CommandExecuted;

    public CadCommandRuntime(
        ICadCommandRegistry registry,
        ICadCommandIntellisenseService intellisense)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _intellisense = intellisense ?? throw new ArgumentNullException(nameof(intellisense));
    }

    public void BeginCommand(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return;
        }

        var token = commandName.Trim();
        var message = "Specify first point or option.";
        if (_registry.TryResolve(token, out var handler))
        {
            token = handler.Name;
        }
        else
        {
            message = string.Create(
                CultureInfo.InvariantCulture,
                $"Started {token.ToUpperInvariant()}.");
        }

        _activeCommand = token;
        _activeTokenSession = new PromptTokenSession(token);
        _activeInput = _activeTokenSession.BuildInput();
        State = BuildState(_activeInput, _activeInput.Length, message);
        OnStateChanged();
    }

    public void Cancel()
    {
        ResetActiveSession();
        State = CadPromptState.Idle with
        {
            LastMessage = "*Cancel*"
        };
        OnStateChanged();
    }

    public CadPromptState Preview(string input, int cursorIndex)
    {
        State = BuildState(input, cursorIndex, State.LastMessage);
        OnStateChanged();
        return State;
    }

    public async ValueTask<CadPromptResolution> SubmitAsync(
        string input,
        ICadEditorSession? session,
        CancellationToken cancellationToken = default)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalized) &&
            normalized.StartsWith('\'') &&
            _activeTokenSession is not null)
        {
            var transparent = normalized[1..].Trim();
            if (!string.IsNullOrWhiteSpace(transparent))
            {
                var transparentResult = await _registry.ExecuteAsync(transparent, session, cancellationToken).ConfigureAwait(false);
                OnCommandExecuted(
                    input: transparent,
                    result: transparentResult,
                    isTransparent: true);
                var nextState = BuildState(_activeInput ?? _activeTokenSession.BuildInput(), (_activeInput ?? _activeTokenSession.BuildInput()).Length, transparentResult.Message);
                State = nextState;
                OnStateChanged();
                return new CadPromptResolution(true, transparentResult, nextState);
            }
        }

        if (string.IsNullOrEmpty(normalized))
        {
            if (!string.IsNullOrWhiteSpace(LastCommandInput))
            {
                normalized = LastCommandInput!;
            }
            else
            {
                var emptyInput = input ?? string.Empty;
                State = BuildState(emptyInput, emptyInput.Length, "Command is empty.");
                OnStateChanged();
                return new CadPromptResolution(false, null, State);
            }
        }

        if (!ContainsCommandToken(normalized) && !string.IsNullOrWhiteSpace(_activeCommand))
        {
            normalized = $"{_activeCommand} {normalized}";
        }

        var result = await _registry.ExecuteAsync(normalized, session, cancellationToken).ConfigureAwait(false);
        OnCommandExecuted(
            input: normalized,
            result: result,
            isTransparent: false);

        if (result.Success)
        {
            LastCommandInput = normalized;
            ResetActiveSession();
        }
        else if (_registry.TryParse(normalized, out var command, out _))
        {
            _activeCommand = command;
            _activeTokenSession = PromptTokenSession.FromInput(command!, normalized);
            _activeInput = _activeTokenSession.BuildInput();
        }

        State = BuildState(_activeInput ?? string.Empty, (_activeInput ?? string.Empty).Length, result.Message);
        OnStateChanged();
        return new CadPromptResolution(true, result, State);
    }

    public async ValueTask<CadPromptResolution> SubmitTokenAsync(
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token.Value))
        {
            if (commit)
            {
                if (_activeTokenSession is null || _activeTokenSession.TokenCount == 0)
                {
                    Cancel();
                    return new CadPromptResolution(true, null, State);
                }

                _activeInput = _activeTokenSession.BuildInput();
                return await SubmitAsync(_activeInput, session, cancellationToken).ConfigureAwait(false);
            }

            return new CadPromptResolution(false, null, State);
        }

        if (string.IsNullOrWhiteSpace(_activeInput))
        {
            if (string.IsNullOrWhiteSpace(_activeCommand))
            {
                return new CadPromptResolution(false, null, State);
            }

            _activeTokenSession ??= new PromptTokenSession(_activeCommand);
            _activeInput = _activeTokenSession.BuildInput();
        }

        if (_activeTokenSession is null)
        {
            _activeTokenSession = new PromptTokenSession(_activeCommand ?? string.Empty);
        }

        if (token.Type == CadPromptTokenType.Keyword &&
            string.Equals(token.Value, "UNDO", StringComparison.OrdinalIgnoreCase))
        {
            if (!_activeTokenSession.TryPopToken())
            {
                State = BuildState(_activeTokenSession.BuildInput(), _activeTokenSession.BuildInput().Length, "Nothing to undo in this command input.");
                OnStateChanged();
                return new CadPromptResolution(false, null, State);
            }
        }
        else
        {
            if (!TryNormalizeTokenForActiveCommand(token, out var normalizedToken, out var validationMessage))
            {
                var currentInput = _activeTokenSession.BuildInput();
                State = BuildState(currentInput, currentInput.Length, validationMessage);
                OnStateChanged();
                return new CadPromptResolution(false, null, State);
            }

            _activeTokenSession.AddToken(normalizedToken);
        }

        _activeInput = _activeTokenSession.BuildInput();

        if (commit)
        {
            return await SubmitAsync(_activeInput, session, cancellationToken).ConfigureAwait(false);
        }

        State = BuildState(_activeInput, _activeInput.Length, State.LastMessage);
        OnStateChanged();
        return new CadPromptResolution(true, null, State);
    }

    private bool TryNormalizeTokenForActiveCommand(
        CadPromptToken token,
        out CadPromptToken normalizedToken,
        out string validationMessage)
    {
        normalizedToken = token with { Value = token.Value.Trim() };
        validationMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(_activeCommand) ||
            !_registry.TryGetDescriptor(_activeCommand, out var descriptor) ||
            _activeTokenSession is null)
        {
            return true;
        }

        var argumentIndex = _activeTokenSession.TokenCount;
        if (descriptor.Syntaxes.Count == 0)
        {
            return true;
        }

        var candidateOrder = BuildSyntaxCandidateOrder(descriptor, argumentIndex + 1);
        string? firstError = null;
        foreach (var syntaxIndex in candidateOrder)
        {
            var syntax = descriptor.Syntaxes[syntaxIndex];
            if (!AreTokensCompatibleWithSyntax(_activeTokenSession.Tokens, syntax))
            {
                continue;
            }

            if (!syntax.TryResolveSlot(argumentIndex, out var slot))
            {
                if (TryResolveKeyword(syntax, token.Value, out var canonicalKeyword))
                {
                    normalizedToken = new CadPromptToken(CadPromptTokenType.Keyword, canonicalKeyword);
                    return true;
                }

                firstError ??= $"Unexpected argument '{token.Value}'.";
                continue;
            }

            if (TryNormalizeTokenForSlot(
                    token,
                    slot,
                    syntax,
                    out normalizedToken,
                    out validationMessage))
            {
                return true;
            }

            firstError ??= validationMessage;
        }

        validationMessage = firstError ?? $"Unexpected argument '{token.Value}'.";
        return false;
    }

    private static bool TryNormalizeTokenForSlot(
        CadPromptToken token,
        CadCommandTokenSlotContract slot,
        CadCommandSyntax syntax,
        out CadPromptToken normalizedToken,
        out string validationMessage)
    {
        var value = token.Value.Trim();
        normalizedToken = token with { Value = value };
        validationMessage = string.Empty;

        if (TryResolveKeyword(syntax, value, out var canonicalKeyword))
        {
            normalizedToken = new CadPromptToken(CadPromptTokenType.Keyword, canonicalKeyword);
            return true;
        }

        switch (slot.Kind)
        {
            case CadCommandParameterKind.Any:
            case CadCommandParameterKind.Text:
                normalizedToken = token with { Type = CadPromptTokenType.Text, Value = value };
                return true;
            case CadCommandParameterKind.Coordinate:
                if (token.Type == CadPromptTokenType.Coordinate || TryParseCoordinateToken(value))
                {
                    normalizedToken = new CadPromptToken(CadPromptTokenType.Coordinate, value);
                    return true;
                }

                validationMessage = BuildTypeError(slot, "coordinate");
                return false;
            case CadCommandParameterKind.Integer:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    normalizedToken = new CadPromptToken(CadPromptTokenType.Number, value);
                    return true;
                }

                validationMessage = BuildTypeError(slot, "integer");
                return false;
            case CadCommandParameterKind.Number:
            case CadCommandParameterKind.Angle:
            case CadCommandParameterKind.Distance:
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    normalizedToken = new CadPromptToken(CadPromptTokenType.Number, value);
                    return true;
                }

                validationMessage = BuildTypeError(slot, "number");
                return false;
            case CadCommandParameterKind.Handle:
                if (token.Type == CadPromptTokenType.Handle || CadCommandParsing.TryParseHandle(value, out _))
                {
                    normalizedToken = new CadPromptToken(CadPromptTokenType.Handle, value);
                    return true;
                }

                validationMessage = BuildTypeError(slot, "handle");
                return false;
            case CadCommandParameterKind.Keyword:
                validationMessage = BuildKeywordError(slot, syntax);
                return false;
            case CadCommandParameterKind.Flag:
                if (IsFlagToken(value))
                {
                    normalizedToken = new CadPromptToken(CadPromptTokenType.Keyword, value.ToUpperInvariant());
                    return true;
                }

                validationMessage = BuildTypeError(slot, "flag");
                return false;
            default:
                return true;
        }
    }

    private static bool TryParseCoordinateToken(string value)
    {
        return CadOperationPayloadCodec.TryParsePoint(value, out _);
    }

    private static bool TryResolveKeyword(CadCommandSyntax syntax, string value, out string canonicalKeyword)
    {
        canonicalKeyword = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || syntax.Keywords.Count == 0)
        {
            return false;
        }

        foreach (var keyword in syntax.Keywords)
        {
            if (!string.Equals(keyword.Keyword, value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            canonicalKeyword = keyword.Keyword;
            return true;
        }

        return false;
    }

    private static string BuildTypeError(CadCommandTokenSlotContract slot, string expectedType)
    {
        return $"Expected {expectedType} for '{slot.Name}'.";
    }

    private static string BuildKeywordError(CadCommandTokenSlotContract slot, CadCommandSyntax syntax)
    {
        if (syntax.Keywords.Count == 0)
        {
            return $"Expected keyword for '{slot.Name}'.";
        }

        var values = string.Join(", ", syntax.Keywords.Select(static keyword => keyword.Keyword));
        return $"Expected keyword for '{slot.Name}' ({values}).";
    }

    private static bool IsFlagToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("OFF", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ||
               value == "1" ||
               value == "0";
    }

    private static IReadOnlyList<int> BuildSyntaxCandidateOrder(CadCommandDescriptor descriptor, int argumentCount)
    {
        var order = new List<int>(descriptor.Syntaxes.Count);
        if (descriptor.TryResolveSyntaxByArgumentCount(argumentCount, out var preferredIndex, out _))
        {
            order.Add(preferredIndex);
        }

        for (var index = 0; index < descriptor.Syntaxes.Count; index++)
        {
            if (!order.Contains(index))
            {
                order.Add(index);
            }
        }

        return order;
    }

    private static bool AreTokensCompatibleWithSyntax(IReadOnlyList<CadPromptToken> tokens, CadCommandSyntax syntax)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (!syntax.TryResolveSlot(index, out var slot))
            {
                if (!TryResolveKeyword(syntax, token.Value, out _))
                {
                    return false;
                }

                continue;
            }

            if (!TryNormalizeTokenForSlot(token, slot, syntax, out _, out _))
            {
                return false;
            }
        }

        return true;
    }

    private void ResetActiveSession()
    {
        _activeCommand = null;
        _activeInput = null;
        _activeTokenSession = null;
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, State);
    }

    private void OnCommandExecuted(string input, CadCommandResult result, bool isTransparent)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var source = CadUndoExecutionContext.Current?.Source ?? CadUndoSource.Unknown;
        string? commandName = null;
        if (_registry.TryParse(input, out var parsedCommand, out _))
        {
            commandName = parsedCommand;
        }
        else if (!string.IsNullOrWhiteSpace(_activeCommand))
        {
            commandName = _activeCommand;
        }

        CommandExecuted?.Invoke(
            this,
            new CadCommandExecutedEventArgs(
                Input: input,
                CommandName: commandName,
                Result: result,
                Source: source,
                IsTransparent: isTransparent,
                TimestampUtc: DateTimeOffset.UtcNow));
    }

    private CadPromptState BuildState(string input, int cursorIndex, string? message)
    {
        input ??= string.Empty;
        var normalizedCursor = Math.Clamp(cursorIndex, 0, input.Length);
        var completions = _intellisense.GetCompletions(input, normalizedCursor);
        var parameterHelp = _intellisense.GetParameterHelp(input, normalizedCursor);
        var activeParameter = ResolveActiveParameterIndex(input, normalizedCursor);

        var activeCommand = _activeCommand;

        return new CadPromptState(
            Prompt: string.IsNullOrWhiteSpace(activeCommand) ? "Command" : $"{activeCommand}",
            ActiveCommand: activeCommand,
            IsActive: !string.IsNullOrWhiteSpace(activeCommand),
            ActiveParameterIndex: activeParameter,
            ParameterHelp: parameterHelp,
            Completions: completions,
            LastMessage: message);
    }

    private static bool ContainsCommandToken(string input)
    {
        for (var index = 0; index < input.Length; index++)
        {
            if (char.IsWhiteSpace(input[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static int ResolveActiveParameterIndex(string input, int cursorIndex)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return 0;
        }

        var normalizedCursor = Math.Clamp(cursorIndex, 0, input.Length);
        var prefix = input[..normalizedCursor];
        var atTokenBoundary = prefix.Length == 0 || char.IsWhiteSpace(prefix[^1]);
        var tokens = Tokenize(prefix);
        if (tokens.Count <= 1)
        {
            return 0;
        }

        return atTokenBoundary
            ? Math.Max(0, tokens.Count - 1)
            : Math.Max(0, tokens.Count - 2);
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

    private sealed class PromptTokenSession
    {
        private readonly List<CadPromptToken> _tokens = new();

        public PromptTokenSession(string command)
        {
            Command = command;
        }

        public string Command { get; }
        public int TokenCount => _tokens.Count;
        public IReadOnlyList<CadPromptToken> Tokens => _tokens;

        public static PromptTokenSession FromInput(string command, string input)
        {
            var session = new PromptTokenSession(command);
            var tokens = Tokenize(input);
            for (var index = 1; index < tokens.Count; index++)
            {
                session._tokens.Add(new CadPromptToken(CadPromptTokenType.Raw, tokens[index]));
            }

            return session;
        }

        public void AddToken(CadPromptToken token)
        {
            if (string.IsNullOrWhiteSpace(token.Value))
            {
                return;
            }

            _tokens.Add(token with { Value = token.Value.Trim() });
        }

        public bool TryPopToken()
        {
            if (_tokens.Count == 0)
            {
                return false;
            }

            _tokens.RemoveAt(_tokens.Count - 1);
            return true;
        }

        public string BuildInput()
        {
            if (_tokens.Count == 0)
            {
                return Command;
            }

            var builder = new System.Text.StringBuilder(Command.Length + (_tokens.Count * 16));
            builder.Append(Command);
            for (var index = 0; index < _tokens.Count; index++)
            {
                builder.Append(' ');
                builder.Append(_tokens[index].Value);
            }

            return builder.ToString();
        }
    }
}
