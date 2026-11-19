using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Diagnostics;

public enum DiagnosticSaveResult { Created, Duplicate, Conflict, Full, SourceFull }
/// <summary>
/// 목록 한 줄. <paramref name="Category"/>와 <paramref name="Note"/>는 사용자가 F10 에서 직접
/// 적어 보낸 것이며, 적지 않았으면 빈 문자열이다. 압축을 풀지 않고도 무슨 제보인지 읽으려고
/// 목록에 함께 싣는다.
/// </summary>
public sealed record DiagnosticStoredReport(
    string ReportId, DateTime ReceivedUtc, long Bytes, string Category = "", string Note = "");

public sealed class DiagnosticStore : IDisposable
{
    private readonly DiagnosticServerOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    // 보낸 곳을 디스크에 남기면 동의 창이 알리지 않은 자료를 14일 쥐게 된다. 그래서 메모리에만 두고,
    // 다시 시작하면 그 전 제보는 어느 곳의 몫에도 들지 않는다.
    private readonly Dictionary<Guid, string> _sources = new();

    public DiagnosticStore(DiagnosticServerOptions options)
    {
        _options = options;
        Directory.CreateDirectory(options.StorageDirectory);
        foreach (var file in new DirectoryInfo(options.StorageDirectory).EnumerateFiles("*.upload"))
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "N", out _)) file.Delete();
    }

    public async Task<DiagnosticSaveResult> SaveAsync(Guid id, string expectedHash, Stream input, string source, CancellationToken cancellation)
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
                CheckNote(root);
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
                var own = stored.Where(file => _sources.TryGetValue(IdOf(file), out var owner) && owner == source).ToArray();
                if (own.Length >= _options.MaximumReportsPerSource || own.Sum(file => file.Length) + bytes.Length > _options.MaximumStorageBytesPerSource)
                    return DiagnosticSaveResult.SourceFull;
                if (stored.Length >= _options.MaximumReports || stored.Sum(file => file.Length) + bytes.Length > _options.MaximumStorageBytes)
                    return DiagnosticSaveResult.Full;
                File.Move(temporary, destination);
                _sources[id] = source;
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
                .Skip(offset).Take(100).Select(file =>
                {
                    var (category, note) = ReadNote(file);
                    return new DiagnosticStoredReport(
                        Path.GetFileNameWithoutExtension(file.Name), file.LastWriteTimeUtc, file.Length, category, note);
                }).ToArray();
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
            _sources.Remove(id);
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

    /// <summary>
    /// 설명 파일에서 사용자 메모만 꺼낸다. 목록을 그리다가 자료 하나가 상했다고 목록 전체가
    /// 실패하면 나머지 제보까지 못 본다. 읽지 못한 줄은 메모 없이 보여 준다.
    /// </summary>
    private static (string Category, string Note) ReadNote(FileInfo file)
    {
        try
        {
            using var stream = file.OpenRead();
            using var document = JsonDocument.Parse(DiagnosticArchive.ReadReport(stream));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Note", out var note) || note.ValueKind != JsonValueKind.Object)
                return ("", "");
            return (Shown(Text(note, "CategoryLabel"), MaximumLabelLength), Shown(Text(note, "Text"), DiagnosticNote.MaximumTextLength));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or DecoderFallbackException)
        {
            return ("", "");
        }
    }

    // 화면의 분류 이름은 열 자 안팎이다. 다음 판이 분류를 더해도 거절하지 않을 만큼 넉넉히 둔다.
    private const int MaximumLabelLength = 64;

    /// <summary>
    /// 메모 길이를 서버에서도 지킨다. 500자 제한이 클라이언트에만 있으면 손으로 지은 요청 몇 건으로
    /// 관리자 목록이 메모리를 다 쓴다. 분류 식별자는 모르는 값이어도 받는다 - 다음 판의 분류일 수 있다.
    /// </summary>
    private static void CheckNote(JsonElement root)
    {
        if (!root.TryGetProperty("Note", out var note) || note.ValueKind == JsonValueKind.Null) return;
        if (note.ValueKind != JsonValueKind.Object ||
            !Fits(note, "Category", MaximumLabelLength) || !Fits(note, "CategoryLabel", MaximumLabelLength) ||
            !Fits(note, "Text", DiagnosticNote.MaximumTextLength))
            throw new InvalidDataException("진단 메모의 형식 또는 길이가 올바르지 않습니다.");
    }

    private static bool Fits(JsonElement note, string name, int maximum) =>
        !note.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ||
        value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= maximum;

    /// <summary>
    /// 제한을 두기 전에 받은 자료도 목록에 실리므로 읽을 때도 자른다. 제어문자는 관리자가 터미널로 목록을
    /// 읽을 때 화면을 조작할 수 있어 줄바꿈만 남기고 공백으로 바꾼다.
    /// </summary>
    private static string Shown(string text, int maximum)
    {
        var length = Math.Min(text.Length, maximum);
        if (length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
        var builder = new StringBuilder(length);
        foreach (var character in text.AsSpan(0, length))
            builder.Append(char.IsControl(character) && character != '\n' ? ' ' : character);
        return builder.ToString();
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private string PathFor(Guid id) => Path.Combine(_options.StorageDirectory, id.ToString("N") + ".zip");
    private static Guid IdOf(FileInfo file) => Guid.ParseExact(Path.GetFileNameWithoutExtension(file.Name), "N");
    private IEnumerable<FileInfo> StoredFiles() => new DirectoryInfo(_options.StorageDirectory).EnumerateFiles("*.zip")
        .Where(file => Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "N", out _));

    private void DeleteExpired()
    {
        var before = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        foreach (var file in StoredFiles().Where(file => file.LastWriteTimeUtc < before).ToArray())
        {
            file.Delete();
            _sources.Remove(IdOf(file));
        }
    }

    public void Dispose() => _gate.Dispose();
}
