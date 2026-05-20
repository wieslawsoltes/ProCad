using System.Collections.Generic;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

public interface IRenderGeometrySampler
{
    IReadOnlyList<XYZ> SampleCircle(Circle circle, int precision);
    IReadOnlyList<XYZ> SampleArc(Arc arc, int precision);
    IReadOnlyList<XYZ> SampleEllipse(Ellipse ellipse, int precision);
    IReadOnlyList<XYZ> SampleSpline(Spline spline, int precision);
    IReadOnlyList<XYZ> SamplePolyline(IPolyline polyline, int precision);
}
