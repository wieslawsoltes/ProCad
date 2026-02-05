using System;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public static class ViewportRenderUtils
{
    private const double CenterTolerance = 1e-3;
    private const double SizeTolerance = 1e-3;
    private const double AngleTolerance = 1e-6;

    public static bool IsPaperViewport(Viewport viewport)
    {
        if (viewport is null)
        {
            return false;
        }

        if (viewport.Width <= 0.0 || viewport.Height <= 0.0 || viewport.ViewHeight <= 0.0)
        {
            return false;
        }

        var center = viewport.Center;
        var viewCenter = viewport.ViewCenter;
        if (!NearlyEqual(center.X, viewCenter.X, CenterTolerance) ||
            !NearlyEqual(center.Y, viewCenter.Y, CenterTolerance))
        {
            return false;
        }

        if (!NearlyEqual(viewport.ViewHeight, viewport.Height, SizeTolerance))
        {
            return false;
        }

        if (viewport.ViewWidth > 0.0 && !NearlyEqual(viewport.ViewWidth, viewport.Width, SizeTolerance))
        {
            return false;
        }

        if (!IsAxisAligned(viewport.ViewDirection))
        {
            return false;
        }

        if (Math.Abs(viewport.TwistAngle) > AngleTolerance)
        {
            return false;
        }

        var scaleFactor = viewport.ScaleFactor;
        if (scaleFactor > 0.0 && !NearlyEqual(scaleFactor, 1.0, 1e-3))
        {
            return false;
        }

        return true;
    }

    private static bool IsAxisAligned(XYZ direction)
    {
        return Math.Abs(direction.X) <= CenterTolerance &&
               Math.Abs(direction.Y) <= CenterTolerance &&
               NearlyEqual(direction.Z, 1.0, CenterTolerance);
    }

    private static bool NearlyEqual(double left, double right, double tolerance)
    {
        return Math.Abs(left - right) <= tolerance;
    }
}
