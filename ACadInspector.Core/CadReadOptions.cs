namespace ACadInspector.Core;

public sealed record CadReadOptions(
    CadFileFormat? Format = null,
    bool ReadSummaryInfo = true,
    bool ClearDxfCache = true,
    bool CreateDxfDefaults = false,
    bool DwgCrcCheck = false
);
