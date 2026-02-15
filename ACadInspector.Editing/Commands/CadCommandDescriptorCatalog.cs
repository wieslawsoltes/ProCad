namespace ACadInspector.Editing.Commands;

internal static class CadCommandDescriptorCatalog
{
    private static readonly IReadOnlyDictionary<string, DescriptorTemplate> Templates =
        new Dictionary<string, DescriptorTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            ["LINE"] = T(
                "Creates a line segment between two points.",
                "LINE x1,y1 x2,y2",
                [P("start", CadCommandParameterKind.Coordinate), P("end", CadCommandParameterKind.Coordinate)]),
            ["PLINE"] = T(
                "Creates a lightweight polyline from a point sequence.",
                "PLINE p1 p2 [p3 ...]",
                [P("p1", CadCommandParameterKind.Coordinate), P("p2", CadCommandParameterKind.Coordinate), P("p3+", CadCommandParameterKind.Coordinate, optional: true)],
                [K("Close"), K("Undo")]),
            ["XLINE"] = T(
                "Creates an infinite construction line.",
                "XLINE basePoint throughPoint",
                [P("basePoint", CadCommandParameterKind.Coordinate), P("throughPoint", CadCommandParameterKind.Coordinate)]),
            ["RAY"] = T(
                "Creates a semi-infinite construction ray.",
                "RAY startPoint throughPoint",
                [P("startPoint", CadCommandParameterKind.Coordinate), P("throughPoint", CadCommandParameterKind.Coordinate)]),
            ["CIRCLE"] = T(
                "Creates a circle from center and radius.",
                "CIRCLE center radius",
                [P("center", CadCommandParameterKind.Coordinate), P("radius", CadCommandParameterKind.Distance)]),
            ["ARC"] = T(
                "Creates an arc from center, radius, start and end angles.",
                "ARC center radius startAngle endAngle",
                [P("center", CadCommandParameterKind.Coordinate), P("radius", CadCommandParameterKind.Distance), P("startAngle", CadCommandParameterKind.Angle), P("endAngle", CadCommandParameterKind.Angle)]),
            ["ELLIPSE"] = T(
                "Creates a full ellipse or elliptical arc.",
                "ELLIPSE center majorAxisEnd ratio [startDeg endDeg]",
                [P("center", CadCommandParameterKind.Coordinate), P("majorAxisEnd", CadCommandParameterKind.Coordinate), P("ratio", CadCommandParameterKind.Number), P("startDeg", CadCommandParameterKind.Angle, optional: true), P("endDeg", CadCommandParameterKind.Angle, optional: true)]),
            ["SPLINE"] = T(
                "Creates a fit-point spline.",
                "SPLINE p1 p2 [p3 ...] [CLOSE]",
                [P("p1", CadCommandParameterKind.Coordinate), P("p2", CadCommandParameterKind.Coordinate), P("p3+", CadCommandParameterKind.Coordinate, optional: true)],
                [K("CLOSE")]),
            ["RECTANG"] = T(
                "Creates a rectangular closed polyline.",
                "RECTANG corner1 corner2",
                [P("corner1", CadCommandParameterKind.Coordinate), P("corner2", CadCommandParameterKind.Coordinate)]),
            ["POLYGON"] = T(
                "Creates an inscribed or circumscribed polygon.",
                "POLYGON sides center radius [INSCRIBED|CIRCUMSCRIBED]",
                [P("sides", CadCommandParameterKind.Integer), P("center", CadCommandParameterKind.Coordinate), P("radius", CadCommandParameterKind.Distance), P("mode", CadCommandParameterKind.Keyword, optional: true, defaultValue: "INSCRIBED")],
                [K("INSCRIBED"), K("CIRCUMSCRIBED")]),
            ["POINT"] = T(
                "Creates a point entity.",
                "POINT location",
                [P("location", CadCommandParameterKind.Coordinate)]),
            ["INSERT"] = T(
                "Inserts a block reference.",
                "INSERT blockName insertionPoint [scale] [rotationDeg]",
                [P("blockName", CadCommandParameterKind.Text), P("insertionPoint", CadCommandParameterKind.Coordinate), P("scale", CadCommandParameterKind.Number, optional: true, defaultValue: "1"), P("rotationDeg", CadCommandParameterKind.Angle, optional: true, defaultValue: "0")]),
            ["XREFRELOAD"] = T(
                "Reloads a referenced external block definition.",
                "XREFRELOAD blockName",
                [P("blockName", CadCommandParameterKind.Text)]),
            ["XREFBIND"] = T(
                "Converts an external reference into a local block definition.",
                "XREFBIND blockName",
                [P("blockName", CadCommandParameterKind.Text)]),
            ["XREFDETACH"] = T(
                "Detaches an external reference and removes all inserts that reference it.",
                "XREFDETACH blockName",
                [P("blockName", CadCommandParameterKind.Text)]),
            ["TEXT"] = T(
                "Creates single-line text.",
                "TEXT insertionPoint height [rotationDeg] value",
                [P("insertionPoint", CadCommandParameterKind.Coordinate), P("height", CadCommandParameterKind.Distance), P("rotationDeg", CadCommandParameterKind.Angle, optional: true, defaultValue: "0"), P("value", CadCommandParameterKind.Text)]),
            ["MTEXT"] = T(
                "Creates multiline text.",
                "MTEXT insertionPoint height width [rotationDeg] value",
                [P("insertionPoint", CadCommandParameterKind.Coordinate), P("height", CadCommandParameterKind.Distance), P("width", CadCommandParameterKind.Distance), P("rotationDeg", CadCommandParameterKind.Angle, optional: true, defaultValue: "0"), P("value", CadCommandParameterKind.Text)]),
            ["DIMLINEAR"] = T(
                "Creates a linear dimension.",
                "DIMLINEAR p1 p2 dimLinePoint",
                [P("p1", CadCommandParameterKind.Coordinate), P("p2", CadCommandParameterKind.Coordinate), P("dimLinePoint", CadCommandParameterKind.Coordinate)]),
            ["DIMALIGNED"] = T(
                "Creates an aligned dimension.",
                "DIMALIGNED p1 p2 dimLinePoint",
                [P("p1", CadCommandParameterKind.Coordinate), P("p2", CadCommandParameterKind.Coordinate), P("dimLinePoint", CadCommandParameterKind.Coordinate)]),
            ["DIMRADIUS"] = T(
                "Creates a radius dimension.",
                "DIMRADIUS centerOrArcPoint dimLinePoint",
                [P("centerOrArcPoint", CadCommandParameterKind.Coordinate), P("dimLinePoint", CadCommandParameterKind.Coordinate)]),
            ["DIMDIAMETER"] = T(
                "Creates a diameter dimension.",
                "DIMDIAMETER centerOrArcPoint dimLinePoint",
                [P("centerOrArcPoint", CadCommandParameterKind.Coordinate), P("dimLinePoint", CadCommandParameterKind.Coordinate)]),
            ["DIMANGULAR"] = T(
                "Creates an angular dimension.",
                "DIMANGULAR p1 p2 vertex dimArcPoint",
                [P("p1", CadCommandParameterKind.Coordinate), P("p2", CadCommandParameterKind.Coordinate), P("vertex", CadCommandParameterKind.Coordinate), P("dimArcPoint", CadCommandParameterKind.Coordinate)]),
            ["LEADER"] = T(
                "Creates a leader annotation.",
                "LEADER startPoint landingPoint [p3 ...]",
                [P("startPoint", CadCommandParameterKind.Coordinate), P("landingPoint", CadCommandParameterKind.Coordinate), P("p3+", CadCommandParameterKind.Coordinate, optional: true)]),
            ["MLEADER"] = T(
                "Creates a multileader annotation.",
                "MLEADER startPoint landingPoint [p3 ...]",
                [P("startPoint", CadCommandParameterKind.Coordinate), P("landingPoint", CadCommandParameterKind.Coordinate), P("p3+", CadCommandParameterKind.Coordinate, optional: true)]),
            ["HATCH"] = T(
                "Creates hatch from closed polyline/circle/ellipse/spline/hatch boundaries.",
                "HATCH [SOLID|patternName] [boundaryHandles...]",
                [P("patternOrSolid", CadCommandParameterKind.Text, optional: true, defaultValue: "SOLID"), P("boundaryHandles", CadCommandParameterKind.Handle, optional: true)],
                [K("SOLID")]),
            ["BOUNDARY"] = T(
                "Creates boundary polylines from source loops.",
                "BOUNDARY [sourceHandles...]",
                [P("sourceHandles", CadCommandParameterKind.Handle, optional: true)]),
            ["ERASE"] = T(
                "Deletes selected or specified entities.",
                "ERASE [handles...]",
                [P("handles", CadCommandParameterKind.Handle, optional: true)]),
            ["MOVE"] = T(
                "Moves entities by displacement.",
                "MOVE dx,dy[,dz] [handles...]",
                [P("displacement", CadCommandParameterKind.Coordinate), P("handles", CadCommandParameterKind.Handle, optional: true)]),
            ["COPY"] = T(
                "Copies entities by displacement.",
                "COPY dx,dy[,dz] [handles...]",
                [P("displacement", CadCommandParameterKind.Coordinate), P("handles", CadCommandParameterKind.Handle, optional: true)]),
            ["ROTATE"] = T(
                "Rotates entities around an optional center.",
                "ROTATE angleDeg [center] [handles...]",
                [P("angleDeg", CadCommandParameterKind.Angle), P("center", CadCommandParameterKind.Coordinate, optional: true), P("handles", CadCommandParameterKind.Handle, optional: true)]),
            ["SCALE"] = T(
                "Scales entities around an optional center.",
                "SCALE factor [center] [handles...]",
                [P("factor", CadCommandParameterKind.Number), P("center", CadCommandParameterKind.Coordinate, optional: true), P("handles", CadCommandParameterKind.Handle, optional: true)]),
            ["MIRROR"] = T(
                "Mirrors entities across an axis line.",
                "MIRROR axisStart axisEnd [handles...]",
                [P("axisStart", CadCommandParameterKind.Coordinate), P("axisEnd", CadCommandParameterKind.Coordinate), P("handles", CadCommandParameterKind.Handle, optional: true)]),
            ["STRETCH"] = T(
                "Stretches nearest grip/vertex by delta.",
                "STRETCH dx,dy[,dz] gripPoint [handles...]",
                [P("displacement", CadCommandParameterKind.Coordinate), P("gripPoint", CadCommandParameterKind.Coordinate), P("handles", CadCommandParameterKind.Handle, optional: true)]),
            ["OFFSET"] = T(
                "Creates offset copies of supported geometry.",
                "OFFSET distance [LEFT|RIGHT|OUTER|INNER] [handles...]",
                [P("distance", CadCommandParameterKind.Distance), P("side", CadCommandParameterKind.Keyword, optional: true), P("handles", CadCommandParameterKind.Handle, optional: true)],
                [K("LEFT"), K("RIGHT"), K("OUTER"), K("INNER")]),
            ["TRIM"] = T(
                "Trims line or open polyline endpoint geometry to a boundary.",
                "TRIM boundaryHandle targetHandle [START|END]",
                [P("boundaryHandle", CadCommandParameterKind.Handle), P("targetHandle", CadCommandParameterKind.Handle), P("side", CadCommandParameterKind.Keyword, optional: true)],
                [K("START"), K("END")]),
            ["EXTEND"] = T(
                "Extends line or open polyline endpoint geometry to a boundary.",
                "EXTEND boundaryHandle targetHandle [START|END]",
                [P("boundaryHandle", CadCommandParameterKind.Handle), P("targetHandle", CadCommandParameterKind.Handle), P("side", CadCommandParameterKind.Keyword, optional: true)],
                [K("START"), K("END")]),
            ["BREAK"] = T(
                "Breaks line/open polyline at one point, or between two points.",
                "BREAK targetHandle firstPoint [secondPoint]",
                [P("targetHandle", CadCommandParameterKind.Handle), P("firstPoint", CadCommandParameterKind.Coordinate), P("secondPoint", CadCommandParameterKind.Coordinate, optional: true)]),
            ["JOIN"] = T(
                "Joins compatible entities into continuous results.",
                "JOIN handle1 handle2 [handleN...]",
                [P("handle1", CadCommandParameterKind.Handle), P("handle2", CadCommandParameterKind.Handle), P("handles", CadCommandParameterKind.Handle, optional: true)]),
            ["FILLET"] = T(
                "Creates tangent arc between two lines/open polylines.",
                "FILLET radius [entity1 entity2]",
                [P("radius", CadCommandParameterKind.Distance), P("entity1", CadCommandParameterKind.Handle, optional: true), P("entity2", CadCommandParameterKind.Handle, optional: true)]),
            ["CHAMFER"] = T(
                "Creates chamfer between two lines/open polylines.",
                "CHAMFER distance1 [distance2] [entity1 entity2]",
                [P("distance1", CadCommandParameterKind.Distance), P("distance2", CadCommandParameterKind.Distance, optional: true), P("entity1", CadCommandParameterKind.Handle, optional: true), P("entity2", CadCommandParameterKind.Handle, optional: true)]),
            ["ARRAY"] = T(
                "Creates rectangular or polar arrays.",
                "ARRAY rows cols rowSpacing colSpacing [handles...]",
                [P("rows", CadCommandParameterKind.Integer), P("cols", CadCommandParameterKind.Integer), P("rowSpacing", CadCommandParameterKind.Distance), P("colSpacing", CadCommandParameterKind.Distance), P("handles", CadCommandParameterKind.Handle, optional: true)],
                [K("POLAR"), K("PATH")]),
            ["EXPLODE"] = T(
                "Explodes supported entities into simpler geometry.",
                "EXPLODE [handles...]",
                [P("handles", CadCommandParameterKind.Handle, optional: true)]),
            ["ALIGN"] = T(
                "Aligns entities from source to destination points.",
                "ALIGN source1 dest1 [source2 dest2] [handles...]",
                [P("source1", CadCommandParameterKind.Coordinate), P("dest1", CadCommandParameterKind.Coordinate), P("source2", CadCommandParameterKind.Coordinate, optional: true), P("dest2", CadCommandParameterKind.Coordinate, optional: true), P("handles", CadCommandParameterKind.Handle, optional: true)]),
            ["MATCHPROP"] = T(
                "Copies visual properties from source to targets.",
                "MATCHPROP sourceHandle [targetHandles...]",
                [P("sourceHandle", CadCommandParameterKind.Handle), P("targetHandles", CadCommandParameterKind.Handle, optional: true)]),
            ["COPYCLIP"] = T(
                "Copies entities to internal CAD clipboard.",
                "COPYCLIP [handles...]",
                [P("handles", CadCommandParameterKind.Handle, optional: true)]),
            ["CUT"] = T(
                "Cuts entities to internal CAD clipboard.",
                "CUT [handles...]",
                [P("handles", CadCommandParameterKind.Handle, optional: true)]),
            ["PASTECLIP"] = T(
                "Pastes clipboard entities at insertion point.",
                "PASTECLIP [insertionPoint]",
                [P("insertionPoint", CadCommandParameterKind.Coordinate, optional: true)]),
            ["PASTEORIG"] = T(
                "Pastes clipboard entities at original base point.",
                "PASTEORIG"),
            ["UNDO"] = T(
                "Undoes the most recent local operation.",
                "UNDO"),
            ["REDO"] = T(
                "Redoes the most recently undone local operation.",
                "REDO"),
            ["CLEARSEL"] = T(
                "Clears current selection.",
                "CLEARSEL"),
            ["SCRIPT"] = T(
                "Runs commands from a script file.",
                "SCRIPT path [CONTINUE]",
                [P("path", CadCommandParameterKind.Text), P("continueFlag", CadCommandParameterKind.Flag, optional: true)],
                [K("CONTINUE")]),
            ["HELP"] = T(
                "Lists available commands or help for one command.",
                "HELP [command]",
                [P("command", CadCommandParameterKind.Text, optional: true)])
        };

    public static bool TryCreate(ICadCommandHandler handler, out CadCommandDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!Templates.TryGetValue(handler.Name, out var template))
        {
            descriptor = null!;
            return false;
        }

        descriptor = template.Create(handler);
        return true;
    }

    public static bool HasTemplate(string commandName)
    {
        return !string.IsNullOrWhiteSpace(commandName) && Templates.ContainsKey(commandName.Trim());
    }

    private static DescriptorTemplate T(
        string description,
        string usage,
        IReadOnlyList<CadCommandParameterDescriptor>? parameters = null,
        IReadOnlyList<CadCommandKeywordDescriptor>? keywords = null)
    {
        return new DescriptorTemplate(
            Description: description,
            Syntaxes:
            [
                new CadCommandSyntax(
                    Usage: usage,
                    Description: description,
                    Parameters: parameters ?? Array.Empty<CadCommandParameterDescriptor>(),
                    Keywords: keywords ?? Array.Empty<CadCommandKeywordDescriptor>())
            ]);
    }

    private static CadCommandParameterDescriptor P(
        string name,
        CadCommandParameterKind kind,
        bool optional = false,
        string? defaultValue = null,
        string? example = null,
        string description = "")
    {
        return new CadCommandParameterDescriptor(
            Name: name,
            Kind: kind,
            IsOptional: optional,
            Description: description,
            DefaultValue: defaultValue,
            Example: example);
    }

    private static CadCommandKeywordDescriptor K(string keyword, string description = "")
    {
        return new CadCommandKeywordDescriptor(keyword, description);
    }

    private sealed record DescriptorTemplate(
        string Description,
        IReadOnlyList<CadCommandSyntax> Syntaxes)
    {
        public CadCommandDescriptor Create(ICadCommandHandler handler)
        {
            var aliases = handler.Aliases.Count == 0
                ? Array.Empty<string>()
                : handler.Aliases
                    .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            return new CadCommandDescriptor(
                Name: handler.Name,
                Aliases: aliases,
                Description: Description,
                Syntaxes: Syntaxes);
        }
    }
}
