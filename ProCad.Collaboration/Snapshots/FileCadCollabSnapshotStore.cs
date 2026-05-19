using System.Text;
using System.Text.Json;
using ProCad.Collaboration.Contracts;

namespace ProCad.Collaboration.Snapshots;

public sealed class FileCadCollabSnapshotStore : ICadCollabSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int CompactThresholdLines = 4_000;
    private const int CompactTailLines = 750;

    private readonly string _snapshotPath;
    private readonly string _oplogPath;
    private readonly string? _legacySnapshotPath;
    private readonly string? _legacyOplogPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileCadCollabSnapshotStore(string basePath, string? legacyBasePath = null)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException("Base path must not be empty.", nameof(basePath));
        }

        Directory.CreateDirectory(basePath);
        _snapshotPath = Path.Combine(basePath, "cadcollab.snapshot.json");
        _oplogPath = Path.Combine(basePath, "cadcollab.oplog.jsonl");

        if (!string.IsNullOrWhiteSpace(legacyBasePath)
            && !string.Equals(
                Path.GetFullPath(basePath),
                Path.GetFullPath(legacyBasePath),
                StringComparison.Ordinal))
        {
            _legacySnapshotPath = Path.Combine(legacyBasePath, "cadcollab.snapshot.json");
            _legacyOplogPath = Path.Combine(legacyBasePath, "cadcollab.oplog.jsonl");
        }
    }

    public async ValueTask<CadCollabSnapshot?> LoadLatestSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MigrateLegacyFileIfNeededAsync(_snapshotPath, _legacySnapshotPath, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(_snapshotPath))
            {
                return null;
            }

            await using var stream = File.OpenRead(_snapshotPath);
            return await JsonSerializer
                .DeserializeAsync<CadCollabSnapshot>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<CadCollabBatch>> LoadBatchesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MigrateLegacyFileIfNeededAsync(_oplogPath, _legacyOplogPath, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(_oplogPath))
            {
                return Array.Empty<CadCollabBatch>();
            }

            var result = new List<CadCollabBatch>();
            using var reader = new StreamReader(_oplogPath, Encoding.UTF8);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var batch = JsonSerializer.Deserialize<CadCollabBatch>(line, JsonOptions);
                    if (batch is not null)
                    {
                        result.Add(batch);
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed log lines and continue reading the tail.
                }
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask AppendBatchAsync(CadCollabBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MigrateLegacyFileIfNeededAsync(_oplogPath, _legacyOplogPath, cancellationToken).ConfigureAwait(false);
            var line = JsonSerializer.Serialize(batch, JsonOptions);
            await using var stream = new FileStream(_oplogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask WriteSnapshotAsync(CadCollabSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tempPath = _snapshotPath + ".tmp";
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(_snapshotPath))
            {
                File.Delete(_snapshotPath);
            }

            File.Move(tempPath, _snapshotPath);
            if (File.Exists(_oplogPath))
            {
                File.Delete(_oplogPath);
            }

            DeleteLegacyFile(_legacySnapshotPath);
            DeleteLegacyFile(_legacyOplogPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask CompactAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MigrateLegacyFileIfNeededAsync(_oplogPath, _legacyOplogPath, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(_oplogPath))
            {
                return;
            }

            var lines = await File.ReadAllLinesAsync(_oplogPath, cancellationToken).ConfigureAwait(false);
            if (lines.Length <= CompactThresholdLines)
            {
                return;
            }

            var start = Math.Max(0, lines.Length - CompactTailLines);
            var tail = new string[lines.Length - start];
            Array.Copy(lines, start, tail, 0, tail.Length);
            await File.WriteAllLinesAsync(_oplogPath, tail, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async ValueTask MigrateLegacyFileIfNeededAsync(
        string currentPath,
        string? legacyPath,
        CancellationToken cancellationToken)
    {
        if (legacyPath is null || File.Exists(currentPath) || !File.Exists(legacyPath))
        {
            return;
        }

        var currentDirectory = Path.GetDirectoryName(currentPath);
        if (!string.IsNullOrWhiteSpace(currentDirectory))
        {
            Directory.CreateDirectory(currentDirectory);
        }

        try
        {
            await using (var source = new FileStream(legacyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var destination = new FileStream(currentPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (FileNotFoundException) when (File.Exists(currentPath))
        {
            return;
        }
        catch (IOException) when (File.Exists(currentPath))
        {
            return;
        }

        File.Delete(legacyPath);
    }

    private static void DeleteLegacyFile(string? legacyPath)
    {
        if (legacyPath is not null && File.Exists(legacyPath))
        {
            File.Delete(legacyPath);
        }
    }
}
