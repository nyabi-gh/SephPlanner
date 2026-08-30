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

namespace SephPlanner.Overlay;

public partial class MainWindow : Window
{
    private const double CompactWidth = 200;
    private const double DetailWidth = 420;

    private readonly ObservableCollection<CellView> _cells = new();
    private readonly ObservableCollection<MoveView> _moves = new();
    private readonly ObservableCollection<OfferView> _offers = new();
    private readonly CatalogStore _catalog = new();
    private readonly CancellationTokenSource _shutdown = new();

    private int _solving;
    private string _lastPlanned = "";
    private bool _expanded;

    /// <summary>상자나 상점이 열리고 닫히는 순간에만 저절로 펼치고 접는다.</summary>
    private bool _hadOffers;

    public MainWindow()
    {
        InitializeComponent();
        GridCells.ItemsSource = _cells;
        MoveList.ItemsSource = _moves;
        OfferList.ItemsSource = _offers;
        ApplyLayout();

        var client = new SnapshotClient();
        client.ConnectionChanged += connected => Dispatcher.Invoke(() =>
            StatusText.Text = connected ? "연결됨" : "게임 대기 중");
        client.SnapshotReceived += OnSnapshot;

        _ = client.RunAsync(_shutdown.Token);
    }

    private void OnSnapshot(GameSnapshot snapshot)
    {
        // 해를 찾는 데 수백 ms 가 걸리므로 UI 스레드에서 돌리지 않는다.
        // 앞선 계산이 아직 진행 중이면 이번 스냅샷은 건너뛴다.
        if (Interlocked.Exchange(ref _solving, 1) == 1) return;

        Task.Run(() =>
        {
            try
            {
                if (!_catalog.Refresh())
                {
                    Dispatcher.Invoke(() => ShowNotice("게임을 한 번 실행해 데이터를 만들어 주세요. (게임 안에서 F9)"));
                    return;
                }

                var plan = PlanBuilder.Build(snapshot, _catalog);
                Dispatcher.Invoke(() => Render(snapshot, plan));
            }
            finally
            {
                Interlocked.Exchange(ref _solving, 0);
            }
        });
    }

    private void ShowNotice(string message)
    {
        NoticeText.Text = message;
        NoticeText.Visibility = Visibility.Visible;
        ScorePanel.Visibility = Visibility.Collapsed;
        NextMoveText.Visibility = Visibility.Collapsed;

        // 안내만 띄우고 격자를 그대로 두면 직전 런의 배치가 남는다.
        _cells.Clear();
        _moves.Clear();
        _offers.Clear();
        OfferPanel.Visibility = Visibility.Collapsed;
        MovePanel.Visibility = Visibility.Collapsed;
        LegendText.Visibility = Visibility.Collapsed;
        _lastPlanned = "";
    }

    private void Render(GameSnapshot snapshot, Plan? plan)
    {
        if (plan is null)
        {
            ShowNotice(snapshot.Inventory is null
                ? "런이 진행 중이 아닙니다."
                : "인벤토리에 아티팩트가 없습니다.");
            return;
        }

        NoticeText.Visibility = snapshot.IsMultiplayer ? Visibility.Visible : Visibility.Collapsed;
        if (snapshot.IsMultiplayer) NoticeText.Text = "멀티플레이 세션 - 제안만 표시합니다.";

        ScorePanel.Visibility = Visibility.Visible;
        CurrentScoreText.Text = $"{plan.Current.Score:0.#}";
        BestScoreText.Text = $"{plan.Best.Score:0.#}";

        var improved = plan.Gain > 0.001;
        GainText.Text = improved ? $"+{plan.Gain:0.#}" : "최적";
        GainText.Foreground = improved ? Brushes.PaleGreen : new SolidColorBrush(Color.FromRgb(0x8A, 0x7F, 0xA6));

        AutoExpand(plan);
        RenderGrid(snapshot, plan);
        RenderOffers(plan);
        LegendText.Visibility = plan.Moves.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

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

        var next = plan.Moves.FirstOrDefault();
        NextMoveText.Text = next is null ? "" : $"{next.Label}  {next.Detail}";
        UpdateNextMoveVisibility();
    }

    /// <summary>
    /// 무엇을 집을지 고르는 순간에는 이름과 후보를 다 봐야 한다. 상자나 상점이 열리면 저절로 펼치고,
    /// 닫히면 되돌린다. 그 사이에 직접 접거나 펼친 것은 상황이 바뀔 때까지 그대로 둔다.
    /// </summary>
    private void AutoExpand(Plan plan)
    {
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
        ExpandButton.Content = _expanded ? "⌃" : "⌄";
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

    private void RenderOffers(Plan plan)
    {
        _offers.Clear();
        foreach (var advice in plan.Offers.Take(6))
        {
            var gain = advice.Gain;
            _offers.Add(new OfferView(
                advice.Candidate.Name,
                gain > 0.001 ? $"+{gain:0.#}" : gain < -0.001 ? $"{gain:0.#}" : "0",
                gain > 0.001 ? Brushes.PaleGreen : gain < -0.001 ? Brushes.Salmon : Brushes.Gray));
        }
        OfferPanel.Visibility = _offers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
                var name = Naming.Of(tablet.Definition.Names, tablet.Definition.Id, "석판");
                cell.SetTablet(name, tablet.Rotation, moved.Contains(position));
            }
            else if (plan.Best.Levels.TryGetValue(position, out var level))
            {
                plan.Best.EffectiveLevels.TryGetValue(position, out var effective);
                plan.Best.InactiveCells.TryGetValue(position, out var reason);
                plan.Names.TryGetValue(position, out var name);
                cell.SetCharm(name ?? "", level, effective, reason, moved.Contains(position));
            }
            else cell.SetEmpty();
        }
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // 테두리 없는 창이라 제목 표시줄이 없다. 키보드로도 닫을 수 있어야 한다.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        base.OnKeyDown(e);
    }

    // 게임에 포커스가 가 있는 동안에도 접고 펼칠 수 있어야 해서 전역 단축키로 등록한다.
    private const int HotkeyId = 0xB1;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModNoRepeat = 0x4000;
    private const int WmHotkey = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    private HwndSource? _source;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _source = (HwndSource)PresentationSource.FromVisual(this)!;
        _source.AddHook(OnWindowMessage);

        var key = (uint)KeyInterop.VirtualKeyFromKey(Key.P);
        if (!RegisterHotKey(_source.Handle, HotkeyId, ModControl | ModAlt | ModNoRepeat, key))
        {
            // 다른 프로그램이 이미 쓰고 있으면 등록에 실패한다. 버튼으로는 여전히 접고 펼 수 있다.
            ExpandButton.ToolTip = "펼치기 (Ctrl+Alt+P 는 다른 프로그램이 쓰는 중)";
        }
    }

    private IntPtr OnWindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey || wParam.ToInt32() != HotkeyId) return IntPtr.Zero;

        OnToggleDetail(this, new RoutedEventArgs());
        handled = true;
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_source != null)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _source.RemoveHook(OnWindowMessage);
        }

        _shutdown.Cancel();
        base.OnClosed(e);
    }
}

public sealed record MoveView(string Label, string Detail);

public sealed record OfferView(string Name, string Gain, Brush Tone);

public sealed class CellView : INotifyPropertyChanged
{
    private static readonly Brush EmptyFill = new SolidColorBrush(Color.FromRgb(0x1C, 0x18, 0x24));
    private static readonly Brush ClosedFill = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x17));
    private static readonly Brush TabletFill = new SolidColorBrush(Color.FromRgb(0x2B, 0x3A, 0x2A));
    private static readonly Brush MutedText = new SolidColorBrush(Color.FromRgb(0x5A, 0x51, 0x70));
    private static readonly Brush NameText = new SolidColorBrush(Color.FromRgb(0xA9, 0xA0, 0xC2));
    private static readonly Brush TabletName = new SolidColorBrush(Color.FromRgb(0x9C, 0xC0, 0x9A));
    private static readonly Brush Wasted = new SolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x27));
    private static readonly Brush OffText = new SolidColorBrush(Color.FromRgb(0xB4, 0x6A, 0x6A));
    private static readonly Brush MovedEdge = new SolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x27));
    private static readonly Brush QuietEdge = new SolidColorBrush(Color.FromRgb(0x2A, 0x24, 0x34));

    private string _title = "";
    private string _label = "";
    private string _tooltip = "";
    private Brush _titleBrush = NameText;
    private Brush _foreground = MutedText;
    private Brush _background = EmptyFill;
    private Brush _borderBrush = QuietEdge;
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
        Fill("", "", "", MutedText, ClosedFill);
        SetEdge(false);
    }

    public void SetEmpty()
    {
        Fill("", "", "", MutedText, EmptyFill);
        SetEdge(false);
    }

    public void SetTablet(string name, int rotation, bool moved)
    {
        Fill(name, $"회전 {rotation}", name, Brushes.DarkSeaGreen, TabletFill);
        TitleBrush = TabletName;
        SetEdge(moved);
    }

    /// <summary>
    /// 보여주는 숫자는 그 칸의 레벨이 아니라 거기 놓인 아티팩트가 실제로 받는 레벨이다.
    /// 상한에 걸려 남는 레벨이 있으면 색으로 알리고, 효과가 꺼졌으면 그 이유를 도움말에 적는다.
    /// </summary>
    public void SetCharm(string name, int level, int effective, CharmInactiveReason reason, bool moved)
    {
        if (reason != CharmInactiveReason.None)
        {
            Fill(name, "꺼짐", Explain(reason), OffText, EmptyFill);
            SetEdge(moved);
            return;
        }

        var wasted = level > effective;
        var label = effective > 0 ? $"+{effective}" : level < 0 ? level.ToString() : "0";
        var tooltip = wasted ? $"칸 레벨 {level}, 이 아티팩트는 {effective}까지만 반영됩니다" : name;

        Fill(name, label, tooltip,
            level < 0 ? Brushes.Salmon : wasted ? Wasted : effective > 0 ? Brushes.PaleGreen : MutedText,
            EmptyFill);
        SetEdge(moved);
    }

    private void Fill(string title, string label, string tooltip, Brush foreground, Brush background)
    {
        Title = title;
        Label = label;
        Tooltip = tooltip;
        TitleBrush = NameText;
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

    private void SetEdge(bool moved)
    {
        BorderBrush = moved ? MovedEdge : QuietEdge;
        BorderThickness = new Thickness(moved ? 2 : 1);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
