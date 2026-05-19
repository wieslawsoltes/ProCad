using ProCad.Editing.Sessions;

namespace ProCad.Editing.Interaction;

public interface ICadInteractiveCommandAdapterRegistry
{
    IReadOnlyList<ICadInteractiveCommandAdapter> GetAdapters();
    bool TryGet(string commandName, out ICadInteractiveCommandAdapter adapter);
    void Reset(ICadEditorSession? session, string? commandName = null);
}

public sealed class CadInteractiveCommandAdapterRegistry : ICadInteractiveCommandAdapterRegistry
{
    private readonly IReadOnlyList<ICadInteractiveCommandAdapter> _adapters;
    private readonly Dictionary<string, ICadInteractiveCommandAdapter> _byCommand =
        new(StringComparer.OrdinalIgnoreCase);

    public CadInteractiveCommandAdapterRegistry(IEnumerable<ICadInteractiveCommandAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        _adapters = adapters
            .Where(static adapter => adapter is not null)
            .DistinctBy(static adapter => adapter.CommandName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var adapter in _adapters)
        {
            _byCommand[adapter.CommandName] = adapter;
            foreach (var alias in GetAliases(adapter.CommandName))
            {
                _byCommand[alias] = adapter;
            }
        }
    }

    public IReadOnlyList<ICadInteractiveCommandAdapter> GetAdapters()
    {
        return _adapters;
    }

    public bool TryGet(string commandName, out ICadInteractiveCommandAdapter adapter)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            adapter = null!;
            return false;
        }

        return _byCommand.TryGetValue(commandName.Trim(), out adapter!);
    }

    public void Reset(ICadEditorSession? session, string? commandName = null)
    {
        if (!string.IsNullOrWhiteSpace(commandName) &&
            _byCommand.TryGetValue(commandName.Trim(), out var commandAdapter))
        {
            if (commandAdapter is ICadInteractiveCommandPreviewProvider preview)
            {
                preview.ResetPreview(session);
            }

            return;
        }

        foreach (var adapter in _adapters)
        {
            if (adapter is ICadInteractiveCommandPreviewProvider preview)
            {
                preview.ResetPreview(session);
            }
        }
    }

    private static IReadOnlyList<string> GetAliases(string commandName)
    {
        return commandName.ToUpperInvariant() switch
        {
            "LINE" => ["L"],
            "PLINE" => ["PL"],
            "XLINE" => ["XL"],
            "RAY" => ["RA"],
            "CIRCLE" => ["C"],
            "ARC" => ["A"],
            "ELLIPSE" => ["EL"],
            "SPLINE" => ["SPL"],
            "RECTANG" => ["REC"],
            "POINT" => ["PO"],
            "INSERT" => ["I"],
            "MOVE" => ["M"],
            "COPY" => ["CO"],
            "ROTATE" => ["RO"],
            "SCALE" => ["SC"],
            "MIRROR" => ["MI"],
            "ERASE" => ["E"],
            "OFFSET" => ["O"],
            "TRIM" => ["TR"],
            "EXTEND" => ["EX"],
            "BREAK" => ["BR"],
            "JOIN" => ["J"],
            "FILLET" => ["F"],
            "CHAMFER" => ["CHA"],
            "ARRAY" => ["AR"],
            "ALIGN" => ["AL"],
            "MATCHPROP" => ["MA"],
            "TEXT" => ["T"],
            "MTEXT" => ["MT"],
            "DIMLINEAR" => ["DLI"],
            "DIMALIGNED" => ["DAL"],
            "DIMRADIUS" => ["DRA"],
            "DIMDIAMETER" => ["DIA"],
            "DIMANGULAR" => ["DAN"],
            "LEADER" => ["LE"],
            "MLEADER" => ["MLE"],
            _ => Array.Empty<string>()
        };
    }
}
