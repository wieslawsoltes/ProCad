using System;
using System.Collections.Generic;
using System.Numerics;
using CSMath;

namespace ACadInspector.Rendering;

internal static class RenderPolygonTriangulator
{
    private const double PlanarTolerance = 1e-5;
    private const float CollinearTolerance = 1e-5f;
    private const float AreaTolerance = 1e-5f;

    public static bool TryTriangulate(IReadOnlyList<XYZ> points, out List<MeshTessellator.Triangle> triangles)
    {
        triangles = new List<MeshTessellator.Triangle>();
        if (points is null || points.Count < 3)
        {
            return false;
        }

        var cleaned = RemoveDuplicatePoints(points);
        if (cleaned.Count < 3)
        {
            return false;
        }

        if (!TryBuildPlane(cleaned, out var origin, out var normal))
        {
            return false;
        }

        if (!IsPlanar(cleaned, origin, normal))
        {
            return false;
        }

        var axis = Math.Abs(normal.Z) < 0.9 ? XYZ.AxisZ : XYZ.AxisY;
        var u = XYZ.Cross(axis, normal).Normalize();
        if (u.IsZero())
        {
            axis = XYZ.AxisX;
            u = XYZ.Cross(axis, normal).Normalize();
        }
        var v = XYZ.Cross(normal, u).Normalize();

        var projected = new List<Vector2>(cleaned.Count);
        foreach (var point in cleaned)
        {
            var vector = point.Subtract(origin);
            projected.Add(new Vector2((float)vector.Dot(u), (float)vector.Dot(v)));
        }

        RemoveCollinear(cleaned, projected);
        if (projected.Count < 3)
        {
            return false;
        }

        var area = SignedArea(projected);
        if (MathF.Abs(area) <= AreaTolerance)
        {
            return false;
        }

        if (area < 0f)
        {
            projected.Reverse();
            cleaned.Reverse();
        }

        var indices = new List<int>(projected.Count);
        for (var i = 0; i < projected.Count; i++)
        {
            indices.Add(i);
        }

        var guard = 0;
        var maxIterations = indices.Count * indices.Count;
        while (indices.Count >= 3 && guard++ < maxIterations)
        {
            var earFound = false;
            for (var i = 0; i < indices.Count; i++)
            {
                var prevIndex = indices[(i - 1 + indices.Count) % indices.Count];
                var currIndex = indices[i];
                var nextIndex = indices[(i + 1) % indices.Count];

                if (!IsConvex(projected[prevIndex], projected[currIndex], projected[nextIndex]))
                {
                    continue;
                }

                if (ContainsPoint(projected, indices, prevIndex, currIndex, nextIndex))
                {
                    continue;
                }

                triangles.Add(new MeshTessellator.Triangle(
                    cleaned[prevIndex],
                    cleaned[currIndex],
                    cleaned[nextIndex]));
                indices.RemoveAt(i);
                earFound = true;
                break;
            }

            if (!earFound)
            {
                break;
            }
        }

        return triangles.Count > 0;
    }

    public static bool TryTriangulateWithHoles(
        IReadOnlyList<IReadOnlyList<XYZ>> loops,
        out List<MeshTessellator.Triangle> triangles)
    {
        triangles = new List<MeshTessellator.Triangle>();
        if (loops is null || loops.Count == 0)
        {
            return false;
        }

        if (loops.Count == 1)
        {
            return TryTriangulate(loops[0], out triangles);
        }

        var allPoints = new List<XYZ>();
        foreach (var loop in loops)
        {
            allPoints.AddRange(loop);
        }

        if (!TryBuildPlane(allPoints, out var origin, out var normal))
        {
            return false;
        }

        if (!IsPlanar(allPoints, origin, normal))
        {
            return false;
        }

        var axis = Math.Abs(normal.Z) < 0.9 ? XYZ.AxisZ : XYZ.AxisY;
        var u = XYZ.Cross(axis, normal).Normalize();
        if (u.IsZero())
        {
            axis = XYZ.AxisX;
            u = XYZ.Cross(axis, normal).Normalize();
        }

        var v = XYZ.Cross(normal, u).Normalize();
        var projected = new List<List<Vector2>>();
        var cleaned3d = new List<List<XYZ>>();
        foreach (var loop in loops)
        {
            var cleanedLoop = RemoveDuplicatePoints(loop);
            if (cleanedLoop.Count < 3)
            {
                continue;
            }

            var list = new List<Vector2>(cleanedLoop.Count);
            foreach (var point in cleanedLoop)
            {
                var vector = point.Subtract(origin);
                list.Add(new Vector2((float)vector.Dot(u), (float)vector.Dot(v)));
            }

            RemoveCollinear(cleanedLoop, list);
            if (list.Count >= 3)
            {
                projected.Add(list);
                cleaned3d.Add(cleanedLoop);
            }
        }

        if (projected.Count == 0)
        {
            return false;
        }

        var outerIndex = 0;
        var maxArea = 0f;
        for (var i = 0; i < projected.Count; i++)
        {
            var area = MathF.Abs(SignedArea(projected[i]));
            if (area > maxArea)
            {
                maxArea = area;
                outerIndex = i;
            }
        }

        var outer2d = projected[outerIndex];
        var outer3d = cleaned3d[outerIndex];

        var outerNode = BuildLinkedList(outer2d, outer3d, clockwise: true);
        if (outerNode is null)
        {
            return false;
        }

        if (projected.Count > 1)
        {
            outerNode = EliminateHoles(projected, cleaned3d, outerIndex, outerNode);
            if (outerNode is null)
            {
                return false;
            }
        }

        EarcutLinked(outerNode, triangles);
        return triangles.Count > 0;
    }

    private static List<XYZ> RemoveDuplicatePoints(IReadOnlyList<XYZ> points)
    {
        var cleaned = new List<XYZ>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (cleaned.Count == 0 || !IsSame(point, cleaned[^1]))
            {
                cleaned.Add(point);
            }
        }

        if (cleaned.Count > 1 && IsSame(cleaned[0], cleaned[^1]))
        {
            cleaned.RemoveAt(cleaned.Count - 1);
        }

        return cleaned;
    }

    private static bool TryBuildPlane(IReadOnlyList<XYZ> points, out XYZ origin, out XYZ normal)
    {
        origin = points[0];
        normal = XYZ.Zero;

        for (var i = 0; i < points.Count - 2; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var c = points[i + 2];
            var ab = b.Subtract(a);
            var ac = c.Subtract(a);
            var cross = XYZ.Cross(ab, ac);
            if (cross.IsZero())
            {
                continue;
            }

            if (cross.GetLengthSquared() <= PlanarTolerance * PlanarTolerance)
            {
                continue;
            }

            origin = a;
            normal = cross.Normalize();
            return true;
        }

        return false;
    }

    private static bool IsPlanar(IReadOnlyList<XYZ> points, XYZ origin, XYZ normal)
    {
        for (var i = 0; i < points.Count; i++)
        {
            var vector = points[i].Subtract(origin);
            var distance = Math.Abs(vector.Dot(normal));
            if (distance > PlanarTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static void RemoveCollinear(List<XYZ> points3d, List<Vector2> points2d)
    {
        if (points2d.Count < 3)
        {
            return;
        }

        var i = 0;
        while (i < points2d.Count && points2d.Count >= 3)
        {
            var prev = points2d[(i - 1 + points2d.Count) % points2d.Count];
            var curr = points2d[i];
            var next = points2d[(i + 1) % points2d.Count];
            var cross = Cross(curr - prev, next - curr);
            if (MathF.Abs(cross) <= CollinearTolerance)
            {
                points2d.RemoveAt(i);
                points3d.RemoveAt(i);
                if (i > 0)
                {
                    i--;
                }
                continue;
            }

            i++;
        }
    }

    private static void RemoveCollinear(List<Vector2> points2d)
    {
        if (points2d.Count < 3)
        {
            return;
        }

        var i = 0;
        while (i < points2d.Count && points2d.Count >= 3)
        {
            var prev = points2d[(i - 1 + points2d.Count) % points2d.Count];
            var curr = points2d[i];
            var next = points2d[(i + 1) % points2d.Count];
            var cross = Cross(curr - prev, next - curr);
            if (MathF.Abs(cross) <= CollinearTolerance)
            {
                points2d.RemoveAt(i);
                if (i > 0)
                {
                    i--;
                }
                continue;
            }

            i++;
        }
    }

    private static bool IsConvex(Vector2 prev, Vector2 curr, Vector2 next)
    {
        var cross = Cross(curr - prev, next - curr);
        return cross >= -CollinearTolerance;
    }

    private static bool ContainsPoint(
        IReadOnlyList<Vector2> points,
        IReadOnlyList<int> indices,
        int a,
        int b,
        int c)
    {
        var p0 = points[a];
        var p1 = points[b];
        var p2 = points[c];
        for (var i = 0; i < indices.Count; i++)
        {
            var index = indices[i];
            if (index == a || index == b || index == c)
            {
                continue;
            }

            if (PointInTriangle(points[index], p0, p1, p2))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var d1 = Sign(p, a, b);
        var d2 = Sign(p, b, c);
        var d3 = Sign(p, c, a);

        var hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
        var hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNeg && hasPos);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
    }

    private static float SignedArea(IReadOnlyList<Vector2> points)
    {
        var sum = 0f;
        for (var i = 0; i < points.Count; i++)
        {
            var current = points[i];
            var next = points[(i + 1) % points.Count];
            sum += current.X * next.Y - next.X * current.Y;
        }

        return sum * 0.5f;
    }

    private static EarcutNode? BuildLinkedList(
        IReadOnlyList<Vector2> points2d,
        IReadOnlyList<XYZ> points3d,
        bool clockwise)
    {
        if (points2d.Count < 3)
        {
            return null;
        }

        var isClockwise = SignedArea(points2d) < 0f;
        EarcutNode? last = null;
        if (clockwise == isClockwise)
        {
            for (var i = 0; i < points2d.Count; i++)
            {
                last = InsertNode(i, points2d[i], points3d[i], last);
            }
        }
        else
        {
            for (var i = points2d.Count - 1; i >= 0; i--)
            {
                last = InsertNode(i, points2d[i], points3d[i], last);
            }
        }

        return FilterPoints(last);
    }

    private static EarcutNode? EliminateHoles(
        IReadOnlyList<List<Vector2>> projected,
        IReadOnlyList<List<XYZ>> cleaned3d,
        int outerIndex,
        EarcutNode outerNode)
    {
        var holes = new List<EarcutNode>();
        for (var i = 0; i < projected.Count; i++)
        {
            if (i == outerIndex)
            {
                continue;
            }

            var hole = BuildLinkedList(projected[i], cleaned3d[i], clockwise: false);
            if (hole is null)
            {
                continue;
            }

            holes.Add(GetLeftmost(hole));
        }

        holes.Sort((left, right) => left.Point2.X.CompareTo(right.Point2.X));
        foreach (var hole in holes)
        {
            outerNode = EliminateHole(hole, outerNode) ?? outerNode;
        }

        return outerNode;
    }

    private static EarcutNode? EliminateHole(EarcutNode hole, EarcutNode outerNode)
    {
        var bridge = FindHoleBridge(hole, outerNode);
        if (bridge is null)
        {
            return outerNode;
        }

        var bridgeReverse = SplitPolygon(bridge, hole);
        FilterPoints(bridgeReverse, bridgeReverse.Next);
        return FilterPoints(bridge, bridge.Next);
    }

    private static EarcutNode? FindHoleBridge(EarcutNode hole, EarcutNode outerNode)
    {
        var p = outerNode;
        var hx = hole.Point2.X;
        var hy = hole.Point2.Y;
        var qx = double.NegativeInfinity;
        EarcutNode? m = null;

        do
        {
            var px = p.Point2.X;
            var py = p.Point2.Y;
            var q = p.Next!;
            var qy = q.Point2.Y;
            if (IsBetween(hy, py, qy) && Math.Abs(qy - py) > CollinearTolerance)
            {
                var x = px + (hy - py) * (q.Point2.X - px) / (qy - py);
                if (x <= hx && x > qx)
                {
                    qx = x;
                    m = px < q.Point2.X ? p : q;
                    if (Math.Abs(x - hx) <= CollinearTolerance)
                    {
                        return m;
                    }
                }
            }

            p = p.Next!;
        }
        while (p != outerNode);

        if (m is null)
        {
            return null;
        }

        var stop = m;
        var tanMin = double.PositiveInfinity;
        p = m;
        var mx = m.Point2.X;
        var my = m.Point2.Y;

        do
        {
            var px = p.Point2.X;
            var py = p.Point2.Y;
            if (hx >= px && px >= mx && Math.Abs(hx - px) > CollinearTolerance &&
                PointInTriangle(
                    p.Point2,
                    new Vector2((float)(hy < my ? hx : qx), (float)hy),
                    new Vector2((float)mx, (float)my),
                    new Vector2((float)(hy < my ? qx : hx), (float)hy)))
            {
                var tanCur = Math.Abs(hy - py) / (hx - px);
                if (LocallyInside(p, hole) &&
                    (tanCur < tanMin ||
                     (Math.Abs(tanCur - tanMin) <= CollinearTolerance &&
                      (px > m.Point2.X || SectorContainsSector(m, p)))))
                {
                    m = p;
                    tanMin = tanCur;
                }
            }

            p = p.Next!;
        }
        while (p != stop);

        return m;
    }

    private static bool LocallyInside(EarcutNode a, EarcutNode b)
    {
        return Area(a.Prev!, a, a.Next!) < 0
            ? Area(a, b, a.Next!) >= 0 && Area(a, a.Prev!, b) >= 0
            : Area(a, b, a.Prev!) < 0 || Area(a, a.Next!, b) < 0;
    }

    private static bool SectorContainsSector(EarcutNode m, EarcutNode p)
    {
        return Area(m.Prev!, m, p.Prev!) < 0 && Area(p.Next!, m, m.Next!) < 0;
    }

    private static EarcutNode SplitPolygon(EarcutNode a, EarcutNode b)
    {
        var a2 = new EarcutNode(a.Index, a.Point2, a.Point3);
        var b2 = new EarcutNode(b.Index, b.Point2, b.Point3);
        var an = a.Next!;
        var bp = b.Prev!;

        a.Next = b;
        b.Prev = a;

        a2.Next = an;
        an.Prev = a2;

        b2.Next = a2;
        a2.Prev = b2;

        bp.Next = b2;
        b2.Prev = bp;

        return b2;
    }

    private static EarcutNode GetLeftmost(EarcutNode start)
    {
        var node = start;
        var leftmost = start;
        do
        {
            if (node.Point2.X < leftmost.Point2.X ||
                (MathF.Abs(node.Point2.X - leftmost.Point2.X) <= CollinearTolerance &&
                 node.Point2.Y < leftmost.Point2.Y))
            {
                leftmost = node;
            }

            node = node.Next!;
        }
        while (node != start);

        return leftmost;
    }

    private static void EarcutLinked(EarcutNode? ear, List<MeshTessellator.Triangle> triangles)
    {
        if (ear is null)
        {
            return;
        }

        var stop = ear;
        var guard = 0;
        while (ear.Prev != ear.Next && guard++ < 10000)
        {
            var prev = ear.Prev!;
            var next = ear.Next!;
            if (IsEar(ear))
            {
                triangles.Add(new MeshTessellator.Triangle(prev.Point3, ear.Point3, next.Point3));
                RemoveNode(ear);
                ear = next.Next!;
                stop = next.Next!;
                continue;
            }

            ear = next;
            if (ear == stop)
            {
                break;
            }
        }
    }

    private static bool IsEar(EarcutNode ear)
    {
        var a = ear.Prev!;
        var b = ear;
        var c = ear.Next!;
        if (Area(a, b, c) >= 0)
        {
            return false;
        }

        var p = ear.Next!.Next!;
        while (p != ear.Prev)
        {
            if (PointInTriangle(p.Point2, a.Point2, b.Point2, c.Point2) &&
                Area(p.Prev!, p, p.Next!) >= 0)
            {
                return false;
            }

            p = p.Next!;
        }

        return true;
    }

    private static double Area(EarcutNode a, EarcutNode b, EarcutNode c)
    {
        return (b.Point2.X - a.Point2.X) * (c.Point2.Y - a.Point2.Y) -
               (b.Point2.Y - a.Point2.Y) * (c.Point2.X - a.Point2.X);
    }

    private static EarcutNode InsertNode(int index, Vector2 point2, XYZ point3, EarcutNode? last)
    {
        var node = new EarcutNode(index, point2, point3);
        if (last is null)
        {
            node.Prev = node;
            node.Next = node;
        }
        else
        {
            node.Next = last.Next;
            node.Prev = last;
            last.Next!.Prev = node;
            last.Next = node;
        }

        return node;
    }

    private static EarcutNode? FilterPoints(EarcutNode? start, EarcutNode? end = null)
    {
        if (start is null)
        {
            return null;
        }

        if (end is null)
        {
            end = start;
        }

        var node = start;
        var again = false;
        do
        {
            again = false;
            if (node is null || node.Next is null || node.Prev is null)
            {
                break;
            }

            if (NodesEqual(node, node.Next) || Math.Abs(Area(node.Prev, node, node.Next)) <= CollinearTolerance)
            {
                RemoveNode(node);
                node = end = node.Prev;
                if (node == node.Next)
                {
                    return null;
                }
                again = true;
            }
            else
            {
                node = node.Next;
            }
        }
        while (again || node != end);

        return end;
    }

    private static void RemoveNode(EarcutNode node)
    {
        node.Next!.Prev = node.Prev;
        node.Prev!.Next = node.Next;
    }

    private static bool NodesEqual(EarcutNode left, EarcutNode right)
    {
        return MathF.Abs(left.Point2.X - right.Point2.X) <= CollinearTolerance &&
               MathF.Abs(left.Point2.Y - right.Point2.Y) <= CollinearTolerance;
    }


    private static bool PointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
    {
        var inside = false;
        var count = polygon.Count;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            var intersect = ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                            (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y + float.Epsilon) + pi.X);
            if (intersect)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        var o1 = Orientation(p1, p2, q1);
        var o2 = Orientation(p1, p2, q2);
        var o3 = Orientation(q1, q2, p1);
        var o4 = Orientation(q1, q2, p2);

        if (o1 != o2 && o3 != o4)
        {
            return true;
        }

        if (o1 == 0 && OnSegment(p1, q1, p2))
        {
            return true;
        }

        if (o2 == 0 && OnSegment(p1, q2, p2))
        {
            return true;
        }

        if (o3 == 0 && OnSegment(q1, p1, q2))
        {
            return true;
        }

        return o4 == 0 && OnSegment(q1, p2, q2);
    }

    private static int Orientation(Vector2 a, Vector2 b, Vector2 c)
    {
        var value = (b.Y - a.Y) * (c.X - b.X) - (b.X - a.X) * (c.Y - b.Y);
        if (MathF.Abs(value) <= CollinearTolerance)
        {
            return 0;
        }

        return value > 0 ? 1 : 2;
    }

    private static bool OnSegment(Vector2 a, Vector2 b, Vector2 c)
    {
        return b.X <= MathF.Max(a.X, c.X) + CollinearTolerance &&
               b.X + CollinearTolerance >= MathF.Min(a.X, c.X) &&
               b.Y <= MathF.Max(a.Y, c.Y) + CollinearTolerance &&
               b.Y + CollinearTolerance >= MathF.Min(a.Y, c.Y);
    }

    private static bool IsBetween(float value, float a, float b)
    {
        return (value >= a && value <= b) || (value >= b && value <= a);
    }


    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.X * b.Y - a.Y * b.X;
    }

    private static bool IsSame(XYZ left, XYZ right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Abs(dx) <= PlanarTolerance &&
               Math.Abs(dy) <= PlanarTolerance &&
               Math.Abs(dz) <= PlanarTolerance;
    }

    private static bool IsSame(Vector2 left, Vector2 right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return MathF.Abs(dx) <= CollinearTolerance &&
               MathF.Abs(dy) <= CollinearTolerance;
    }

    private sealed class EarcutNode
    {
        public int Index { get; }
        public Vector2 Point2 { get; }
        public XYZ Point3 { get; }
        public EarcutNode? Prev { get; set; }
        public EarcutNode? Next { get; set; }

        public EarcutNode(int index, Vector2 point2, XYZ point3)
        {
            Index = index;
            Point2 = point2;
            Point3 = point3;
        }
    }
}
