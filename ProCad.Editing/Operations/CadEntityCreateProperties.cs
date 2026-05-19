using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Header;
using ACadSharp.Tables;

namespace ProCad.Editing.Operations;

public readonly record struct CadEntityCreateProperties(
    string LayerName,
    string LineTypeName,
    Color Color,
    LineWeightType LineWeight,
    double LineTypeScale,
    bool IsInvisible,
    Transparency Transparency,
    string? TextStyleName,
    string? DimensionStyleName)
{
    public static CadEntityCreateProperties FromHeader(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var header = document.Header ?? new CadHeader();
        var layerName = string.IsNullOrWhiteSpace(header.CurrentLayerName)
            ? Layer.DefaultName
            : header.CurrentLayerName;
        var lineTypeName = string.IsNullOrWhiteSpace(header.CurrentLineTypeName)
            ? LineType.ByLayerName
            : header.CurrentLineTypeName;

        return new CadEntityCreateProperties(
            LayerName: layerName,
            LineTypeName: lineTypeName,
            Color: header.CurrentEntityColor,
            LineWeight: header.CurrentEntityLineWeight,
            LineTypeScale: header.CurrentEntityLinetypeScale,
            IsInvisible: false,
            Transparency: Transparency.ByLayer,
            TextStyleName: string.IsNullOrWhiteSpace(header.CurrentTextStyleName) ? null : header.CurrentTextStyleName,
            DimensionStyleName: string.IsNullOrWhiteSpace(header.CurrentDimensionStyleName) ? null : header.CurrentDimensionStyleName);
    }

    public static CadEntityCreateProperties FromEntity(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        string? textStyleName = null;
        string? dimensionStyleName = null;

        if (entity is IText text && text.Style is not null)
        {
            textStyleName = text.Style.Name;
        }

        if (entity is MultiLeader mLeader && mLeader.TextStyle is not null)
        {
            textStyleName ??= mLeader.TextStyle.Name;
        }

        if (entity is Dimension dimension && dimension.Style is not null)
        {
            dimensionStyleName = dimension.Style.Name;
        }
        else if (entity is Leader leader && leader.Style is not null)
        {
            dimensionStyleName = leader.Style.Name;
        }

        return new CadEntityCreateProperties(
            LayerName: entity.Layer?.Name ?? Layer.DefaultName,
            LineTypeName: entity.LineType?.Name ?? LineType.ByLayerName,
            Color: entity.Color,
            LineWeight: entity.LineWeight,
            LineTypeScale: entity.LineTypeScale,
            IsInvisible: entity.IsInvisible,
            Transparency: entity.Transparency,
            TextStyleName: textStyleName,
            DimensionStyleName: dimensionStyleName);
    }
}
