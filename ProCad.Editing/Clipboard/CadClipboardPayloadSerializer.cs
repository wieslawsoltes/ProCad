using System.Text.Json;
using System.Text.Json.Serialization;
using CSMath;

namespace ProCad.Editing.Clipboard;

public static class CadClipboardPayloadSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(CadClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var serializable = ToSerializable(payload);
        return JsonSerializer.Serialize(serializable, SerializerOptions);
    }

    public static bool TryDeserialize(string? json, out CadClipboardPayload payload)
    {
        payload = null!;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        SerializablePayload? serializable;
        try
        {
            serializable = JsonSerializer.Deserialize<SerializablePayload>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (serializable is null || serializable.Entities is null)
        {
            return false;
        }

        var entities = new List<CadClipboardEntity>(serializable.Entities.Count);
        for (var index = 0; index < serializable.Entities.Count; index++)
        {
            var serializableEntity = serializable.Entities[index];
            if (string.IsNullOrWhiteSpace(serializableEntity.EntityType))
            {
                continue;
            }

            var payloadMap = new Dictionary<string, string>(StringComparer.Ordinal);
            if (serializableEntity.Payload is not null)
            {
                foreach (var entry in serializableEntity.Payload)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key))
                    {
                        continue;
                    }

                    payloadMap[entry.Key] = entry.Value ?? string.Empty;
                }
            }

            entities.Add(new CadClipboardEntity(
                serializableEntity.EntityType,
                payloadMap,
                new XYZ(
                    serializableEntity.ReferencePoint.X,
                    serializableEntity.ReferencePoint.Y,
                    serializableEntity.ReferencePoint.Z)));
        }

        if (entities.Count == 0)
        {
            return false;
        }

        payload = new CadClipboardPayload(
            entities,
            new XYZ(serializable.BasePoint.X, serializable.BasePoint.Y, serializable.BasePoint.Z),
            string.IsNullOrWhiteSpace(serializable.SchemaVersion) ? "1.0" : serializable.SchemaVersion!,
            ToDependencies(serializable.Dependencies));
        return true;
    }

    private static SerializablePayload ToSerializable(CadClipboardPayload payload)
    {
        var entities = new List<SerializableEntity>(payload.Entities.Count);
        for (var index = 0; index < payload.Entities.Count; index++)
        {
            var entity = payload.Entities[index];
            var entityPayload = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entity.Payload)
            {
                entityPayload[entry.Key] = entry.Value;
            }

            entities.Add(new SerializableEntity(
                entity.EntityType,
                entityPayload,
                new SerializablePoint(
                    entity.ReferencePoint.X,
                    entity.ReferencePoint.Y,
                    entity.ReferencePoint.Z)));
        }

        return new SerializablePayload(
            entities,
            new SerializablePoint(payload.BasePoint.X, payload.BasePoint.Y, payload.BasePoint.Z),
            payload.SchemaVersion,
            ToSerializable(payload.Dependencies));
    }

    private static SerializableDependencies? ToSerializable(CadClipboardDependencies? dependencies)
    {
        if (dependencies is null)
        {
            return null;
        }

        var blocks = new List<SerializableBlockDependency>(dependencies.BlockDependencies.Count);
        for (var blockIndex = 0; blockIndex < dependencies.BlockDependencies.Count; blockIndex++)
        {
            var block = dependencies.BlockDependencies[blockIndex];
            var entities = new List<SerializableEntity>(block.Entities.Count);
            for (var entityIndex = 0; entityIndex < block.Entities.Count; entityIndex++)
            {
                var entity = block.Entities[entityIndex];
                var payload = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in entity.Payload)
                {
                    payload[entry.Key] = entry.Value;
                }

                entities.Add(new SerializableEntity(
                    entity.EntityType,
                    payload,
                    new SerializablePoint(
                        entity.ReferencePoint.X,
                        entity.ReferencePoint.Y,
                        entity.ReferencePoint.Z)));
            }

            blocks.Add(new SerializableBlockDependency(block.Name, entities));
        }

        return new SerializableDependencies(
            NormalizeStringList(dependencies.LayerNames),
            NormalizeStringList(dependencies.LineTypeNames),
            NormalizeStringList(dependencies.TextStyleNames),
            NormalizeStringList(dependencies.DimensionStyleNames),
            blocks);
    }

    private static CadClipboardDependencies ToDependencies(SerializableDependencies? dependencies)
    {
        if (dependencies is null)
        {
            return CadClipboardDependencies.Empty;
        }

        var blocks = new List<CadClipboardBlockDependency>(dependencies.BlockDependencies?.Count ?? 0);
        if (dependencies.BlockDependencies is not null)
        {
            for (var blockIndex = 0; blockIndex < dependencies.BlockDependencies.Count; blockIndex++)
            {
                var block = dependencies.BlockDependencies[blockIndex];
                if (string.IsNullOrWhiteSpace(block.Name))
                {
                    continue;
                }

                var blockEntities = new List<CadClipboardEntity>(block.Entities?.Count ?? 0);
                if (block.Entities is not null)
                {
                    for (var entityIndex = 0; entityIndex < block.Entities.Count; entityIndex++)
                    {
                        var entity = block.Entities[entityIndex];
                        if (string.IsNullOrWhiteSpace(entity.EntityType))
                        {
                            continue;
                        }

                        var payload = new Dictionary<string, string>(StringComparer.Ordinal);
                        if (entity.Payload is not null)
                        {
                            foreach (var entry in entity.Payload)
                            {
                                if (string.IsNullOrWhiteSpace(entry.Key))
                                {
                                    continue;
                                }

                                payload[entry.Key] = entry.Value ?? string.Empty;
                            }
                        }

                        blockEntities.Add(new CadClipboardEntity(
                            entity.EntityType,
                            payload,
                            new XYZ(entity.ReferencePoint.X, entity.ReferencePoint.Y, entity.ReferencePoint.Z)));
                    }
                }

                blocks.Add(new CadClipboardBlockDependency(block.Name, blockEntities));
            }
        }

        return new CadClipboardDependencies(
            NormalizeStringList(dependencies.LayerNames),
            NormalizeStringList(dependencies.LineTypeNames),
            NormalizeStringList(dependencies.TextStyleNames),
            NormalizeStringList(dependencies.DimensionStyleNames),
            blocks);
    }

    private static IReadOnlyList<string> NormalizeStringList(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (!string.IsNullOrWhiteSpace(value))
            {
                set.Add(value.Trim());
            }
        }

        if (set.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new string[set.Count];
        set.CopyTo(result);
        return result;
    }

    private sealed record SerializablePayload(
        List<SerializableEntity> Entities,
        SerializablePoint BasePoint,
        string? SchemaVersion,
        SerializableDependencies? Dependencies);

    private sealed record SerializableEntity(
        string EntityType,
        Dictionary<string, string>? Payload,
        SerializablePoint ReferencePoint);

    private sealed record SerializableDependencies(
        IReadOnlyList<string>? LayerNames,
        IReadOnlyList<string>? LineTypeNames,
        IReadOnlyList<string>? TextStyleNames,
        IReadOnlyList<string>? DimensionStyleNames,
        List<SerializableBlockDependency>? BlockDependencies);

    private sealed record SerializableBlockDependency(
        string Name,
        List<SerializableEntity>? Entities);

    private sealed record SerializablePoint(double X, double Y, double Z);
}
