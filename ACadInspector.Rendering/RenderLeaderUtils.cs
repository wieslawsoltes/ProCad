using System;
using ACadSharp;

namespace ACadInspector.Rendering;

internal static class RenderLeaderUtils
{
    public static RenderColor ResolveColor(Color color, RenderColor fallback)
    {
        if (color.IsByLayer || color.IsByBlock)
        {
            return fallback;
        }

        return new RenderColor(color.R, color.G, color.B, 255);
    }

    public static float ResolveLineWeight(LineWeightType weight, CadRenderSceneSettings settings, float fallback)
    {
        var weightValue = (short)weight;
        if (weightValue < 0)
        {
            return fallback;
        }

        float lineWeightMm;
        if (weightValue == 0)
        {
            lineWeightMm = settings.MinLineWeightMm;
        }
        else
        {
            lineWeightMm = Math.Max(settings.MinLineWeightMm, weightValue / 100f);
        }

        return lineWeightMm / settings.MillimetersPerUnit;
    }
}
