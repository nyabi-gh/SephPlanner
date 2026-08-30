using System.IO;
using System.Text.Json;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;

namespace SephPlanner.Overlay;

/// <summary>
/// 플러그인이 덤프해 둔 석판/아티팩트 정의를 읽어 둔다. 게임 패치로 데이터가 바뀌면
/// 플러그인이 파일을 다시 쓰므로, 파일이 갱신되면 다시 읽는다.
/// </summary>
public sealed class CatalogStore : ICatalog
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private Dictionary<int, TabletDefinition> _tablets = new();
    private Dictionary<int, CharmDefinition> _charms = new();
    private Dictionary<string, ComboDefinition> _combos = new();
    private DateTime _loadedAt = DateTime.MinValue;

    public bool IsLoaded => _tablets.Count > 0;

    public bool Refresh()
    {
        var tabletPath = Path.Combine(IpcContract.DataDirectory, IpcContract.TabletDbFile);
        var charmPath = Path.Combine(IpcContract.DataDirectory, IpcContract.CharmDbFile);
        if (!File.Exists(tabletPath) || !File.Exists(charmPath)) return false;

        var stamp = File.GetLastWriteTimeUtc(tabletPath);
        if (stamp <= _loadedAt) return IsLoaded;

        try
        {
            var tablets = JsonSerializer.Deserialize<List<TabletDefinition>>(File.ReadAllText(tabletPath), Options);
            var charms = JsonSerializer.Deserialize<List<CharmDefinition>>(File.ReadAllText(charmPath), Options);
            if (tablets is null || charms is null) return false;

            // 콤보 파일은 나중에 생긴 것이라 없을 수 있다. 그때는 콤보 없이 동작한다.
            var comboPath = Path.Combine(IpcContract.DataDirectory, IpcContract.ComboDbFile);
            var combos = File.Exists(comboPath)
                ? JsonSerializer.Deserialize<List<ComboDefinition>>(File.ReadAllText(comboPath), Options)
                : null;

            _tablets = tablets.GroupBy(t => t.EntityId).ToDictionary(g => g.Key, g => g.First());
            _charms = charms.GroupBy(c => c.EntityId).ToDictionary(g => g.Key, g => g.First());
            _combos = (combos ?? new List<ComboDefinition>())
                .GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
            _loadedAt = stamp;
            return true;
        }
        catch (JsonException)
        {
            // 플러그인이 쓰는 도중에 읽었을 수 있다. 다음 기회에 다시 시도한다.
            return false;
        }
    }

    public TabletDefinition? Tablet(int entityId) =>
        _tablets.TryGetValue(entityId, out var definition) ? definition : null;

    public CharmDefinition? Charm(int entityId) =>
        _charms.TryGetValue(entityId, out var definition) ? definition : null;

    public ComboDefinition? Combo(string categoryId) =>
        _combos.TryGetValue(categoryId, out var definition) ? definition : null;
}
