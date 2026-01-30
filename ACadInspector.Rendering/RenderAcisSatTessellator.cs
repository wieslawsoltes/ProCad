using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

internal static class RenderAcisSatTessellator
{
    private const double VertexTolerance = 1e-5;

    public static bool TryTessellate(ModelerGeometry geometry, out List<MeshTessellator.Triangle> triangles)
    {
        return TryTessellate(geometry, new CadRenderSceneSettings(), out triangles);
    }

    public static bool TryTessellate(
        ModelerGeometry geometry,
        CadRenderSceneSettings settings,
        out List<MeshTessellator.Triangle> triangles)
    {
        triangles = new List<MeshTessellator.Triangle>();
        if (geometry is null)
        {
            return false;
        }

        var data = geometry.ProprietaryData?.ToString();
        return TryTessellate(data, geometry.Point, settings, out triangles);
    }

    public static bool TryTessellate(string? data, XYZ offset, out List<MeshTessellator.Triangle> triangles)
    {
        return TryTessellate(data, offset, new CadRenderSceneSettings(), out triangles);
    }

    public static bool TryTessellate(
        string? data,
        XYZ offset,
        CadRenderSceneSettings settings,
        out List<MeshTessellator.Triangle> triangles)
    {
        triangles = new List<MeshTessellator.Triangle>();
        if (!RenderAcisSatParser.TryParse(data, out var document))
        {
            return false;
        }

        var recordsById = document.Records.ToDictionary(record => record.Id, record => record);
        var pointById = ExtractPoints(document.Records);
        if (pointById.Count == 0)
        {
            return false;
        }

        var vertexById = ExtractVertices(document.Records, recordsById, pointById, offset);
        if (vertexById.Count == 0)
        {
            return false;
        }

        var sampling = ResolveSampling(settings);
        var nurbsCurvesById = ExtractNurbsCurves(document, recordsById, pointById, vertexById);
        var curvesById = ExtractCurves(document, recordsById);
        var surfacesById = ExtractSurfaces(document, recordsById, pointById, vertexById);
        var edgeById = ExtractEdges(
            document.Records,
            recordsById,
            vertexById,
            pointById,
            curvesById,
            nurbsCurvesById,
            sampling);
        if (edgeById.Count == 0)
        {
            return false;
        }

        var coedgeById = ExtractCoedges(document.Records, recordsById, edgeById);
        var loopById = ExtractLoops(document.Records, recordsById, coedgeById);
        if (loopById.Count == 0)
        {
            return false;
        }

        var faces = ExtractFaces(document.Records, recordsById, loopById);
        foreach (var face in faces)
        {
            if (face.LoopIds.Count == 0)
            {
                continue;
            }

            if (TryAppendSurfaceTriangles(
                face,
                loopById,
                recordsById,
                document,
                surfacesById,
                nurbsCurvesById,
                pointById,
                vertexById,
                sampling,
                triangles))
            {
                continue;
            }

            var loops = new List<IReadOnlyList<XYZ>>();
            foreach (var loopId in face.LoopIds)
            {
                if (!loopById.TryGetValue(loopId, out var loop) || loop.Coedges.Count == 0)
                {
                    continue;
                }

                if (!TryBuildLoopVertices(loop.Coedges, edgeById, out var polygon))
                {
                    continue;
                }

                loops.Add(polygon);
            }

            if (loops.Count == 0)
            {
                continue;
            }

            if (loops.Count == 1)
            {
                if (RenderPolygonTriangulator.TryTriangulate(loops[0], out var faceTriangles))
                {
                    triangles.AddRange(faceTriangles);
                }
                continue;
            }

            if (RenderPolygonTriangulator.TryTriangulateWithHoles(loops, out var holeTriangles))
            {
                triangles.AddRange(holeTriangles);
                continue;
            }

            foreach (var loop in loops)
            {
                if (RenderPolygonTriangulator.TryTriangulate(loop, out var faceTriangles))
                {
                    triangles.AddRange(faceTriangles);
                }
            }
        }

        return triangles.Count > 0;
    }

    private static Dictionary<int, XYZ> ExtractPoints(IReadOnlyList<RenderAcisSatRecord> records)
    {
        var points = new Dictionary<int, XYZ>();
        foreach (var record in records)
        {
            if (!IsPointRecord(record.Type))
            {
                continue;
            }

            if (record.Numbers.Count < 3)
            {
                continue;
            }

            var count = record.Numbers.Count;
            var point = new XYZ(
                record.Numbers[count - 3],
                record.Numbers[count - 2],
                record.Numbers[count - 1]);
            points[record.Id] = point;
        }

        return points;
    }

    private static Dictionary<int, XYZ> ExtractVertices(
        IReadOnlyList<RenderAcisSatRecord> records,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        IReadOnlyDictionary<int, XYZ> points,
        XYZ offset)
    {
        var vertices = new Dictionary<int, XYZ>();
        foreach (var record in records)
        {
            if (!IsVertexRecord(record.Type))
            {
                continue;
            }

            var pointId = FindFirstReference(record, recordsById, IsPointRecord);
            if (pointId > 0 && points.TryGetValue(pointId, out var point))
            {
                vertices[record.Id] = ApplyOffset(point, offset);
                continue;
            }

            if (record.Numbers.Count >= 3)
            {
                var count = record.Numbers.Count;
                var direct = new XYZ(
                    record.Numbers[count - 3],
                    record.Numbers[count - 2],
                    record.Numbers[count - 1]);
                vertices[record.Id] = ApplyOffset(direct, offset);
            }
        }

        return vertices;
    }

    private static Dictionary<int, CurveDefinition> ExtractCurves(
        RenderAcisSatDocument document,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById)
    {
        var curves = new Dictionary<int, CurveDefinition>();
        foreach (var record in document.Records)
        {
            if (!IsCurveRecord(record.Type))
            {
                continue;
            }

            var pointReferences = CollectPointReferences(record, document, recordsById);
            curves[record.Id] = new CurveDefinition(record.Id, record.Type, record.Numbers, pointReferences);
        }

        return curves;
    }

    private static Dictionary<int, NurbsCurveDefinition> ExtractNurbsCurves(
        RenderAcisSatDocument document,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById)
    {
        var curves = new Dictionary<int, NurbsCurveDefinition>();
        foreach (var record in document.Records)
        {
            if (!IsCurveRecord(record.Type))
            {
                continue;
            }

            if (TryParseNurbsCurve(record, document, pointsById, verticesById, out var curve))
            {
                curves[record.Id] = curve;
            }
        }

        return curves;
    }

    private static Dictionary<int, SurfaceDefinition> ExtractSurfaces(
        RenderAcisSatDocument document,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById)
    {
        var surfaces = new Dictionary<int, SurfaceDefinition>();
        foreach (var record in document.Records)
        {
            if (!IsSurfaceRecord(record.Type))
            {
                continue;
            }

            if (TryParseNurbsSurface(record, document, pointsById, verticesById, out var nurbsSurface))
            {
                surfaces[record.Id] = SurfaceDefinition.FromNurbs(record.Id, nurbsSurface);
                continue;
            }

            if (TryParseConeSurface(record, out var coneSurface))
            {
                surfaces[record.Id] = coneSurface;
                continue;
            }

            if (TryParseCylinderSurface(record, out var cylinderSurface))
            {
                surfaces[record.Id] = cylinderSurface;
                continue;
            }

            if (TryParseTorusSurface(record, out var torusSurface))
            {
                surfaces[record.Id] = torusSurface;
                continue;
            }

            if (TryParsePlaneSurface(record, out var planeSurface))
            {
                surfaces[record.Id] = planeSurface;
                continue;
            }

            if (TryParseSphereSurface(record, out var sphereSurface))
            {
                surfaces[record.Id] = sphereSurface;
            }
        }

        return surfaces;
    }

    private static Dictionary<int, EdgeGeometry> ExtractEdges(
        IReadOnlyList<RenderAcisSatRecord> records,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        IReadOnlyDictionary<int, XYZ> vertices,
        IReadOnlyDictionary<int, XYZ> points,
        IReadOnlyDictionary<int, CurveDefinition> curves,
        IReadOnlyDictionary<int, NurbsCurveDefinition> nurbsCurves,
        CurveSamplingSettings sampling)
    {
        var edges = new Dictionary<int, EdgeGeometry>();
        foreach (var record in records)
        {
            if (!IsEdgeRecord(record.Type))
            {
                continue;
            }

            var vertexRefs = FindReferences(record, recordsById, IsVertexRecord);
            if (vertexRefs.Count < 2)
            {
                continue;
            }

            var startId = vertexRefs[0];
            var endId = vertexRefs[1];
            if (!vertices.TryGetValue(startId, out var start) || !vertices.TryGetValue(endId, out var end))
            {
                continue;
            }

            List<XYZ> pointsList;
            var curveId = FindFirstReference(record, recordsById, IsCurveRecord);
            if (curveId > 0 && curves.TryGetValue(curveId, out var curve) &&
                TryBuildCurvePoints(curve, start, end, points, vertices, nurbsCurves, sampling, out pointsList))
            {
                AlignCurvePoints(pointsList, start, end);
            }
            else
            {
                pointsList = new List<XYZ> { start, end };
            }

            edges[record.Id] = new EdgeGeometry(startId, endId, pointsList);
        }

        return edges;
    }

    private static Dictionary<int, CoedgeDefinition> ExtractCoedges(
        IReadOnlyList<RenderAcisSatRecord> records,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        IReadOnlyDictionary<int, EdgeGeometry> edges)
    {
        var coedges = new Dictionary<int, CoedgeDefinition>();
        foreach (var record in records)
        {
            if (!IsCoedgeRecord(record.Type))
            {
                continue;
            }

            var edgeId = FindFirstReference(record, recordsById, IsEdgeRecord);
            if (edgeId <= 0 || !edges.ContainsKey(edgeId))
            {
                continue;
            }

            var pcurveId = FindFirstReference(record, recordsById, IsPcurveRecord);
            var reversed = IsCoedgeReversed(record);
            coedges[record.Id] = new CoedgeDefinition(edgeId, pcurveId, reversed);
        }

        return coedges;
    }

    private static Dictionary<int, LoopDefinition> ExtractLoops(
        IReadOnlyList<RenderAcisSatRecord> records,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        IReadOnlyDictionary<int, CoedgeDefinition> coedges)
    {
        var loops = new Dictionary<int, LoopDefinition>();
        foreach (var record in records)
        {
            if (!IsLoopRecord(record.Type))
            {
                continue;
            }

            var references = FindReferences(record, recordsById, IsCoedgeRecord);
            if (references.Count == 0)
            {
                continue;
            }

            var coedgeList = new List<CoedgeDefinition>(references.Count);
            foreach (var coedgeId in references)
            {
                if (coedges.TryGetValue(coedgeId, out var coedge))
                {
                    coedgeList.Add(coedge);
                }
            }

            if (coedgeList.Count > 0)
            {
                loops[record.Id] = new LoopDefinition(coedgeList);
            }
        }

        return loops;
    }

    private static List<FaceDefinition> ExtractFaces(
        IReadOnlyList<RenderAcisSatRecord> records,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        IReadOnlyDictionary<int, LoopDefinition> loops)
    {
        var faces = new List<FaceDefinition>();
        foreach (var record in records)
        {
            if (!IsFaceRecord(record.Type))
            {
                continue;
            }

            var references = FindReferences(record, recordsById, IsLoopRecord);
            var loopIds = new List<int>();
            foreach (var loopId in references)
            {
                if (loops.ContainsKey(loopId))
                {
                    loopIds.Add(loopId);
                }
            }

            if (loopIds.Count > 0)
            {
                var surfaceId = FindFirstReference(record, recordsById, IsSurfaceRecord);
                faces.Add(new FaceDefinition(surfaceId, loopIds));
            }
        }

        return faces;
    }

    private static bool TryBuildLoopVertices(
        IReadOnlyList<CoedgeDefinition> coedges,
        IReadOnlyDictionary<int, EdgeGeometry> edges,
        out List<XYZ> polygon)
    {
        polygon = new List<XYZ>();
        if (coedges.Count == 0)
        {
            return false;
        }

        foreach (var coedge in coedges)
        {
            if (!edges.TryGetValue(coedge.EdgeId, out var edge))
            {
                return false;
            }

            if (edge.Points.Count < 2)
            {
                continue;
            }

            AppendEdgePoints(polygon, edge.Points, coedge.Reversed);
        }

        if (polygon.Count < 3)
        {
            return false;
        }

        if (!IsClose(polygon[0], polygon[^1]))
        {
            polygon.Add(polygon[0]);
        }

        return polygon.Count >= 3;
    }

    private static void AppendEdgePoints(List<XYZ> polygon, IReadOnlyList<XYZ> edgePoints, bool reverseEdge)
    {
        if (edgePoints.Count == 0)
        {
            return;
        }

        if (polygon.Count == 0)
        {
            if (reverseEdge)
            {
                for (var i = edgePoints.Count - 1; i >= 0; i--)
                {
                    polygon.Add(edgePoints[i]);
                }
            }
            else
            {
                polygon.AddRange(edgePoints);
            }
            return;
        }

        var last = polygon[^1];
        var start = reverseEdge ? edgePoints[^1] : edgePoints[0];
        var end = reverseEdge ? edgePoints[0] : edgePoints[^1];
        var flip = false;
        if (IsClose(last, start))
        {
            flip = false;
        }
        else if (IsClose(last, end))
        {
            flip = true;
        }
        else
        {
            var startDist = DistanceSquared(last, start);
            var endDist = DistanceSquared(last, end);
            flip = endDist < startDist;
        }

        var effectiveReverse = reverseEdge ^ flip;
        if (effectiveReverse)
        {
            for (var i = edgePoints.Count - 2; i >= 0; i--)
            {
                polygon.Add(edgePoints[i]);
            }
        }
        else
        {
            for (var i = 1; i < edgePoints.Count; i++)
            {
                polygon.Add(edgePoints[i]);
            }
        }
    }

    private static bool TryAppendSurfaceTriangles(
        FaceDefinition face,
        IReadOnlyDictionary<int, LoopDefinition> loops,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        RenderAcisSatDocument document,
        IReadOnlyDictionary<int, SurfaceDefinition> surfaces,
        IReadOnlyDictionary<int, NurbsCurveDefinition> nurbsCurves,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById,
        CurveSamplingSettings sampling,
        List<MeshTessellator.Triangle> triangles)
    {
        if (face.SurfaceId <= 0 || !surfaces.TryGetValue(face.SurfaceId, out var surface))
        {
            return false;
        }

        var paramLoops = new List<IReadOnlyList<Vector2>>();
        foreach (var loopId in face.LoopIds)
        {
            if (!loops.TryGetValue(loopId, out var loop))
            {
                continue;
            }

            if (!TryBuildParamLoop(
                loop.Coedges,
                recordsById,
                document,
                nurbsCurves,
                pointsById,
                verticesById,
                sampling,
                out var paramLoop))
            {
                return false;
            }

            paramLoops.Add(paramLoop);
        }

        if (paramLoops.Count == 0)
        {
            return TryAppendSurfaceGrid(surface, sampling, triangles);
        }

        var uvLoops = new List<IReadOnlyList<XYZ>>(paramLoops.Count);
        foreach (var loop in paramLoops)
        {
            var list = new List<XYZ>(loop.Count);
            foreach (var point in loop)
            {
                list.Add(new XYZ(point.X, point.Y, 0.0));
            }

            uvLoops.Add(list);
        }

        if (!RenderPolygonTriangulator.TryTriangulateWithHoles(uvLoops, out var uvTriangles))
        {
            return TryAppendSurfaceGrid(surface, sampling, triangles);
        }

        if (TryGetUvBounds(uvLoops, out var uMin, out var uMax, out var vMin, out var vMax))
        {
            ResolveSurfaceSegments(surface, sampling, uMin, uMax, vMin, vMax, out var uSegments, out var vSegments);
            var maxU = uSegments > 0 ? (uMax - uMin) / uSegments : 0.0;
            var maxV = vSegments > 0 ? (vMax - vMin) / vSegments : 0.0;
            uvTriangles = SubdivideUvTriangles(uvTriangles, maxU, maxV);
        }

        foreach (var triangle in uvTriangles)
        {
            var a = surface.Evaluate(triangle.A.X, triangle.A.Y);
            var b = surface.Evaluate(triangle.B.X, triangle.B.Y);
            var c = surface.Evaluate(triangle.C.X, triangle.C.Y);
            var midU = (triangle.A.X + triangle.B.X + triangle.C.X) / 3.0;
            var midV = (triangle.A.Y + triangle.B.Y + triangle.C.Y) / 3.0;
            if (surface.TryEvaluateNormal(midU, midV, out var surfaceNormal))
            {
                var triNormal = XYZ.FindNormal(a, b, c);
                if (!triNormal.IsZero() && triNormal.Dot(surfaceNormal) < 0.0)
                {
                    triangles.Add(new MeshTessellator.Triangle(a, c, b));
                    continue;
                }
            }

            triangles.Add(new MeshTessellator.Triangle(a, b, c));
        }

        return uvTriangles.Count > 0;
    }

    private static bool TryBuildParamLoop(
        IReadOnlyList<CoedgeDefinition> coedges,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        RenderAcisSatDocument document,
        IReadOnlyDictionary<int, NurbsCurveDefinition> nurbsCurves,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById,
        CurveSamplingSettings sampling,
        out List<Vector2> polygon)
    {
        polygon = new List<Vector2>();
        if (coedges.Count == 0)
        {
            return false;
        }

        foreach (var coedge in coedges)
        {
            if (coedge.PcurveId <= 0 || !recordsById.TryGetValue(coedge.PcurveId, out var pcurve))
            {
                return false;
            }

            if (!TryBuildParamCurvePoints(
                pcurve,
                document,
                recordsById,
                pointsById,
                verticesById,
                nurbsCurves,
                sampling,
                out var curvePoints))
            {
                return false;
            }

            AppendParamPoints(polygon, curvePoints, coedge.Reversed);
        }

        if (polygon.Count < 3)
        {
            return false;
        }

        if (!IsClose(polygon[0], polygon[^1]))
        {
            polygon.Add(polygon[0]);
        }

        return polygon.Count >= 3;
    }

    private static void AppendParamPoints(List<Vector2> polygon, IReadOnlyList<Vector2> points, bool reverseEdge)
    {
        if (points.Count == 0)
        {
            return;
        }

        if (polygon.Count == 0)
        {
            if (reverseEdge)
            {
                for (var i = points.Count - 1; i >= 0; i--)
                {
                    polygon.Add(points[i]);
                }
            }
            else
            {
                polygon.AddRange(points);
            }
            return;
        }

        var last = polygon[^1];
        var start = reverseEdge ? points[^1] : points[0];
        var end = reverseEdge ? points[0] : points[^1];
        var flip = false;
        if (IsClose(last, start))
        {
            flip = false;
        }
        else if (IsClose(last, end))
        {
            flip = true;
        }
        else
        {
            var startDist = DistanceSquared(last, start);
            var endDist = DistanceSquared(last, end);
            flip = endDist < startDist;
        }

        var effectiveReverse = reverseEdge ^ flip;
        if (effectiveReverse)
        {
            for (var i = points.Count - 2; i >= 0; i--)
            {
                polygon.Add(points[i]);
            }
        }
        else
        {
            for (var i = 1; i < points.Count; i++)
            {
                polygon.Add(points[i]);
            }
        }
    }

    private static bool TryBuildParamCurvePoints(
        RenderAcisSatRecord pcurve,
        RenderAcisSatDocument document,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById,
        IReadOnlyDictionary<int, NurbsCurveDefinition> nurbsCurves,
        CurveSamplingSettings sampling,
        out List<Vector2> points)
    {
        points = new List<Vector2>();
        if (pcurve is null)
        {
            return false;
        }

        var curveRecord = pcurve;
        var curveId = FindFirstReference(pcurve, recordsById, IsCurveRecord);
        if (curveId > 0 && recordsById.TryGetValue(curveId, out var curveCandidate))
        {
            curveRecord = curveCandidate;
        }

        if (nurbsCurves.TryGetValue(curveRecord.Id, out var nurbsCurve))
        {
            var sampled = SampleNurbsCurve(nurbsCurve, sampling);
            foreach (var point in sampled)
            {
                points.Add(new Vector2((float)point.X, (float)point.Y));
            }

            return points.Count >= 2;
        }

        var controlPoints = ResolveControlPoints(
            new CurveDefinition(curveRecord.Id, curveRecord.Type, curveRecord.Numbers, CollectPointReferences(curveRecord, document, recordsById)),
            pointsById,
            verticesById);
        if (controlPoints.Count >= 2)
        {
            foreach (var point in controlPoints)
            {
                points.Add(new Vector2((float)point.X, (float)point.Y));
            }

            return points.Count >= 2;
        }

        if (curveRecord.Type.Contains("ellipse", StringComparison.OrdinalIgnoreCase))
        {
            var curve = new CurveDefinition(curveRecord.Id, curveRecord.Type, curveRecord.Numbers, Array.Empty<int>());
            var start = controlPoints.Count > 0 ? controlPoints[0] : XYZ.Zero;
            var end = controlPoints.Count > 1 ? controlPoints[^1] : start;
            if (TrySampleEllipse(curve, start, end, sampling, out var ellipsePoints))
            {
                foreach (var point in ellipsePoints)
                {
                    points.Add(new Vector2((float)point.X, (float)point.Y));
                }

                return points.Count >= 2;
            }
        }

        return false;
    }

    private static bool TryAppendSurfaceGrid(
        SurfaceDefinition surface,
        CurveSamplingSettings sampling,
        List<MeshTessellator.Triangle> triangles)
    {
        if (!surface.TryGetParameterRange(out var uStart, out var uEnd, out var vStart, out var vEnd))
        {
            return false;
        }

        ResolveSurfaceSegments(surface, sampling, uStart, uEnd, vStart, vEnd, out var uSegments, out var vSegments);
        if (uEnd <= uStart || vEnd <= vStart)
        {
            return false;
        }

        var du = (uEnd - uStart) / uSegments;
        var dv = (vEnd - vStart) / vSegments;
        for (var i = 0; i < uSegments; i++)
        {
            var u0 = uStart + du * i;
            var u1 = uStart + du * (i + 1);
            for (var j = 0; j < vSegments; j++)
            {
                var v0 = vStart + dv * j;
                var v1 = vStart + dv * (j + 1);

                var p00 = surface.Evaluate(u0, v0);
                var p10 = surface.Evaluate(u1, v0);
                var p11 = surface.Evaluate(u1, v1);
                var p01 = surface.Evaluate(u0, v1);

                var midU = (u0 + u1) * 0.5;
                var midV = (v0 + v1) * 0.5;
                var hasNormal = surface.TryEvaluateNormal(midU, midV, out var surfaceNormal);

                var triNormal0 = XYZ.FindNormal(p00, p10, p11);
                if (hasNormal && !triNormal0.IsZero() && triNormal0.Dot(surfaceNormal) < 0.0)
                {
                    triangles.Add(new MeshTessellator.Triangle(p00, p11, p10));
                }
                else
                {
                    triangles.Add(new MeshTessellator.Triangle(p00, p10, p11));
                }

                var triNormal1 = XYZ.FindNormal(p00, p11, p01);
                if (hasNormal && !triNormal1.IsZero() && triNormal1.Dot(surfaceNormal) < 0.0)
                {
                    triangles.Add(new MeshTessellator.Triangle(p00, p01, p11));
                }
                else
                {
                    triangles.Add(new MeshTessellator.Triangle(p00, p11, p01));
                }
            }
        }

        return true;
    }

    private static List<XYZ> SampleNurbsCurve(NurbsCurveDefinition curve, CurveSamplingSettings sampling)
    {
        var points = new List<XYZ>();
        if (curve is null || curve.ControlPoints.Length == 0 || curve.Knots.Length == 0)
        {
            return points;
        }

        var start = curve.Knots[curve.Degree];
        var endIndex = curve.Knots.Length - curve.Degree - 1;
        if (endIndex <= curve.Degree)
        {
            return points;
        }

        var end = curve.Knots[endIndex];
        if (end <= start)
        {
            return points;
        }

        var segments = Math.Max(sampling.SplineSegments, curve.ControlPoints.Length * 3);
        segments = Math.Max(segments, 8);
        for (var i = 0; i <= segments; i++)
        {
            var t = start + (end - start) * i / segments;
            var point = RenderNurbsEvaluator.EvaluateCurve(
                curve.Degree,
                curve.Knots,
                curve.ControlPoints,
                curve.Weights,
                t);
            points.Add(point);
        }

        return points;
    }

    private static bool TryBuildCurvePoints(
        CurveDefinition curve,
        XYZ start,
        XYZ end,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById,
        IReadOnlyDictionary<int, NurbsCurveDefinition> nurbsCurves,
        CurveSamplingSettings sampling,
        out List<XYZ> points)
    {
        points = new List<XYZ>();
        if (curve is null)
        {
            return false;
        }

        if (nurbsCurves.TryGetValue(curve.Id, out var nurbsCurve))
        {
            points = SampleNurbsCurve(nurbsCurve, sampling);
            return points.Count >= 2;
        }

        if (curve.Type.Contains("ellipse", StringComparison.OrdinalIgnoreCase))
        {
            return TrySampleEllipse(curve, start, end, sampling, out points);
        }

        if (curve.Type.Contains("spline", StringComparison.OrdinalIgnoreCase) ||
            curve.Type.Contains("intcurve", StringComparison.OrdinalIgnoreCase))
        {
            return TrySampleSpline(curve, pointsById, verticesById, sampling, out points);
        }

        var controlPoints = ResolveControlPoints(curve, pointsById, verticesById);
        if (controlPoints.Count >= 2)
        {
            points.AddRange(controlPoints);
            return true;
        }

        return false;
    }

    private static bool TrySampleEllipse(
        CurveDefinition curve,
        XYZ start,
        XYZ end,
        CurveSamplingSettings sampling,
        out List<XYZ> points)
    {
        points = new List<XYZ>();
        if (curve.Numbers.Count < 10)
        {
            return false;
        }

        var count = curve.Numbers.Count;
        var center = new XYZ(
            curve.Numbers[count - 10],
            curve.Numbers[count - 9],
            curve.Numbers[count - 8]);
        var normal = new XYZ(
            curve.Numbers[count - 7],
            curve.Numbers[count - 6],
            curve.Numbers[count - 5]);
        var major = new XYZ(
            curve.Numbers[count - 4],
            curve.Numbers[count - 3],
            curve.Numbers[count - 2]);
        var ratio = curve.Numbers[count - 1];
        if (major.IsZero() || normal.IsZero())
        {
            return false;
        }

        var a = major.GetLength();
        if (a <= 0)
        {
            return false;
        }

        var b = a * ratio;
        if (Math.Abs(b) <= 0)
        {
            return false;
        }

        var u = major.Normalize();
        var v = XYZ.Cross(normal.Normalize(), u).Normalize();
        if (v.IsZero())
        {
            return false;
        }

        var startTheta = ResolveEllipseAngle(center, u, v, a, b, start);
        var endTheta = ResolveEllipseAngle(center, u, v, a, b, end);
        var sweep = endTheta - startTheta;
        if (sweep <= 0)
        {
            sweep += MathHelper.TwoPI;
        }

        var segments = ResolveCurveSegments(sweep, sampling.EllipseSegments, minSegments: 8);
        var ccw = SampleEllipse(center, u, v, a, b, startTheta, sweep, segments);
        var cw = SampleEllipse(center, u, v, a, b, startTheta, sweep - MathHelper.TwoPI, segments);

        points = DistanceSquared(ccw[^1], end) <= DistanceSquared(cw[^1], end)
            ? ccw
            : cw;
        return points.Count >= 2;
    }

    private static List<XYZ> SampleEllipse(
        XYZ center,
        XYZ u,
        XYZ v,
        double a,
        double b,
        double start,
        double sweep,
        int segments)
    {
        var points = new List<XYZ>(segments + 1);
        if (segments <= 0)
        {
            return points;
        }

        for (var i = 0; i <= segments; i++)
        {
            var t = start + sweep * i / segments;
            var cos = Math.Cos(t);
            var sin = Math.Sin(t);
            var point = new XYZ(
                center.X + u.X * (a * cos) + v.X * (b * sin),
                center.Y + u.Y * (a * cos) + v.Y * (b * sin),
                center.Z + u.Z * (a * cos) + v.Z * (b * sin));
            points.Add(point);
        }

        return points;
    }

    private static bool TrySampleSpline(
        CurveDefinition curve,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById,
        CurveSamplingSettings sampling,
        out List<XYZ> points)
    {
        points = new List<XYZ>();
        var controlPoints = ResolveControlPoints(curve, pointsById, verticesById);
        if (controlPoints.Count < 2)
        {
            return false;
        }

        var segmentsPerSpan = Math.Max(4, sampling.SplineSegments / Math.Max(controlPoints.Count - 1, 1));
        points.AddRange(SampleCatmullRom(controlPoints, segmentsPerSpan));
        return points.Count >= 2;
    }

    private static List<XYZ> ResolveControlPoints(
        CurveDefinition curve,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById)
    {
        var controlPoints = new List<XYZ>();
        foreach (var reference in curve.PointReferences)
        {
            if (pointsById.TryGetValue(reference, out var point) ||
                verticesById.TryGetValue(reference, out point))
            {
                controlPoints.Add(point);
            }
        }

        return controlPoints;
    }

    private static List<XYZ> SampleCatmullRom(IReadOnlyList<XYZ> points, int segmentsPerSpan)
    {
        var result = new List<XYZ>();
        if (points.Count < 2)
        {
            return result;
        }

        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = i == 0 ? points[i] : points[i - 1];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i + 2 < points.Count ? points[i + 2] : points[i + 1];

            for (var j = 0; j <= segmentsPerSpan; j++)
            {
                if (i > 0 && j == 0)
                {
                    continue;
                }

                var t = j / (double)segmentsPerSpan;
                var t2 = t * t;
                var t3 = t2 * t;

                var x = 0.5 * ((2 * p1.X) + (-p0.X + p2.X) * t
                    + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2
                    + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                var y = 0.5 * ((2 * p1.Y) + (-p0.Y + p2.Y) * t
                    + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2
                    + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                var z = 0.5 * ((2 * p1.Z) + (-p0.Z + p2.Z) * t
                    + (2 * p0.Z - 5 * p1.Z + 4 * p2.Z - p3.Z) * t2
                    + (-p0.Z + 3 * p1.Z - 3 * p2.Z + p3.Z) * t3);

                result.Add(new XYZ(x, y, z));
            }
        }

        return result;
    }

    private static bool TryParseNurbsCurve(
        RenderAcisSatRecord record,
        RenderAcisSatDocument document,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById,
        [NotNullWhen(true)] out NurbsCurveDefinition? curve)
    {
        if (TryParseNurbsCurveTokens(record.Tokens, record.References, pointsById, verticesById, out curve))
        {
            return true;
        }

        foreach (var subtypeIndex in record.SubtypeReferences)
        {
            if (subtypeIndex < 0 || subtypeIndex >= document.Subtypes.Count)
            {
                continue;
            }

            var subtype = document.Subtypes[subtypeIndex];
            if (TryParseNurbsCurveTokens(subtype.Tokens, subtype.References, pointsById, verticesById, out curve))
            {
                return true;
            }
        }

        curve = default;
        return false;
    }

    private static bool TryParseNurbsSurface(
        RenderAcisSatRecord record,
        RenderAcisSatDocument document,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById,
        [NotNullWhen(true)] out NurbsSurfaceDefinition? surface)
    {
        if (TryParseNurbsSurfaceTokens(record.Tokens, record.References, pointsById, verticesById, out surface))
        {
            return true;
        }

        foreach (var subtypeIndex in record.SubtypeReferences)
        {
            if (subtypeIndex < 0 || subtypeIndex >= document.Subtypes.Count)
            {
                continue;
            }

            var subtype = document.Subtypes[subtypeIndex];
            if (TryParseNurbsSurfaceTokens(subtype.Tokens, subtype.References, pointsById, verticesById, out surface))
            {
                return true;
            }
        }

        surface = default;
        return false;
    }

    private static bool TryParseNurbsCurveTokens(
        IReadOnlyList<string> tokens,
        IReadOnlyList<int> references,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById,
        [NotNullWhen(true)] out NurbsCurveDefinition? curve)
    {
        curve = default;
        if (!TryFindNurbsStart(tokens, out var startIndex, out var isRational))
        {
            return false;
        }

        var cursor = new TokenCursor(tokens, startIndex + 1);
        if (!cursor.TryReadInt(out var degree))
        {
            return false;
        }

        if (!cursor.TryReadToken(out var closureToken) || IsNumericToken(closureToken))
        {
            return false;
        }

        if (!cursor.TryReadToken(out var nextToken))
        {
            return false;
        }

        int numKnots;
        if (IsNumericToken(nextToken))
        {
            if (!TryParseInt(nextToken, out numKnots))
            {
                return false;
            }
        }
        else
        {
            if (!cursor.TryReadInt(out numKnots))
            {
                return false;
            }
        }

        if (numKnots <= 0)
        {
            return false;
        }

        if (!TryReadKnotPairs(cursor, numKnots, out var knots))
        {
            return false;
        }

        var controlPointCount = knots.Length - degree - 1;
        if (controlPointCount <= 0)
        {
            return false;
        }

        var remainingNumbers = ExtractRemainingNumbers(tokens, cursor.Index);
        if (TryBuildControlPoints(
            remainingNumbers,
            controlPointCount,
            isRational,
            out var controlPoints,
            out var weights))
        {
            curve = new NurbsCurveDefinition(degree, knots, controlPoints, weights);
            return true;
        }

        if (TryResolveControlPointsFromReferences(references, controlPointCount, pointsById, verticesById, out controlPoints))
        {
            weights = BuildWeights(controlPointCount, isRational, remainingNumbers);
            curve = new NurbsCurveDefinition(degree, knots, controlPoints, weights);
            return true;
        }

        return false;
    }

    private static bool TryParseNurbsSurfaceTokens(
        IReadOnlyList<string> tokens,
        IReadOnlyList<int> references,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById,
        [NotNullWhen(true)] out NurbsSurfaceDefinition? surface)
    {
        surface = default;
        if (!TryFindNurbsStart(tokens, out var startIndex, out var isRational))
        {
            return false;
        }

        var cursor = new TokenCursor(tokens, startIndex + 1);
        if (!cursor.TryReadInt(out var degreeU))
        {
            return false;
        }

        if (!cursor.TryReadInt(out var degreeV))
        {
            return false;
        }

        if (!cursor.TryReadToken(out var closureU) || IsNumericToken(closureU))
        {
            return false;
        }

        if (!cursor.TryReadToken(out var closureV) || IsNumericToken(closureV))
        {
            return false;
        }

        if (!cursor.TryReadToken(out var singularityU) || IsNumericToken(singularityU))
        {
            return false;
        }

        if (!cursor.TryReadToken(out var singularityV) || IsNumericToken(singularityV))
        {
            return false;
        }

        if (!cursor.TryReadInt(out var numKnotsU) || !cursor.TryReadInt(out var numKnotsV))
        {
            return false;
        }

        if (numKnotsU <= 0 || numKnotsV <= 0)
        {
            return false;
        }

        if (!TryReadKnotPairs(cursor, numKnotsU, out var knotsU))
        {
            return false;
        }

        if (!TryReadKnotPairs(cursor, numKnotsV, out var knotsV))
        {
            return false;
        }

        var countU = knotsU.Length - degreeU - 1;
        var countV = knotsV.Length - degreeV - 1;
        if (countU <= 0 || countV <= 0)
        {
            return false;
        }

        var controlPointCount = countU * countV;
        var remainingNumbers = ExtractRemainingNumbers(tokens, cursor.Index);
        if (TryBuildControlPoints(
            remainingNumbers,
            controlPointCount,
            isRational,
            out var controlPoints,
            out var weights))
        {
            surface = new NurbsSurfaceDefinition(
                degreeU,
                degreeV,
                knotsU,
                knotsV,
                controlPoints,
                weights,
                countU,
                countV);
            return true;
        }

        if (TryResolveControlPointsFromReferences(references, controlPointCount, pointsById, verticesById, out controlPoints))
        {
            weights = BuildWeights(controlPointCount, isRational, remainingNumbers);
            surface = new NurbsSurfaceDefinition(
                degreeU,
                degreeV,
                knotsU,
                knotsV,
                controlPoints,
                weights,
                countU,
                countV);
            return true;
        }

        return false;
    }

    private static bool TryFindNurbsStart(IReadOnlyList<string> tokens, out int index, out bool isRational)
    {
        index = -1;
        isRational = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (string.Equals(token, "nurbs", StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                isRational = true;
                return true;
            }

            if (string.Equals(token, "nubs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "bs3_curve", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "bs3_surface", StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                isRational = false;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadKnotPairs(TokenCursor cursor, int count, out double[] knots)
    {
        knots = Array.Empty<double>();
        if (count <= 0)
        {
            return false;
        }

        var values = new List<double>(count * 2);
        for (var i = 0; i < count; i++)
        {
            if (!cursor.TryReadDouble(out var knotValue))
            {
                return false;
            }

            var multiplicity = 1;
            if (cursor.TryPeekInt(out var multToken))
            {
                if (!cursor.TryReadInt(out multiplicity))
                {
                    multiplicity = 1;
                }
            }

            multiplicity = Math.Max(1, multiplicity);
            for (var j = 0; j < multiplicity; j++)
            {
                values.Add(knotValue);
            }
        }

        knots = values.ToArray();
        return knots.Length > 0;
    }

    private static List<double> ExtractRemainingNumbers(IReadOnlyList<string> tokens, int startIndex)
    {
        var numbers = new List<double>();
        for (var i = startIndex; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token == "{" || token == "}")
            {
                continue;
            }

            if (string.Equals(token, "ref", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                numbers.Add(value);
            }
        }

        return numbers;
    }

    private static bool TryBuildControlPoints(
        IReadOnlyList<double> numbers,
        int controlPointCount,
        bool isRational,
        out XYZ[] controlPoints,
        out double[] weights)
    {
        controlPoints = Array.Empty<XYZ>();
        weights = Array.Empty<double>();
        if (controlPointCount <= 0 || numbers.Count == 0)
        {
            return false;
        }

        var coordsPerPoint = numbers.Count >= controlPointCount * 3
            ? 3
            : numbers.Count >= controlPointCount * 2 ? 2 : 0;
        if (coordsPerPoint == 0)
        {
            return false;
        }

        controlPoints = new XYZ[controlPointCount];
        var offset = 0;
        for (var i = 0; i < controlPointCount; i++)
        {
            if (offset + coordsPerPoint > numbers.Count)
            {
                return false;
            }

            var x = numbers[offset++];
            var y = numbers[offset++];
            var z = coordsPerPoint == 3 ? numbers[offset++] : 0.0;
            controlPoints[i] = new XYZ(x, y, z);
        }

        weights = BuildWeights(controlPointCount, isRational, numbers.Skip(offset).ToArray());
        return true;
    }

    private static bool TryResolveControlPointsFromReferences(
        IReadOnlyList<int> references,
        int controlPointCount,
        IReadOnlyDictionary<int, XYZ> pointsById,
        IReadOnlyDictionary<int, XYZ> verticesById,
        out XYZ[] controlPoints)
    {
        controlPoints = Array.Empty<XYZ>();
        if (controlPointCount <= 0)
        {
            return false;
        }

        var points = new List<XYZ>(controlPointCount);
        foreach (var reference in references)
        {
            if (points.Count >= controlPointCount)
            {
                break;
            }

            if (pointsById.TryGetValue(reference, out var point) ||
                verticesById.TryGetValue(reference, out point))
            {
                points.Add(point);
            }
        }

        if (points.Count != controlPointCount)
        {
            return false;
        }

        controlPoints = points.ToArray();
        return true;
    }

    private static double[] BuildWeights(int controlPointCount, bool isRational, IReadOnlyList<double> remainingNumbers)
    {
        if (!isRational || controlPointCount <= 0)
        {
            return Array.Empty<double>();
        }

        if (remainingNumbers.Count >= controlPointCount)
        {
            var weights = new double[controlPointCount];
            for (var i = 0; i < controlPointCount; i++)
            {
                weights[i] = remainingNumbers[i];
            }

            return weights;
        }

        var defaults = new double[controlPointCount];
        for (var i = 0; i < defaults.Length; i++)
        {
            defaults[i] = 1.0;
        }

        return defaults;
    }

    private static bool TryGetTailStart(IReadOnlyList<double> numbers, int count, out int startIndex)
    {
        startIndex = numbers.Count - count;
        return startIndex >= 0;
    }

    private static bool TryReadTailRange(
        IReadOnlyList<double> numbers,
        int baseCount,
        out double uStart,
        out double uEnd,
        out double vStart,
        out double vEnd)
    {
        uStart = 0.0;
        uEnd = 0.0;
        vStart = 0.0;
        vEnd = 0.0;

        if (numbers.Count < baseCount + 4)
        {
            return false;
        }

        var uStartCandidate = numbers[^4];
        var uEndCandidate = numbers[^3];
        var vStartCandidate = numbers[^2];
        var vEndCandidate = numbers[^1];
        if (uEndCandidate > uStartCandidate && vEndCandidate > vStartCandidate)
        {
            uStart = uStartCandidate;
            uEnd = uEndCandidate;
            vStart = vStartCandidate;
            vEnd = vEndCandidate;
            return true;
        }

        return false;
    }

    private static bool TryBuildAxes(XYZ axisCandidate, XYZ refAxisCandidate, out XYZ axis, out XYZ uAxis, out XYZ vAxis)
    {
        axis = XYZ.Zero;
        uAxis = XYZ.Zero;
        vAxis = XYZ.Zero;

        if (axisCandidate.IsZero())
        {
            return false;
        }

        axis = axisCandidate.Normalize();
        uAxis = refAxisCandidate.IsZero() ? XYZ.AxisX : refAxisCandidate.Normalize();
        vAxis = XYZ.Cross(axis, uAxis).Normalize();
        if (vAxis.IsZero())
        {
            uAxis = XYZ.Cross(axis, XYZ.AxisX).Normalize();
            if (uAxis.IsZero())
            {
                uAxis = XYZ.AxisY;
            }
            vAxis = XYZ.Cross(axis, uAxis).Normalize();
        }

        if (vAxis.IsZero())
        {
            return false;
        }

        uAxis = XYZ.Cross(vAxis, axis).Normalize();
        return !uAxis.IsZero();
    }

    private static bool TryParseConeSurface(RenderAcisSatRecord record, out SurfaceDefinition surface)
    {
        surface = SurfaceDefinition.Empty;
        if (!record.Type.Contains("cone", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var numbers = record.Numbers;
        var preferUScale = false;
        if (numbers.Count >= 12)
        {
            var remainder = (numbers.Count - 12) % 4;
            preferUScale = remainder == 1;
        }

        if (preferUScale)
        {
            if (TryParseConeSurface(record, hasUScale: true, out surface))
            {
                return true;
            }

            return TryParseConeSurface(record, hasUScale: false, out surface);
        }

        if (TryParseConeSurface(record, hasUScale: false, out surface))
        {
            return true;
        }

        return TryParseConeSurface(record, hasUScale: true, out surface);
    }

    private static bool TryParseConeSurface(RenderAcisSatRecord record, bool hasUScale, out SurfaceDefinition surface)
    {
        surface = SurfaceDefinition.Empty;
        var numbers = record.Numbers;
        var baseCount = hasUScale ? 13 : 12;
        if (numbers.Count < baseCount)
        {
            return false;
        }

        var rangeFound = TryReadTailRange(numbers, baseCount, out var uStart, out var uEnd, out var vStart, out var vEnd);
        var start = numbers.Count - baseCount - (rangeFound ? 4 : 0);
        if (start < 0)
        {
            return false;
        }

        if (!TryParseConeSurfaceData(numbers, start, hasUScale, out var center, out var axis, out var uAxis, out var vAxis,
                out var majorRadius, out var minorRadius, out var height))
        {
            if (!rangeFound)
            {
                return false;
            }

            start = numbers.Count - baseCount;
            if (start < 0 ||
                !TryParseConeSurfaceData(numbers, start, hasUScale, out center, out axis, out uAxis, out vAxis,
                    out majorRadius, out minorRadius, out height))
            {
                return false;
            }

            uStart = 0.0;
            uEnd = 0.0;
            vStart = 0.0;
            vEnd = 0.0;
        }

        surface = SurfaceDefinition.FromCone(
            record.Id,
            center,
            axis,
            uAxis,
            vAxis,
            majorRadius,
            minorRadius,
            height,
            uStart,
            uEnd,
            vStart,
            vEnd);
        return true;
    }

    private static bool TryParseConeSurfaceData(
        IReadOnlyList<double> numbers,
        int start,
        bool hasUScale,
        out XYZ center,
        out XYZ axis,
        out XYZ uAxis,
        out XYZ vAxis,
        out double majorRadius,
        out double minorRadius,
        out double height)
    {
        center = XYZ.Zero;
        axis = XYZ.Zero;
        uAxis = XYZ.Zero;
        vAxis = XYZ.Zero;
        majorRadius = 0.0;
        minorRadius = 0.0;
        height = 0.0;

        if (start < 0 || start + (hasUScale ? 12 : 11) >= numbers.Count)
        {
            return false;
        }

        center = new XYZ(numbers[start], numbers[start + 1], numbers[start + 2]);
        var normal = new XYZ(numbers[start + 3], numbers[start + 4], numbers[start + 5]);
        var major = new XYZ(numbers[start + 6], numbers[start + 7], numbers[start + 8]);
        var ratio = Math.Abs(numbers[start + 9]);
        var angleSin = numbers[start + 10];
        var angleCos = numbers[start + 11];
        var uScale = hasUScale ? Math.Abs(numbers[start + 12]) : major.GetLength();

        if (!TryBuildAxes(normal, major, out axis, out uAxis, out vAxis))
        {
            return false;
        }

        majorRadius = major.GetLength();
        if (majorRadius <= 0.0)
        {
            return false;
        }

        minorRadius = majorRadius * ratio;
        height = ResolveConeHeight(majorRadius, angleSin, angleCos, uScale);
        return true;
    }

    private static bool TryParseCylinderSurface(RenderAcisSatRecord record, out SurfaceDefinition surface)
    {
        surface = SurfaceDefinition.Empty;
        if (!record.Type.Contains("cylinder", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var numbers = record.Numbers;
        var preferHeight = false;
        if (numbers.Count >= 10)
        {
            var remainder = (numbers.Count - 10) % 4;
            preferHeight = remainder == 1;
        }

        if (preferHeight)
        {
            if (TryParseCylinderSurface(record, hasHeight: true, out surface))
            {
                return true;
            }

            return TryParseCylinderSurface(record, hasHeight: false, out surface);
        }

        if (TryParseCylinderSurface(record, hasHeight: false, out surface))
        {
            return true;
        }

        return TryParseCylinderSurface(record, hasHeight: true, out surface);
    }

    private static bool TryParseCylinderSurface(RenderAcisSatRecord record, bool hasHeight, out SurfaceDefinition surface)
    {
        surface = SurfaceDefinition.Empty;
        var numbers = record.Numbers;
        var baseCount = hasHeight ? 11 : 10;
        if (numbers.Count < baseCount)
        {
            return false;
        }

        var rangeFound = TryReadTailRange(numbers, baseCount, out var uStart, out var uEnd, out var vStart, out var vEnd);
        var start = numbers.Count - baseCount - (rangeFound ? 4 : 0);
        if (start < 0)
        {
            return false;
        }

        if (!TryParseCylinderSurfaceData(numbers, start, hasHeight, out var center, out var axis, out var uAxis, out var vAxis,
                out var majorRadius, out var minorRadius, out var height))
        {
            if (!rangeFound)
            {
                return false;
            }

            start = numbers.Count - baseCount;
            if (start < 0 ||
                !TryParseCylinderSurfaceData(numbers, start, hasHeight, out center, out axis, out uAxis, out vAxis,
                    out majorRadius, out minorRadius, out height))
            {
                return false;
            }

            uStart = 0.0;
            uEnd = 0.0;
            vStart = 0.0;
            vEnd = 0.0;
        }

        surface = SurfaceDefinition.FromCylinder(
            record.Id,
            center,
            axis,
            uAxis,
            vAxis,
            majorRadius,
            minorRadius,
            height,
            uStart,
            uEnd,
            vStart,
            vEnd);
        return true;
    }

    private static bool TryParseCylinderSurfaceData(
        IReadOnlyList<double> numbers,
        int start,
        bool hasHeight,
        out XYZ center,
        out XYZ axis,
        out XYZ uAxis,
        out XYZ vAxis,
        out double majorRadius,
        out double minorRadius,
        out double height)
    {
        center = XYZ.Zero;
        axis = XYZ.Zero;
        uAxis = XYZ.Zero;
        vAxis = XYZ.Zero;
        majorRadius = 0.0;
        minorRadius = 0.0;
        height = 0.0;

        if (start < 0 || start + (hasHeight ? 10 : 9) >= numbers.Count)
        {
            return false;
        }

        center = new XYZ(numbers[start], numbers[start + 1], numbers[start + 2]);
        var normal = new XYZ(numbers[start + 3], numbers[start + 4], numbers[start + 5]);
        var major = new XYZ(numbers[start + 6], numbers[start + 7], numbers[start + 8]);
        var ratio = Math.Abs(numbers[start + 9]);
        height = hasHeight ? Math.Abs(numbers[start + 10]) : 0.0;

        if (!TryBuildAxes(normal, major, out axis, out uAxis, out vAxis))
        {
            return false;
        }

        majorRadius = major.GetLength();
        if (majorRadius <= 0.0)
        {
            return false;
        }

        minorRadius = majorRadius * ratio;
        if (height <= 0.0)
        {
            height = majorRadius;
        }

        return true;
    }

    private static bool TryParseTorusSurface(RenderAcisSatRecord record, out SurfaceDefinition surface)
    {
        surface = SurfaceDefinition.Empty;
        if (!record.Type.Contains("torus", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var numbers = record.Numbers;
        const int baseCount = 11;
        if (numbers.Count < baseCount)
        {
            return false;
        }

        var rangeFound = TryReadTailRange(numbers, baseCount, out var uStart, out var uEnd, out var vStart, out var vEnd);
        var start = numbers.Count - baseCount - (rangeFound ? 4 : 0);
        if (start < 0)
        {
            return false;
        }

        if (!TryParseTorusSurfaceData(numbers, start, out var center, out var axis, out var uAxis, out var vAxis,
                out var majorRadius, out var minorRadius))
        {
            if (!rangeFound)
            {
                return false;
            }

            start = numbers.Count - baseCount;
            if (start < 0 ||
                !TryParseTorusSurfaceData(numbers, start, out center, out axis, out uAxis, out vAxis,
                    out majorRadius, out minorRadius))
            {
                return false;
            }

            uStart = 0.0;
            uEnd = 0.0;
            vStart = 0.0;
            vEnd = 0.0;
        }

        surface = SurfaceDefinition.FromTorus(
            record.Id,
            center,
            axis,
            uAxis,
            vAxis,
            majorRadius,
            minorRadius,
            uStart,
            uEnd,
            vStart,
            vEnd);
        return true;
    }

    private static bool TryParseTorusSurfaceData(
        IReadOnlyList<double> numbers,
        int start,
        out XYZ center,
        out XYZ axis,
        out XYZ uAxis,
        out XYZ vAxis,
        out double majorRadius,
        out double minorRadius)
    {
        center = XYZ.Zero;
        axis = XYZ.Zero;
        uAxis = XYZ.Zero;
        vAxis = XYZ.Zero;
        majorRadius = 0.0;
        minorRadius = 0.0;

        if (start < 0 || start + 10 >= numbers.Count)
        {
            return false;
        }

        center = new XYZ(numbers[start], numbers[start + 1], numbers[start + 2]);
        var axisCandidate = new XYZ(numbers[start + 3], numbers[start + 4], numbers[start + 5]);
        var refAxisCandidate = new XYZ(numbers[start + 6], numbers[start + 7], numbers[start + 8]);
        majorRadius = Math.Abs(numbers[start + 9]);
        minorRadius = Math.Abs(numbers[start + 10]);
        if (majorRadius <= 0.0 || minorRadius <= 0.0)
        {
            return false;
        }

        if (!TryBuildAxes(axisCandidate, refAxisCandidate, out axis, out uAxis, out vAxis))
        {
            return false;
        }

        return true;
    }

    private static double ResolveConeHeight(double radius, double angleSin, double angleCos, double fallback)
    {
        if (Math.Abs(angleSin) > 1e-6)
        {
            var height = radius * Math.Abs(angleCos / angleSin);
            if (height > 0.0)
            {
                return height;
            }
        }

        if (fallback > 0.0)
        {
            return fallback;
        }

        return radius;
    }

    private static bool TryParsePlaneSurface(RenderAcisSatRecord record, out SurfaceDefinition surface)
    {
        surface = SurfaceDefinition.Empty;
        if (!record.Type.Contains("plane", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var numbers = record.Numbers;
        const int baseCount = 9;
        if (numbers.Count < baseCount)
        {
            return false;
        }

        var rangeFound = TryReadTailRange(numbers, baseCount, out var uStart, out var uEnd, out var vStart, out var vEnd);
        var start = numbers.Count - baseCount - (rangeFound ? 4 : 0);
        if (start < 0)
        {
            return false;
        }

        var origin = new XYZ(numbers[start], numbers[start + 1], numbers[start + 2]);
        var normal = new XYZ(numbers[start + 3], numbers[start + 4], numbers[start + 5]);
        var axis = new XYZ(numbers[start + 6], numbers[start + 7], numbers[start + 8]);
        if (normal.IsZero())
        {
            return false;
        }

        var n = normal.Normalize();
        var u = axis.IsZero() ? XYZ.AxisX : axis.Normalize();
        var v = XYZ.Cross(n, u).Normalize();
        if (v.IsZero())
        {
            u = XYZ.AxisY;
            v = XYZ.Cross(n, u).Normalize();
        }

        u = XYZ.Cross(v, n).Normalize();
        surface = SurfaceDefinition.FromPlane(record.Id, origin, u, v, n, uStart, uEnd, vStart, vEnd);
        return true;
    }

    private static bool TryParseSphereSurface(RenderAcisSatRecord record, out SurfaceDefinition surface)
    {
        surface = SurfaceDefinition.Empty;
        if (!record.Type.Contains("sphere", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var numbers = record.Numbers;
        const int baseCount = 10;
        if (numbers.Count < baseCount)
        {
            return false;
        }

        var rangeFound = TryReadTailRange(numbers, baseCount, out _, out _, out _, out _);
        var start = numbers.Count - baseCount - (rangeFound ? 4 : 0);
        if (start < 0)
        {
            return false;
        }

        var center = new XYZ(numbers[start], numbers[start + 1], numbers[start + 2]);
        var radius = Math.Abs(numbers[start + 3]);
        if (radius <= 0)
        {
            return false;
        }

        var u = new XYZ(numbers[start + 4], numbers[start + 5], numbers[start + 6]);
        var v = new XYZ(numbers[start + 7], numbers[start + 8], numbers[start + 9]);

        surface = SurfaceDefinition.FromSphere(record.Id, center, radius, u, v);
        return true;
    }

    private static void AlignCurvePoints(List<XYZ> points, XYZ start, XYZ end)
    {
        if (points.Count == 0)
        {
            return;
        }

        if (IsClose(points[0], end) && IsClose(points[^1], start))
        {
            points.Reverse();
        }

        if (!IsClose(points[0], start))
        {
            points.Insert(0, start);
        }

        if (!IsClose(points[^1], end))
        {
            points.Add(end);
        }
    }

    private static double ResolveEllipseAngle(
        XYZ center,
        XYZ u,
        XYZ v,
        double a,
        double b,
        XYZ point)
    {
        var delta = point.Subtract(center);
        var x = delta.Dot(u) / a;
        var y = delta.Dot(v) / b;
        return Math.Atan2(y, x);
    }

    private static int ResolveCurveSegments(double sweep, int baseSegments, int minSegments)
    {
        var segments = Math.Max(baseSegments, minSegments);
        var ratio = Math.Abs(sweep) / MathHelper.TwoPI;
        var scaled = (int)Math.Ceiling(segments * ratio);
        return Math.Clamp(scaled, minSegments, segments);
    }

    private static void ResolveSurfaceSegments(
        SurfaceDefinition surface,
        CurveSamplingSettings sampling,
        double uStart,
        double uEnd,
        double vStart,
        double vEnd,
        out int uSegments,
        out int vSegments)
    {
        var splineSegments = Math.Max(4, sampling.SplineSegments);
        var circleSegments = Math.Max(8, sampling.EllipseSegments);
        switch (surface.Kind)
        {
            case SurfaceKind.Sphere:
                uSegments = ResolveCurveSegments(uEnd - uStart, circleSegments, 8);
                vSegments = ResolveCurveSegments(vEnd - vStart, circleSegments, 6);
                break;
            case SurfaceKind.Torus:
                uSegments = ResolveCurveSegments(uEnd - uStart, circleSegments, 8);
                vSegments = ResolveCurveSegments(vEnd - vStart, circleSegments, 8);
                break;
            case SurfaceKind.Cylinder:
            case SurfaceKind.Cone:
                uSegments = ResolveCurveSegments(uEnd - uStart, circleSegments, 8);
                var heightSpan = Math.Abs(vEnd - vStart);
                var radius = surface.Radius > 1e-6 ? surface.Radius : surface.MinorRadius;
                var aspect = radius > 1e-6 ? heightSpan / radius : 1.0;
                var scaled = (int)Math.Ceiling(uSegments * aspect / MathHelper.TwoPI);
                var maxSegments = Math.Max(8, splineSegments);
                vSegments = Math.Clamp(scaled, 2, maxSegments);
                break;
            case SurfaceKind.Plane:
                uSegments = Math.Max(1, splineSegments);
                vSegments = Math.Max(1, splineSegments);
                break;
            default:
                uSegments = splineSegments;
                vSegments = splineSegments;
                break;
        }
    }

    private static bool TryGetUvBounds(
        IReadOnlyList<IReadOnlyList<XYZ>> loops,
        out double uMin,
        out double uMax,
        out double vMin,
        out double vMax)
    {
        uMin = double.PositiveInfinity;
        uMax = double.NegativeInfinity;
        vMin = double.PositiveInfinity;
        vMax = double.NegativeInfinity;

        foreach (var loop in loops)
        {
            for (var i = 0; i < loop.Count; i++)
            {
                var point = loop[i];
                if (point.X < uMin)
                {
                    uMin = point.X;
                }

                if (point.X > uMax)
                {
                    uMax = point.X;
                }

                if (point.Y < vMin)
                {
                    vMin = point.Y;
                }

                if (point.Y > vMax)
                {
                    vMax = point.Y;
                }
            }
        }

        if (double.IsInfinity(uMin) || double.IsInfinity(vMin))
        {
            return false;
        }

        return uMax > uMin && vMax > vMin;
    }

    private static List<MeshTessellator.Triangle> SubdivideUvTriangles(
        IReadOnlyList<MeshTessellator.Triangle> triangles,
        double maxU,
        double maxV)
    {
        if (triangles.Count == 0)
        {
            return new List<MeshTessellator.Triangle>();
        }

        if (maxU <= 0.0 || maxV <= 0.0)
        {
            return triangles is List<MeshTessellator.Triangle> list
                ? list
                : new List<MeshTessellator.Triangle>(triangles);
        }

        const int maxDepth = 2;
        const double edgeScale = 1.5;
        var thresholdU = maxU * edgeScale;
        var thresholdV = maxV * edgeScale;
        var result = new List<MeshTessellator.Triangle>(triangles.Count * 2);
        var stack = new Stack<(MeshTessellator.Triangle Triangle, int Depth)>(triangles.Count);
        for (var i = 0; i < triangles.Count; i++)
        {
            stack.Push((triangles[i], 0));
        }

        while (stack.Count > 0)
        {
            var (triangle, depth) = stack.Pop();
            if (depth >= maxDepth || !NeedsSubdivision(triangle, thresholdU, thresholdV))
            {
                result.Add(triangle);
                continue;
            }

            var ab = MidpointUv(triangle.A, triangle.B);
            var bc = MidpointUv(triangle.B, triangle.C);
            var ca = MidpointUv(triangle.C, triangle.A);

            var nextDepth = depth + 1;
            stack.Push((new MeshTessellator.Triangle(triangle.A, ab, ca), nextDepth));
            stack.Push((new MeshTessellator.Triangle(ab, triangle.B, bc), nextDepth));
            stack.Push((new MeshTessellator.Triangle(ca, bc, triangle.C), nextDepth));
            stack.Push((new MeshTessellator.Triangle(ab, bc, ca), nextDepth));
        }

        return result;
    }

    private static bool NeedsSubdivision(MeshTessellator.Triangle triangle, double maxU, double maxV)
    {
        return Exceeds(triangle.A, triangle.B, maxU, maxV) ||
               Exceeds(triangle.B, triangle.C, maxU, maxV) ||
               Exceeds(triangle.C, triangle.A, maxU, maxV);
    }

    private static bool Exceeds(XYZ a, XYZ b, double maxU, double maxV)
    {
        return Math.Abs(a.X - b.X) > maxU || Math.Abs(a.Y - b.Y) > maxV;
    }

    private static XYZ MidpointUv(XYZ a, XYZ b)
    {
        return new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, 0.0);
    }

    private static List<int> CollectPointReferences(
        RenderAcisSatRecord record,
        RenderAcisSatDocument document,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById)
    {
        var references = new List<int>();
        AppendPointReferences(references, record.References, recordsById);

        var visited = new HashSet<int>();
        foreach (var subtypeIndex in record.SubtypeReferences)
        {
            AppendSubtypeReferences(subtypeIndex, document, recordsById, visited, references);
        }

        return references;
    }

    private static void AppendSubtypeReferences(
        int subtypeIndex,
        RenderAcisSatDocument document,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        HashSet<int> visited,
        List<int> references)
    {
        if (subtypeIndex < 0 || subtypeIndex >= document.Subtypes.Count)
        {
            return;
        }

        if (!visited.Add(subtypeIndex))
        {
            return;
        }

        var subtype = document.Subtypes[subtypeIndex];
        AppendPointReferences(references, subtype.References, recordsById);

        foreach (var nested in subtype.SubtypeReferences)
        {
            AppendSubtypeReferences(nested, document, recordsById, visited, references);
        }
    }

    private static void AppendPointReferences(
        List<int> target,
        IReadOnlyList<int> references,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById)
    {
        foreach (var reference in references)
        {
            if (!recordsById.TryGetValue(reference, out var record))
            {
                continue;
            }

            if (IsPointRecord(record.Type) || IsVertexRecord(record.Type))
            {
                target.Add(reference);
            }
        }
    }

    private static int FindFirstReference(
        RenderAcisSatRecord record,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        Func<string, bool> typePredicate)
    {
        foreach (var reference in record.References)
        {
            if (recordsById.TryGetValue(reference, out var target) && typePredicate(target.Type))
            {
                return reference;
            }
        }

        return 0;
    }

    private static List<int> FindReferences(
        RenderAcisSatRecord record,
        IReadOnlyDictionary<int, RenderAcisSatRecord> recordsById,
        Func<string, bool> typePredicate)
    {
        var references = new List<int>();
        foreach (var reference in record.References)
        {
            if (recordsById.TryGetValue(reference, out var target) && typePredicate(target.Type))
            {
                references.Add(reference);
            }
        }

        return references;
    }

    private static bool IsPointRecord(string type)
    {
        return type.Contains("point", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("position", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVertexRecord(string type)
    {
        return type.Contains("vertex", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEdgeRecord(string type)
    {
        return type.Contains("edge", StringComparison.OrdinalIgnoreCase) &&
               !type.Contains("coedge", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCurveRecord(string type)
    {
        if (IsPcurveRecord(type))
        {
            return false;
        }

        return type.Contains("curve", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("ellipse", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("spline", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPcurveRecord(string type)
    {
        return type.Contains("pcurve", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("parcur", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoedgeRecord(string type)
    {
        return type.Contains("coedge", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoedgeReversed(RenderAcisSatRecord record)
    {
        for (var i = 0; i < record.Tokens.Count; i++)
        {
            var token = record.Tokens[i];
            if (string.Equals(token, "reversed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(token, "forward", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsLoopRecord(string type)
    {
        return type.Contains("loop", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFaceRecord(string type)
    {
        return type.Contains("face", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSurfaceRecord(string type)
    {
        return type.Contains("surface", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumericToken(string token)
    {
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static bool TryParseInt(string token, out int value)
    {
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            value = (int)Math.Round(number);
            return true;
        }

        value = 0;
        return false;
    }

    private static XYZ ApplyOffset(XYZ point, XYZ offset)
    {
        if (offset.IsZero())
        {
            return point;
        }

        return new XYZ(point.X + offset.X, point.Y + offset.Y, point.Z + offset.Z);
    }

    private static bool IsClose(XYZ left, XYZ right)
    {
        return DistanceSquared(left, right) <= VertexTolerance * VertexTolerance;
    }

    private static bool IsClose(Vector2 left, Vector2 right)
    {
        return DistanceSquared(left, right) <= (float)(VertexTolerance * VertexTolerance);
    }

    private static double DistanceSquared(XYZ left, XYZ right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private static float DistanceSquared(Vector2 left, Vector2 right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return dx * dx + dy * dy;
    }

    private static CurveSamplingSettings ResolveSampling(CadRenderSceneSettings settings)
    {
        return new CurveSamplingSettings(
            settings.ResolveCirclePrecision(),
            settings.ResolveSplinePrecision());
    }

    private readonly struct CurveSamplingSettings
    {
        public int EllipseSegments { get; }
        public int SplineSegments { get; }

        public CurveSamplingSettings(int ellipseSegments, int splineSegments)
        {
            EllipseSegments = ellipseSegments;
            SplineSegments = splineSegments;
        }
    }

    private sealed class NurbsCurveDefinition
    {
        public int Degree { get; }
        public double[] Knots { get; }
        public XYZ[] ControlPoints { get; }
        public double[] Weights { get; }

        public NurbsCurveDefinition(int degree, double[] knots, XYZ[] controlPoints, double[] weights)
        {
            Degree = degree;
            Knots = knots;
            ControlPoints = controlPoints;
            Weights = weights;
        }
    }

    private sealed class NurbsSurfaceDefinition
    {
        public int DegreeU { get; }
        public int DegreeV { get; }
        public double[] KnotsU { get; }
        public double[] KnotsV { get; }
        public XYZ[] ControlPoints { get; }
        public double[] Weights { get; }
        public int CountU { get; }
        public int CountV { get; }

        public NurbsSurfaceDefinition(
            int degreeU,
            int degreeV,
            double[] knotsU,
            double[] knotsV,
            XYZ[] controlPoints,
            double[] weights,
            int countU,
            int countV)
        {
            DegreeU = degreeU;
            DegreeV = degreeV;
            KnotsU = knotsU;
            KnotsV = knotsV;
            ControlPoints = controlPoints;
            Weights = weights;
            CountU = countU;
            CountV = countV;
        }

        public bool TryGetRange(out double uStart, out double uEnd, out double vStart, out double vEnd)
        {
            uStart = 0.0;
            uEnd = 0.0;
            vStart = 0.0;
            vEnd = 0.0;

            if (KnotsU.Length <= DegreeU || KnotsV.Length <= DegreeV)
            {
                return false;
            }

            var endUIndex = KnotsU.Length - DegreeU - 1;
            var endVIndex = KnotsV.Length - DegreeV - 1;
            if (endUIndex < 0 || endVIndex < 0)
            {
                return false;
            }

            uStart = KnotsU[DegreeU];
            uEnd = KnotsU[endUIndex];
            vStart = KnotsV[DegreeV];
            vEnd = KnotsV[endVIndex];
            return uEnd > uStart && vEnd > vStart;
        }
    }

    private enum SurfaceKind
    {
        Unknown,
        Nurbs,
        Plane,
        Sphere,
        Cone,
        Cylinder,
        Torus
    }

    private sealed class SurfaceDefinition
    {
        public static SurfaceDefinition Empty { get; } =
            new SurfaceDefinition(0, SurfaceKind.Unknown, null, XYZ.Zero, XYZ.AxisX, XYZ.AxisY, XYZ.AxisZ, XYZ.Zero, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

        public int Id { get; }
        public SurfaceKind Kind { get; }
        public NurbsSurfaceDefinition? Nurbs { get; }
        public XYZ Origin { get; }
        public XYZ UAxis { get; }
        public XYZ VAxis { get; }
        public XYZ WAxis { get; }
        public XYZ Center { get; }
        public double Radius { get; }
        public double MinorRadius { get; }
        public double Height { get; }
        private readonly double _uStart;
        private readonly double _uEnd;
        private readonly double _vStart;
        private readonly double _vEnd;

        private SurfaceDefinition(
            int id,
            SurfaceKind kind,
            NurbsSurfaceDefinition? nurbs,
            XYZ origin,
            XYZ uAxis,
            XYZ vAxis,
            XYZ wAxis,
            XYZ center,
            double radius,
            double minorRadius,
            double height,
            double uStart,
            double uEnd,
            double vStart,
            double vEnd)
        {
            Id = id;
            Kind = kind;
            Nurbs = nurbs;
            Origin = origin;
            UAxis = uAxis;
            VAxis = vAxis;
            WAxis = wAxis;
            Center = center;
            Radius = radius;
            MinorRadius = minorRadius;
            Height = height;
            _uStart = uStart;
            _uEnd = uEnd;
            _vStart = vStart;
            _vEnd = vEnd;
        }

        public static SurfaceDefinition FromNurbs(int id, NurbsSurfaceDefinition nurbs)
        {
            var rangeOk = nurbs.TryGetRange(out var uStart, out var uEnd, out var vStart, out var vEnd);
            return new SurfaceDefinition(
                id,
                SurfaceKind.Nurbs,
                nurbs,
                XYZ.Zero,
                XYZ.AxisX,
                XYZ.AxisY,
                XYZ.AxisZ,
                XYZ.Zero,
                0.0,
                0.0,
                0.0,
                rangeOk ? uStart : 0.0,
                rangeOk ? uEnd : 0.0,
                rangeOk ? vStart : 0.0,
                rangeOk ? vEnd : 0.0);
        }

        public static SurfaceDefinition FromPlane(int id, XYZ origin, XYZ uAxis, XYZ vAxis, XYZ wAxis)
        {
            return FromPlane(id, origin, uAxis, vAxis, wAxis, 0.0, 0.0, 0.0, 0.0);
        }

        public static SurfaceDefinition FromPlane(
            int id,
            XYZ origin,
            XYZ uAxis,
            XYZ vAxis,
            XYZ wAxis,
            double uStart,
            double uEnd,
            double vStart,
            double vEnd)
        {
            return new SurfaceDefinition(
                id,
                SurfaceKind.Plane,
                null,
                origin,
                uAxis,
                vAxis,
                wAxis,
                XYZ.Zero,
                0.0,
                0.0,
                0.0,
                uStart,
                uEnd,
                vStart,
                vEnd);
        }

        public static SurfaceDefinition FromSphere(int id, XYZ center, double radius, XYZ uAxis, XYZ vAxis)
        {
            var u = uAxis.IsZero() ? XYZ.AxisX : uAxis.Normalize();
            var v = vAxis.IsZero() ? XYZ.AxisY : vAxis.Normalize();
            var w = XYZ.Cross(u, v).Normalize();
            if (w.IsZero())
            {
                v = XYZ.AxisY;
                w = XYZ.Cross(u, v).Normalize();
            }

            v = XYZ.Cross(w, u).Normalize();
            return new SurfaceDefinition(
                id,
                SurfaceKind.Sphere,
                null,
                XYZ.Zero,
                u,
                v,
                w,
                center,
                radius,
                radius,
                0.0,
                0.0,
                MathHelper.TwoPI,
                0.0,
                Math.PI);
        }

        public static SurfaceDefinition FromCone(
            int id,
            XYZ center,
            XYZ axis,
            XYZ uAxis,
            XYZ vAxis,
            double majorRadius,
            double minorRadius,
            double height,
            double uStart,
            double uEnd,
            double vStart,
            double vEnd)
        {
            if (uEnd <= uStart || vEnd <= vStart)
            {
                uStart = 0.0;
                uEnd = MathHelper.TwoPI;
                vStart = 0.0;
                vEnd = height > 0.0 ? height : 1.0;
            }

            return new SurfaceDefinition(
                id,
                SurfaceKind.Cone,
                null,
                XYZ.Zero,
                uAxis,
                vAxis,
                axis,
                center,
                majorRadius,
                minorRadius,
                height,
                uStart,
                uEnd,
                vStart,
                vEnd);
        }

        public static SurfaceDefinition FromCylinder(
            int id,
            XYZ center,
            XYZ axis,
            XYZ uAxis,
            XYZ vAxis,
            double majorRadius,
            double minorRadius,
            double height,
            double uStart,
            double uEnd,
            double vStart,
            double vEnd)
        {
            if (uEnd <= uStart || vEnd <= vStart)
            {
                uStart = 0.0;
                uEnd = MathHelper.TwoPI;
                vStart = 0.0;
                vEnd = height > 0.0 ? height : 1.0;
            }

            return new SurfaceDefinition(
                id,
                SurfaceKind.Cylinder,
                null,
                XYZ.Zero,
                uAxis,
                vAxis,
                axis,
                center,
                majorRadius,
                minorRadius,
                height,
                uStart,
                uEnd,
                vStart,
                vEnd);
        }

        public static SurfaceDefinition FromTorus(
            int id,
            XYZ center,
            XYZ axis,
            XYZ uAxis,
            XYZ vAxis,
            double majorRadius,
            double minorRadius,
            double uStart,
            double uEnd,
            double vStart,
            double vEnd)
        {
            if (uEnd <= uStart || vEnd <= vStart)
            {
                uStart = 0.0;
                uEnd = MathHelper.TwoPI;
                vStart = 0.0;
                vEnd = MathHelper.TwoPI;
            }

            return new SurfaceDefinition(
                id,
                SurfaceKind.Torus,
                null,
                XYZ.Zero,
                uAxis,
                vAxis,
                axis,
                center,
                majorRadius,
                minorRadius,
                0.0,
                uStart,
                uEnd,
                vStart,
                vEnd);
        }

        public bool TryGetParameterRange(out double uStart, out double uEnd, out double vStart, out double vEnd)
        {
            uStart = _uStart;
            uEnd = _uEnd;
            vStart = _vStart;
            vEnd = _vEnd;

            return Kind switch
            {
                SurfaceKind.Nurbs => uEnd > uStart && vEnd > vStart,
                SurfaceKind.Sphere => uEnd > uStart && vEnd > vStart,
                SurfaceKind.Cone => uEnd > uStart && vEnd > vStart,
                SurfaceKind.Cylinder => uEnd > uStart && vEnd > vStart,
                SurfaceKind.Torus => uEnd > uStart && vEnd > vStart,
                SurfaceKind.Plane => uEnd > uStart && vEnd > vStart,
                _ => false
            };
        }

        public bool TryEvaluateNormal(double u, double v, out XYZ normal)
        {
            normal = XYZ.Zero;
            switch (Kind)
            {
                case SurfaceKind.Plane:
                    normal = WAxis.IsZero() ? XYZ.AxisZ : WAxis.Normalize();
                    return true;
                case SurfaceKind.Sphere:
                    {
                        var point = Evaluate(u, v);
                        var delta = point.Subtract(Center);
                        if (delta.IsZero())
                        {
                            return false;
                        }

                        normal = delta.Normalize();
                        return true;
                    }
                case SurfaceKind.Cone:
                    return TryEvaluateConeNormal(u, v, out normal);
                case SurfaceKind.Cylinder:
                    return TryEvaluateCylinderNormal(u, out normal);
                case SurfaceKind.Torus:
                    return TryEvaluateTorusNormal(u, v, out normal);
                case SurfaceKind.Nurbs:
                    return TryEvaluateNurbsNormal(u, v, out normal);
                default:
                    return false;
            }
        }

        public XYZ Evaluate(double u, double v)
        {
            switch (Kind)
            {
                case SurfaceKind.Nurbs when Nurbs is not null:
                    return RenderNurbsEvaluator.EvaluateSurface(
                        Nurbs.DegreeU,
                        Nurbs.DegreeV,
                        Nurbs.KnotsU,
                        Nurbs.KnotsV,
                        Nurbs.ControlPoints,
                        Nurbs.Weights,
                        Nurbs.CountU,
                        Nurbs.CountV,
                        u,
                        v);
                case SurfaceKind.Plane:
                    return new XYZ(
                        Origin.X + UAxis.X * u + VAxis.X * v,
                        Origin.Y + UAxis.Y * u + VAxis.Y * v,
                        Origin.Z + UAxis.Z * u + VAxis.Z * v);
                case SurfaceKind.Sphere:
                    {
                        var sinV = Math.Sin(v);
                        var cosV = Math.Cos(v);
                        var cosU = Math.Cos(u);
                        var sinU = Math.Sin(u);
                        var dir = new XYZ(
                            UAxis.X * (cosU * sinV) + VAxis.X * (sinU * sinV) + WAxis.X * cosV,
                            UAxis.Y * (cosU * sinV) + VAxis.Y * (sinU * sinV) + WAxis.Y * cosV,
                            UAxis.Z * (cosU * sinV) + VAxis.Z * (sinU * sinV) + WAxis.Z * cosV);
                        return new XYZ(
                            Center.X + dir.X * Radius,
                            Center.Y + dir.Y * Radius,
                            Center.Z + dir.Z * Radius);
                    }
                case SurfaceKind.Cone:
                    {
                        var cosU = Math.Cos(u);
                        var sinU = Math.Sin(u);
                        var radial = new XYZ(
                            UAxis.X * (Radius * cosU) + VAxis.X * (MinorRadius * sinU),
                            UAxis.Y * (Radius * cosU) + VAxis.Y * (MinorRadius * sinU),
                            UAxis.Z * (Radius * cosU) + VAxis.Z * (MinorRadius * sinU));
                        var scale = Height > 1e-6 ? 1.0 - (v / Height) : 1.0;
                        return new XYZ(
                            Center.X + WAxis.X * v + radial.X * scale,
                            Center.Y + WAxis.Y * v + radial.Y * scale,
                            Center.Z + WAxis.Z * v + radial.Z * scale);
                    }
                case SurfaceKind.Cylinder:
                    {
                        var cosU = Math.Cos(u);
                        var sinU = Math.Sin(u);
                        var radial = new XYZ(
                            UAxis.X * (Radius * cosU) + VAxis.X * (MinorRadius * sinU),
                            UAxis.Y * (Radius * cosU) + VAxis.Y * (MinorRadius * sinU),
                            UAxis.Z * (Radius * cosU) + VAxis.Z * (MinorRadius * sinU));
                        return new XYZ(
                            Center.X + WAxis.X * v + radial.X,
                            Center.Y + WAxis.Y * v + radial.Y,
                            Center.Z + WAxis.Z * v + radial.Z);
                    }
                case SurfaceKind.Torus:
                    {
                        var cosU = Math.Cos(u);
                        var sinU = Math.Sin(u);
                        var cosV = Math.Cos(v);
                        var sinV = Math.Sin(v);
                        var radial = new XYZ(
                            UAxis.X * cosU + VAxis.X * sinU,
                            UAxis.Y * cosU + VAxis.Y * sinU,
                            UAxis.Z * cosU + VAxis.Z * sinU);
                        var ring = Radius + MinorRadius * cosV;
                        return new XYZ(
                            Center.X + radial.X * ring + WAxis.X * (MinorRadius * sinV),
                            Center.Y + radial.Y * ring + WAxis.Y * (MinorRadius * sinV),
                            Center.Z + radial.Z * ring + WAxis.Z * (MinorRadius * sinV));
                    }
                default:
                    return XYZ.Zero;
            }
        }

        private bool TryEvaluateCylinderNormal(double u, out XYZ normal)
        {
            var cosU = Math.Cos(u);
            var sinU = Math.Sin(u);
            var du = new XYZ(
                -UAxis.X * (Radius * sinU) + VAxis.X * (MinorRadius * cosU),
                -UAxis.Y * (Radius * sinU) + VAxis.Y * (MinorRadius * cosU),
                -UAxis.Z * (Radius * sinU) + VAxis.Z * (MinorRadius * cosU));
            var dv = WAxis;
            normal = XYZ.Cross(du, dv);
            if (normal.IsZero())
            {
                return false;
            }

            normal = normal.Normalize();
            return true;
        }

        private bool TryEvaluateConeNormal(double u, double v, out XYZ normal)
        {
            var cosU = Math.Cos(u);
            var sinU = Math.Sin(u);
            var radial = new XYZ(
                UAxis.X * (Radius * cosU) + VAxis.X * (MinorRadius * sinU),
                UAxis.Y * (Radius * cosU) + VAxis.Y * (MinorRadius * sinU),
                UAxis.Z * (Radius * cosU) + VAxis.Z * (MinorRadius * sinU));
            var scale = Height > 1e-6 ? 1.0 - (v / Height) : 1.0;
            var du = new XYZ(
                (-UAxis.X * (Radius * sinU) + VAxis.X * (MinorRadius * cosU)) * scale,
                (-UAxis.Y * (Radius * sinU) + VAxis.Y * (MinorRadius * cosU)) * scale,
                (-UAxis.Z * (Radius * sinU) + VAxis.Z * (MinorRadius * cosU)) * scale);
            var dv = Height > 1e-6
                ? new XYZ(
                    WAxis.X + radial.X * (-1.0 / Height),
                    WAxis.Y + radial.Y * (-1.0 / Height),
                    WAxis.Z + radial.Z * (-1.0 / Height))
                : WAxis;
            normal = XYZ.Cross(du, dv);
            if (normal.IsZero())
            {
                return false;
            }

            normal = normal.Normalize();
            return true;
        }

        private bool TryEvaluateTorusNormal(double u, double v, out XYZ normal)
        {
            var cosU = Math.Cos(u);
            var sinU = Math.Sin(u);
            var cosV = Math.Cos(v);
            var sinV = Math.Sin(v);
            var radial = new XYZ(
                UAxis.X * cosU + VAxis.X * sinU,
                UAxis.Y * cosU + VAxis.Y * sinU,
                UAxis.Z * cosU + VAxis.Z * sinU);
            var radialPerp = new XYZ(
                -UAxis.X * sinU + VAxis.X * cosU,
                -UAxis.Y * sinU + VAxis.Y * cosU,
                -UAxis.Z * sinU + VAxis.Z * cosU);
            var du = new XYZ(
                radialPerp.X * (Radius + MinorRadius * cosV),
                radialPerp.Y * (Radius + MinorRadius * cosV),
                radialPerp.Z * (Radius + MinorRadius * cosV));
            var dv = new XYZ(
                radial.X * (-MinorRadius * sinV) + WAxis.X * (MinorRadius * cosV),
                radial.Y * (-MinorRadius * sinV) + WAxis.Y * (MinorRadius * cosV),
                radial.Z * (-MinorRadius * sinV) + WAxis.Z * (MinorRadius * cosV));
            normal = XYZ.Cross(du, dv);
            if (normal.IsZero())
            {
                return false;
            }

            normal = normal.Normalize();
            return true;
        }

        private bool TryEvaluateNurbsNormal(double u, double v, out XYZ normal)
        {
            normal = XYZ.Zero;
            if (Nurbs is null)
            {
                return false;
            }

            var du = (_uEnd - _uStart) * 1e-3;
            var dv = (_vEnd - _vStart) * 1e-3;
            if (du <= 0.0)
            {
                du = 1e-3;
            }

            if (dv <= 0.0)
            {
                dv = 1e-3;
            }

            var u1 = u + du;
            if (u1 > _uEnd && u - du >= _uStart)
            {
                u1 = u - du;
            }

            var v1 = v + dv;
            if (v1 > _vEnd && v - dv >= _vStart)
            {
                v1 = v - dv;
            }

            var p = Evaluate(u, v);
            var pu = Evaluate(u1, v);
            var pv = Evaluate(u, v1);
            normal = XYZ.FindNormal(p, pu, pv);
            if (normal.IsZero())
            {
                return false;
            }

            normal = normal.Normalize();
            return true;
        }
    }

    private sealed class CoedgeDefinition
    {
        public int EdgeId { get; }
        public int PcurveId { get; }
        public bool Reversed { get; }

        public CoedgeDefinition(int edgeId, int pcurveId, bool reversed)
        {
            EdgeId = edgeId;
            PcurveId = pcurveId;
            Reversed = reversed;
        }
    }

    private sealed class LoopDefinition
    {
        public IReadOnlyList<CoedgeDefinition> Coedges { get; }

        public LoopDefinition(IReadOnlyList<CoedgeDefinition> coedges)
        {
            Coedges = coedges;
        }
    }

    private sealed class FaceDefinition
    {
        public int SurfaceId { get; }
        public IReadOnlyList<int> LoopIds { get; }

        public FaceDefinition(int surfaceId, IReadOnlyList<int> loopIds)
        {
            SurfaceId = surfaceId;
            LoopIds = loopIds;
        }
    }

    private sealed class TokenCursor
    {
        private readonly IReadOnlyList<string> _tokens;
        private int _index;

        public TokenCursor(IReadOnlyList<string> tokens, int startIndex)
        {
            _tokens = tokens;
            _index = Math.Max(0, startIndex);
        }

        public int Index => _index;

        public bool TryReadToken(out string token)
        {
            while (_index < _tokens.Count)
            {
                var candidate = _tokens[_index++];
                if (candidate == "{" || candidate == "}")
                {
                    continue;
                }

                if (string.Equals(candidate, "ref", StringComparison.OrdinalIgnoreCase))
                {
                    if (_index < _tokens.Count)
                    {
                        _index++;
                    }
                    continue;
                }

                token = candidate;
                return true;
            }

            token = string.Empty;
            return false;
        }

        public bool TryReadInt(out int value)
        {
            if (TryReadToken(out var token))
            {
                return TryParseInt(token, out value);
            }

            value = 0;
            return false;
        }

        public bool TryReadDouble(out double value)
        {
            if (TryReadToken(out var token))
            {
                return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            value = 0.0;
            return false;
        }

        public bool TryPeekInt(out int value)
        {
            if (TryPeekToken(out var token))
            {
                return TryParseInt(token, out value);
            }

            value = 0;
            return false;
        }

        private bool TryPeekToken(out string token)
        {
            var index = _index;
            while (index < _tokens.Count)
            {
                var candidate = _tokens[index++];
                if (candidate == "{" || candidate == "}")
                {
                    continue;
                }

                if (string.Equals(candidate, "ref", StringComparison.OrdinalIgnoreCase))
                {
                    if (index < _tokens.Count)
                    {
                        index++;
                    }
                    continue;
                }

                token = candidate;
                return true;
            }

            token = string.Empty;
            return false;
        }
    }

    private sealed class CurveDefinition
    {
        public int Id { get; }
        public string Type { get; }
        public IReadOnlyList<double> Numbers { get; }
        public IReadOnlyList<int> PointReferences { get; }

        public CurveDefinition(int id, string type, IReadOnlyList<double> numbers, IReadOnlyList<int> pointReferences)
        {
            Id = id;
            Type = type;
            Numbers = numbers;
            PointReferences = pointReferences;
        }
    }

    private sealed class EdgeGeometry
    {
        public int StartVertexId { get; }
        public int EndVertexId { get; }
        public IReadOnlyList<XYZ> Points { get; }

        public EdgeGeometry(int startVertexId, int endVertexId, IReadOnlyList<XYZ> points)
        {
            StartVertexId = startVertexId;
            EndVertexId = endVertexId;
            Points = points;
        }
    }
}
