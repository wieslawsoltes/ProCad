using System;
using ACadInspector.Editing.Interaction;

namespace ACadInspector.Services;

public enum CadShortcutScope
{
    Always = 0,
    CommandInactiveOnly,
    CommandActiveOnly
}

public enum CadShortcutActionKind
{
    Command = 0,
    SelectAll,
    ArmSelectionMode
}

public enum CadSelectionShortcutMode
{
    Lasso = 0,
    Fence,
    Polygon
}

public readonly record struct CadShortcutGesture(
    string Key,
    CadInteractionModifiers Modifiers)
{
    public bool Matches(CadInteractionEvent interactionEvent)
    {
        if (interactionEvent.Modifiers != Modifiers)
        {
            return false;
        }

        return string.Equals(
            Key.Trim(),
            interactionEvent.Key?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record CadShortcutBinding(
    CadShortcutGesture Gesture,
    CadShortcutActionKind Action,
    string? CommandName = null,
    CadSelectionShortcutMode? SelectionMode = null,
    CadShortcutScope Scope = CadShortcutScope.Always,
    bool TransparentWhenCommandActive = true);
