using System;
using System.Collections.Generic;
using System.Reflection;
using ACadInspector.Core;
using ACadSharp;

namespace ACadInspector.Scripting;

public sealed class CadScriptHostOptions
{
    public CadScriptHostOptions(IReadOnlyList<string> imports, IReadOnlyList<Assembly> assemblies)
    {
        Imports = imports;
        Assemblies = assemblies;
    }

    public IReadOnlyList<string> Imports { get; }

    public IReadOnlyList<Assembly> Assemblies { get; }

    public static CadScriptHostOptions CreateDefault()
    {
        var imports = new[]
        {
            "System",
            "System.IO",
            "System.Linq",
            "System.Collections.Generic",
            "ACadSharp",
            "ACadSharp.Entities",
            "ACadSharp.Tables",
            "ACadSharp.Objects",
            "ACadSharp.Header",
            "ACadInspector.Core"
        };

        var assemblies = new List<Assembly>
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(List<>).Assembly,
            typeof(CadDocument).Assembly,
            typeof(CadFileFormat).Assembly,
            typeof(CadBatchQueryEngine).Assembly
        };

        return new CadScriptHostOptions(imports, assemblies);
    }
}
