using System;
using System.Collections.Generic;
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
    ArmSelectionMode,
    CycleSelection
}

public enum CadSelectionShortcutMode
{
    Lasso = 0,
    Fence,
    Polygon
}

public enum CadShortcutProfile
{
    AutoCadLike = 0,
    Minimal
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
    bool TransparentWhenCommandActive = true,
    int Priority = 0);

public static class CadShortcutBindingCatalog
{
    public static IReadOnlyList<CadShortcutBinding> Create(CadShortcutProfile profile)
    {
        return profile switch
        {
            CadShortcutProfile.Minimal => CreateMinimal(),
            _ => CreateAutoCadLike()
        };
    }

    private static IReadOnlyList<CadShortcutBinding> CreateAutoCadLike()
    {
        static CadShortcutBinding Command(
            string key,
            CadInteractionModifiers modifiers,
            string commandName,
            CadShortcutScope scope = CadShortcutScope.Always,
            bool transparentWhenCommandActive = true,
            int priority = 0)
        {
            return new CadShortcutBinding(
                Gesture: new CadShortcutGesture(key, modifiers),
                Action: CadShortcutActionKind.Command,
                CommandName: commandName,
                Scope: scope,
                TransparentWhenCommandActive: transparentWhenCommandActive,
                Priority: priority);
        }

        static CadShortcutBinding ArmSelection(string key, CadSelectionShortcutMode mode)
        {
            return new CadShortcutBinding(
                Gesture: new CadShortcutGesture(key, CadInteractionModifiers.Alt),
                Action: CadShortcutActionKind.ArmSelectionMode,
                SelectionMode: mode,
                Scope: CadShortcutScope.CommandInactiveOnly,
                TransparentWhenCommandActive: false,
                Priority: 20);
        }

        var inactive = CadShortcutScope.CommandInactiveOnly;
        var drawModifiers = CadInteractionModifiers.Control | CadInteractionModifiers.Shift;
        var modifyModifiers = CadInteractionModifiers.Control | CadInteractionModifiers.Alt;
        var advancedModifiers = CadInteractionModifiers.Control | CadInteractionModifiers.Shift | CadInteractionModifiers.Alt;

        return
        [
            Command("Delete", CadInteractionModifiers.Shift, "CUT"),
            Command("Insert", CadInteractionModifiers.Shift, "PASTECLIP"),
            Command("Insert", CadInteractionModifiers.Control, "COPYCLIP"),
            Command("Z", CadInteractionModifiers.Control, "UNDO"),
            Command("Z", CadInteractionModifiers.Control | CadInteractionModifiers.Shift, "REDO"),
            Command("Y", CadInteractionModifiers.Control, "REDO"),
            Command("C", CadInteractionModifiers.Control, "COPYCLIP"),
            Command("X", CadInteractionModifiers.Control, "CUT"),
            Command("V", CadInteractionModifiers.Control, "PASTECLIP"),
            Command("V", CadInteractionModifiers.Control | CadInteractionModifiers.Shift, "PASTEORIG"),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("A", CadInteractionModifiers.Control),
                Action: CadShortcutActionKind.SelectAll,
                Scope: CadShortcutScope.Always,
                TransparentWhenCommandActive: false,
                Priority: 40),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("Space", CadInteractionModifiers.Shift),
                Action: CadShortcutActionKind.CycleSelection,
                Scope: CadShortcutScope.CommandInactiveOnly,
                TransparentWhenCommandActive: false,
                Priority: 80),
            Command("Delete", CadInteractionModifiers.None, "ERASE", inactive, transparentWhenCommandActive: false),
            ArmSelection("L", CadSelectionShortcutMode.Lasso),
            ArmSelection("F", CadSelectionShortcutMode.Fence),
            ArmSelection("P", CadSelectionShortcutMode.Polygon),

            // Draw/annotation command launch matrix.
            Command("F1", drawModifiers, "LINE", inactive, transparentWhenCommandActive: false),
            Command("F2", drawModifiers, "PLINE", inactive, transparentWhenCommandActive: false),
            Command("F3", drawModifiers, "CIRCLE", inactive, transparentWhenCommandActive: false),
            Command("F4", drawModifiers, "ARC", inactive, transparentWhenCommandActive: false),
            Command("F5", drawModifiers, "RECTANG", inactive, transparentWhenCommandActive: false),
            Command("F6", drawModifiers, "POLYGON", inactive, transparentWhenCommandActive: false),
            Command("F7", drawModifiers, "TEXT", inactive, transparentWhenCommandActive: false),
            Command("F8", drawModifiers, "MTEXT", inactive, transparentWhenCommandActive: false),
            Command("F9", drawModifiers, "INSERT", inactive, transparentWhenCommandActive: false),
            Command("D", drawModifiers, "DIMLINEAR", inactive, transparentWhenCommandActive: false),
            Command("G", drawModifiers, "LEADER", inactive, transparentWhenCommandActive: false),
            Command("M", modifyModifiers, "MLEADER", inactive, transparentWhenCommandActive: false),

            // Modify command launch matrix.
            Command("F1", modifyModifiers, "MOVE", inactive, transparentWhenCommandActive: false),
            Command("F2", modifyModifiers, "COPY", inactive, transparentWhenCommandActive: false),
            Command("F3", modifyModifiers, "ROTATE", inactive, transparentWhenCommandActive: false),
            Command("F4", modifyModifiers, "SCALE", inactive, transparentWhenCommandActive: false),
            Command("F5", modifyModifiers, "MIRROR", inactive, transparentWhenCommandActive: false),
            Command("F6", modifyModifiers, "OFFSET", inactive, transparentWhenCommandActive: false),
            Command("F7", modifyModifiers, "TRIM", inactive, transparentWhenCommandActive: false),
            Command("F8", modifyModifiers, "EXTEND", inactive, transparentWhenCommandActive: false),
            Command("F9", modifyModifiers, "BREAK", inactive, transparentWhenCommandActive: false),
            Command("F10", modifyModifiers, "JOIN", inactive, transparentWhenCommandActive: false),
            Command("F11", modifyModifiers, "FILLET", inactive, transparentWhenCommandActive: false),
            Command("F12", modifyModifiers, "CHAMFER", inactive, transparentWhenCommandActive: false),
            Command("A", drawModifiers, "ARRAY", inactive, transparentWhenCommandActive: false),
            Command("I", drawModifiers, "ALIGN", inactive, transparentWhenCommandActive: false),
            Command("H", drawModifiers, "MATCHPROP", inactive, transparentWhenCommandActive: false),
            Command("X", drawModifiers, "EXPLODE", inactive, transparentWhenCommandActive: false),
            Command("K", drawModifiers, "STRETCH", inactive, transparentWhenCommandActive: false),

            // Advanced no-conflict matrix for power users.
            Command("M", advancedModifiers, "MOVE", inactive, transparentWhenCommandActive: false, priority: 10),
            Command("O", advancedModifiers, "OFFSET", inactive, transparentWhenCommandActive: false, priority: 10),
            Command("R", advancedModifiers, "ROTATE", inactive, transparentWhenCommandActive: false, priority: 10),
            Command("S", advancedModifiers, "SCALE", inactive, transparentWhenCommandActive: false, priority: 10),
            Command("T", advancedModifiers, "TRIM", inactive, transparentWhenCommandActive: false, priority: 10),
            Command("E", advancedModifiers, "EXTEND", inactive, transparentWhenCommandActive: false, priority: 10),
            Command("J", advancedModifiers, "JOIN", inactive, transparentWhenCommandActive: false, priority: 10),
            Command("F", advancedModifiers, "FILLET", inactive, transparentWhenCommandActive: false, priority: 10)
        ];
    }

    private static IReadOnlyList<CadShortcutBinding> CreateMinimal()
    {
        var inactive = CadShortcutScope.CommandInactiveOnly;
        return
        [
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("A", CadInteractionModifiers.Control),
                Action: CadShortcutActionKind.SelectAll,
                Scope: CadShortcutScope.Always,
                TransparentWhenCommandActive: false,
                Priority: 20),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("Space", CadInteractionModifiers.Shift),
                Action: CadShortcutActionKind.CycleSelection,
                Scope: CadShortcutScope.CommandInactiveOnly,
                TransparentWhenCommandActive: false,
                Priority: 40),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("Delete", CadInteractionModifiers.None),
                Action: CadShortcutActionKind.Command,
                CommandName: "ERASE",
                Scope: inactive,
                TransparentWhenCommandActive: false),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("Z", CadInteractionModifiers.Control),
                Action: CadShortcutActionKind.Command,
                CommandName: "UNDO"),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("Y", CadInteractionModifiers.Control),
                Action: CadShortcutActionKind.Command,
                CommandName: "REDO"),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("C", CadInteractionModifiers.Control),
                Action: CadShortcutActionKind.Command,
                CommandName: "COPYCLIP"),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("X", CadInteractionModifiers.Control),
                Action: CadShortcutActionKind.Command,
                CommandName: "CUT"),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("V", CadInteractionModifiers.Control),
                Action: CadShortcutActionKind.Command,
                CommandName: "PASTECLIP"),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("V", CadInteractionModifiers.Control | CadInteractionModifiers.Shift),
                Action: CadShortcutActionKind.Command,
                CommandName: "PASTEORIG"),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("L", CadInteractionModifiers.Alt),
                Action: CadShortcutActionKind.ArmSelectionMode,
                SelectionMode: CadSelectionShortcutMode.Lasso,
                Scope: inactive,
                TransparentWhenCommandActive: false),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("F", CadInteractionModifiers.Alt),
                Action: CadShortcutActionKind.ArmSelectionMode,
                SelectionMode: CadSelectionShortcutMode.Fence,
                Scope: inactive,
                TransparentWhenCommandActive: false),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("P", CadInteractionModifiers.Alt),
                Action: CadShortcutActionKind.ArmSelectionMode,
                SelectionMode: CadSelectionShortcutMode.Polygon,
                Scope: inactive,
                TransparentWhenCommandActive: false)
        ];
    }
}
