using System;
using ACadSharp;

namespace ACadInspector.Rendering;

internal static class RenderStyleUtils
{
    public static float ResolveLineWeight(LineWeightType lineWeight, CadRenderSceneSettings settings)
    {
        if (!settings.DisplayLineWeight)
        {
            return 0f;
        }

        var weightValue = (short)lineWeight;
        float lineWeightMm;
        if (weightValue < 0)
        {
            lineWeightMm = settings.DefaultLineWeightMm;
        }
        else if (weightValue == 0)
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
