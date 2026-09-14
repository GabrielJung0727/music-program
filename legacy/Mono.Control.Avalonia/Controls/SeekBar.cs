using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Mono.Protocol;

namespace Mono.Control.Controls;

/// <summary>
/// 시크바 — 트랙 · 재생 채움 · 반응 히트맵 · 타임스탬프 핀을 한 번에 그린다.
/// 마커마다 Border를 만들면 큰 곡에서 프레임이 떨어지므로 직접 렌더한다.
/// 시킹이 금지된 룸에서는 SeekEnabled=false 로 두어 표시는 하되 조작만 막는다.
/// </summary>
public sealed class SeekBar : Avalonia.Controls.Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(Value), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<SeekBar, double>(nameof(Maximum), 1d);

    public static readonly StyledProperty<IReadOnlyList<SnapshotHeatBucket>?> HeatmapProperty =
        AvaloniaProperty.Register<SeekBar, IReadOnlyList<SnapshotHeatBucket>?>(nameof(Heatmap));

    public static readonly StyledProperty<IReadOnlyList<SnapshotPin>?> PinsProperty =
        AvaloniaProperty.Register<SeekBar, IReadOnlyList<SnapshotPin>?>(nameof(Pins));

    public static readonly StyledProperty<bool> SeekEnabledProperty =
        AvaloniaProperty.Register<SeekBar, bool>(nameof(SeekEnabled), true);

    static SeekBar()
    {
        AffectsRender<SeekBar>(ValueProperty, MaximumProperty, HeatmapProperty, PinsProperty, SeekEnabledProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public IReadOnlyList<SnapshotHeatBucket>? Heatmap { get => GetValue(HeatmapProperty); set => SetValue(HeatmapProperty, value); }
    public IReadOnlyList<SnapshotPin>? Pins { get => GetValue(PinsProperty); set => SetValue(PinsProperty, value); }
    public bool SeekEnabled { get => GetValue(SeekEnabledProperty); set => SetValue(SeekEnabledProperty, value); }

    /// <summary>사용자가 시킹을 마쳤을 때 media_time(ms)을 전달한다.</summary>
    public event EventHandler<double>? Seeked;

    private bool _dragging;

    public SeekBar()
    {
        Height = 26;
        Focusable = false;
    }

    private IBrush Res(string key, IBrush fallback)
        => this.TryFindResource(key, out var v) && v is IBrush b ? b : fallback;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        if (w <= 0) return;

        var line = Res("Mono.Border", Brushes.Gainsboro);
        var accent = Res("Mono.Accent", Brushes.MediumSlateBlue);
        var warn = Res("Mono.Warn", Brushes.Orange);
        // 히트맵은 강조색을 옅게 깐다. 리소스가 단색이 아니면 기본색으로 물러난다.
        var heatColor = accent is ISolidColorBrush s ? s.Color : Colors.MediumSlateBlue;

        const double trackH = 4;
        var trackY = (Bounds.Height - trackH) / 2;
        var duration = (long)Math.Max(1, Maximum);

        // 히트맵을 트랙 뒤에 깔아 "많이 반응한 구간"을 먼저 읽히게 한다.
        foreach (var band in SeekLayers.Bands(Heatmap ?? [], duration))
        {
            var x = band.Start * w;
            var bw = Math.Max(1, (band.End - band.Start) * w);
            var h = 6 + band.Intensity * 12;
            ctx.FillRectangle(
                new ImmutableSolidColorBrush(heatColor, 0.10 + band.Intensity * 0.30),
                new Rect(x, trackY + trackH / 2 - h / 2, bw, h));
        }

        ctx.FillRectangle(line, new Rect(0, trackY, w, trackH), 2);

        var progress = Math.Clamp(Value / duration, 0, 1);
        if (progress > 0)
            ctx.FillRectangle(accent, new Rect(0, trackY, progress * w, trackH), 2);

        foreach (var mark in SeekLayers.Marks(Pins ?? [], duration))
            ctx.FillRectangle(warn, new Rect(mark.Position * w - 1, trackY - 5, 2, trackH + 10), 1);

        // 핸들은 시킹이 가능할 때만 — 못 만지는 컨트롤에 손잡이를 그리지 않는다.
        if (SeekEnabled)
            ctx.DrawEllipse(accent, null, new Point(progress * w, trackY + trackH / 2), 6, 6);
    }

    private double PositionToMs(double x)
        => Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * Math.Max(1, Maximum);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!SeekEnabled) return;
        _dragging = true;
        e.Pointer.Capture(this);
        Value = PositionToMs(e.GetPosition(this).X);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) Value = PositionToMs(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        Seeked?.Invoke(this, Value);
    }
}
