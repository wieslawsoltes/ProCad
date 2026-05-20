using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ProCad.Rendering;

public sealed class RenderDiagnostics
{
    private readonly Dictionary<string, RenderUnsupportedEntityInfo> _unsupported =
        new(StringComparer.Ordinal);
    private readonly List<RenderDynamicBlockMappingIssue> _dynamicBlockMappings = new();

    private readonly int _maxSamples;

    public IReadOnlyDictionary<string, RenderUnsupportedEntityInfo> Unsupported => _unsupported;
    public IReadOnlyList<RenderDynamicBlockMappingIssue> DynamicBlockMappings => _dynamicBlockMappings;

    public RenderDiagnostics(int maxSamples = 5)
    {
        _maxSamples = Math.Max(1, maxSamples);
    }

    public void TrackUnsupported(Entity entity)
    {
        if (entity is null)
        {
            return;
        }

        var typeName = entity.GetType().Name;
        if (!_unsupported.TryGetValue(typeName, out var info))
        {
            info = new RenderUnsupportedEntityInfo(typeName, _maxSamples);
            _unsupported[typeName] = info;
        }

        info.Add(entity.Handle);
    }

    public void TrackDynamicBlockMappingMismatch(Insert insert, BlockRecord source, BlockRecord representation)
    {
        if (insert is null || source is null || representation is null)
        {
            return;
        }

        _dynamicBlockMappings.Add(new RenderDynamicBlockMappingIssue(insert.Handle, source.Handle, representation.Handle));
    }
}

public sealed class RenderUnsupportedEntityInfo
{
    private readonly List<ulong> _sampleHandles;
    private readonly int _maxSamples;

    public string EntityType { get; }
    public int Count { get; private set; }
    public IReadOnlyList<ulong> SampleHandles => _sampleHandles;

    internal RenderUnsupportedEntityInfo(string entityType, int maxSamples)
    {
        EntityType = entityType;
        _maxSamples = maxSamples;
        _sampleHandles = new List<ulong>(maxSamples);
    }

    internal void Add(ulong handle)
    {
        Count++;
        if (_sampleHandles.Count < _maxSamples)
        {
            _sampleHandles.Add(handle);
        }
    }
}

public sealed class RenderDynamicBlockMappingIssue
{
    public ulong InsertHandle { get; }
    public ulong SourceBlockHandle { get; }
    public ulong RepresentationHandle { get; }

    public RenderDynamicBlockMappingIssue(ulong insertHandle, ulong sourceBlockHandle, ulong representationHandle)
    {
        InsertHandle = insertHandle;
        SourceBlockHandle = sourceBlockHandle;
        RepresentationHandle = representationHandle;
    }
}
