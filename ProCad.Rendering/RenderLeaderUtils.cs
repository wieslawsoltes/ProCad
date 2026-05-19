using System;
using ACadSharp;

namespace ProCad.Rendering;

internal static class RenderLeaderUtils
{
    public static RenderColor ResolveColor(Color color, RenderColor fallback, CadRenderSceneSettings settings)
    {
        return RenderStyleUtils.ResolveColorOrFallback(color, settings, fallback);
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
