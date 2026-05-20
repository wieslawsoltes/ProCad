using System.Globalization;
using System.Linq;
using System.Numerics;
using ProCad.Editing.Commands;
using ProCad.Editing.Prompt;
using ProCad.Editing.Sessions;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Interaction;

public abstract class CadPointPickInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly string[] _stagePrompts;
    private readonly HashSet<string> _keywords;
    private readonly bool _continueUntilExplicitCommit;
    private readonly Dictionary<Guid, AdapterState> _states = new();
    private readonly object _sync = new();

    protected CadPointPickInteractiveCommandAdapter(
        string commandName,
        IReadOnlyList<string> stagePrompts,
        IReadOnlyList<string>? keywords = null,
        bool continueUntilExplicitCommit = false)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            throw new ArgumentException("Command name cannot be empty.", nameof(commandName));
        }

        if (stagePrompts is null || stagePrompts.Count == 0)
        {
            throw new ArgumentException("At least one stage prompt is required.", nameof(stagePrompts));
        }

        CommandName = commandName.Trim().ToUpperInvariant();
        _stagePrompts = stagePrompts
            .Where(static prompt => !string.IsNullOrWhiteSpace(prompt))
            .ToArray();
        if (_stagePrompts.Length == 0)
        {
            throw new ArgumentException("At least one non-empty stage prompt is required.", nameof(stagePrompts));
        }

        _keywords = keywords is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(keywords.Where(static keyword => !string.IsNullOrWhiteSpace(keyword)), StringComparer.OrdinalIgnoreCase);
        _continueUntilExplicitCommit = continueUntilExplicitCommit;
    }

    public string CommandName { get; }

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (string.IsNullOrWhiteSpace(token.Value))
        {
            if (commit)
            {
                var resolution = await runtime
                    .SubmitTokenAsync(token, session, commit: true, cancellationToken)
                    .ConfigureAwait(false);
                if (!resolution.State.IsActive)
                {
                    ResetPreview(session);
                }

                return resolution;
            }

            return new CadPromptResolution(Handled: false, Result: null, runtime.State);
        }

        if (token.Type == CadPromptTokenType.Coordinate &&
            TryParsePoint(token.Value, out var point))
        {
            var state = GetOrCreateState(session);
            state.PickedPoints.Add(point);
            var commitNow = commit ||
                            (!_continueUntilExplicitCommit &&
                             state.PickedPoints.Count >= _stagePrompts.Length);

            var resolution = await runtime
                .SubmitTokenAsync(token, session, commitNow, cancellationToken)
                .ConfigureAwait(false);

            if (commitNow || !resolution.State.IsActive)
            {
                ResetPreview(session);
            }

            return resolution;
        }

        if (token.Type is CadPromptTokenType.Keyword or CadPromptTokenType.Text &&
            _keywords.Contains(token.Value))
        {
            return await runtime
                .SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Keyword, token.Value), session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        return await runtime
            .SubmitTokenAsync(token, session, commit, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var state = GetState(session);
        if (state is null)
        {
            preview = default!;
            return false;
        }

        var nextStage = Math.Clamp(state.PickedPoints.Count, 0, _stagePrompts.Length - 1);
        var status = _stagePrompts[nextStage];
        var hints = new List<CadToolVisualHint>(state.PickedPoints.Count * 2 + 3);
        for (var index = 0; index < state.PickedPoints.Count; index++)
        {
            var anchor = state.PickedPoints[index];
            var markerText = index < _stagePrompts.Length
                ? string.Create(CultureInfo.InvariantCulture, $"{index + 1}: {_stagePrompts[index]}")
                : string.Create(CultureInfo.InvariantCulture, $"P{index + 1}");
            hints.Add(new CadToolVisualHint(
                Kind: "PickPoint",
                Anchor: anchor,
                SecondaryAnchor: null,
                Text: markerText));

            if (index <= 0)
            {
                continue;
            }

            hints.Add(new CadToolVisualHint(
                Kind: "HelperLine",
                Anchor: state.PickedPoints[index - 1],
                SecondaryAnchor: anchor,
                Text: null));
        }

        if (state.PickedPoints.Count > 0)
        {
            hints.Add(new CadToolVisualHint(
                Kind: "RubberBand",
                Anchor: state.PickedPoints[^1],
                SecondaryAnchor: cursorPoint,
                Text: status));
        }
        else
        {
            hints.Add(new CadToolVisualHint(
                Kind: "Prompt",
                Anchor: cursorPoint,
                SecondaryAnchor: null,
                Text: status));
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: status ?? fallbackStatus,
            Hints: hints);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _states.Remove(key);
        }
    }

    private AdapterState GetOrCreateState(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            if (_states.TryGetValue(key, out var state))
            {
                return state;
            }

            state = new AdapterState();
            _states[key] = state;
            return state;
        }
    }

    private AdapterState? GetState(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            return _states.TryGetValue(key, out var state) ? state : null;
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }

    private static bool TryParsePoint(string value, out Vector2 point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var split = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Length < 2)
        {
            return false;
        }

        if (!float.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        point = new Vector2(x, y);
        return true;
    }

    private sealed class AdapterState
    {
        public List<Vector2> PickedPoints { get; } = new();
    }
}

public sealed class DimLinearInteractiveCommandAdapter : CadPointPickInteractiveCommandAdapter
{
    public DimLinearInteractiveCommandAdapter()
        : base(
            commandName: "DIMLINEAR",
            stagePrompts:
            [
                "Specify first extension line origin",
                "Specify second extension line origin",
                "Specify dimension line location"
            ],
            keywords:
            [
                "MText",
                "Text",
                "Horizontal",
                "Vertical",
                "Rotated"
            ])
    {
    }
}

public sealed class DimAlignedInteractiveCommandAdapter : CadPointPickInteractiveCommandAdapter
{
    public DimAlignedInteractiveCommandAdapter()
        : base(
            commandName: "DIMALIGNED",
            stagePrompts:
            [
                "Specify first extension line origin",
                "Specify second extension line origin",
                "Specify dimension line location"
            ],
            keywords:
            [
                "MText",
                "Text",
                "Angle"
            ])
    {
    }
}

public sealed class DimRadiusInteractiveCommandAdapter : CadPointPickInteractiveCommandAdapter
{
    public DimRadiusInteractiveCommandAdapter()
        : base(
            commandName: "DIMRADIUS",
            stagePrompts:
            [
                "Specify circle center or arc point",
                "Specify dimension line location"
            ],
            keywords:
            [
                "MText",
                "Text",
                "Angle"
            ])
    {
    }
}

public sealed class DimDiameterInteractiveCommandAdapter : CadPointPickInteractiveCommandAdapter
{
    public DimDiameterInteractiveCommandAdapter()
        : base(
            commandName: "DIMDIAMETER",
            stagePrompts:
            [
                "Specify circle center or arc point",
                "Specify dimension line location"
            ],
            keywords:
            [
                "MText",
                "Text",
                "Angle"
            ])
    {
    }
}

public sealed class DimAngularInteractiveCommandAdapter : CadPointPickInteractiveCommandAdapter
{
    public DimAngularInteractiveCommandAdapter()
        : base(
            commandName: "DIMANGULAR",
            stagePrompts:
            [
                "Specify first line point",
                "Specify second line point",
                "Specify angle vertex",
                "Specify dimension arc line location"
            ],
            keywords:
            [
                "MText",
                "Text",
                "Quadrant"
            ])
    {
    }
}

public sealed class LeaderInteractiveCommandAdapter : CadPointPickInteractiveCommandAdapter
{
    public LeaderInteractiveCommandAdapter()
        : base(
            commandName: "LEADER",
            stagePrompts:
            [
                "Specify leader start point",
                "Specify leader landing point"
            ],
            keywords:
            [
                "Annotation",
                "Format",
                "Undo"
            ],
            continueUntilExplicitCommit: true)
    {
    }
}

public sealed class MLeaderInteractiveCommandAdapter : CadPointPickInteractiveCommandAdapter
{
    public MLeaderInteractiveCommandAdapter()
        : base(
            commandName: "MLEADER",
            stagePrompts:
            [
                "Specify multileader start point",
                "Specify multileader landing point"
            ],
            keywords:
            [
                "Content",
                "Style",
                "Landing"
            ],
            continueUntilExplicitCommit: true)
    {
    }
}

public abstract class CadDeltaInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, Vector2> _basePoints = new();
    private readonly object _sync = new();

    protected CadDeltaInteractiveCommandAdapter(string commandName)
    {
        CommandName = commandName;
    }

    public string CommandName { get; }

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        var key = ResolveSessionKey(session);
        Vector2? basePoint = null;
        lock (_sync)
        {
            if (_basePoints.TryGetValue(key, out var existing))
            {
                basePoint = existing;
            }
            else
            {
                _basePoints[key] = picked;
            }
        }

        if (basePoint is null)
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit: false, cancellationToken)
                .ConfigureAwait(false);
        }

        var delta = picked - basePoint.Value;
        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"{CommandName} {delta.X:0.###},{delta.Y:0.###}");

        ResetPreview(session);
        return await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        Vector2 basePoint;
        lock (_sync)
        {
            if (!_basePoints.TryGetValue(key, out basePoint))
            {
                preview = default!;
                return false;
            }
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: "Specify second point",
            Hints:
            [
                new CadToolVisualHint("PickPoint", basePoint, null, "Base"),
                new CadToolVisualHint("RubberBand", basePoint, cursorPoint, "Displacement")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _basePoints.Remove(key);
        }
    }

    protected static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }

    internal static bool TryParsePoint(string value, out Vector2 point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var split = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Length < 2)
        {
            return false;
        }

        if (!float.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        point = new Vector2(x, y);
        return true;
    }
}

public sealed class MoveInteractiveCommandAdapter : CadDeltaInteractiveCommandAdapter
{
    public MoveInteractiveCommandAdapter()
        : base("MOVE")
    {
    }
}

public sealed class CopyInteractiveCommandAdapter : CadDeltaInteractiveCommandAdapter
{
    public CopyInteractiveCommandAdapter()
        : base("COPY")
    {
    }
}

public sealed class RotateInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, Vector2> _centers = new();
    private readonly object _sync = new();

    public string CommandName => "ROTATE";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        var key = session?.SessionId.Value ?? Guid.Empty;
        Vector2? center = null;
        lock (_sync)
        {
            if (_centers.TryGetValue(key, out var existing))
            {
                center = existing;
            }
            else
            {
                _centers[key] = picked;
            }
        }

        if (center is null)
        {
            return new CadPromptResolution(
                Handled: true,
                Result: null,
                State: runtime.State);
        }

        var vector = picked - center.Value;
        if (vector.LengthSquared() <= 1e-8f)
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail("Rotation angle cannot be resolved from identical points."),
                State: runtime.State);
        }

        var angle = Math.Atan2(vector.Y, vector.X) * (180.0 / Math.PI);
        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"ROTATE {angle:0.###} {center.Value.X:0.###},{center.Value.Y:0.###}");

        ResetPreview(session);
        return await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = session?.SessionId.Value ?? Guid.Empty;
        Vector2 center;
        lock (_sync)
        {
            if (!_centers.TryGetValue(key, out center))
            {
                preview = default!;
                return false;
            }
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: "Specify rotation angle point",
            Hints:
            [
                new CadToolVisualHint("PickPoint", center, null, "Center"),
                new CadToolVisualHint("RubberBand", center, cursorPoint, "Angle")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = session?.SessionId.Value ?? Guid.Empty;
        lock (_sync)
        {
            _centers.Remove(key);
        }
    }
}

public sealed class ScaleInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, List<Vector2>> _pickedPoints = new();
    private readonly object _sync = new();

    public string CommandName => "SCALE";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        var key = session?.SessionId.Value ?? Guid.Empty;
        List<Vector2> points;
        lock (_sync)
        {
            if (!_pickedPoints.TryGetValue(key, out points!))
            {
                points = new List<Vector2>(3);
                _pickedPoints[key] = points;
            }

            points.Add(picked);
        }

        if (points.Count < 3)
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit: false, cancellationToken)
                .ConfigureAwait(false);
        }

        var center = points[0];
        var reference = points[1];
        var target = points[2];
        var referenceDistance = Vector2.Distance(center, reference);
        var targetDistance = Vector2.Distance(center, target);
        if (referenceDistance <= 1e-6f || targetDistance <= 1e-6f)
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail("Scale reference distance must be greater than zero."),
                State: runtime.State);
        }

        var factor = targetDistance / referenceDistance;
        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"SCALE {factor:0.###} {center.X:0.###},{center.Y:0.###}");

        ResetPreview(session);
        return await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = session?.SessionId.Value ?? Guid.Empty;
        List<Vector2>? points;
        lock (_sync)
        {
            _pickedPoints.TryGetValue(key, out points);
            if (points is null || points.Count == 0)
            {
                preview = default!;
                return false;
            }
        }

        var hints = new List<CadToolVisualHint>
        {
            new("PickPoint", points[0], null, "Center")
        };

        string status;
        if (points.Count == 1)
        {
            status = "Specify reference point";
            hints.Add(new CadToolVisualHint("RubberBand", points[0], cursorPoint, "Reference"));
        }
        else
        {
            status = "Specify new scale point";
            hints.Add(new CadToolVisualHint("PickPoint", points[1], null, "Reference"));
            hints.Add(new CadToolVisualHint("RubberBand", points[0], cursorPoint, "Scale"));
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: status,
            Hints: hints);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = session?.SessionId.Value ?? Guid.Empty;
        lock (_sync)
        {
            _pickedPoints.Remove(key);
        }
    }
}

public sealed class MirrorInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, Vector2> _firstPoints = new();
    private readonly object _sync = new();

    public string CommandName => "MIRROR";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        var key = session?.SessionId.Value ?? Guid.Empty;
        Vector2? firstPoint = null;
        lock (_sync)
        {
            if (_firstPoints.TryGetValue(key, out var existing))
            {
                firstPoint = existing;
            }
            else
            {
                _firstPoints[key] = picked;
            }
        }

        if (firstPoint is null)
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit: false, cancellationToken)
                .ConfigureAwait(false);
        }

        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"MIRROR {firstPoint.Value.X:0.###},{firstPoint.Value.Y:0.###} {picked.X:0.###},{picked.Y:0.###}");

        ResetPreview(session);
        return await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = session?.SessionId.Value ?? Guid.Empty;
        Vector2 firstPoint;
        lock (_sync)
        {
            if (!_firstPoints.TryGetValue(key, out firstPoint))
            {
                preview = default!;
                return false;
            }
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: "Specify second point of mirror line",
            Hints:
            [
                new CadToolVisualHint("PickPoint", firstPoint, null, "Axis start"),
                new CadToolVisualHint("RubberBand", firstPoint, cursorPoint, "Axis")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = session?.SessionId.Value ?? Guid.Empty;
        lock (_sync)
        {
            _firstPoints.Remove(key);
        }
    }
}

public sealed class EraseInteractiveCommandAdapter : CadSelectionCommitInteractiveCommandAdapter
{
    public EraseInteractiveCommandAdapter()
        : base("ERASE")
    {
    }
}

public sealed class LineInteractiveCommandAdapter : CadPointPickInteractiveCommandAdapter
{
    public LineInteractiveCommandAdapter()
        : base(
            commandName: "LINE",
            stagePrompts:
            [
                "Specify first point",
                "Specify next point"
            ])
    {
    }
}

public sealed class PlineInteractiveCommandAdapter : CadPointPickInteractiveCommandAdapter
{
    public PlineInteractiveCommandAdapter()
        : base(
            commandName: "PLINE",
            stagePrompts:
            [
                "Specify start point",
                "Specify next point"
            ],
            keywords:
            [
                "Close",
                "Undo"
            ],
            continueUntilExplicitCommit: true)
    {
    }
}

public sealed class RectangInteractiveCommandAdapter : CadPointPickInteractiveCommandAdapter
{
    public RectangInteractiveCommandAdapter()
        : base(
            commandName: "RECTANG",
            stagePrompts:
            [
                "Specify first corner",
                "Specify other corner"
            ])
    {
    }
}

public sealed class PointInteractiveCommandAdapter : CadPointPickInteractiveCommandAdapter
{
    public PointInteractiveCommandAdapter()
        : base(
            commandName: "POINT",
            stagePrompts:
            [
                "Specify point location"
            ])
    {
    }
}

public sealed class InsertInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, string> _blockNames = new();
    private readonly object _sync = new();

    public string CommandName => "INSERT";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        CadPromptResolution resolution;
        switch (token.Type)
        {
            case CadPromptTokenType.Coordinate:
                resolution = await runtime
                    .SubmitTokenAsync(token, session, commit: true, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case CadPromptTokenType.Keyword:
            case CadPromptTokenType.Text:
            case CadPromptTokenType.Raw:
            {
                var value = token.Value.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    resolution = new CadPromptResolution(Handled: false, Result: null, runtime.State);
                    break;
                }

                var key = ResolveSessionKey(session);
                lock (_sync)
                {
                    _blockNames[key] = value;
                }

                resolution = await runtime
                    .SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Text, value), session, commit: false, cancellationToken)
                    .ConfigureAwait(false);
                break;
            }
            default:
                resolution = await runtime
                    .SubmitTokenAsync(token, session, commit, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }

        if (!resolution.State.IsActive)
        {
            ResetPreview(session);
        }

        return resolution;
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        string? blockName = null;
        lock (_sync)
        {
            _blockNames.TryGetValue(key, out blockName);
        }

        var status = string.IsNullOrWhiteSpace(blockName)
            ? "Specify block name"
            : $"Specify insertion point for '{blockName}'";
        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: status,
            Hints:
            [
                new CadToolVisualHint("Prompt", cursorPoint, null, status)
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _blockNames.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }
}

public abstract class CadDirectedLineInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, Vector2> _firstPoints = new();
    private readonly object _sync = new();
    private readonly string _secondPointPrompt;

    protected CadDirectedLineInteractiveCommandAdapter(
        string commandName,
        string secondPointPrompt)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            throw new ArgumentException("Command name cannot be empty.", nameof(commandName));
        }

        CommandName = commandName.Trim().ToUpperInvariant();
        _secondPointPrompt = string.IsNullOrWhiteSpace(secondPointPrompt)
            ? "Specify second point"
            : secondPointPrompt;
    }

    public string CommandName { get; }

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        var key = ResolveSessionKey(session);
        Vector2? firstPoint = null;
        lock (_sync)
        {
            if (_firstPoints.TryGetValue(key, out var existing))
            {
                firstPoint = existing;
            }
            else
            {
                _firstPoints[key] = picked;
            }
        }

        if (firstPoint is null)
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit: false, cancellationToken)
                .ConfigureAwait(false);
        }

        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"{CommandName} {firstPoint.Value.X:0.###},{firstPoint.Value.Y:0.###} {picked.X:0.###},{picked.Y:0.###}");
        ResetPreview(session);
        return await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        Vector2 firstPoint;
        lock (_sync)
        {
            if (!_firstPoints.TryGetValue(key, out firstPoint))
            {
                preview = default!;
                return false;
            }
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: _secondPointPrompt,
            Hints:
            [
                new CadToolVisualHint("PickPoint", firstPoint, null, "Start"),
                new CadToolVisualHint("RubberBand", firstPoint, cursorPoint, "Direction")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _firstPoints.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }
}

public sealed class XLineInteractiveCommandAdapter : CadDirectedLineInteractiveCommandAdapter
{
    public XLineInteractiveCommandAdapter()
        : base(
            commandName: "XLINE",
            secondPointPrompt: "Specify second point")
    {
    }
}

public sealed class RayInteractiveCommandAdapter : CadDirectedLineInteractiveCommandAdapter
{
    public RayInteractiveCommandAdapter()
        : base(
            commandName: "RAY",
            secondPointPrompt: "Specify direction point")
    {
    }
}

public sealed class EllipseInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, List<Vector2>> _pickedPoints = new();
    private readonly object _sync = new();

    public string CommandName => "ELLIPSE";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        var key = ResolveSessionKey(session);
        List<Vector2> points;
        lock (_sync)
        {
            if (!_pickedPoints.TryGetValue(key, out points!))
            {
                points = new List<Vector2>(3);
                _pickedPoints[key] = points;
            }

            points.Add(picked);
        }

        if (points.Count < 3)
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit: false, cancellationToken)
                .ConfigureAwait(false);
        }

        var center = points[0];
        var majorEnd = points[1];
        var ratioPoint = points[2];
        var majorLength = Vector2.Distance(center, majorEnd);
        if (majorLength <= 1e-6f)
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail("ELLIPSE major axis point must differ from center."),
                State: runtime.State);
        }

        var minorLength = Vector2.Distance(center, ratioPoint);
        var ratio = Math.Clamp(minorLength / majorLength, 0.01f, 1.0f);
        var ratioToken = new CadPromptToken(
            CadPromptTokenType.Number,
            ratio.ToString("0.###", CultureInfo.InvariantCulture));

        var resolution = await runtime
            .SubmitTokenAsync(ratioToken, session, commit: true, cancellationToken)
            .ConfigureAwait(false);
        ResetPreview(session);
        return resolution;
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        List<Vector2>? points;
        lock (_sync)
        {
            _pickedPoints.TryGetValue(key, out points);
            if (points is null || points.Count == 0)
            {
                preview = default!;
                return false;
            }
        }

        var hints = new List<CadToolVisualHint>(4)
        {
            new("PickPoint", points[0], null, "Center")
        };

        string status;
        if (points.Count == 1)
        {
            status = "Specify major axis endpoint";
            hints.Add(new CadToolVisualHint("RubberBand", points[0], cursorPoint, "Major axis"));
        }
        else
        {
            status = "Specify minor axis ratio";
            hints.Add(new CadToolVisualHint("PickPoint", points[1], null, "Major axis"));
            hints.Add(new CadToolVisualHint("RubberBand", points[0], cursorPoint, "Minor ratio"));
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: status,
            Hints: hints);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _pickedPoints.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }
}

public sealed class PolygonInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private const int DefaultSides = 6;
    private readonly Dictionary<Guid, Vector2> _centers = new();
    private readonly object _sync = new();

    public string CommandName => "POLYGON";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        var key = ResolveSessionKey(session);
        Vector2? center = null;
        lock (_sync)
        {
            if (_centers.TryGetValue(key, out var value))
            {
                center = value;
            }
            else
            {
                _centers[key] = picked;
            }
        }

        if (center is null)
        {
            return new CadPromptResolution(
                Handled: true,
                Result: null,
                State: runtime.State);
        }

        var radius = Vector2.Distance(center.Value, picked);
        if (radius <= 1e-6f)
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail("POLYGON radius must be greater than zero."),
                State: runtime.State);
        }

        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"POLYGON {DefaultSides} {center.Value.X:0.###},{center.Value.Y:0.###} {radius:0.###} INSCRIBED");
        ResetPreview(session);
        return await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        Vector2 center;
        lock (_sync)
        {
            if (!_centers.TryGetValue(key, out center))
            {
                preview = default!;
                return false;
            }
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: $"Specify radius ({DefaultSides} sides)",
            Hints:
            [
                new CadToolVisualHint("PickPoint", center, null, "Center"),
                new CadToolVisualHint("RubberBand", center, cursorPoint, "Radius")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _centers.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }
}

public abstract class CadSelectionCommitInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    protected CadSelectionCommitInteractiveCommandAdapter(
        string commandName,
        int minimumSelectionCount = 1)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            throw new ArgumentException("Command name cannot be empty.", nameof(commandName));
        }

        CommandName = commandName.Trim().ToUpperInvariant();
        MinimumSelectionCount = Math.Max(1, minimumSelectionCount);
    }

    public string CommandName { get; }
    protected int MinimumSelectionCount { get; }

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!CadInteractiveAdapterSelectionHelpers.TryGetSelectionHandles(session, MinimumSelectionCount, out var handles, out var error))
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail(error),
                State: runtime.State);
        }

        var commandText = BuildCommandText(handles);
        return await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
    }

    protected virtual string BuildCommandText(IReadOnlyList<string> handles)
    {
        return handles.Count == 0
            ? CommandName
            : $"{CommandName} {string.Join(' ', handles)}";
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        if (!CadInteractiveAdapterSelectionHelpers.TryGetSelectedEntities(session, out var entities))
        {
            var status = fallbackStatus ?? $"Select entities before running {CommandName}.";
            preview = new CadInteractiveCommandPreview(
                CommandName: CommandName,
                Prompt: fallbackPrompt,
                Status: status,
                Hints:
                [
                    new CadToolVisualHint("Prompt", cursorPoint, null, status)
                ]);
            return true;
        }

        var distinctEntities = entities
            .Where(static entity => entity is not null)
            .DistinctBy(static entity => entity.Handle == 0 ? $"{entity.GetType().Name}:{entity.GetHashCode()}" : entity.Handle.ToString("X", CultureInfo.InvariantCulture))
            .ToArray();

        var hints = new List<CadToolVisualHint>(distinctEntities.Length + 1);
        for (var index = 0; index < distinctEntities.Length; index++)
        {
            var entity = distinctEntities[index];
            if (CadInteractiveAdapterSelectionHelpers.TryResolveEntityAnchor(entity, out var anchor))
            {
                hints.Add(new CadToolVisualHint(
                    Kind: "PickPoint",
                    Anchor: anchor,
                    SecondaryAnchor: null,
                    Text: $"{index + 1}: {entity.ObjectName}"));
            }
        }

        var statusText = fallbackStatus ??
                         $"{CommandName}: {distinctEntities.Length} selected. Click or press Enter to confirm.";
        hints.Add(new CadToolVisualHint(
            Kind: "Prompt",
            Anchor: cursorPoint,
            SecondaryAnchor: null,
            Text: statusText));

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: statusText,
            Hints: hints);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
    }
}

public sealed class BoundaryInteractiveCommandAdapter : CadSelectionCommitInteractiveCommandAdapter
{
    public BoundaryInteractiveCommandAdapter()
        : base("BOUNDARY")
    {
    }
}

public sealed class HatchInteractiveCommandAdapter : CadSelectionCommitInteractiveCommandAdapter
{
    public HatchInteractiveCommandAdapter()
        : base("HATCH")
    {
    }

    protected override string BuildCommandText(IReadOnlyList<string> handles)
    {
        return handles.Count == 0
            ? CommandName
            : $"{CommandName} SOLID {string.Join(' ', handles)}";
    }
}

public sealed class CopyClipInteractiveCommandAdapter : CadSelectionCommitInteractiveCommandAdapter
{
    public CopyClipInteractiveCommandAdapter()
        : base("COPYCLIP")
    {
    }
}

public sealed class CutInteractiveCommandAdapter : CadSelectionCommitInteractiveCommandAdapter
{
    public CutInteractiveCommandAdapter()
        : base("CUT")
    {
    }
}

public sealed class ExplodeInteractiveCommandAdapter : CadSelectionCommitInteractiveCommandAdapter
{
    public ExplodeInteractiveCommandAdapter()
        : base("EXPLODE")
    {
    }
}

public sealed class JoinInteractiveCommandAdapter : CadSelectionCommitInteractiveCommandAdapter
{
    public JoinInteractiveCommandAdapter()
        : base("JOIN", minimumSelectionCount: 2)
    {
    }
}

public sealed class FilletInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private const double DefaultRadius = 1.0;
    private readonly Dictionary<Guid, double> _radii = new();
    private readonly object _sync = new();

    public string CommandName => "FILLET";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        if (TryParsePositiveScalar(token, out var radius))
        {
            SetRadius(session, radius);
            return await runtime
                .SubmitTokenAsync(
                    new CadPromptToken(CadPromptTokenType.Number, radius.ToString("0.###", CultureInfo.InvariantCulture)),
                    session,
                    commit: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!CadInteractiveAdapterSelectionHelpers.TryResolveNearestSelectedFilletChamferTargets(session, picked, 2, out var targets, out var error))
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail(error),
                State: runtime.State);
        }

        var activeRadius = GetRadius(session);
        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"FILLET {activeRadius:0.###} {targets[0].Handle:X} {targets[1].Handle:X}");
        var resolution = await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
        ResetPreview(session);
        return resolution;
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var radius = GetRadius(session);
        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: $"Select first/second line or open polyline near intersection (R={radius:0.###})",
            Hints:
            [
                new CadToolVisualHint("Prompt", cursorPoint, null, $"Radius {radius:0.###}")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _radii.Remove(key);
        }
    }

    private double GetRadius(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            return _radii.TryGetValue(key, out var value) ? value : DefaultRadius;
        }
    }

    private void SetRadius(ICadEditorSession? session, double radius)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _radii[key] = radius;
        }
    }

    private static bool TryParsePositiveScalar(CadPromptToken token, out double value)
    {
        value = 0.0;
        return token.Type is CadPromptTokenType.Number or CadPromptTokenType.Text or CadPromptTokenType.Raw &&
               double.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               value > 0.0;
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }
}

public sealed class ChamferInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, ChamferState> _states = new();
    private readonly object _sync = new();
    private const double DefaultDistance = 1.0;

    public string CommandName => "CHAMFER";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        if (TryParseChamferDistances(token, out var firstDistance, out var secondDistance))
        {
            SetDistances(session, firstDistance, secondDistance);
            var formatted = firstDistance.Equals(secondDistance)
                ? firstDistance.ToString("0.###", CultureInfo.InvariantCulture)
                : string.Create(CultureInfo.InvariantCulture, $"{firstDistance:0.###},{secondDistance:0.###}");
            return await runtime
                .SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Text, formatted), session, commit: false, cancellationToken)
                .ConfigureAwait(false);
        }

        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!CadInteractiveAdapterSelectionHelpers.TryResolveNearestSelectedFilletChamferTargets(session, picked, 2, out var targets, out var error))
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail(error),
                State: runtime.State);
        }

        var state = GetOrCreateState(session);
        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"CHAMFER {state.FirstDistance:0.###} {state.SecondDistance:0.###} {targets[0].Handle:X} {targets[1].Handle:X}");
        var resolution = await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
        ResetPreview(session);
        return resolution;
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var state = GetOrCreateState(session);
        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: $"Select two lines/open polylines (D1={state.FirstDistance:0.###}, D2={state.SecondDistance:0.###})",
            Hints:
            [
                new CadToolVisualHint("Prompt", cursorPoint, null, $"D1 {state.FirstDistance:0.###}, D2 {state.SecondDistance:0.###}")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _states.Remove(key);
        }
    }

    private ChamferState GetOrCreateState(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            if (_states.TryGetValue(key, out var state))
            {
                return state;
            }

            state = new ChamferState(DefaultDistance, DefaultDistance);
            _states[key] = state;
            return state;
        }
    }

    private void SetDistances(ICadEditorSession? session, double firstDistance, double secondDistance)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _states[key] = new ChamferState(firstDistance, secondDistance);
        }
    }

    private static bool TryParseChamferDistances(CadPromptToken token, out double firstDistance, out double secondDistance)
    {
        firstDistance = 0.0;
        secondDistance = 0.0;
        if (token.Type is not (CadPromptTokenType.Number or CadPromptTokenType.Text or CadPromptTokenType.Raw))
        {
            return false;
        }

        var value = token.Value.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        var split = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Length == 1 &&
            double.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out firstDistance) &&
            firstDistance > 0.0)
        {
            secondDistance = firstDistance;
            return true;
        }

        if (split.Length == 2 &&
            double.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out firstDistance) &&
            double.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out secondDistance) &&
            firstDistance > 0.0 &&
            secondDistance > 0.0)
        {
            return true;
        }

        return false;
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }

    private sealed record ChamferState(double FirstDistance, double SecondDistance);
}

public sealed class ArrayInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private const int DefaultRows = 2;
    private const int DefaultColumns = 2;
    private const int DefaultPolarItems = 6;
    private const double MinimumSpacing = 1e-3;

    private readonly Dictionary<Guid, ArrayState> _states = new();
    private readonly object _sync = new();

    public string CommandName => "ARRAY";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var state = GetOrCreateState(session);
        if (TryResolveModeKeyword(token, out var mode))
        {
            SetMode(session, mode);
            var keyword = mode switch
            {
                CadArrayInteractiveMode.Polar => "POLAR",
                CadArrayInteractiveMode.Path => "PATH",
                _ => "RECTANGULAR"
            };
            return await runtime
                .SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Keyword, keyword), session, commit: false, cancellationToken)
                .ConfigureAwait(false);
        }

        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        if (state.Mode == CadArrayInteractiveMode.Path)
        {
            if (!CadInteractiveAdapterSelectionHelpers.TryResolvePathHandleAndTargets(
                    session,
                    picked,
                    out var pathHandle,
                    out var targetHandles,
                    out var pathSelectionError))
            {
                return new CadPromptResolution(
                    Handled: true,
                    Result: CadCommandResult.Fail(pathSelectionError),
                    State: runtime.State);
            }

            var pathCommandText = BuildPathCommandText(state, pathHandle, targetHandles);
            var pathResolution = await runtime
                .SubmitAsync(pathCommandText, session, cancellationToken)
                .ConfigureAwait(false);
            ResetPreview(session);
            return pathResolution;
        }

        if (!CadInteractiveAdapterSelectionHelpers.TryGetSelectionHandles(session, 1, out var handles, out var selectionError))
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail(selectionError),
                State: runtime.State);
        }

        if (state.BasePoint is null)
        {
            state.BasePoint = picked;
            return new CadPromptResolution(true, null, runtime.State);
        }

        var commandText = state.Mode == CadArrayInteractiveMode.Polar
            ? BuildPolarCommandText(state, picked, handles)
            : BuildRectangularCommandText(state, picked, handles);

        var resolution = await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
        ResetPreview(session);
        return resolution;
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var state = GetOrCreateState(session);
        var status = state.Mode switch
        {
            CadArrayInteractiveMode.Polar => "Specify polar angle step point",
            CadArrayInteractiveMode.Path => "Pick near path entity to distribute copies",
            _ => "Specify opposite corner for row/column spacing"
        };
        var modeLabel = state.Mode switch
        {
            CadArrayInteractiveMode.Polar => "Mode: Polar",
            CadArrayInteractiveMode.Path => "Mode: Path",
            _ => "Mode: Rectangular"
        };
        var hints = new List<CadToolVisualHint>(2)
        {
            new("Prompt", cursorPoint, null, modeLabel)
        };

        if (state.Mode != CadArrayInteractiveMode.Path &&
            state.BasePoint is { } basePoint)
        {
            hints.Add(new CadToolVisualHint("RubberBand", basePoint, cursorPoint, status));
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: status,
            Hints: hints);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _states.Remove(key);
        }
    }

    private ArrayState GetOrCreateState(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            if (_states.TryGetValue(key, out var state))
            {
                return state;
            }

            state = new ArrayState(
                CadArrayInteractiveMode.Rectangular,
                null,
                DefaultRows,
                DefaultColumns,
                DefaultPolarItems);
            _states[key] = state;
            return state;
        }
    }

    private void SetMode(ICadEditorSession? session, CadArrayInteractiveMode mode)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out var state))
            {
                state = new ArrayState(mode, null, DefaultRows, DefaultColumns, DefaultPolarItems);
                _states[key] = state;
                return;
            }

            state.Mode = mode;
            state.BasePoint = null;
        }
    }

    private static bool TryResolveModeKeyword(CadPromptToken token, out CadArrayInteractiveMode mode)
    {
        mode = CadArrayInteractiveMode.Rectangular;
        var value = token.Value.Trim();
        if (value.Equals("POLAR", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("P", StringComparison.OrdinalIgnoreCase))
        {
            mode = CadArrayInteractiveMode.Polar;
            return true;
        }

        if (value.Equals("PATH", StringComparison.OrdinalIgnoreCase))
        {
            mode = CadArrayInteractiveMode.Path;
            return true;
        }

        if (value.Equals("RECTANGULAR", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("R", StringComparison.OrdinalIgnoreCase))
        {
            mode = CadArrayInteractiveMode.Rectangular;
            return true;
        }

        return false;
    }

    private static string BuildRectangularCommandText(ArrayState state, Vector2 picked, IReadOnlyList<string> handles)
    {
        var basePoint = state.BasePoint ?? picked;
        var spacingX = Math.Max(MinimumSpacing, Math.Abs(picked.X - basePoint.X));
        var spacingY = Math.Max(MinimumSpacing, Math.Abs(picked.Y - basePoint.Y));
        var handlesToken = string.Join(' ', handles);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"ARRAY {Math.Max(1, state.Rows)} {Math.Max(1, state.Columns)} {spacingY:0.###} {spacingX:0.###} {handlesToken}");
    }

    private static string BuildPolarCommandText(ArrayState state, Vector2 picked, IReadOnlyList<string> handles)
    {
        var center = state.BasePoint ?? picked;
        var direction = picked - center;
        var angleStepDegrees = direction.LengthSquared() <= float.Epsilon
            ? 360.0 / Math.Max(2, state.PolarItems)
            : Math.Abs(Math.Atan2(direction.Y, direction.X) * (180.0 / Math.PI));
        if (angleStepDegrees <= 0.0)
        {
            angleStepDegrees = 360.0 / Math.Max(2, state.PolarItems);
        }

        var handlesToken = string.Join(' ', handles);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"ARRAY POLAR {Math.Max(2, state.PolarItems)} {angleStepDegrees:0.###} {center.X:0.###},{center.Y:0.###} {handlesToken}");
    }

    private static string BuildPathCommandText(ArrayState state, string pathHandle, IReadOnlyList<string> targetHandles)
    {
        var handlesToken = string.Join(' ', targetHandles);
        return $"ARRAY PATH {Math.Max(2, state.PolarItems)} {pathHandle} {handlesToken}";
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }

    private sealed class ArrayState
    {
        public ArrayState(
            CadArrayInteractiveMode mode,
            Vector2? basePoint,
            int rows,
            int columns,
            int polarItems)
        {
            Mode = mode;
            BasePoint = basePoint;
            Rows = rows;
            Columns = columns;
            PolarItems = polarItems;
        }

        public CadArrayInteractiveMode Mode { get; set; }
        public Vector2? BasePoint { get; set; }
        public int Rows { get; }
        public int Columns { get; }
        public int PolarItems { get; }
    }

    private enum CadArrayInteractiveMode
    {
        Rectangular,
        Polar,
        Path
    }
}

public sealed class AlignInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, Vector2> _sourcePoints = new();
    private readonly object _sync = new();

    public string CommandName => "ALIGN";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!CadInteractiveAdapterSelectionHelpers.TryGetSelectionHandles(session, 1, out var handles, out var selectionError))
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail(selectionError),
                State: runtime.State);
        }

        var key = ResolveSessionKey(session);
        Vector2? source = null;
        lock (_sync)
        {
            if (_sourcePoints.TryGetValue(key, out var sourcePoint))
            {
                source = sourcePoint;
            }
            else
            {
                _sourcePoints[key] = picked;
            }
        }

        if (source is null)
        {
            return new CadPromptResolution(true, null, runtime.State);
        }

        var handlesToken = string.Join(' ', handles);
        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"ALIGN {source.Value.X:0.###},{source.Value.Y:0.###} {picked.X:0.###},{picked.Y:0.###} {handlesToken}");
        var resolution = await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
        ResetPreview(session);
        return resolution;
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            if (!_sourcePoints.TryGetValue(key, out var sourcePoint))
            {
                preview = new CadInteractiveCommandPreview(
                    CommandName: CommandName,
                    Prompt: fallbackPrompt,
                    Status: "Specify source point",
                    Hints:
                    [
                        new CadToolVisualHint("Prompt", cursorPoint, null, "Source")
                    ]);
                return true;
            }

            preview = new CadInteractiveCommandPreview(
                CommandName: CommandName,
                Prompt: fallbackPrompt,
                Status: "Specify destination point",
                Hints:
                [
                    new CadToolVisualHint("PickPoint", sourcePoint, null, "Source"),
                    new CadToolVisualHint("RubberBand", sourcePoint, cursorPoint, "Destination")
                ]);
            return true;
        }
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _sourcePoints.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }
}

public sealed class MatchPropInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    public string CommandName => "MATCHPROP";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!CadInteractiveAdapterSelectionHelpers.TryResolveMatchPropSelection(
                session,
                picked,
                out var sourceHandle,
                out var targetHandles,
                out var selectionError))
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail(selectionError),
                State: runtime.State);
        }

        var commandText = $"MATCHPROP {sourceHandle} {string.Join(' ', targetHandles)}";
        return await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: "Select source entity (nearest pick) and apply to other selected targets",
            Hints:
            [
                new CadToolVisualHint("Prompt", cursorPoint, null, "Source")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
    }
}

public sealed class SplineInteractiveCommandAdapter : CadPointPickInteractiveCommandAdapter
{
    public SplineInteractiveCommandAdapter()
        : base(
            commandName: "SPLINE",
            stagePrompts:
            [
                "Specify first fit point",
                "Specify next fit point"
            ],
            keywords:
            [
                "Close"
            ],
            continueUntilExplicitCommit: true)
    {
    }
}

public sealed class PasteClipInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, Vector2> _anchors = new();
    private readonly object _sync = new();

    public string CommandName => "PASTECLIP";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var point))
        {
            return await runtime
                .SubmitAsync(CommandName, session, cancellationToken)
                .ConfigureAwait(false);
        }

        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _anchors[key] = point;
        }

        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"PASTECLIP {point.X:0.###},{point.Y:0.###}");
        var result = await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
        ResetPreview(session);
        return result;
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: "Specify insertion point",
            Hints:
            [
                new CadToolVisualHint("PickPoint", cursorPoint, null, "Insertion")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _anchors.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }
}

public sealed class OffsetInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, Vector2> _points = new();
    private readonly object _sync = new();

    public string CommandName => "OFFSET";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!CadInteractiveAdapterSelectionHelpers.TryGetSelectionHandles(session, 1, out var handles, out var selectionError))
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail(selectionError),
                State: runtime.State);
        }

        var key = ResolveSessionKey(session);
        Vector2? basePoint = null;
        lock (_sync)
        {
            if (_points.TryGetValue(key, out var value))
            {
                basePoint = value;
            }
            else
            {
                _points[key] = picked;
            }
        }

        if (basePoint is null)
        {
            return new CadPromptResolution(
                Handled: true,
                Result: null,
                State: runtime.State);
        }

        var distance = Vector2.Distance(basePoint.Value, picked);
        if (distance <= 1e-6f)
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail("OFFSET distance must be greater than zero."),
                State: runtime.State);
        }

        var side = picked.X >= basePoint.Value.X ? "LEFT" : "RIGHT";
        var commandText = $"OFFSET {distance.ToString("0.###", CultureInfo.InvariantCulture)} {side} {string.Join(' ', handles)}";
        ResetPreview(session);
        return await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        Vector2 basePoint;
        lock (_sync)
        {
            if (!_points.TryGetValue(key, out basePoint))
            {
                preview = default!;
                return false;
            }
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: "Specify offset distance",
            Hints:
            [
                new CadToolVisualHint("PickPoint", basePoint, null, "Base"),
                new CadToolVisualHint("RubberBand", basePoint, cursorPoint, "Distance")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _points.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }
}

public sealed class StretchInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, Vector2> _basePoints = new();
    private readonly object _sync = new();

    public string CommandName => "STRETCH";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!CadInteractiveAdapterSelectionHelpers.TryGetSelectionHandles(session, 1, out var handles, out var selectionError))
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail(selectionError),
                State: runtime.State);
        }

        var key = ResolveSessionKey(session);
        Vector2? basePoint = null;
        lock (_sync)
        {
            if (_basePoints.TryGetValue(key, out var value))
            {
                basePoint = value;
            }
            else
            {
                _basePoints[key] = picked;
            }
        }

        if (basePoint is null)
        {
            return new CadPromptResolution(
                Handled: true,
                Result: null,
                State: runtime.State);
        }

        var delta = picked - basePoint.Value;
        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"STRETCH {delta.X:0.###},{delta.Y:0.###} {basePoint.Value.X:0.###},{basePoint.Value.Y:0.###} {string.Join(' ', handles)}");
        ResetPreview(session);
        return await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        Vector2 basePoint;
        lock (_sync)
        {
            if (!_basePoints.TryGetValue(key, out basePoint))
            {
                preview = default!;
                return false;
            }
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: "Specify stretch target point",
            Hints:
            [
                new CadToolVisualHint("PickPoint", basePoint, null, "Grip"),
                new CadToolVisualHint("RubberBand", basePoint, cursorPoint, "Delta")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _basePoints.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }
}

public sealed class BreakInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, BreakSessionState> _states = new();
    private readonly object _sync = new();

    private readonly record struct BreakSessionState(
        ulong TargetHandle,
        Vector2 SegmentStart,
        Vector2 SegmentEnd,
        XY FirstPoint);

    public string CommandName => "BREAK";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var key = ResolveSessionKey(session);
        if (token.Type == CadPromptTokenType.Coordinate &&
            TryParseCoordinateToken(token.Value, out var point))
        {
            BreakSessionState? pendingState = null;
            lock (_sync)
            {
                if (_states.TryGetValue(key, out var existing))
                {
                    pendingState = existing;
                }
            }

            if (pendingState is null)
            {
                if (!CadInteractiveAdapterSelectionHelpers.TryResolveNearestSelectedBreakTarget(session, point, out var firstTarget, out var firstError))
                {
                    return new CadPromptResolution(
                        Handled: true,
                        Result: CadCommandResult.Fail(firstError),
                        State: runtime.State);
                }

                lock (_sync)
                {
                    _states[key] = new BreakSessionState(
                        firstTarget.Entity.Handle,
                        firstTarget.SegmentStart,
                        firstTarget.SegmentEnd,
                        firstTarget.ProjectedPointExact);
                }

                return new CadPromptResolution(
                    Handled: true,
                    Result: null,
                    State: runtime.State);
            }

            if (!CadInteractiveAdapterSelectionHelpers.TryResolveBreakTargetByHandle(
                    session,
                    pendingState.Value.TargetHandle,
                    point,
                    out var secondTarget,
                    out var secondError))
            {
                ResetPreview(session);
                return new CadPromptResolution(
                    Handled: true,
                    Result: CadCommandResult.Fail(secondError),
                    State: runtime.State);
            }

            var twoPointCommandText = string.Create(
                CultureInfo.InvariantCulture,
                $"BREAK {pendingState.Value.TargetHandle:X} {pendingState.Value.FirstPoint.X:R},{pendingState.Value.FirstPoint.Y:R} {secondTarget.ProjectedPointExact.X:R},{secondTarget.ProjectedPointExact.Y:R}");
            ResetPreview(session);
            return await runtime
                .SubmitAsync(twoPointCommandText, session, cancellationToken)
                .ConfigureAwait(false);
        }

        if (commit)
        {
            BreakSessionState pendingState;
            var hasPendingState = false;
            lock (_sync)
            {
                hasPendingState = _states.TryGetValue(key, out pendingState);
            }

            if (hasPendingState)
            {
                var onePointCommandText = string.Create(
                    CultureInfo.InvariantCulture,
                    $"BREAK {pendingState.TargetHandle:X} {pendingState.FirstPoint.X:R},{pendingState.FirstPoint.Y:R}");
                ResetPreview(session);
                return await runtime
                    .SubmitAsync(onePointCommandText, session, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var resolution = await runtime
            .SubmitTokenAsync(token, session, commit, cancellationToken)
            .ConfigureAwait(false);
        if (!resolution.State.IsActive)
        {
            ResetPreview(session);
        }

        return resolution;
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        BreakSessionState? pendingState = null;
        lock (_sync)
        {
            if (_states.TryGetValue(key, out var existing))
            {
                pendingState = existing;
            }
        }

        if (pendingState is not null)
        {
            if (!CadInteractiveAdapterSelectionHelpers.TryResolveBreakTargetByHandle(
                    session,
                    pendingState.Value.TargetHandle,
                    cursorPoint,
                    out var secondTarget,
                    out _))
            {
                preview = default!;
                return false;
            }

            preview = new CadInteractiveCommandPreview(
                CommandName: CommandName,
                Prompt: fallbackPrompt,
                Status: fallbackStatus ?? "Specify second break point or press Enter for single-point break.",
                Hints:
                [
                    new CadToolVisualHint("RubberBand", pendingState.Value.SegmentStart, pendingState.Value.SegmentEnd, "Selected"),
                    new CadToolVisualHint("PickPoint", ToVector2(pendingState.Value.FirstPoint), null, "First"),
                    new CadToolVisualHint("PickPoint", secondTarget.ProjectedPoint, null, "Second"),
                    new CadToolVisualHint("RubberBand", ToVector2(pendingState.Value.FirstPoint), secondTarget.ProjectedPoint, "Break span"),
                    new CadToolVisualHint("HelperLine", secondTarget.ProjectedPoint, cursorPoint, "Second break point")
                ]);
            return true;
        }

        preview = default!;
        if (!CadInteractiveAdapterSelectionHelpers.TryResolveNearestSelectedBreakTarget(session, cursorPoint, out var firstTarget, out _))
        {
            return false;
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: fallbackStatus ?? "Specify first break point.",
            Hints:
            [
                new CadToolVisualHint("RubberBand", firstTarget.SegmentStart, firstTarget.SegmentEnd, "Selected"),
                new CadToolVisualHint("PickPoint", firstTarget.ProjectedPoint, null, "First"),
                new CadToolVisualHint("HelperLine", firstTarget.ProjectedPoint, cursorPoint, "First break point")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _states.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }

    private static Vector2 ToVector2(XY point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static bool TryParseCoordinateToken(string value, out XY point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var split = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Length < 2)
        {
            return false;
        }

        if (!double.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        point = new XY(x, y);
        return true;
    }
}

public abstract class CadTrimExtendInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    protected CadTrimExtendInteractiveCommandAdapter(string commandName)
    {
        CommandName = commandName;
    }

    public string CommandName { get; }

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !CadDeltaInteractiveCommandAdapter.TryParsePoint(token.Value, out var point))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!CadInteractiveAdapterSelectionHelpers.TryResolveBoundaryAndTarget(session, point, out var boundary, out var target, out var endpoint, out var error))
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail(error),
                State: runtime.State);
        }

        var commandText = $"{CommandName} {boundary.Handle:X} {target.Entity.Handle:X} {endpoint}";
        return await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        preview = default!;
        if (!CadInteractiveAdapterSelectionHelpers.TryResolveBoundaryAndTarget(
                session,
                cursorPoint,
                out var boundary,
                out var target,
                out var endpoint,
                out _))
        {
            return false;
        }

        var targetPoint = string.Equals(endpoint, "START", StringComparison.OrdinalIgnoreCase)
            ? target.StartPoint
            : target.EndPoint;
        var boundaryAnchor = CadInteractiveAdapterSelectionHelpers.TryResolveEntityAnchor(boundary, out var resolvedAnchor)
            ? resolvedAnchor
            : Vector2.Zero;
        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: fallbackStatus ?? string.Create(CultureInfo.InvariantCulture, $"{CommandName}: pick trim/extend point."),
            Hints:
            [
                new CadToolVisualHint("PickPoint", boundaryAnchor, null, "Boundary"),
                new CadToolVisualHint("PickPoint", targetPoint, null, $"Target {endpoint}"),
                new CadToolVisualHint("HelperLine", targetPoint, cursorPoint, CommandName)
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
    }
}

public sealed class TrimInteractiveCommandAdapter : CadTrimExtendInteractiveCommandAdapter
{
    public TrimInteractiveCommandAdapter()
        : base("TRIM")
    {
    }
}

public sealed class ExtendInteractiveCommandAdapter : CadTrimExtendInteractiveCommandAdapter
{
    public ExtendInteractiveCommandAdapter()
        : base("EXTEND")
    {
    }
}

internal static class CadInteractiveAdapterSelectionHelpers
{
    public readonly record struct CadBreakTarget(
        Entity Entity,
        Vector2 SegmentStart,
        Vector2 SegmentEnd,
        Vector2 ProjectedPoint,
        XY ProjectedPointExact);

    public readonly record struct CadTrimExtendTarget(
        Entity Entity,
        Vector2 StartPoint,
        Vector2 EndPoint);

    public static bool TryResolveBoundaryAndTarget(
        ICadEditorSession? session,
        Vector2 point,
        out Entity boundary,
        out CadTrimExtendTarget target,
        out string endpoint,
        out string error)
    {
        boundary = null!;
        target = default;
        endpoint = "END";
        error = "Select at least two entities (including one line or open polyline) before using TRIM/EXTEND.";

        if (!TryGetSelectedEntities(session, out var entities) || entities.Count < 2)
        {
            return false;
        }

        var validEntities = entities
            .Where(entity => entity.Handle != 0)
            .DistinctBy(entity => entity.Handle)
            .ToArray();
        if (validEntities.Length < 2)
        {
            error = "TRIM/EXTEND requires at least two selected entities with valid handles.";
            return false;
        }

        var bestDistanceSquared = float.MaxValue;
        var selectedTarget = (Entity?)null;
        var selectedStart = Vector2.Zero;
        var selectedEnd = Vector2.Zero;

        foreach (var candidate in validEntities)
        {
            switch (candidate)
            {
                case Line line:
                {
                    var start = new Vector2((float)line.StartPoint.X, (float)line.StartPoint.Y);
                    var end = new Vector2((float)line.EndPoint.X, (float)line.EndPoint.Y);
                    var distanceSquared = DistanceSquaredToLine(point, line);
                    if (distanceSquared < bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        selectedTarget = line;
                        selectedStart = start;
                        selectedEnd = end;
                    }

                    break;
                }
                case LwPolyline polyline when !polyline.IsClosed && polyline.Vertices.Count >= 2:
                {
                    var start = new Vector2((float)polyline.Vertices[0].Location.X, (float)polyline.Vertices[0].Location.Y);
                    var end = new Vector2(
                        (float)polyline.Vertices[polyline.Vertices.Count - 1].Location.X,
                        (float)polyline.Vertices[polyline.Vertices.Count - 1].Location.Y);
                    var distanceSquared = DistanceSquaredToOpenPolyline(point, polyline);
                    if (distanceSquared < bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        selectedTarget = polyline;
                        selectedStart = start;
                        selectedEnd = end;
                    }

                    break;
                }
            }
        }

        if (selectedTarget is null)
        {
            error = "TRIM/EXTEND requires a selected line or open polyline target.";
            return false;
        }

        var selectedBoundary = validEntities
            .Where(entity => !ReferenceEquals(entity, selectedTarget))
            .OrderBy(entity => DistanceSquaredToEntity(point, entity))
            .FirstOrDefault();
        if (selectedBoundary is null)
        {
            error = "TRIM/EXTEND requires a distinct boundary entity.";
            return false;
        }

        target = new CadTrimExtendTarget(selectedTarget, selectedStart, selectedEnd);
        boundary = selectedBoundary;
        endpoint = Vector2.DistanceSquared(point, selectedStart) <= Vector2.DistanceSquared(point, selectedEnd)
            ? "START"
            : "END";
        return true;
    }

    public static bool TryResolveNearestSelectedBreakTarget(
        ICadEditorSession? session,
        Vector2 point,
        out CadBreakTarget target,
        out string error)
    {
        return TryResolveNearestSelectedBreakTarget(
            session,
            new XY(point.X, point.Y),
            out target,
            out error);
    }

    public static bool TryResolveNearestSelectedBreakTarget(
        ICadEditorSession? session,
        XY point,
        out CadBreakTarget target,
        out string error)
    {
        target = default;
        error = "BREAK requires a selected line or open polyline target.";

        if (!TryGetSelectedEntities(session, out var entities))
        {
            return false;
        }

        var bestDistanceSquared = double.MaxValue;
        var hasCandidate = false;

        foreach (var entity in entities)
        {
            if (entity.Handle == 0 ||
                !TryResolveBreakTarget(entity, point, out var candidate))
            {
                continue;
            }

            var distanceSquared = DistanceSquared(point, candidate.ProjectedPointExact);
            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = distanceSquared;
            target = candidate;
            hasCandidate = true;
        }

        return hasCandidate;
    }

    public static bool TryResolveBreakTargetByHandle(
        ICadEditorSession? session,
        ulong handle,
        Vector2 point,
        out CadBreakTarget target,
        out string error)
    {
        return TryResolveBreakTargetByHandle(
            session,
            handle,
            new XY(point.X, point.Y),
            out target,
            out error);
    }

    public static bool TryResolveBreakTargetByHandle(
        ICadEditorSession? session,
        ulong handle,
        XY point,
        out CadBreakTarget target,
        out string error)
    {
        target = default;
        error = $"BREAK target handle '{handle:X}' was not found.";

        if (session is not CadDocumentSession documentSession)
        {
            error = "BREAK requires an active document session.";
            return false;
        }

        if (!documentSession.EntityIndex.TryGetByHandle(handle, out var entity, out _) ||
            !TryResolveBreakTarget(entity, point, out target))
        {
            error = $"BREAK target handle '{handle:X}' must resolve to a LINE or open LWPOLYLINE.";
            return false;
        }

        return true;
    }

    public static bool TryGetSelectionHandles(
        ICadEditorSession? session,
        int minimumCount,
        out string[] handles,
        out string error)
    {
        handles = Array.Empty<string>();
        error = "Select entities before running this command.";
        if (!TryGetSelectedEntities(session, out var entities))
        {
            return false;
        }

        var selectedHandles = entities
            .Where(entity => entity.Handle != 0)
            .Select(entity => entity.Handle.ToString("X", CultureInfo.InvariantCulture))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (selectedHandles.Length < minimumCount)
        {
            error = minimumCount <= 1
                ? "Select entities with valid handles before running this command."
                : $"Select at least {minimumCount} entities before running this command.";
            return false;
        }

        handles = selectedHandles;
        return true;
    }

    public static bool TryResolveNearestSelectedLines(
        ICadEditorSession? session,
        Vector2 point,
        int minimumCount,
        out Line[] lines,
        out string error)
    {
        lines = Array.Empty<Line>();
        error = minimumCount <= 1
            ? "Select a line before running this command."
            : $"Select at least {minimumCount} lines before running this command.";
        if (!TryGetSelectedEntities(session, out var entities))
        {
            return false;
        }

        var selectedLines = entities
            .OfType<Line>()
            .Where(line => line.Handle != 0)
            .DistinctBy(line => line.Handle)
            .OrderBy(line => DistanceSquaredToLine(point, line))
            .Take(Math.Max(1, minimumCount))
            .ToArray();

        if (selectedLines.Length < Math.Max(1, minimumCount))
        {
            return false;
        }

        lines = selectedLines;
        return true;
    }

    public static bool TryResolveNearestSelectedFilletChamferTargets(
        ICadEditorSession? session,
        Vector2 point,
        int minimumCount,
        out Entity[] targets,
        out string error)
    {
        targets = Array.Empty<Entity>();
        var requiredCount = Math.Max(1, minimumCount);
        error = requiredCount <= 1
            ? "Select a line or open polyline before running this command."
            : $"Select at least {requiredCount} lines or open polylines before running this command.";
        if (!TryGetSelectedEntities(session, out var entities))
        {
            return false;
        }

        var selectedTargets = entities
            .Where(static entity => entity.Handle != 0 && IsFilletChamferTarget(entity))
            .DistinctBy(static entity => entity.Handle)
            .OrderBy(entity => DistanceSquaredToFilletChamferTarget(point, entity))
            .Take(requiredCount)
            .ToArray();

        if (selectedTargets.Length < requiredCount)
        {
            return false;
        }

        targets = selectedTargets;
        return true;
    }

    public static bool TryResolveMatchPropSelection(
        ICadEditorSession? session,
        Vector2 point,
        out string sourceHandle,
        out string[] targetHandles,
        out string error)
    {
        sourceHandle = string.Empty;
        targetHandles = Array.Empty<string>();
        error = "Select at least two entities before running MATCHPROP.";
        if (!TryGetSelectedEntities(session, out var entities))
        {
            return false;
        }

        var validEntities = entities
            .Where(entity => entity.Handle != 0)
            .DistinctBy(entity => entity.Handle)
            .ToArray();
        if (validEntities.Length < 2)
        {
            error = "MATCHPROP requires a source plus at least one target with valid handles.";
            return false;
        }

        var sourceEntity = validEntities
            .OrderBy(entity => DistanceSquaredToEntity(point, entity))
            .First();
        sourceHandle = sourceEntity.Handle.ToString("X", CultureInfo.InvariantCulture);
        targetHandles = validEntities
            .Where(entity => entity.Handle != sourceEntity.Handle)
            .Select(entity => entity.Handle.ToString("X", CultureInfo.InvariantCulture))
            .ToArray();
        if (targetHandles.Length == 0)
        {
            error = "MATCHPROP requires at least one target different from source.";
            return false;
        }

        return true;
    }

    public static bool TryResolvePathHandleAndTargets(
        ICadEditorSession? session,
        Vector2 point,
        out string pathHandle,
        out string[] targetHandles,
        out string error)
    {
        pathHandle = string.Empty;
        targetHandles = Array.Empty<string>();
        error = "Select a path (LINE/LWPOLYLINE) and at least one target entity before using ARRAY PATH.";
        if (!TryGetSelectedEntities(session, out var entities))
        {
            return false;
        }

        var validEntities = entities
            .Where(entity => entity.Handle != 0)
            .DistinctBy(entity => entity.Handle)
            .ToArray();
        if (validEntities.Length < 2)
        {
            return false;
        }

        var pathEntity = validEntities
            .Where(entity => entity is Line or LwPolyline)
            .OrderBy(entity => DistanceSquaredToEntity(point, entity))
            .FirstOrDefault();
        if (pathEntity is null)
        {
            error = "ARRAY PATH requires a selected LINE or LWPOLYLINE path entity.";
            return false;
        }

        pathHandle = pathEntity.Handle.ToString("X", CultureInfo.InvariantCulture);
        targetHandles = validEntities
            .Where(entity => entity.Handle != pathEntity.Handle)
            .Select(entity => entity.Handle.ToString("X", CultureInfo.InvariantCulture))
            .ToArray();
        if (targetHandles.Length == 0)
        {
            error = "ARRAY PATH requires at least one selected target entity in addition to the path.";
            return false;
        }

        return true;
    }

    public static bool TryResolveEntityAnchor(Entity entity, out Vector2 anchor)
    {
        switch (entity)
        {
            case Line line:
                anchor = new Vector2(
                (float)((line.StartPoint.X + line.EndPoint.X) * 0.5),
                (float)((line.StartPoint.Y + line.EndPoint.Y) * 0.5));
                return true;
            case Arc arc:
                anchor = new Vector2((float)arc.Center.X, (float)arc.Center.Y);
                return true;
            case Circle circle:
                anchor = new Vector2((float)circle.Center.X, (float)circle.Center.Y);
                return true;
            case Ellipse ellipse:
                anchor = new Vector2((float)ellipse.Center.X, (float)ellipse.Center.Y);
                return true;
            case Point point:
                anchor = new Vector2((float)point.Location.X, (float)point.Location.Y);
                return true;
            case XLine xline:
                anchor = new Vector2((float)xline.FirstPoint.X, (float)xline.FirstPoint.Y);
                return true;
            case Ray ray:
                anchor = new Vector2((float)ray.StartPoint.X, (float)ray.StartPoint.Y);
                return true;
            case TextEntity text:
                anchor = new Vector2((float)text.InsertPoint.X, (float)text.InsertPoint.Y);
                return true;
            case MText mText:
                anchor = new Vector2((float)mText.InsertPoint.X, (float)mText.InsertPoint.Y);
                return true;
            case LwPolyline polyline when polyline.Vertices.Count > 0:
                anchor = new Vector2(
                    (float)polyline.Vertices.Average(static vertex => vertex.Location.X),
                    (float)polyline.Vertices.Average(static vertex => vertex.Location.Y));
                return true;
            case Spline spline when spline.ControlPoints.Count > 0:
                anchor = new Vector2(
                    (float)spline.ControlPoints.Average(static point => point.X),
                    (float)spline.ControlPoints.Average(static point => point.Y));
                return true;
            default:
                anchor = default;
                return false;
        }
    }

    public static bool TryGetSelectedEntities(ICadEditorSession? session, out List<Entity> entities)
    {
        entities = new List<Entity>();
        if (session is null)
        {
            return false;
        }

        foreach (var item in session.SelectionSet.Items)
        {
            if (item is Entity entity)
            {
                entities.Add(entity);
            }
        }

        return entities.Count > 0;
    }

    private static float DistanceSquaredToLine(Vector2 point, Line line)
    {
        var start = new Vector2((float)line.StartPoint.X, (float)line.StartPoint.Y);
        var end = new Vector2((float)line.EndPoint.X, (float)line.EndPoint.Y);
        var projection = ProjectPointToSegment(point, start, end);
        return Vector2.DistanceSquared(point, projection);
    }

    private static float DistanceSquaredToOpenPolyline(Vector2 point, LwPolyline polyline)
    {
        if (polyline.Vertices.Count < 2)
        {
            return float.MaxValue;
        }

        var best = float.MaxValue;
        for (var i = 0; i < polyline.Vertices.Count - 1; i++)
        {
            var start = new Vector2((float)polyline.Vertices[i].Location.X, (float)polyline.Vertices[i].Location.Y);
            var end = new Vector2((float)polyline.Vertices[i + 1].Location.X, (float)polyline.Vertices[i + 1].Location.Y);
            var projection = ProjectPointToSegment(point, start, end);
            var distanceSquared = Vector2.DistanceSquared(point, projection);
            if (distanceSquared < best)
            {
                best = distanceSquared;
            }
        }

        return best;
    }

    private static bool TryResolveBreakTarget(Entity entity, XY point, out CadBreakTarget target)
    {
        target = default;

        switch (entity)
        {
            case Line line:
            {
                var start = new XY(line.StartPoint.X, line.StartPoint.Y);
                var end = new XY(line.EndPoint.X, line.EndPoint.Y);
                var projected = ProjectPointToSegment(point, start, end);
                target = new CadBreakTarget(
                    line,
                    new Vector2((float)start.X, (float)start.Y),
                    new Vector2((float)end.X, (float)end.Y),
                    new Vector2((float)projected.X, (float)projected.Y),
                    projected);
                return true;
            }
            case LwPolyline polyline when !polyline.IsClosed && polyline.Vertices.Count >= 2:
            {
                var bestDistanceSquared = double.MaxValue;
                var hasCandidate = false;
                var candidate = default(CadBreakTarget);
                for (var i = 0; i < polyline.Vertices.Count - 1; i++)
                {
                    var start = new XY(
                        polyline.Vertices[i].Location.X,
                        polyline.Vertices[i].Location.Y);
                    var end = new XY(
                        polyline.Vertices[i + 1].Location.X,
                        polyline.Vertices[i + 1].Location.Y);
                    var projected = ProjectPointToSegment(point, start, end);
                    var distanceSquared = DistanceSquared(point, projected);
                    if (distanceSquared >= bestDistanceSquared)
                    {
                        continue;
                    }

                    bestDistanceSquared = distanceSquared;
                    candidate = new CadBreakTarget(
                        polyline,
                        new Vector2((float)start.X, (float)start.Y),
                        new Vector2((float)end.X, (float)end.Y),
                        new Vector2((float)projected.X, (float)projected.Y),
                        projected);
                    hasCandidate = true;
                }

                if (hasCandidate)
                {
                    target = candidate;
                }

                return hasCandidate;
            }
            default:
                return false;
        }
    }

    private static XY ProjectPointToSegment(XY point, XY start, XY end)
    {
        var axisX = end.X - start.X;
        var axisY = end.Y - start.Y;
        var axisLengthSquared = (axisX * axisX) + (axisY * axisY);
        if (axisLengthSquared <= double.Epsilon)
        {
            return start;
        }

        var t = ((point.X - start.X) * axisX + (point.Y - start.Y) * axisY) / axisLengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        return new XY(start.X + axisX * t, start.Y + axisY * t);
    }

    private static Vector2 ProjectPointToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var axis = end - start;
        var axisLengthSquared = axis.LengthSquared();
        if (axisLengthSquared <= float.Epsilon)
        {
            return start;
        }

        var t = Vector2.Dot(point - start, axis) / axisLengthSquared;
        t = Math.Clamp(t, 0f, 1f);
        return start + axis * t;
    }

    private static double DistanceSquared(XY first, XY second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return (dx * dx) + (dy * dy);
    }

    private static float DistanceSquaredToEntity(Vector2 point, Entity entity)
    {
        return entity switch
        {
            Line line => DistanceSquaredToLine(point, line),
            Arc arc => DistanceSquaredToCircle(point, arc.Center, arc.Radius),
            Circle circle => DistanceSquaredToCircle(point, circle),
            Point cadPoint => Vector2.DistanceSquared(point, new Vector2((float)cadPoint.Location.X, (float)cadPoint.Location.Y)),
            LwPolyline polyline => DistanceSquaredToPolyline(point, polyline),
            XLine xline => Vector2.DistanceSquared(point, new Vector2((float)xline.FirstPoint.X, (float)xline.FirstPoint.Y)),
            Ray ray => Vector2.DistanceSquared(point, new Vector2((float)ray.StartPoint.X, (float)ray.StartPoint.Y)),
            TextEntity text => Vector2.DistanceSquared(point, new Vector2((float)text.InsertPoint.X, (float)text.InsertPoint.Y)),
            MText mtext => Vector2.DistanceSquared(point, new Vector2((float)mtext.InsertPoint.X, (float)mtext.InsertPoint.Y)),
            _ => float.MaxValue
        };
    }

    private static bool IsFilletChamferTarget(Entity entity)
    {
        return entity is Line ||
               entity is LwPolyline polyline && !polyline.IsClosed && polyline.Vertices.Count >= 2;
    }

    private static float DistanceSquaredToFilletChamferTarget(Vector2 point, Entity entity)
    {
        return entity switch
        {
            Line line => DistanceSquaredToLine(point, line),
            LwPolyline polyline when !polyline.IsClosed => DistanceSquaredToOpenPolyline(point, polyline),
            _ => float.MaxValue
        };
    }

    private static float DistanceSquaredToCircle(Vector2 point, Circle circle)
    {
        return DistanceSquaredToCircle(point, circle.Center, circle.Radius);
    }

    private static float DistanceSquaredToCircle(Vector2 point, XYZ centerPoint, double radiusValue)
    {
        var center = new Vector2((float)centerPoint.X, (float)centerPoint.Y);
        var radius = (float)Math.Abs(radiusValue);
        var distance = Math.Abs(Vector2.Distance(point, center) - radius);
        return distance * distance;
    }

    private static float DistanceSquaredToPolyline(Vector2 point, LwPolyline polyline)
    {
        var vertices = polyline.Vertices;
        if (vertices.Count == 0)
        {
            return float.MaxValue;
        }

        var best = float.MaxValue;
        for (var index = 0; index < vertices.Count; index++)
        {
            var vertex = vertices[index];
            var candidate = new Vector2((float)vertex.Location.X, (float)vertex.Location.Y);
            best = Math.Min(best, Vector2.DistanceSquared(point, candidate));
        }

        return best;
    }
}

public sealed class TextInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, Vector2> _pickedPoints = new();
    private readonly object _sync = new();

    public string CommandName => "TEXT";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !TryParsePoint(token.Value, out var insertPoint))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _pickedPoints[key] = insertPoint;
        }

        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"TEXT {insertPoint.X:0.###},{insertPoint.Y:0.###} 2.5 0 \"Text\"");
        var result = await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
        ResetPreview(session);
        return result;
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            if (!_pickedPoints.TryGetValue(key, out var anchor))
            {
                preview = default!;
                return false;
            }

            preview = new CadInteractiveCommandPreview(
                CommandName: CommandName,
                Prompt: fallbackPrompt,
                Status: "Place text",
                Hints:
                [
                    new CadToolVisualHint("PickPoint", anchor, null, "Text"),
                    new CadToolVisualHint("Prompt", anchor, null, "Default: height=2.5, value=\"Text\"")
                ]);
            return true;
        }
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _pickedPoints.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }

    private static bool TryParsePoint(string value, out Vector2 point)
    {
        point = default;
        var split = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Length < 2)
        {
            return false;
        }

        return float.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out point.X) &&
               float.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out point.Y);
    }
}

public sealed class MTextInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, Vector2> _pickedPoints = new();
    private readonly object _sync = new();

    public string CommandName => "MTEXT";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !TryParsePoint(token.Value, out var insertPoint))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _pickedPoints[key] = insertPoint;
        }

        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"MTEXT {insertPoint.X:0.###},{insertPoint.Y:0.###} 2.5 30 0 \"Text\"");
        var result = await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
        ResetPreview(session);
        return result;
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            if (!_pickedPoints.TryGetValue(key, out var anchor))
            {
                preview = default!;
                return false;
            }

            preview = new CadInteractiveCommandPreview(
                CommandName: CommandName,
                Prompt: fallbackPrompt,
                Status: "Place mtext",
                Hints:
                [
                    new CadToolVisualHint("PickPoint", anchor, null, "MText"),
                    new CadToolVisualHint("Prompt", anchor, null, "Default: h=2.5, w=30, value=\"Text\"")
                ]);
            return true;
        }
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _pickedPoints.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }

    private static bool TryParsePoint(string value, out Vector2 point)
    {
        point = default;
        var split = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Length < 2)
        {
            return false;
        }

        return float.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out point.X) &&
               float.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out point.Y);
    }
}

public sealed class CircleInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, Vector2> _centers = new();
    private readonly object _sync = new();

    public string CommandName => "CIRCLE";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        var sessionKey = ResolveSessionKey(session);
        Vector2? center = null;
        lock (_sync)
        {
            if (_centers.TryGetValue(sessionKey, out var value))
            {
                center = value;
            }
            else
            {
                _centers[sessionKey] = picked;
            }
        }

        if (center is null)
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit: false, cancellationToken)
                .ConfigureAwait(false);
        }

        var radius = Vector2.Distance(center.Value, picked);
        if (radius <= 1e-6f)
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail("Circle radius must be greater than zero."),
                State: runtime.State);
        }

        ResetPreview(session);
        return await runtime
            .SubmitTokenAsync(
                new CadPromptToken(CadPromptTokenType.Number, radius.ToString("0.###", CultureInfo.InvariantCulture)),
                session,
                commit: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        Vector2 center;
        lock (_sync)
        {
            if (!_centers.TryGetValue(key, out center))
            {
                preview = default!;
                return false;
            }
        }

        var radius = Vector2.Distance(center, cursorPoint);
        var status = string.Create(CultureInfo.InvariantCulture, $"Specify radius (R={radius:0.###})");
        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: status,
            Hints:
            [
                new CadToolVisualHint("PickPoint", center, null, "Center"),
                new CadToolVisualHint("HelperLine", center, cursorPoint, "Radius"),
                new CadToolVisualHint("PreviewCircle", center, cursorPoint, $"R={radius:0.###}")
            ]);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _centers.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }

    private static bool TryParsePoint(string value, out Vector2 point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var split = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Length < 2)
        {
            return false;
        }

        if (!float.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        point = new Vector2(x, y);
        return true;
    }
}

public sealed class ArcInteractiveCommandAdapter :
    ICadInteractiveCommandAdapter,
    ICadInteractiveCommandPreviewProvider
{
    private readonly Dictionary<Guid, List<Vector2>> _pickedPoints = new();
    private readonly object _sync = new();

    public string CommandName => "ARC";

    public async ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (token.Type != CadPromptTokenType.Coordinate ||
            !TryParsePoint(token.Value, out var picked))
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        var key = ResolveSessionKey(session);
        List<Vector2> points;
        lock (_sync)
        {
            if (!_pickedPoints.TryGetValue(key, out points!))
            {
                points = new List<Vector2>(3);
                _pickedPoints[key] = points;
            }

            points.Add(picked);
        }

        if (points.Count < 3)
        {
            return await runtime
                .SubmitTokenAsync(token, session, commit: false, cancellationToken)
                .ConfigureAwait(false);
        }

        var center = points[0];
        var start = points[1];
        var end = points[2];
        var radius = Vector2.Distance(center, start);
        if (radius <= 1e-6f)
        {
            return new CadPromptResolution(
                Handled: true,
                Result: CadCommandResult.Fail("Arc radius must be greater than zero."),
                State: runtime.State);
        }

        var startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X) * (180.0 / Math.PI);
        var endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X) * (180.0 / Math.PI);
        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"ARC {center.X:0.###},{center.Y:0.###} {radius:0.###} {startAngle:0.###} {endAngle:0.###}");

        ResetPreview(session);
        return await runtime
            .SubmitAsync(commandText, session, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview)
    {
        var key = ResolveSessionKey(session);
        List<Vector2>? points;
        lock (_sync)
        {
            _pickedPoints.TryGetValue(key, out points);
            if (points is null || points.Count == 0)
            {
                preview = default!;
                return false;
            }
        }

        var hints = new List<CadToolVisualHint>(4)
        {
            new("PickPoint", points[0], null, "Center")
        };

        string status;
        if (points.Count > 1)
        {
            var center = points[0];
            var start = points[1];
            var radius = Vector2.Distance(center, start);
            var sweepDegrees = ResolveCounterClockwiseSweepDegrees(center, start, cursorPoint);
            status = string.Create(CultureInfo.InvariantCulture, $"Specify arc end point (R={radius:0.###}, Sweep={sweepDegrees:0.#} deg)");
            hints.Add(new("PickPoint", start, null, "Start"));
            hints.Add(new("HelperLine", center, start, "Start radius"));
            hints.Add(new("HelperLine", center, cursorPoint, "End radius"));
            hints.Add(new(
                Kind: "PreviewArc",
                Anchor: center,
                SecondaryAnchor: start,
                Text: string.Create(CultureInfo.InvariantCulture, $"Sweep {sweepDegrees:0.#} deg"),
                TertiaryAnchor: cursorPoint));
        }
        else
        {
            var center = points[0];
            var radius = Vector2.Distance(center, cursorPoint);
            status = string.Create(CultureInfo.InvariantCulture, $"Specify arc start point (R={radius:0.###})");
            hints.Add(new("HelperLine", center, cursorPoint, "Start radius"));
        }

        preview = new CadInteractiveCommandPreview(
            CommandName: CommandName,
            Prompt: fallbackPrompt,
            Status: status,
            Hints: hints);
        return true;
    }

    public void ResetPreview(ICadEditorSession? session)
    {
        var key = ResolveSessionKey(session);
        lock (_sync)
        {
            _pickedPoints.Remove(key);
        }
    }

    private static Guid ResolveSessionKey(ICadEditorSession? session)
    {
        return session?.SessionId.Value ?? Guid.Empty;
    }

    private static bool TryParsePoint(string value, out Vector2 point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var split = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Length < 2)
        {
            return false;
        }

        if (!float.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        point = new Vector2(x, y);
        return true;
    }

    private static float ResolveCounterClockwiseSweepDegrees(Vector2 center, Vector2 start, Vector2 endCandidate)
    {
        var startDirection = start - center;
        var endDirection = endCandidate - center;
        if (startDirection.LengthSquared() <= float.Epsilon ||
            endDirection.LengthSquared() <= float.Epsilon)
        {
            return 0f;
        }

        var startAngle = MathF.Atan2(startDirection.Y, startDirection.X);
        var endAngle = MathF.Atan2(endDirection.Y, endDirection.X);
        var sweep = endAngle - startAngle;
        while (sweep < 0f)
        {
            sweep += MathF.PI * 2f;
        }

        return sweep * (180f / MathF.PI);
    }
}
