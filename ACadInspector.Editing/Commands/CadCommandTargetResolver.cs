using ACadInspector.Editing.Sessions;
using ACadSharp.Entities;

namespace ACadInspector.Editing.Commands;

internal static class CadCommandTargetResolver
{
    public static bool TryResolve(
        CadDocumentSession session,
        IReadOnlyList<string> tokens,
        out IReadOnlyList<Entity> entities,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(tokens);

        error = null;
        var resolved = new List<Entity>();

        if (tokens.Count > 0)
        {
            foreach (var token in tokens)
            {
                if (!CadCommandParsing.TryParseHandle(token, out var handle))
                {
                    error = $"Invalid handle '{token}'.";
                    entities = Array.Empty<Entity>();
                    return false;
                }

                if (!session.EntityIndex.TryGetByHandle(handle, out var entity, out _))
                {
                    error = $"Entity handle '{token}' was not found.";
                    entities = Array.Empty<Entity>();
                    return false;
                }

                resolved.Add(entity);
            }
        }
        else
        {
            foreach (var item in session.SelectionSet.Items)
            {
                if (item is Entity entity)
                {
                    resolved.Add(entity);
                }
            }
        }

        entities = resolved
            .Distinct()
            .ToArray();

        if (entities.Count == 0)
        {
            error = "No target entities. Select entities first or pass handles.";
            return false;
        }

        return true;
    }
}
