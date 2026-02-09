using ACadInspector.Editing.Interaction;
using ACadInspector.Services;
using Xunit;

namespace ACadInspector.Tests.Services;

public sealed class CadShortcutBindingCatalogTests
{
    [Fact]
    public void Create_AutoCadLike_IncludesFunctionKeyCommandMatrix()
    {
        var bindings = CadShortcutBindingCatalog.Create(CadShortcutProfile.AutoCadLike);

        Assert.Contains(
            bindings,
            static binding =>
                binding.Action == CadShortcutActionKind.Command &&
                string.Equals(binding.CommandName, "LINE", System.StringComparison.OrdinalIgnoreCase) &&
                binding.Gesture.Key == "F1" &&
                binding.Gesture.Modifiers == (CadInteractionModifiers.Control | CadInteractionModifiers.Shift));
    }

    [Fact]
    public void Create_Minimal_ExcludesFunctionKeyCommandMatrix()
    {
        var bindings = CadShortcutBindingCatalog.Create(CadShortcutProfile.Minimal);

        Assert.DoesNotContain(
            bindings,
            static binding =>
                binding.Action == CadShortcutActionKind.Command &&
                string.Equals(binding.CommandName, "LINE", System.StringComparison.OrdinalIgnoreCase) &&
                binding.Gesture.Key == "F1" &&
                binding.Gesture.Modifiers == (CadInteractionModifiers.Control | CadInteractionModifiers.Shift));
    }

    [Fact]
    public void Create_BothProfiles_IncludeSelectionCycleShortcut()
    {
        var autoCadBindings = CadShortcutBindingCatalog.Create(CadShortcutProfile.AutoCadLike);
        var minimalBindings = CadShortcutBindingCatalog.Create(CadShortcutProfile.Minimal);

        Assert.Contains(
            autoCadBindings,
            static binding =>
                binding.Action == CadShortcutActionKind.CycleSelection &&
                binding.Gesture.Key == "Space" &&
                binding.Gesture.Modifiers == CadInteractionModifiers.Shift);
        Assert.Contains(
            minimalBindings,
            static binding =>
                binding.Action == CadShortcutActionKind.CycleSelection &&
                binding.Gesture.Key == "Space" &&
                binding.Gesture.Modifiers == CadInteractionModifiers.Shift);
    }
}
