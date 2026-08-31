using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Overlay;

public partial class MainWindow : Window
{
    private const double CompactWidth = 232;
    private const double DetailWidth = 420;

    private readonly ObservableCollection<CellView> _cells = new();
    private readonly ObservableCollection<MoveView> _moves = new();
    private readonly ObservableCollection<OfferView> _offers = new();
    private readonly ObservableCollection<MixView> _mixes = new();
    private readonly ObservableCollection<ComboChipView> _chips = new();
    private readonly CatalogStore _catalog = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly UserSettings _settings = UserSettings.Load();

    /// <summary>--preview 에서만 쓰는 내장 카탈로그. 덤프가 없는 PC에서도 화면이 나와야 한다.</summary>
    private readonly ICatalog? _previewCatalog;

    private ICatalog ActiveCatalog => _previewCatalog ?? (ICatalog)_catalog;

    /// <summary>계산 스레드가 읽으므로 통째로 갈아끼우고 제자리에서 바꾸지 않는다.</summary>
    private volatile PlanPreferences _preferences = PlanPreferences.None;

    /// <summary>선호가 바뀌었을 때 같은 상태를 다시 풀기 위한 최신 스냅샷.</summary>
    private volatile GameSnapshot? _lastSnapshot;

    private int _solving;
    private string _lastPlanned = "";
    private bool _expanded;

    /// <summary>자동 배치 버튼이 보낼 최종 배치. 스냅샷이 갱신될 때마다 함께 바뀐다.</summary>
    private Plan? _plan;

    /// <summary>상자나 상점이 열리고 닫히는 순간에만 저절로 펼치고 접는다.</summary>
    private bool _hadOffers;

    public MainWindow()
    {
        InitializeComponent();
        GridCells.ItemsSource = _cells;
        MoveList.ItemsSource = _moves;
        OfferList.ItemsSource = _offers;
        MixList.ItemsSource = _mixes;
        ChipList.ItemsSource = _chips;
        _preferences = _settings.ToPreferences();
        RestorePosition();
        ApplyOpacity();
        UpdateIconModeButton();
        UpdateRecommendButton();
        ApplyLayout();

        // 게임 없이 화면을 확인하는 통로. 파이프를 열지 않으므로 실제 오버레이와 같이 떠도 안전하다.
        if (Environment.GetCommandLineArgs().Contains("--preview"))
        {
            StatusText.Text = "미리보기";
            _previewCatalog = PreviewSnapshot.Catalog();
            OnSnapshot(PreviewSnapshot.Build());
            return;
        }

        var client = new SnapshotClient();
        client.ConnectionChanged += connected => Dispatcher.Invoke(() =>
            StatusText.Text = connected ? "연결됨" : "게임 대기 중");
        client.SnapshotReceived += OnSnapshot;
        client.ProtocolMismatch += version => Dispatcher.Invoke(() =>
            ShowNotice(version < IpcContract.ProtocolVersion
                ? "플러그인이 구버전입니다. BepInEx/plugins 의 SephPlanner DLL 두 개를 함께 업데이트해 주세요."
                : "오버레이가 구버전입니다. SephPlanner.Overlay.exe 를 함께 업데이트해 주세요."));

        _ = client.RunAsync(_shutdown.Token);
    }

    /// <summary>
    /// 아직 처리하지 못한 가장 최근 스냅샷.
    ///
    /// 계산 중에 온 스냅샷을 버리면 안 된다. 플러그인은 상태가 직전과 같으면 다시 보내지 않으므로,
    /// 한 번 버린 상태는 영영 오지 않는다. 리롤한 선택지가 옛것으로 남아 있던 것이 이 때문이다.
    /// </summary>
    private GameSnapshot? _pending;

    private void OnSnapshot(GameSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        Volatile.Write(ref _pending, snapshot);
        Drain();
    }

    /// <summary>빌드 우선이나 강화 지정이 바뀌면 마지막 상태를 새 기준으로 다시 푼다.</summary>
    private void Resolve()
    {
        _settings.Save();
        _preferences = _settings.ToPreferences();
        if (_lastSnapshot is { } snapshot) OnSnapshot(snapshot);
    }

    /// <summary>해를 찾는 데 수백 ms 가 걸리므로 UI 스레드에서 돌리지 않는다.</summary>
    private void Drain()
    {
        if (Interlocked.Exchange(ref _solving, 1) == 1) return;

        Task.Run(() =>
        {
            try
            {
                while (Interlocked.Exchange(ref _pending, null) is { } snapshot)
                {
                    if (_previewCatalog is null && !_catalog.Refresh())
                    {
                        Dispatcher.Invoke(() => ShowNotice("게임을 한 번 실행해 데이터를 만들어 주세요. (게임 안에서 F9)"));
                        continue;
                    }

                    var plan = PlanBuilder.Build(snapshot, ActiveCatalog, _preferences);
                    Dispatcher.Invoke(() => Render(snapshot, plan));
                }
            }
            catch (Exception ex)
            {
                // 여기서 삼키지 않으면 태스크가 조용히 죽는데, 플러그인은 같은 상태를 다시
                // 보내지 않으므로 그 상태는 영영 오지 않는다. 알리고 다음 스냅샷을 계속 받는다.
                try
                {
                    Dispatcher.Invoke(() => ShowWarning("계산 중 오류가 났습니다: " + ex.Message));
                }
                catch (OperationCanceledException)
                {
                    // 창이 닫히는 중이다.
                }
            }
            finally
            {
                Interlocked.Exchange(ref _solving, 0);
            }

            // 비운 뒤 문을 닫기 직전에 새 스냅샷이 들어왔을 수 있다.
            if (Volatile.Read(ref _pending) != null) Drain();
        });
    }

    private void ShowNotice(string message)
    {
        NoticeText.Text = message;
        NoticeText.Visibility = Visibility.Visible;
        ScorePanel.Visibility = Visibility.Collapsed;
        NextMoveText.Visibility = Visibility.Collapsed;

        // 안내만 띄우고 격자를 그대로 두면 직전 탐험의 배치가 남는다.
        _cells.Clear();
        _moves.Clear();
        _offers.Clear();
        _mixes.Clear();
        _chips.Clear();
        BuildPanel.Visibility = Visibility.Collapsed;
        OfferPanel.Visibility = Visibility.Collapsed;
        MixPanel.Visibility = Visibility.Collapsed;
        MovePanel.Visibility = Visibility.Collapsed;
        LegendRow.Visibility = Visibility.Collapsed;
        _lastPlanned = "";
        _plan = null;
    }

    /// <summary>마지막으로 아이콘 캐시를 비웠을 때의 카탈로그 버전.</summary>
    private int _iconCatalogVersion;

    private void Render(GameSnapshot snapshot, Plan? plan)
    {
        // F9 재덤프로 아이콘이 바뀌었을 수 있다. 카탈로그가 다시 읽힌 시점에 함께 비운다.
        if (_catalog.Version != _iconCatalogVersion)
        {
            _iconCatalogVersion = _catalog.Version;
            IconStore.Clear();
        }

        if (plan is null)
        {
            // "탐험"은 게임 자체가 쓰는 말이다 ("탐험 시작 시", "탐험 중" - ko-KR.json).
            ShowNotice(snapshot.Inventory is null
                ? "탐험 중이 아닙니다. 탐험을 시작하면 배치를 분석합니다."
                : "인벤토리에 아티팩트가 없습니다.");
            return;
        }

        ShowWarning(Warning(snapshot, plan));

        ScorePanel.Visibility = Visibility.Visible;
        CurrentScoreText.Text = $"{plan.Current.Score:0.#}";
        BestScoreText.Text = $"{plan.Best.Score:0.#}";

        var improved = plan.Gain > 0.001;
        GainText.Text = improved ? $"+{plan.Gain:0.#}" : "최적";
        GainText.Foreground = improved ? Theme.Good : Theme.TextDim;

        // 최적에 도달했으면 지금 점수를 어둡게 둘 이유가 없다.
        CurrentScoreText.Foreground = improved ? Theme.TextDim : Theme.TextBright;

        AutoExpand(plan);
        RenderGrid(snapshot, plan);
        RenderOffers(plan, snapshot.Run?.Gold ?? 0);
        RenderMixes(plan, snapshot.Mixer);
        RenderChips(snapshot);

        LegendText.Text = plan.Moves.Count > 0
            ? "노란 테두리 = 옮겨야 할 자리 · 아티팩트 우클릭 = 강화 우선"
            : "아티팩트 우클릭 = 강화 우선 지정";
        LegendRow.Visibility = Visibility.Visible;

        // 제안이 그대로면 목록을 다시 만들지 않는다. 스냅샷마다 깜빡이는 것을 막는다.
        var signature = string.Join("|", plan.Moves.Select(m => $"{m.Label}{m.Detail}"));
        if (signature != _lastPlanned)
        {
            _lastPlanned = signature;
            _moves.Clear();
            foreach (var move in plan.Moves.Take(6))
                _moves.Add(new MoveView(move.Label, move.Detail));
            if (plan.Moves.Count > 6)
                _moves.Add(new MoveView($"… 외 {plan.Moves.Count - 6}개", ""));
        }

        MovePanel.Visibility = _moves.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // 멀티 세션에서는 쓰기 경로의 동기화가 검증되지 않아 자동 배치를 내놓지 않는다 (docs/LEGAL.md).
        // Targets 가 비어 있으면(석판이 빠진 불완전 배치 등) 버튼을 보여줄 이유도 없다.
        _plan = plan;
        AutoPlaceButton.Visibility = plan.Moves.Count > 0 && plan.Targets.Count > 0 && !snapshot.IsMultiplayer
            ? Visibility.Visible
            : Visibility.Collapsed;

        var next = plan.Moves.FirstOrDefault();
        NextMoveText.Text = next is null ? "" : $"{next.Label}  {next.Detail}";
        UpdateNextMoveVisibility();
    }

    /// <summary>
    /// 지금 화면에서 알려야 할 것. 점수를 믿을 수 없는 상황을 멀티 안내보다 먼저 보여준다.
    /// </summary>
    private static string Warning(GameSnapshot snapshot, Plan plan)
    {
        if (plan.Best.UnplacedTablets > 0)
        {
            return $"석판 {plan.Best.UnplacedTablets}개는 놓을 자리가 없어 계산에서 빠졌습니다. " +
                   "점수가 실제와 다를 수 있습니다.";
        }

        if (plan.LevelMismatches > 0)
        {
            // 무엇이 원인인지는 여기서 알 수 없다. 다만 어긋난다는 사실은 확실하므로 그것만 말한다.
            return $"칸 {plan.LevelMismatches}개의 레벨이 게임과 다릅니다. 아직 읽지 못하는 효과가 " +
                   "걸려 있어 점수가 실제와 다를 수 있습니다.";
        }

        if (plan.SkippedOffers > 0)
            return $"선택지가 많아 {plan.SkippedOffers}개는 평가하지 못했습니다.";

        return snapshot.IsMultiplayer ? "멀티플레이 세션 - 제안만 표시합니다." : "";
    }

    private void ShowWarning(string message)
    {
        NoticeText.Text = message;
        NoticeText.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 무엇을 집을지 고르는 순간에는 이름과 후보를 다 봐야 한다. 상자나 상점이 열리면 저절로 펼치고,
    /// 닫히면 되돌린다. 그 사이에 직접 접거나 펼친 것은 상황이 바뀔 때까지 그대로 둔다.
    /// </summary>
    private void AutoExpand(Plan plan)
    {
        // 추천이 꺼져 있으면 자동 여닫이도 쉰다. 끄는 순간 후보가 사라지는 것은 상자가 닫힌 것이
        // 아니므로, 버튼을 누른 사용자 눈앞에서 창을 접어 버리면 안 된다. 접고 펴는 것은
        // 사용자 몫으로 남긴다.
        if (!_settings.Recommendations)
        {
            _hadOffers = false;
            return;
        }

        var hasOffers = plan.Offers.Count > 0;
        if (hasOffers == _hadOffers) return;

        _hadOffers = hasOffers;
        _expanded = hasOffers;
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        DetailPanel.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        Width = _expanded ? DetailWidth : CompactWidth;
        ExpandButton.Content = _expanded ? "▲" : "▼";
        ExpandButton.ToolTip = (_expanded ? "접기" : "펼치기") + " (Ctrl+Alt+P)";
    }

    private void UpdateNextMoveVisibility() =>
        NextMoveText.Visibility = !_expanded && NextMoveText.Text.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnToggleDetail(object sender, RoutedEventArgs e)
    {
        _expanded = !_expanded;
        ApplyLayout();
        UpdateNextMoveVisibility();
    }

    private void RenderOffers(Plan plan, int gold)
    {
        _offers.Clear();
        foreach (var advice in plan.Offers.Take(6))
        {
            var gain = advice.Gain;
            var price = advice.Candidate.Price;

            _offers.Add(new OfferView(
                advice.Candidate.Name,
                Reach(advice.Effect),
                advice.ComboText,
                price > 0 ? $"{price}골드" : "",
                gain > 0.001 ? $"+{gain:0.#}" : gain < -0.001 ? $"{gain:0.#}" : "0",
                gain > 0.001 ? Theme.Good : gain < -0.001 ? Theme.Bad : Theme.TextDim,
                !advice.Affordable ? Theme.TextDim
                    : advice.MatchesPriority || advice.MatchesPreset ? Theme.Mint : Theme.Text,
                advice.Affordable ? Theme.TextDim : Theme.Bad,
                advice.ComboCompletes ? Theme.Good : Theme.Mint,
                _settings.IconMode ? IconStore.Get(advice.Candidate.DefinitionId) : null,
                Explain(advice, gold)));
        }
        OfferPanel.Visibility = _offers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 합칠 만한 석판 쌍. 사람이 따라 해야 하는 일이라 무엇을 어느 각도로 넣는지까지 적는다.
    /// </summary>
    private void RenderMixes(Plan plan, MixerState? mixer)
    {
        _mixes.Clear();
        foreach (var advice in plan.Mixes.Take(4))
        {
            var gain = advice.Gain;

            _mixes.Add(new MixView(
                $"{advice.NameA} + {advice.NameB}",
                Turn(advice),
                mixer is { Cost: > 0 } ? $"{mixer.Cost}골드" : "",
                gain > 0.001 ? $"+{gain:0.#}" : gain < -0.001 ? $"{gain:0.#}" : "0",
                gain > 0.001 ? Theme.Good : gain < -0.001 ? Theme.Bad : Theme.TextDim,
                advice.Affordable ? Theme.Text : Theme.TextDim,
                advice.Affordable ? Theme.TextDim : Theme.Bad,
                Explain(advice)));
        }

        // 합성기가 있는데 권할 쌍이 하나도 없으면 그 사실을 말한다. 빈 자리는 아무 말도 안 한다.
        MixNote.Text = mixer is not null && _mixes.Count == 0
            ? "합칠 만한 짝이 없습니다. 조건 질의가 서로 다르면 합쳐지지 않습니다."
            : "";
        MixPanel.Visibility = mixer is not null && (_mixes.Count > 0 || MixNote.Text.Length > 0)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>합성 전에 재료를 돌려 놓아야 하는지. 돌릴 것이 없으면 빈 문자열이다.</summary>
    private static string Turn(MixAdvice advice)
    {
        var parts = new List<string>();
        if (advice.RotationA != 0) parts.Add($"{advice.NameA} {advice.RotationA * 90}°");
        if (advice.RotationB != 0) parts.Add($"{advice.NameB} {advice.RotationB * 90}°");
        return parts.Count > 0 ? "돌려서: " + string.Join(", ", parts) : "";
    }

    private static string Explain(MixAdvice advice)
    {
        var lines = new List<string>
        {
            $"{advice.NameA} 와(과) {advice.NameB} 을(를) 합칩니다. 재료 둘은 사라집니다.",
        };

        if (advice.RotationA != 0 || advice.RotationB != 0)
        {
            lines.Add("합성기에 넣기 전에 " + Turn(advice).Replace("돌려서: ", "") +
                      " 만큼 돌려 두어야 이 결과가 나옵니다.");
        }

        lines.Add(advice.Rotatable
            ? "결과는 돌릴 수 있습니다 (재료가 둘 다 돌아가므로)."
            : "결과는 돌릴 수 없습니다 (재료 중 하나가 돌아가지 않으므로).");

        var reach = Reach(advice.Effect);
        if (reach.Length > 0) lines.Add($"결과가 미치는 범위: {reach}");

        if (!advice.Affordable) lines.Add("소지금이 모자랍니다.");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 석판이 실제로 미치는 범위. 증가분이 같아 보일 때 무엇이 다른지 이 줄에서 드러난다.
    /// </summary>
    private static string Reach(TabletEffectSummary effect)
    {
        if (effect.IsEmpty) return "";

        var text = effect.RaisedCells > 0 ? $"{effect.RaisedCells}칸 +{effect.RaisedTotal}" : "";
        if (effect.LoweredCells > 0) text += $" −{effect.LoweredTotal}";
        if (effect.DisabledCells > 0) text += $" 막힘{effect.DisabledCells}";
        return text.Trim();
    }

    /// <summary>
    /// 아티팩트가 무슨 일을 하는지, 그리고 우리가 그 값어치를 어떻게 정했는지. 잰 값일 때는
    /// 굳이 말하지 않고, 근거가 레어도뿐일 때만 밝힌다 - 그 자리가 추천이 가장 흔들리는 곳이라
    /// 사용자가 "왜 이게 위에 있지"라고 물을 지점이다.
    /// </summary>
    private static IReadOnlyList<string> Describe(CharmDefinition? definition)
    {
        if (definition is null) return Array.Empty<string>();

        var lines = new List<string>(definition.EffectLines);
        var worth = CharmWorth.Resolve(definition, CharmValueStore.Book.Of(definition));

        var note = CharmValueStore.Book.Of(definition)?.Note ?? "";
        if (worth.Source == CharmWorthSource.Curated && note.Length > 0) lines.Add(note);

        if (worth.Source == CharmWorthSource.Rarity)
            lines.Add("값어치는 레어도로 어림잡은 것입니다. 효과의 세기는 아직 점수에 없습니다.");

        return lines;
    }

    private static string Explain(OfferAdvice advice, int gold)
    {
        var lines = new List<string>();
        lines.AddRange(Describe(advice.Candidate.Charm));

        if (!advice.Affordable) lines.Add($"소지금 {gold}골드로는 살 수 없습니다.");

        if (advice.MatchesPreset) lines.Add("가져온 빌드가 즐겨찾기로 찍어 둔 아티팩트입니다.");
        if (advice.MatchesPriority) lines.Add("밀고 있는 빌드의 아티팩트입니다.");
        if (advice.Displaced.Length > 0) lines.Add($"가방이 차 있어, 집으면 빠지는 것: {advice.Displaced}");

        if (advice.ComboText.Length > 0)
        {
            lines.Add(advice.ComboCompletes
                ? $"콤보가 발동합니다: {advice.ComboText}"
                : $"콤보 진행: {advice.ComboText}");
        }

        var effect = advice.Effect;
        if (!effect.IsEmpty)
        {
            if (effect.RaisedCells > 0) lines.Add($"{effect.RaisedCells}칸의 레벨을 모두 합쳐 {effect.RaisedTotal} 올립니다.");
            if (effect.LoweredCells > 0) lines.Add($"{effect.LoweredCells}칸은 합쳐 {effect.LoweredTotal} 내립니다.");
            if (effect.DisabledCells > 0) lines.Add($"{effect.DisabledCells}칸은 쓸 수 없게 만듭니다.");
            if (effect.MultipliedCells > 0) lines.Add($"{effect.MultipliedCells}칸에 배수가 걸립니다.");
            if (effect.IgnoreCriteriaCells > 0) lines.Add($"{effect.IgnoreCriteriaCells}칸은 배치 조건을 무시합니다.");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private void RenderGrid(GameSnapshot snapshot, Plan plan)
    {
        var inventory = snapshot.Inventory!;
        var total = inventory.Width * inventory.Height;
        while (_cells.Count < total) _cells.Add(new CellView());
        while (_cells.Count > total) _cells.RemoveAt(_cells.Count - 1);

        var tabletCells = plan.Best.Tablets
            .GroupBy(placement => placement.Position)
            .ToDictionary(group => group.Key, group => group.First());
        var moved = new HashSet<GridPos>(plan.Moves.Select(m => m.To));

        for (var index = 0; index < _cells.Count; index++)
        {
            var cell = _cells[index];
            var position = new GridPos(index % inventory.Width, index / inventory.Width);

            if (index >= inventory.Storage) cell.SetClosed();
            else if (tabletCells.TryGetValue(position, out var tablet))
            {
                var name = Naming.OfTablet(tablet);
                cell.SetTablet(name, tablet.Rotation, moved.Contains(position),
                    _settings.IconMode ? IconStore.Get(tablet.Definition.EntityId) : null);
            }
            else if (plan.Best.Levels.TryGetValue(position, out var level))
            {
                plan.Best.EffectiveLevels.TryGetValue(position, out var effective);
                plan.Best.InactiveCells.TryGetValue(position, out var reason);
                plan.Names.TryGetValue(position, out var name);
                plan.Charms.TryGetValue(position, out var charmId);
                cell.SetCharm(
                    name ?? "", level, effective, reason, moved.Contains(position),
                    charmId, _preferences.PinnedCharms.Contains(charmId),
                    _settings.IconMode ? IconStore.Get(charmId) : null,
                    Describe(ActiveCatalog.Charm(charmId)));
            }
            else cell.SetEmpty();
        }
    }

    /// <summary>
    /// 콤보 칩. 눌러서 그 콤보를 빌드로 지정하거나 해제한다. 지금 세어져 있는 콤보와,
    /// 개수가 0이 되어도 지정을 풀 수 있도록 이미 지정된 카테고리를 함께 보여준다.
    /// </summary>
    private void RenderChips(GameSnapshot snapshot)
    {
        _chips.Clear();

        // 빌드 우선은 추천에만 작용하므로 추천을 끄면 함께 접는다. 지정 자체는 남아 있다.
        if (!_settings.Recommendations)
        {
            BuildPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var counts = snapshot.Inventory?.ComboCounts ?? new Dictionary<string, int>();
        var shown = new HashSet<string>();

        foreach (var pair in counts.OrderByDescending(p => p.Value))
            AddChip(pair.Key, pair.Value, shown);
        foreach (var category in _preferences.PriorityCategories)
            AddChip(category, 0, shown);

        RenderPreset();

        // 칩이 하나도 없어도 접지 않는다. 빌드 코드를 가져올 자리가 여기뿐이다.
        BuildPanel.Visibility = Visibility.Visible;
    }

    /// <summary>가져온 빌드가 무엇을 알려 주는지 한 줄로 보여준다. 없으면 무엇을 하는 자리인지 알린다.</summary>
    private void RenderPreset()
    {
        var preset = _settings.Preset();
        PresetClearButton.Visibility = preset is null ? Visibility.Collapsed : Visibility.Visible;
        PresetPasteButton.Content = preset is null ? "빌드 코드 붙여넣기" : "다른 빌드";

        if (preset is null)
        {
            PresetText.Text = _presetMessage.Length > 0 ? _presetMessage : "가져온 빌드 없음";
            PresetText.Foreground = _presetMessage.Length > 0 ? Theme.Amber : Theme.TextDim;
            return;
        }

        var parts = new List<string> { $"아티팩트 {preset.FavoriteCharms.Count}개" };

        var avoided = preset.CategoryBias.Count(pair => pair.Value < 0);
        if (avoided > 0) parts.Add($"피하는 콤보 {avoided}개");

        PresetText.Text = "가져온 빌드 · " + string.Join(" · ", parts);
        PresetText.Foreground = Theme.TextDim;
    }

    /// <summary>가져오기에 실패한 이유. 다음 가져오기까지 남겨 둔다.</summary>
    private string _presetMessage = "";

    private void OnPastePreset(object sender, RoutedEventArgs e)
    {
        string code;
        try
        {
            code = Clipboard.GetText();
        }
        catch (Exception)
        {
            // 다른 앱이 클립보드를 쥐고 있으면 읽기가 실패한다. 오버레이가 죽을 일은 아니다.
            _presetMessage = "클립보드를 읽지 못했습니다. 잠시 뒤 다시 눌러 보세요.";
            RenderPreset();
            return;
        }

        if (!Core.Planning.PresetCode.TryParse(code, out var preset, out var error))
        {
            _presetMessage = error;
            RenderPreset();
            return;
        }

        _presetMessage = "";
        _settings.PresetCode = code.Trim();

        // 프리셋이 노리는 콤보는 칩을 대신 눌러 주는 것으로 잇는다. 그래야 그 뒤로는 손으로 켠
        // 것과 똑같이 끄고 켤 수 있다. 음수(피하는 카테고리)는 켜지 않는다.
        foreach (var pair in preset.CategoryBias)
        {
            if (pair.Value <= 0) continue;
            if (!_settings.PriorityCategories.Contains(pair.Key))
                _settings.PriorityCategories.Add(pair.Key);
        }

        Resolve();
    }

    private void OnClearPreset(object sender, RoutedEventArgs e)
    {
        _settings.PresetCode = null;
        _presetMessage = "";
        Resolve();
    }

    private void AddChip(string categoryId, int count, HashSet<string> shown)
    {
        if (!shown.Add(categoryId)) return;

        var combo = ActiveCatalog.Combo(categoryId);
        if (combo is null) return;

        var selected = _preferences.PriorityCategories.Contains(categoryId);
        var name = combo.Names.TryGetValue(Naming.CurrentLanguage, out var text) && text.Length > 0
            ? text
            : combo.Id;

        var lines = new List<string>();
        foreach (var effect in combo.Effects)
            lines.Add($"{effect.Threshold}개 - {effect.Text}");
        lines.Add(selected
            ? "누르면 빌드 지정을 해제합니다."
            : "누르면 이 콤보를 빌드로 지정해 추천에서 크게 칩니다.");

        _chips.Add(new ComboChipView(
            categoryId,
            $"{(selected ? "●" : "○")} {name} {count}",
            selected ? Theme.Mint : Theme.TextDim,
            string.Join(Environment.NewLine, lines)));
    }

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ComboChipView chip) return;

        if (!_settings.PriorityCategories.Remove(chip.CategoryId))
            _settings.PriorityCategories.Add(chip.CategoryId);
        Resolve();
    }

    private void OnCellRightClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CellView cell || cell.CharmId == 0) return;

        if (!_settings.PinnedCharms.Remove(cell.CharmId))
            _settings.PinnedCharms.Add(cell.CharmId);
        Resolve();
    }

    private void OnToggleRecommendations(object sender, RoutedEventArgs e)
    {
        _settings.Recommendations = !_settings.Recommendations;
        UpdateRecommendButton();

        // 후보 평가를 돌릴지 자체가 바뀌므로 다시 푼다. Resolve 가 저장까지 한다.
        Resolve();
    }

    private void UpdateRecommendButton() =>
        RecommendButton.Content = _settings.Recommendations ? "추천 끄기" : "추천 켜기";

    private void OnToggleIconMode(object sender, RoutedEventArgs e)
    {
        _settings.IconMode = !_settings.IconMode;
        _settings.Save();
        UpdateIconModeButton();

        // 계산은 그대로 두고 화면만 다시 그린다.
        if (_lastSnapshot is { } snapshot && _plan is { } plan) Render(snapshot, plan);
    }

    private void UpdateIconModeButton() =>
        IconModeButton.Content = _settings.IconMode ? "글자로 보기" : "아이콘으로 보기";

    private async void OnAutoPlace(object sender, RoutedEventArgs e)
    {
        var plan = _plan;
        if (plan is null || plan.Targets.Count == 0) return;

        // 응답이 올 때까지 같은 명령이 두 번 나가지 않게 잠근다. async void 라 예외가 새 나가면
        // 프로세스가 죽으므로 버튼 복구까지 finally 로 지킨다.
        AutoPlaceButton.IsEnabled = false;
        try
        {
            var response = await CommandClient.SendAsync(new ApplyPlanCommand { Targets = plan.Targets });
            ShowWarning(response ?? "플러그인과 연결할 수 없어 자동 배치를 보내지 못했습니다.");
        }
        catch (Exception ex)
        {
            ShowWarning("자동 배치 요청 중 오류가 났습니다: " + ex.Message);
        }
        finally
        {
            AutoPlaceButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 게임 화면을 가리는 게 부담스러울 때를 위한 단계식 투명도. 완전히 사라지는 값은 두지 않는다.
    /// </summary>
    private static readonly double[] OpacitySteps = { 1.0, 0.85, 0.7, 0.55 };

    private void OnCycleOpacity(object sender, RoutedEventArgs e)
    {
        var index = Array.IndexOf(OpacitySteps, _settings.Opacity);
        _settings.Opacity = OpacitySteps[(index + 1) % OpacitySteps.Length];
        _settings.Save();
        ApplyOpacity();
    }

    private void ApplyOpacity()
    {
        // 저장된 값이 손상됐거나 단계 밖이면 불투명으로 되돌린다.
        if (Array.IndexOf(OpacitySteps, _settings.Opacity) < 0) _settings.Opacity = 1.0;

        Opacity = _settings.Opacity;
        OpacityButton.ToolTip = $"투명도 {_settings.Opacity:P0} - 누를 때마다 한 단계씩 투명해집니다";
    }

    /// <summary>모니터 구성이 바뀌어 저장된 위치가 화면 밖이면 기본 위치로 되돌아간다.</summary>
    private void RestorePosition()
    {
        if (_settings.WindowLeft is not { } left || _settings.WindowTop is not { } top) return;

        var onScreen =
            left > SystemParameters.VirtualScreenLeft - CompactWidth &&
            left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            top > SystemParameters.VirtualScreenTop - 40 &&
            top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        if (!onScreen) return;

        Left = left;
        Top = top;
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        DragMove();

        // DragMove 는 끌기가 끝날 때까지 돌아오지 않는다. 여기서 저장하면 곧 마지막 위치다.
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // 테두리 없는 창이라 제목 표시줄이 없다. 키보드로도 닫을 수 있어야 한다.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        base.OnKeyDown(e);
    }

    // 게임에 포커스가 가 있는 동안에도 조작할 수 있어야 해서 전역 단축키로 등록한다.
    private const int ExpandHotkeyId = 0xB1;
    private const int VisibilityHotkeyId = 0xB2;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModNoRepeat = 0x4000;
    private const int WmHotkey = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    private HwndSource? _source;

    /// <summary>숨긴 뒤 되돌릴 길이 단축키뿐이므로, 등록에 실패했으면 숨기기도 막는다.</summary>
    private bool _canHide;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _source = (HwndSource)PresentationSource.FromVisual(this)!;
        _source.AddHook(OnWindowMessage);

        var keyP = (uint)KeyInterop.VirtualKeyFromKey(Key.P);
        if (!RegisterHotKey(_source.Handle, ExpandHotkeyId, ModControl | ModAlt | ModNoRepeat, keyP))
        {
            // 다른 프로그램이 이미 쓰고 있으면 등록에 실패한다. 버튼으로는 여전히 접고 펼 수 있다.
            ExpandButton.ToolTip = "펼치기 (Ctrl+Alt+P 는 다른 프로그램이 쓰는 중)";
        }

        var keyO = (uint)KeyInterop.VirtualKeyFromKey(Key.O);
        _canHide = RegisterHotKey(_source.Handle, VisibilityHotkeyId, ModControl | ModAlt | ModNoRepeat, keyO);
        if (!_canHide)
        {
            TitleText.ToolTip = "Ctrl+Alt+P = 접기/펼치기 (Ctrl+Alt+O 는 다른 프로그램이 쓰는 중)";
        }
    }

    private IntPtr OnWindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey) return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case ExpandHotkeyId:
                OnToggleDetail(this, new RoutedEventArgs());
                handled = true;
                break;
            case VisibilityHotkeyId when _canHide:
                Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_source != null)
        {
            UnregisterHotKey(_source.Handle, ExpandHotkeyId);
            UnregisterHotKey(_source.Handle, VisibilityHotkeyId);
            _source.RemoveHook(OnWindowMessage);
        }

        _shutdown.Cancel();
        base.OnClosed(e);
    }
}

public sealed record MoveView(string Label, string Detail);

public sealed record ComboChipView(string CategoryId, string Text, Brush Foreground, string Tooltip);

public sealed record MixView(
    string Pair, string Turn, string Price, string Gain,
    Brush Tone, Brush NameTone, Brush PriceTone, string Tooltip);

public sealed record OfferView(
    string Name, string Reach, string Combo, string Price, string Gain,
    Brush Tone, Brush NameTone, Brush PriceTone, Brush ComboTone,
    ImageSource? Icon, string Tooltip)
{
    /// <summary>살 수 있는 후보에 빈 도움말이 뜨지 않게 한다.</summary>
    public bool HasTooltip => Tooltip.Length > 0;

    public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class CellView : INotifyPropertyChanged
{
    private string _title = "";
    private string _label = "";
    private string _tooltip = "";
    private Brush _titleBrush = Theme.Text;
    private Brush _foreground = Theme.TextDim;
    private Brush _background = Theme.EmptyFill;
    private Brush _borderBrush = Theme.SlotEdge;
    private Thickness _borderThickness = new(1);

    public string Title { get => _title; private set { _title = value; Raise(nameof(Title)); } }
    public string Label { get => _label; private set { _label = value; Raise(nameof(Label)); } }

    public string Tooltip
    {
        get => _tooltip;
        private set
        {
            _tooltip = value;
            Raise(nameof(Tooltip));
            Raise(nameof(HasTooltip));
        }
    }

    /// <summary>내용이 없는 칸에 빈 도움말이 뜨지 않게 한다.</summary>
    public bool HasTooltip => _tooltip.Length > 0;

    public Brush TitleBrush { get => _titleBrush; private set { _titleBrush = value; Raise(nameof(TitleBrush)); } }
    public Brush Foreground { get => _foreground; private set { _foreground = value; Raise(nameof(Foreground)); } }
    public Brush Background { get => _background; private set { _background = value; Raise(nameof(Background)); } }
    public Brush BorderBrush { get => _borderBrush; private set { _borderBrush = value; Raise(nameof(BorderBrush)); } }

    public Thickness BorderThickness
    {
        get => _borderThickness;
        private set { _borderThickness = value; Raise(nameof(BorderThickness)); }
    }

    public void SetClosed()
    {
        CharmId = 0;
        Icon = null;
        Fill("", "", "", Theme.TextDim, Theme.ClosedFill);
        SetEdge(false);
    }

    public void SetEmpty()
    {
        CharmId = 0;
        Icon = null;
        Fill("", "", "", Theme.TextDim, Theme.EmptyFill);
        SetEdge(false);
    }

    public void SetTablet(string name, int rotation, bool moved, ImageSource? icon = null)
    {
        CharmId = 0;
        Icon = icon;
        Fill(name, $"회전 {rotation}", name, Theme.TextDim, Theme.TabletFill);
        TitleBrush = Theme.TabletText;
        SetEdge(moved, Theme.TabletEdge);
    }

    /// <summary>우클릭으로 강화 우선을 지정할 때 이 칸의 아티팩트를 식별한다. 0이면 아티팩트가 아니다.</summary>
    public int CharmId { get; private set; }

    private ImageSource? _icon;

    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            _icon = value;
            Raise(nameof(Icon));
            Raise(nameof(IconVisibility));
            Raise(nameof(TitleVisibility));
        }
    }

    /// <summary>아이콘이 있으면 이름 글자는 숨긴다. 전체 이름은 도움말에 있다.</summary>
    public Visibility IconVisibility => _icon is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TitleVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 보여주는 숫자는 그 칸의 레벨이 아니라 거기 놓인 아티팩트가 실제로 받는 레벨이다.
    /// 상한에 걸려 남는 레벨이 있으면 색으로 알리고, 효과가 꺼졌으면 그 이유를 도움말에 적는다.
    /// </summary>
    public void SetCharm(
        string name, int level, int effective, CharmInactiveReason reason, bool moved,
        int charmId = 0, bool pinned = false, ImageSource? icon = null,
        IReadOnlyList<string>? effectLines = null)
    {
        CharmId = charmId;
        Icon = icon;
        var title = pinned ? "★ " + name : name;

        // 아이콘 모드에서는 이름 줄이 숨으므로 강화 표시가 레벨 줄로 내려온다.
        var star = pinned && icon is not null ? "★" : "";

        // 이름 다음에 무슨 아티팩트인지가 오고, 이 자리에서만 해당하는 이야기는 그 뒤에 온다.
        var lines = new List<string> { name };
        if (effectLines is not null) lines.AddRange(effectLines);

        if (reason != CharmInactiveReason.None)
        {
            lines.Add(Explain(reason));
            if (pinned) lines.Add(PinNote);

            Fill(title, star + "꺼짐", string.Join(Environment.NewLine, lines), Theme.Bad, Theme.SlotFill);
            SetEdge(moved);
            return;
        }

        var wasted = level > effective;
        var label = effective > 0 ? $"+{effective}" : level < 0 ? level.ToString() : "0";
        if (wasted) lines.Add($"칸 레벨 {level}, 이 아티팩트는 {effective}까지만 반영됩니다");
        if (pinned) lines.Add(PinNote);

        Fill(title, star + label, string.Join(Environment.NewLine, lines),
            level < 0 ? Theme.Bad : wasted ? Theme.Orange : effective > 0 ? Theme.Good : Theme.TextDim,
            Theme.SlotFill);
        SetEdge(moved);
    }

    private const string PinNote = "강화 우선: 가치를 2배로 칩니다. 우클릭으로 해제합니다.";

    private void Fill(string title, string label, string tooltip, Brush foreground, Brush background)
    {
        Title = title;
        Label = label;
        Tooltip = tooltip;
        TitleBrush = Theme.Text;
        Foreground = foreground;
        Background = background;
    }

    private static string Explain(CharmInactiveReason reason) => reason switch
    {
        CharmInactiveReason.Weapon => "연동된 무기를 들고 있지 않아 꺼져 있습니다. 옮겨도 켜지지 않습니다.",
        CharmInactiveReason.Disabled => "석판이 이 칸을 사용 불가로 만들었습니다.",
        CharmInactiveReason.NegativeLevel => "레벨이 0 미만이라 꺼져 있습니다.",
        CharmInactiveReason.Criteria => "이 아티팩트의 배치 조건을 만족하지 못했습니다.",
        _ => "",
    };

    private void SetEdge(bool moved, Brush? quiet = null)
    {
        BorderBrush = moved ? Theme.GoldEdge : quiet ?? Theme.SlotEdge;
        BorderThickness = new Thickness(moved ? 2 : 1);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
