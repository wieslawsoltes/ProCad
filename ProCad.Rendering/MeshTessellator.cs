using System;
using System.Collections.Generic;
using CSMath;

namespace ProCad.Rendering;

internal static class MeshTessellator
{
    internal readonly struct Triangle
    {
        public XYZ A { get; }
        public XYZ B { get; }
        public XYZ C { get; }

        public Triangle(XYZ a, XYZ b, XYZ c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    public static List<Triangle> Tessellate(IReadOnlyList<XYZ> vertices, int subdivisionLevel)
    {
        var triangles = new List<Triangle>(Math.Max(vertices.Count - 2, 0));
        if (vertices.Count < 3)
        {
            return triangles;
        }

        var root = vertices[0];
        for (var i = 1; i < vertices.Count - 1; i++)
        {
            triangles.Add(new Triangle(root, vertices[i], vertices[i + 1]));
        }

        if (subdivisionLevel <= 0)
        {
            return triangles;
        }

        var current = triangles;
        for (var level = 0; level < subdivisionLevel; level++)
        {
            var next = new List<Triangle>(current.Count * 4);
            foreach (var triangle in current)
            {
                var ab = Midpoint(triangle.A, triangle.B);
                var bc = Midpoint(triangle.B, triangle.C);
                var ca = Midpoint(triangle.C, triangle.A);

                next.Add(new Triangle(triangle.A, ab, ca));
                next.Add(new Triangle(ab, triangle.B, bc));
                next.Add(new Triangle(ca, bc, triangle.C));
                next.Add(new Triangle(ab, bc, ca));
            }

            current = next;
        }

        return current;
    }

    private static XYZ Midpoint(XYZ left, XYZ right)
    {
        return new XYZ((left.X + right.X) * 0.5, (left.Y + right.Y) * 0.5, (left.Z + right.Z) * 0.5);
    }
}
