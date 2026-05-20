using System.Diagnostics.CodeAnalysis;

namespace ProCad.Rendering;

/// <summary>
/// Resolves SHX/SHP linetype shape geometry for rendering.
/// </summary>
public interface IRenderShapeResolver
{
    /// <summary>
    /// Attempts to resolve a linetype shape by file name and shape number.
    /// </summary>
    /// <param name="shapeFile">Shape file name or path.</param>
    /// <param name="shapeNumber">Shape number inside the shape file.</param>
    /// <param name="settings">Render scene settings with support paths.</param>
    /// <param name="geometry">The resolved shape geometry.</param>
    /// <returns>true when the shape geometry is available.</returns>
    bool TryResolveShape(
        string? shapeFile,
        short shapeNumber,
        CadRenderSceneSettings settings,
        [NotNullWhen(true)] out RenderShapeGeometry? geometry);
}
