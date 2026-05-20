using ProCad.Editing.Clipboard;
using ProCad.Services;
using CSMath;
using Xunit;

namespace ProCad.Tests.Services;

public sealed class AvaloniaCadSystemClipboardBridgeTests
{
    [Fact]
    public async Task WriteAndReadAsync_UsesCustomMimeAndTextFallback()
    {
        var platform = new FakeClipboardPlatformFacade();
        var bridge = new AvaloniaCadSystemClipboardBridge(platform);
        var payload = CreatePayload();

        await bridge.WriteAsync(payload);

        Assert.NotNull(platform.StoredCadJson);
        Assert.NotNull(platform.StoredTextPayload);
        Assert.StartsWith(CadClipboardFormats.CadTextPrefix, platform.StoredTextPayload!, StringComparison.Ordinal);

        var hydrated = await bridge.ReadAsync();
        Assert.NotNull(hydrated);
        Assert.Single(hydrated!.Entities);

        platform.ClearCadJson();
        var hydratedFromText = await bridge.ReadAsync();
        Assert.NotNull(hydratedFromText);
        Assert.Single(hydratedFromText!.Entities);
    }

    [Fact]
    public async Task ReadAsync_AcceptsLegacyCadJsonFormat()
    {
        var payload = CreatePayload();
        var platform = new FakeClipboardPlatformFacade();
        platform.SetFormat(CadClipboardFormats.LegacyCadJsonMime, CadClipboardPayloadSerializer.Serialize(payload));
        var bridge = new AvaloniaCadSystemClipboardBridge(platform);

        var hydrated = await bridge.ReadAsync();

        Assert.NotNull(hydrated);
        Assert.Single(hydrated!.Entities);
    }

    [Fact]
    public async Task ReadAsync_AcceptsLegacyTextPrefix()
    {
        var payload = CreatePayload();
        var platform = new FakeClipboardPlatformFacade();
        platform.SetText(string.Concat(
            CadClipboardFormats.LegacyCadTextPrefix,
            CadClipboardPayloadSerializer.Serialize(payload)));
        var bridge = new AvaloniaCadSystemClipboardBridge(platform);

        var hydrated = await bridge.ReadAsync();

        Assert.NotNull(hydrated);
        Assert.Single(hydrated!.Entities);
    }

    private static CadClipboardPayload CreatePayload()
    {
        return new CadClipboardPayload(
            Entities:
            [
                new CadClipboardEntity(
                    EntityType: "LINE",
                    Payload: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["entityType"] = "LINE",
                        ["start"] = "0,0,0",
                        ["end"] = "2,0,0"
                    },
                    ReferencePoint: XYZ.Zero)
            ],
            BasePoint: XYZ.Zero,
            Dependencies: CadClipboardDependencies.Empty);
    }

    private sealed class FakeClipboardPlatformFacade : ICadClipboardPlatformFacade
    {
        private readonly Dictionary<string, string> _formats = new(StringComparer.Ordinal);

        public string? StoredCadJson { get; private set; }
        public string? StoredTextPayload { get; private set; }

        public Task WriteAsync(
            string cadJson,
            string? dxfText,
            string textPayload,
            CancellationToken cancellationToken = default)
        {
            StoredCadJson = cadJson;
            StoredTextPayload = textPayload;
            _formats[CadClipboardFormats.CadJsonMime] = cadJson;
            if (!string.IsNullOrWhiteSpace(dxfText))
            {
                _formats[CadClipboardFormats.CadDxfMime] = dxfText;
            }

            return Task.CompletedTask;
        }

        public Task<string?> ReadFormatAsync(
            string formatIdentifier,
            CancellationToken cancellationToken = default)
        {
            _formats.TryGetValue(formatIdentifier, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task<string?> ReadTextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StoredTextPayload);
        }

        public void ClearCadJson()
        {
            StoredCadJson = null;
            _formats.Remove(CadClipboardFormats.CadJsonMime);
        }

        public void SetFormat(string formatIdentifier, string value)
        {
            _formats[formatIdentifier] = value;
        }

        public void SetText(string value)
        {
            StoredTextPayload = value;
        }
    }
}
