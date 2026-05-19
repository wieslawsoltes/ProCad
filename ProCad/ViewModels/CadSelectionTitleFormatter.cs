using ACadSharp;

namespace ProCad.ViewModels;

internal static class CadSelectionTitleFormatter
{
    public static string BuildTitle(object selected)
    {
        var typeName = selected.GetType().Name;
        if (selected is INamedCadObject named)
        {
            return $"{typeName} - {named.Name}";
        }

        if (selected is IHandledCadObject handled)
        {
            return $"{typeName} - {handled.Handle:X}";
        }

        return typeName;
    }
}
