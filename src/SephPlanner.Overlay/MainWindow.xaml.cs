using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;

namespace SephPlanner.Overlay;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<CellView> _cells = new();
    private readonly ObservableCollection<string> _moves = new();
    private readonly ObservableCollection<OfferView> _offers = new();
    private readonly CatalogStore _catalog = new();
    private readonly CancellationTokenSource _shutdown = new();

    private int _solving;
    private string _lastPlanned = "";

    public MainWindow()
    {
        InitializeComponent();
        GridCells.ItemsSource = _cells;
        MoveList.ItemsSource = _moves;
        OfferList.ItemsSource = _offers;

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

        // 안내만 띄우고 격자를 그대로 두면 직전 런의 배치가 남는다.
        _cells.Clear();
        _moves.Clear();
        _offers.Clear();
        OfferPanel.Visibility = Visibility.Collapsed;
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

        RenderGrid(snapshot, plan);
        RenderOffers(plan);

        // 제안이 그대로면 목록을 다시 만들지 않는다. 스냅샷마다 깜빡이는 것을 막는다.
        var signature = string.Join("|", plan.Moves.Select(m => $"{m.Label}{m.Detail}"));
        if (signature == _lastPlanned) return;
        _lastPlanned = signature;

        _moves.Clear();
        foreach (var move in plan.Moves.Take(6))
            _moves.Add($"{move.Label}  {move.Detail}");
        if (plan.Moves.Count > 6) _moves.Add($"… 외 {plan.Moves.Count - 6}개");
    }

    private void RenderOffers(Plan plan)
    {
        _offers.Clear();
        foreach (var advice in plan.Offers.Take(4))
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
                cell.SetTablet(Naming.Short(name), $"{name} · 회전 {tablet.Rotation}", moved.Contains(position));
            }
            else if (plan.Best.Levels.TryGetValue(position, out var level)) cell.SetLevel(level, moved.Contains(position));
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

    protected override void OnClosed(EventArgs e)
    {
        _shutdown.Cancel();
        base.OnClosed(e);
    }
}

public sealed record OfferView(string Name, string Gain, Brush Tone);

public sealed class CellView : INotifyPropertyChanged
{
    private static readonly Brush EmptyFill = new SolidColorBrush(Color.FromRgb(0x1C, 0x18, 0x24));
    private static readonly Brush ClosedFill = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x17));
    private static readonly Brush TabletFill = new SolidColorBrush(Color.FromRgb(0x2B, 0x3A, 0x2A));
    private static readonly Brush MutedText = new SolidColorBrush(Color.FromRgb(0x5A, 0x51, 0x70));
    private static readonly Brush MovedEdge = new SolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x27));
    private static readonly Brush QuietEdge = new SolidColorBrush(Color.FromRgb(0x2A, 0x24, 0x34));

    private string _label = "";
    private string _tooltip = "";
    private Brush _foreground = MutedText;
    private Brush _background = EmptyFill;
    private Brush _borderBrush = QuietEdge;
    private Thickness _borderThickness = new(1);

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
        Label = "";
        Tooltip = "";
        Background = ClosedFill;
        SetEdge(false);
    }

    public void SetEmpty()
    {
        Label = "";
        Tooltip = "";
        Foreground = MutedText;
        Background = EmptyFill;
        SetEdge(false);
    }

    public void SetTablet(string label, string tooltip, bool moved)
    {
        Label = label;
        Tooltip = tooltip;
        Foreground = Brushes.DarkSeaGreen;
        Background = TabletFill;
        SetEdge(moved);
    }

    public void SetLevel(int level, bool moved)
    {
        Label = level > 0 ? $"+{level}" : level.ToString();
        Tooltip = "";
        Foreground = level < 0 ? Brushes.Salmon : level > 0 ? Brushes.PaleGreen : MutedText;
        Background = EmptyFill;
        SetEdge(moved);
    }

    private void SetEdge(bool moved)
    {
        BorderBrush = moved ? MovedEdge : QuietEdge;
        BorderThickness = new Thickness(moved ? 2 : 1);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
