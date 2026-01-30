using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

public sealed class RenderCacheStampProvider : IRenderCacheStampProvider
{
    private readonly ConditionalWeakTable<CadDocument, StampState> _documents = new();

    public long GetStamp(CadDocument document)
    {
        if (document is null)
        {
            return 0;
        }

        var state = _documents.GetOrCreateValue(document);
        EnsureTracking(document, state);
        return Volatile.Read(ref state.Stamp);
    }

    public long AdvanceStamp(CadDocument document)
    {
        if (document is null)
        {
            return 0;
        }

        var state = _documents.GetOrCreateValue(document);
        EnsureTracking(document, state);
        return Interlocked.Increment(ref state.Stamp);
    }

    private void EnsureTracking(CadDocument document, StampState state)
    {
        if (state.IsTracking)
        {
            return;
        }

        state.IsTracking = true;
        state.CollectionChanged = (_, _) => AdvanceStamp(document);
        state.BlockRecordChanged = (_, args) =>
        {
            if (args.Item is BlockRecord record)
            {
                TrackEntityCollection(document, state, record.Entities);
            }

            AdvanceStamp(document);
        };

        TrackBlockRecords(document, state);
        TrackCollection(document.Layers, state);
        TrackCollection(document.LineTypes, state);
        TrackCollection(document.TextStyles, state);
        TrackCollection(document.DimensionStyles, state);
        TrackCollection(document.Scales, state);
        TrackCollection(document.Layouts, state);
        TrackCollection(document.RootDictionary, state);
    }

    private static void TrackCollection<T>(IObservableCadCollection<T>? collection, StampState state)
        where T : CadObject
    {
        if (collection is null)
        {
            return;
        }

        if (!state.TrackedCollections.Add(collection))
        {
            return;
        }

        collection.OnAdd += state.CollectionChanged;
        collection.OnRemove += state.CollectionChanged;
    }

    private static void TrackBlockRecords(CadDocument document, StampState state)
    {
        if (!state.TrackedCollections.Add(document.BlockRecords))
        {
            return;
        }

        document.BlockRecords.OnAdd += state.BlockRecordChanged;
        document.BlockRecords.OnRemove += state.CollectionChanged;

        foreach (var record in document.BlockRecords)
        {
            TrackEntityCollection(document, state, record.Entities);
        }
    }

    private static void TrackEntityCollection(
        CadDocument document,
        StampState state,
        CadObjectCollection<Entity> entities)
    {
        if (!state.TrackedCollections.Add(entities))
        {
            return;
        }

        entities.OnAdd += state.CollectionChanged;
        entities.OnRemove += state.CollectionChanged;
    }

    private sealed class StampState
    {
        public long Stamp;
        public bool IsTracking;
        public EventHandler<CollectionChangedEventArgs>? CollectionChanged;
        public EventHandler<CollectionChangedEventArgs>? BlockRecordChanged;
        public HashSet<object> TrackedCollections =
            new(ReferenceEqualityComparer.Instance);
    }
}
