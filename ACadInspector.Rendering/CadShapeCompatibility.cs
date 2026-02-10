using System.Runtime.CompilerServices;
using ACadSharp.Entities;

namespace ACadInspector.Rendering;

internal static class CadShapeCompatibility
{
    public static bool TryGetShapeNumber(Shape shape, out short shapeNumber)
    {
        shapeNumber = 0;
        if (shape is null)
        {
            return false;
        }

        try
        {
            var shapeIndex = GetShapeIndex(shape);
            if (shapeIndex == 0 || shapeIndex > short.MaxValue)
            {
                return false;
            }

            shapeNumber = (short)shapeIndex;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void SetShapeNumberForTests(Shape shape, short shapeNumber)
    {
        if (shape is null || shapeNumber <= 0)
        {
            return;
        }

        SetShapeIndex(shape, (ushort)shapeNumber);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ShapeIndex")]
    private static extern ushort GetShapeIndex(Shape shape);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_ShapeIndex")]
    private static extern void SetShapeIndex(Shape shape, ushort index);
}
