using System;
using System.Buffers;
using CSMath;

namespace ProCad.Rendering;

internal static class RenderNurbsEvaluator
{
    public static XYZ EvaluateCurve(
        int degree,
        ReadOnlySpan<double> knots,
        ReadOnlySpan<XYZ> controlPoints,
        ReadOnlySpan<double> weights,
        double parameter)
    {
        if (controlPoints.Length == 0 || degree < 0 || knots.Length == 0)
        {
            return XYZ.Zero;
        }

        var count = controlPoints.Length;
        var n = count - 1;
        var clamped = ClampParameter(parameter, degree, knots);
        var span = FindSpan(n, degree, clamped, knots);
        var basis = ArrayPool<double>.Shared.Rent(degree + 1);

        try
        {
            BasisFunctions(span, clamped, degree, knots, basis.AsSpan(0, degree + 1));
            var weighted = weights.Length == count;
            var sumX = 0.0;
            var sumY = 0.0;
            var sumZ = 0.0;
            var sumW = 0.0;

            for (var i = 0; i <= degree; i++)
            {
                var index = span - degree + i;
                if ((uint)index >= (uint)count)
                {
                    continue;
                }

                var weight = weighted ? weights[index] : 1.0;
                var coeff = basis[i] * weight;
                var point = controlPoints[index];
                sumX += point.X * coeff;
                sumY += point.Y * coeff;
                sumZ += point.Z * coeff;
                sumW += coeff;
            }

            if (Math.Abs(sumW) <= double.Epsilon)
            {
                return new XYZ(sumX, sumY, sumZ);
            }

            return new XYZ(sumX / sumW, sumY / sumW, sumZ / sumW);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(basis);
        }
    }

    public static XYZ EvaluateSurface(
        int degreeU,
        int degreeV,
        ReadOnlySpan<double> knotsU,
        ReadOnlySpan<double> knotsV,
        ReadOnlySpan<XYZ> controlPoints,
        ReadOnlySpan<double> weights,
        int countU,
        int countV,
        double parameterU,
        double parameterV)
    {
        if (controlPoints.Length == 0 || countU <= 0 || countV <= 0)
        {
            return XYZ.Zero;
        }

        var nU = countU - 1;
        var nV = countV - 1;
        var u = ClampParameter(parameterU, degreeU, knotsU);
        var v = ClampParameter(parameterV, degreeV, knotsV);
        var spanU = FindSpan(nU, degreeU, u, knotsU);
        var spanV = FindSpan(nV, degreeV, v, knotsV);
        var basisU = ArrayPool<double>.Shared.Rent(degreeU + 1);
        var basisV = ArrayPool<double>.Shared.Rent(degreeV + 1);

        try
        {
            BasisFunctions(spanU, u, degreeU, knotsU, basisU.AsSpan(0, degreeU + 1));
            BasisFunctions(spanV, v, degreeV, knotsV, basisV.AsSpan(0, degreeV + 1));

            var weighted = weights.Length == controlPoints.Length;
            var sumX = 0.0;
            var sumY = 0.0;
            var sumZ = 0.0;
            var sumW = 0.0;

            for (var l = 0; l <= degreeV; l++)
            {
                var tempX = 0.0;
                var tempY = 0.0;
                var tempZ = 0.0;
                var tempW = 0.0;
                var vIndex = spanV - degreeV + l;
                if ((uint)vIndex >= (uint)countV)
                {
                    continue;
                }

                for (var k = 0; k <= degreeU; k++)
                {
                    var uIndex = spanU - degreeU + k;
                    if ((uint)uIndex >= (uint)countU)
                    {
                        continue;
                    }

                    var index = vIndex * countU + uIndex;
                    if ((uint)index >= (uint)controlPoints.Length)
                    {
                        continue;
                    }

                    var weight = weighted ? weights[index] : 1.0;
                    var coeff = basisU[k] * weight;
                    var point = controlPoints[index];
                    tempX += point.X * coeff;
                    tempY += point.Y * coeff;
                    tempZ += point.Z * coeff;
                    tempW += coeff;
                }

                var coeffV = basisV[l];
                sumX += coeffV * tempX;
                sumY += coeffV * tempY;
                sumZ += coeffV * tempZ;
                sumW += coeffV * tempW;
            }

            if (Math.Abs(sumW) <= double.Epsilon)
            {
                return new XYZ(sumX, sumY, sumZ);
            }

            return new XYZ(sumX / sumW, sumY / sumW, sumZ / sumW);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(basisU);
            ArrayPool<double>.Shared.Return(basisV);
        }
    }

    private static double ClampParameter(double parameter, int degree, ReadOnlySpan<double> knots)
    {
        if (knots.Length == 0 || degree < 0 || knots.Length <= degree)
        {
            return parameter;
        }

        var start = knots[degree];
        var endIndex = knots.Length - degree - 1;
        if (endIndex < 0 || endIndex >= knots.Length)
        {
            return parameter;
        }

        var end = knots[endIndex];
        if (parameter < start)
        {
            return start;
        }

        if (parameter > end)
        {
            return end;
        }

        return parameter;
    }

    private static int FindSpan(int n, int degree, double parameter, ReadOnlySpan<double> knots)
    {
        if (parameter >= knots[n + 1])
        {
            return n;
        }

        if (parameter <= knots[degree])
        {
            return degree;
        }

        var low = degree;
        var high = n + 1;
        var mid = (low + high) / 2;

        while (parameter < knots[mid] || parameter >= knots[mid + 1])
        {
            if (parameter < knots[mid])
            {
                high = mid;
            }
            else
            {
                low = mid;
            }

            mid = (low + high) / 2;
        }

        return mid;
    }

    private static void BasisFunctions(
        int span,
        double parameter,
        int degree,
        ReadOnlySpan<double> knots,
        Span<double> basis)
    {
        basis.Clear();
        basis[0] = 1.0;
        var left = ArrayPool<double>.Shared.Rent(degree + 1);
        var right = ArrayPool<double>.Shared.Rent(degree + 1);

        try
        {
            var leftSpan = left.AsSpan(0, degree + 1);
            var rightSpan = right.AsSpan(0, degree + 1);
            for (var j = 1; j <= degree; j++)
            {
                leftSpan[j] = parameter - knots[span + 1 - j];
                rightSpan[j] = knots[span + j] - parameter;
                var saved = 0.0;

                for (var r = 0; r < j; r++)
                {
                    var denom = rightSpan[r + 1] + leftSpan[j - r];
                    var temp = Math.Abs(denom) <= double.Epsilon ? 0.0 : basis[r] / denom;
                    basis[r] = saved + rightSpan[r + 1] * temp;
                    saved = leftSpan[j - r] * temp;
                }

                basis[j] = saved;
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(left);
            ArrayPool<double>.Shared.Return(right);
        }
    }
}
