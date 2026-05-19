namespace ProCad.Core;

public sealed record CadWriteOptions(
    CadFileFormat Format,
    bool WriteBinaryDxf = false,
    bool WriteAllDxfHeaderVariables = false
);
