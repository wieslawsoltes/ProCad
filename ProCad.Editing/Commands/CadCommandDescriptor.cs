namespace ProCad.Editing.Commands;

public enum CadCommandParameterKind
{
    Any = 0,
    Coordinate,
    Number,
    Integer,
    Angle,
    Distance,
    Handle,
    Text,
    Keyword,
    Flag
}

public sealed record CadCommandKeywordDescriptor(
    string Keyword,
    string Description = "");

public sealed record CadCommandParameterDescriptor(
    string Name,
    CadCommandParameterKind Kind,
    bool IsOptional = false,
    string Description = "",
    string? DefaultValue = null,
    string? Example = null,
    bool IsVariadic = false)
{
    public bool IsVariadicEffective =>
        IsVariadic ||
        Name.EndsWith("+", StringComparison.Ordinal) ||
        Name.EndsWith("...", StringComparison.Ordinal);
}

public sealed record CadCommandTokenSlotContract(
    int SlotIndex,
    string Name,
    CadCommandParameterKind Kind,
    bool IsOptional,
    bool IsVariadic,
    string? Description,
    string? DefaultValue,
    string? Example);

public sealed record CadCommandSyntax(
    string Usage,
    string Description,
    IReadOnlyList<CadCommandParameterDescriptor> Parameters,
    IReadOnlyList<CadCommandKeywordDescriptor> Keywords,
    string? BranchId = null)
{
    public static CadCommandSyntax Empty(string usage)
    {
        return new CadCommandSyntax(
            usage,
            string.Empty,
            Array.Empty<CadCommandParameterDescriptor>(),
            Array.Empty<CadCommandKeywordDescriptor>(),
            BranchId: null);
    }

    public IReadOnlyList<CadCommandTokenSlotContract> BuildTokenSlots()
    {
        if (Parameters.Count == 0)
        {
            return Array.Empty<CadCommandTokenSlotContract>();
        }

        var slots = new CadCommandTokenSlotContract[Parameters.Count];
        for (var index = 0; index < Parameters.Count; index++)
        {
            var parameter = Parameters[index];
            slots[index] = new CadCommandTokenSlotContract(
                SlotIndex: index,
                Name: parameter.Name,
                Kind: parameter.Kind,
                IsOptional: parameter.IsOptional,
                IsVariadic: parameter.IsVariadicEffective,
                Description: parameter.Description,
                DefaultValue: parameter.DefaultValue,
                Example: parameter.Example);
        }

        return slots;
    }

    public bool TryResolveSlot(int argumentIndex, out CadCommandTokenSlotContract slot)
    {
        slot = default!;
        if (argumentIndex < 0)
        {
            return false;
        }

        var slots = BuildTokenSlots();
        if (slots.Count == 0)
        {
            return false;
        }

        if (argumentIndex < slots.Count)
        {
            slot = slots[argumentIndex];
            return true;
        }

        var trailing = slots[^1];
        if (!trailing.IsVariadic)
        {
            return false;
        }

        slot = trailing with { SlotIndex = argumentIndex };
        return true;
    }

    public int CountRequiredSlots()
    {
        if (Parameters.Count == 0)
        {
            return 0;
        }

        var required = 0;
        for (var index = 0; index < Parameters.Count; index++)
        {
            var parameter = Parameters[index];
            if (!parameter.IsOptional && !parameter.IsVariadicEffective)
            {
                required++;
            }
        }

        return required;
    }
}

public sealed record CadCommandDescriptor(
    string Name,
    IReadOnlyList<string> Aliases,
    string Description,
    IReadOnlyList<CadCommandSyntax> Syntaxes)
{
    public static CadCommandDescriptor CreateDefault(ICadCommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var aliases = handler.Aliases.Count == 0
            ? Array.Empty<string>()
            : handler.Aliases.Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return new CadCommandDescriptor(
            handler.Name,
            aliases,
            $"{handler.Name} command",
            new[] { CadCommandSyntax.Empty(handler.Name) });
    }

    public string BuildParameterHelp(int activeParameterIndex, int syntaxIndex = 0)
    {
        var syntax = ResolveSyntaxOrDefault(syntaxIndex);
        if (syntax.Parameters.Count == 0)
        {
            return syntax.Keywords.Count == 0
                ? syntax.Usage
                : $"{syntax.Usage} | keywords: {string.Join(", ", syntax.Keywords.Select(static keyword => keyword.Keyword))}";
        }

        var tokens = new List<string>(syntax.Parameters.Count + 1) { Name };
        for (var index = 0; index < syntax.Parameters.Count; index++)
        {
            var parameter = syntax.Parameters[index];
            var token = parameter.IsOptional
                ? $"[{parameter.Name}]"
                : $"<{parameter.Name}>";
            if (index == activeParameterIndex)
            {
                token = $"*{token}*";
            }

            tokens.Add(token);
        }

        if (syntax.Keywords.Count > 0)
        {
            tokens.Add($"| keywords: {string.Join(", ", syntax.Keywords.Select(static keyword => keyword.Keyword))}");
        }

        return string.Join(' ', tokens);
    }

    public bool TryResolveSyntaxByArgumentCount(int argumentCount, out int syntaxIndex, out CadCommandSyntax syntax)
    {
        syntaxIndex = 0;
        syntax = CadCommandSyntax.Empty(Name);
        if (Syntaxes.Count == 0)
        {
            return false;
        }

        var normalizedCount = Math.Max(0, argumentCount);
        var bestIndex = 0;
        var bestScore = int.MinValue;
        for (var index = 0; index < Syntaxes.Count; index++)
        {
            var candidate = Syntaxes[index];
            var required = candidate.CountRequiredSlots();
            var supportsVariadic = candidate.Parameters.Count > 0 && candidate.Parameters[^1].IsVariadicEffective;
            var maxExplicit = candidate.Parameters.Count;
            var overflowPenalty = !supportsVariadic && normalizedCount > maxExplicit
                ? normalizedCount - maxExplicit
                : 0;
            var underflowPenalty = normalizedCount < required
                ? required - normalizedCount
                : 0;
            var coverage = Math.Min(normalizedCount, maxExplicit);

            var score = (coverage * 4) - (overflowPenalty * 10) - (underflowPenalty * 6);
            if (supportsVariadic && normalizedCount >= maxExplicit)
            {
                score += 2;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        syntaxIndex = bestIndex;
        syntax = Syntaxes[bestIndex];
        return true;
    }

    private CadCommandSyntax ResolveSyntaxOrDefault(int syntaxIndex)
    {
        if (Syntaxes.Count == 0)
        {
            return CadCommandSyntax.Empty(Name);
        }

        if ((uint)syntaxIndex >= (uint)Syntaxes.Count)
        {
            return Syntaxes[0];
        }

        return Syntaxes[syntaxIndex];
    }
}

public sealed record CadCommandCompletionItem(
    string Value,
    string DisplayText,
    string Kind,
    string Description = "");
