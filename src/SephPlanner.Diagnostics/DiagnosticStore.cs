using System.Security.Cryptography;
using System.Text.Json;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Diagnostics;

public enum DiagnosticSaveResult { Created, Duplicate, Conflict, Full }
public sealed record DiagnosticStoredReport(string ReportId, DateTime ReceivedUtc, long Bytes);

public sealed class DiagnosticStore : IDisposable
{
    private readonly DiagnosticServerOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DiagnosticStore(DiagnosticServerOptions options)
    {
        _options = options;
        Directory.CreateDirectory(options.StorageDirectory);
        foreach (var file in new DirectoryInfo(options.StorageDirectory).EnumerateFiles("*.upload"))
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "N", out _)) file.Delete();
    }

    public async Task<DiagnosticSaveResult> SaveAsync(Guid id, string expectedHash, Stream input, CancellationToken cancellation)
    {
        var temporary = Path.Combine(_options.StorageDirectory, Guid.NewGuid().ToString("N") + ".upload");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            {
                var buffer = new byte[8192];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellation)) != 0)
                {
                    total += read;
                    if (total > DiagnosticArchive.MaximumArchiveBytes) throw new InvalidDataException("진단 압축 파일이 너무 큽니다.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                }
                await output.FlushAsync(cancellation);
                output.Flush(true);
            }
            var bytes = await File.ReadAllBytesAsync(temporary, cancellation);
            if (DiagnosticArchive.Hash(bytes) != expectedHash) throw new InvalidDataException("진단 자료의 해시가 다릅니다.");
            using (var content = new MemoryStream(bytes))
            {
                var files = DiagnosticArchive.Read(content);
                using var metadata = JsonDocument.Parse(files["report.json"]);
                var root = metadata.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Version", out var version) || version.ValueKind != JsonValueKind.Number ||
                    !version.TryGetInt32(out var number) || number != DiagnosticArchive.SchemaVersion ||
                    !root.TryGetProperty("ReportId", out var report) || report.ValueKind != JsonValueKind.String || report.GetString() != id.ToString("N"))
                    throw new InvalidDataException("진단 설명의 형식 또는 제보 번호가 다릅니다.");
            }

            await _gate.WaitAsync(cancellation);
            try
            {
                DeleteExpired();
                var destination = PathFor(id);
                if (File.Exists(destination))
                {
                    await using var existing = File.OpenRead(destination);
                    var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(existing, cancellation));
                    return hash == expectedHash ? DiagnosticSaveResult.Duplicate : DiagnosticSaveResult.Conflict;
                }
                var stored = StoredFiles().ToArray();
                if (stored.Length >= _options.MaximumReports || stored.Sum(file => file.Length) + bytes.Length > _options.MaximumStorageBytes)
                    return DiagnosticSaveResult.Full;
                File.Move(temporary, destination);
                return DiagnosticSaveResult.Created;
            }
            finally { _gate.Release(); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<IReadOnlyList<DiagnosticStoredReport>> ListAsync(int offset, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            DeleteExpired();
            return StoredFiles().OrderByDescending(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal)
                .Skip(offset).Take(100).Select(file => new DiagnosticStoredReport(Path.GetFileNameWithoutExtension(file.Name), file.LastWriteTimeUtc, file.Length)).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<byte[]?> ReadAsync(Guid id, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            DeleteExpired();
            var path = PathFor(id);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellation) : null;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            var path = PathFor(id);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task PruneAsync(CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try { DeleteExpired(); }
        finally { _gate.Release(); }
    }

    private string PathFor(Guid id) => Path.Combine(_options.StorageDirectory, id.ToString("N") + ".zip");
    private IEnumerable<FileInfo> StoredFiles() => new DirectoryInfo(_options.StorageDirectory).EnumerateFiles("*.zip")
        .Where(file => Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "N", out _));

    private void DeleteExpired()
    {
        var before = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        foreach (var file in StoredFiles().Where(file => file.LastWriteTimeUtc < before)) file.Delete();
    }

    public void Dispose() => _gate.Dispose();
}
