using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SephPlanner.Core.Ipc;

namespace SephPlanner.Overlay;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<CellView> _cells = new();
    private readonly CancellationTokenSource _cts = new();

    public MainWindow()
    {
        InitializeComponent();
        GridCells.ItemsSource = _cells;
        ResetCells(GridWidth, GridHeight);

        var client = new SnapshotClient();
        client.ConnectionChanged += connected => Dispatcher.Invoke(() =>
        {
            StatusText.Text = connected ? "연결됨" : "게임 대기 중";
        });
        client.SnapshotReceived += snapshot => Dispatcher.Invoke(() => Render(snapshot));

        _ = client.RunAsync(_cts.Token);
    }

    // 게임의 GridInventory 최대 크기와 같다.
    private const int GridWidth = 6;
    private const int GridHeight = 7;

    private void ResetCells(int width, int height)
    {
        _cells.Clear();
        for (var i = 0; i < width * height; i++) _cells.Add(new CellView());
    }

    private void Render(GameSnapshot snapshot)
    {
        var inv = snapshot.Inventory;
        if (inv is null) return;

        HintText.Text = snapshot.IsMultiplayer
            ? "멀티플레이 세션입니다. 조언만 표시합니다."
            : $"아이템 {inv.Items.Count}개 · 석판 {inv.Tablets.Count}개";

        for (var i = 0; i < _cells.Count; i++) _cells[i].Reset();

        foreach (var (key, level) in inv.LevelMatrix)
        {
            var parts = key.Split(',');
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y)) continue;

            var index = y * GridWidth + x;
            if (index < 0 || index >= _cells.Count) continue;

            _cells[index].Label = level > 0 ? $"+{level}" : level.ToString();
            // 레벨이 음수면 아티팩트가 죽는 자리다. 눈에 띄어야 한다.
            _cells[index].Foreground = level < 0 ? Brushes.Salmon
                : level > 0 ? Brushes.PaleGreen
                : new SolidColorBrush(Color.FromRgb(0x8A, 0x7F, 0xA6));
        }
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e) => DragMove();

    protected override void OnClosed(System.EventArgs e)
    {
        _cts.Cancel();
        base.OnClosed(e);
    }
}

public sealed class CellView : System.ComponentModel.INotifyPropertyChanged
{
    private string _label = "";
    private Brush _foreground = Brushes.Gray;

    public string Label
    {
        get => _label;
        set { _label = value; Raise(nameof(Label)); }
    }

    public Brush Foreground
    {
        get => _foreground;
        set { _foreground = value; Raise(nameof(Foreground)); }
    }

    public Brush Background { get; } = new SolidColorBrush(Color.FromRgb(0x1C, 0x18, 0x24));

    public void Reset()
    {
        Label = "";
        Foreground = Brushes.Gray;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
