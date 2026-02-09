using ACadInspector.Editing.Clipboard;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using ACadInspector.Editing.Sessions;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ACadInspector.Editing.Tests.Clipboard;

public sealed class CadClipboardIntegrationTests
{
    [Fact]
    public async Task CopyClipThenPasteClip_RemapsDependencyGraphIntoTargetDocument()
    {
        var clipboard = new InMemoryCadClipboardService();
        var sourceDocument = CreateSourceDocument();
        var sourceSession = (CadDocumentSession)new CadEditorSessionFactory().Create(sourceDocument);
        var sourceRegistry = CreateRegistry(clipboard);

        var selected = sourceDocument.Entities.ToArray();
        sourceSession.SetSelection(selected.Cast<object?>().ToArray(), CadSelectionMode.Replace);

        var copy = await sourceRegistry.ExecuteAsync("COPYCLIP", sourceSession);
        Assert.True(copy.Success);
        Assert.True(clipboard.TryGetPayload(out var payload));
        Assert.NotNull(payload.Dependencies);
        Assert.Contains("L_CLIP", payload.Dependencies!.LayerNames);
        Assert.Contains("LT_CLIP", payload.Dependencies.LineTypeNames);
        Assert.Contains("TS_CLIP", payload.Dependencies.TextStyleNames);
        Assert.Contains(payload.Dependencies.BlockDependencies, static dependency => dependency.Name == "B_CLIP");

        var targetDocument = new CadDocument();
        var targetSession = (CadDocumentSession)new CadEditorSessionFactory().Create(targetDocument);
        var targetRegistry = CreateRegistry(clipboard);

        var paste = await targetRegistry.ExecuteAsync("PASTECLIP 30,5", targetSession);
        Assert.True(paste.Success);

        Assert.True(targetDocument.Layers.TryGetValue("L_CLIP", out _));
        Assert.True(targetDocument.LineTypes.TryGetValue("LT_CLIP", out _));
        Assert.True(targetDocument.TextStyles.TryGetValue("TS_CLIP", out _));
        Assert.True(targetDocument.BlockRecords.TryGetValue("B_CLIP", out var pastedBlock));
        Assert.NotNull(pastedBlock);
        Assert.NotEmpty(pastedBlock.Entities);
        Assert.Contains(targetDocument.Entities.OfType<Insert>(), insert => insert.Block.Name == "B_CLIP");
    }

    [Fact]
    public void ClipboardPayloadSerializer_RoundTripsDependencyGraph()
    {
        var payload = new CadClipboardPayload(
            Entities:
            [
                new CadClipboardEntity(
                    EntityType: CadOperationPayloadCodec.EntityTypeLine,
                    Payload: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [CadOperationPayloadCodec.EntityTypeKey] = CadOperationPayloadCodec.EntityTypeLine,
                        [CadOperationPayloadCodec.StartKey] = "0,0,0",
                        [CadOperationPayloadCodec.EndKey] = "1,0,0"
                    },
                    ReferencePoint: XYZ.Zero)
            ],
            BasePoint: XYZ.Zero,
            Dependencies: new CadClipboardDependencies(
                LayerNames: ["L0", "L_TEST"],
                LineTypeNames: ["BYLAYER", "DASHED"],
                TextStyleNames: ["STANDARD", "TS_TEST"],
                DimensionStyleNames: ["STANDARD"],
                BlockDependencies:
                [
                    new CadClipboardBlockDependency(
                        Name: "B_TEST",
                        Entities:
                        [
                            new CadClipboardEntity(
                                EntityType: CadOperationPayloadCodec.EntityTypeCircle,
                                Payload: new Dictionary<string, string>(StringComparer.Ordinal)
                                {
                                    [CadOperationPayloadCodec.EntityTypeKey] = CadOperationPayloadCodec.EntityTypeCircle,
                                    [CadOperationPayloadCodec.CenterKey] = "0,0,0",
                                    [CadOperationPayloadCodec.RadiusKey] = "2"
                                },
                                ReferencePoint: XYZ.Zero)
                        ])
                ]));

        var json = CadClipboardPayloadSerializer.Serialize(payload);
        Assert.True(CadClipboardPayloadSerializer.TryDeserialize(json, out var roundTripped));
        Assert.Equal(payload.Entities.Count, roundTripped.Entities.Count);
        Assert.Equal(payload.Dependencies!.LayerNames, roundTripped.Dependencies!.LayerNames);
        Assert.Equal(payload.Dependencies.LineTypeNames, roundTripped.Dependencies.LineTypeNames);
        Assert.Equal(payload.Dependencies.TextStyleNames, roundTripped.Dependencies.TextStyleNames);
        Assert.Equal(payload.Dependencies.DimensionStyleNames, roundTripped.Dependencies.DimensionStyleNames);
        Assert.Equal(payload.Dependencies.BlockDependencies.Count, roundTripped.Dependencies.BlockDependencies.Count);
    }

    [Fact]
    public async Task SystemClipboardCadClipboardService_HydratesFromBridgeWhenLocalClipboardEmpty()
    {
        var expected = new CadClipboardPayload(
            Entities:
            [
                new CadClipboardEntity(
                    EntityType: CadOperationPayloadCodec.EntityTypePoint,
                    Payload: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [CadOperationPayloadCodec.EntityTypeKey] = CadOperationPayloadCodec.EntityTypePoint,
                        [CadOperationPayloadCodec.LocationKey] = "4,8,0"
                    },
                    ReferencePoint: new XYZ(4d, 8d, 0d))
            ],
            BasePoint: new XYZ(4d, 8d, 0d),
            Dependencies: CadClipboardDependencies.Empty);

        var bridge = new FakeSystemClipboardBridge(expected);
        var service = new SystemClipboardCadClipboardService(bridge);

        Assert.False(service.TryGetPayload(out _));
        var hydrated = await service.TryHydrateAsync();
        Assert.True(hydrated);
        Assert.True(service.TryGetPayload(out var payload));
        Assert.Equal(expected.BasePoint.X, payload.BasePoint.X, 6);

        await service.PublishAsync(payload);
        Assert.NotNull(bridge.LastWrittenPayload);
    }

    private static CadCommandRegistry CreateRegistry(ICadClipboardService clipboard)
    {
        var registry = new CadCommandRegistry();
        registry.Register(new CopyClipCadCommand(clipboard));
        registry.Register(new PasteClipCadCommand(clipboard));
        return registry;
    }

    private static CadDocument CreateSourceDocument()
    {
        var document = new CadDocument();
        var layer = new Layer("L_CLIP");
        document.Layers.Add(layer);

        var lineType = new LineType("LT_CLIP");
        document.LineTypes.Add(lineType);

        var textStyle = new TextStyle("TS_CLIP");
        document.TextStyles.Add(textStyle);

        var line = new Line(new XYZ(0d, 0d, 0d), new XYZ(10d, 0d, 0d))
        {
            Layer = layer,
            LineType = lineType
        };

        var text = new TextEntity
        {
            Value = "Clipboard",
            InsertPoint = new XYZ(2d, 3d, 0d),
            Height = 2d,
            Style = textStyle,
            Layer = layer
        };

        var block = new BlockRecord("B_CLIP");
        block.Entities.Add(new Circle
        {
            Center = XYZ.Zero,
            Radius = 1.5,
            Layer = layer
        });
        document.BlockRecords.Add(block);

        var insert = new Insert(block)
        {
            InsertPoint = new XYZ(4d, 4d, 0d),
            Layer = layer
        };

        document.Entities.Add(line);
        document.Entities.Add(text);
        document.Entities.Add(insert);
        return document;
    }

    private sealed class FakeSystemClipboardBridge : ICadSystemClipboardBridge
    {
        private readonly CadClipboardPayload? _readPayload;

        public FakeSystemClipboardBridge(CadClipboardPayload? readPayload = null)
        {
            _readPayload = readPayload;
        }

        public CadClipboardPayload? LastWrittenPayload { get; private set; }

        public ValueTask WriteAsync(CadClipboardPayload payload, CancellationToken cancellationToken = default)
        {
            LastWrittenPayload = payload;
            return ValueTask.CompletedTask;
        }

        public ValueTask<CadClipboardPayload?> ReadAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_readPayload);
        }
    }
}
