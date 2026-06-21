using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using Client.Models;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Client.Controls;

public sealed class MileageOdometerChartView : GraphicsView
{
    public static readonly BindableProperty EntriesProperty = BindableProperty.Create(
        nameof(Entries),
        typeof(IEnumerable),
        typeof(MileageOdometerChartView),
        default(IEnumerable),
        propertyChanged: OnEntriesChanged);

    public static readonly BindableProperty LineColorProperty = BindableProperty.Create(
        nameof(LineColor),
        typeof(Color),
        typeof(MileageOdometerChartView),
        Color.FromArgb("#8B5CF6"),
        propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty AxisColorProperty = BindableProperty.Create(
        nameof(AxisColor),
        typeof(Color),
        typeof(MileageOdometerChartView),
        Color.FromArgb("#6F6680"),
        propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty GridLineColorProperty = BindableProperty.Create(
        nameof(GridLineColor),
        typeof(Color),
        typeof(MileageOdometerChartView),
        Color.FromArgb("#241A36"),
        propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty LabelColorProperty = BindableProperty.Create(
        nameof(LabelColor),
        typeof(Color),
        typeof(MileageOdometerChartView),
        Colors.White,
        propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty MutedLabelColorProperty = BindableProperty.Create(
        nameof(MutedLabelColor),
        typeof(Color),
        typeof(MileageOdometerChartView),
        Color.FromArgb("#9B93AA"),
        propertyChanged: OnVisualPropertyChanged);

    private readonly MileageOdometerChartDrawable chartDrawable = new();

    public MileageOdometerChartView()
    {
        Drawable = chartDrawable;
        MinimumHeightRequest = 240;
    }

    public IEnumerable? Entries
    {
        get => (IEnumerable?)GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public Color AxisColor
    {
        get => (Color)GetValue(AxisColorProperty);
        set => SetValue(AxisColorProperty, value);
    }

    public Color GridLineColor
    {
        get => (Color)GetValue(GridLineColorProperty);
        set => SetValue(GridLineColorProperty, value);
    }

    public Color LabelColor
    {
        get => (Color)GetValue(LabelColorProperty);
        set => SetValue(LabelColorProperty, value);
    }

    public Color MutedLabelColor
    {
        get => (Color)GetValue(MutedLabelColorProperty);
        set => SetValue(MutedLabelColorProperty, value);
    }

    private static void OnEntriesChanged(BindableObject bindable, object oldValue, object newValue)
    {
        MileageOdometerChartView chartView = (MileageOdometerChartView)bindable;

        if (oldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= chartView.OnEntriesCollectionChanged;
        }

        if (newValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += chartView.OnEntriesCollectionChanged;
        }

        chartView.RefreshDrawable();
    }

    private static void OnVisualPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((MileageOdometerChartView)bindable).RefreshDrawable();
    }

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshDrawable();
    }

    private void RefreshDrawable()
    {
        chartDrawable.Entries = Entries?
            .OfType<MileageEntry>()
            .Where(static entry => entry.Odometer > 0)
            .OrderBy(static entry => entry.RecordedAtUtc)
            .ToList() ?? [];
        chartDrawable.LineColor = LineColor;
        chartDrawable.AxisColor = AxisColor;
        chartDrawable.GridLineColor = GridLineColor;
        chartDrawable.LabelColor = LabelColor;
        chartDrawable.MutedLabelColor = MutedLabelColor;
        Invalidate();
    }

    private sealed class MileageOdometerChartDrawable : IDrawable
    {
        private const float LeftPadding = 74;
        private const float TopPadding = 38;
        private const float RightPadding = 24;
        private const float BottomPadding = 54;
        private const int YTickCount = 5;

        public IReadOnlyList<MileageEntry> Entries { get; set; } = [];

        public Color LineColor { get; set; } = Color.FromArgb("#8B5CF6");

        public Color AxisColor { get; set; } = Color.FromArgb("#6F6680");

        public Color GridLineColor { get; set; } = Color.FromArgb("#241A36");

        public Color LabelColor { get; set; } = Colors.White;

        public Color MutedLabelColor { get; set; } = Color.FromArgb("#9B93AA");

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (dirtyRect.Width <= LeftPadding + RightPadding || dirtyRect.Height <= TopPadding + BottomPadding)
            {
                return;
            }

            canvas.Antialias = true;

            RectF plotArea = new(
                dirtyRect.Left + LeftPadding,
                dirtyRect.Top + TopPadding,
                dirtyRect.Width - LeftPadding - RightPadding,
                dirtyRect.Height - TopPadding - BottomPadding);

            DrawTitles(canvas, dirtyRect, plotArea);

            if (Entries.Count == 0)
            {
                DrawAxes(canvas, plotArea);
                DrawEmptyState(canvas, plotArea);
                return;
            }

            (double minY, double maxY) = CreateOdometerRange();
            DrawGridAndLabels(canvas, plotArea, minY, maxY);
            DrawAxes(canvas, plotArea);
            DrawDateLabels(canvas, plotArea);
            DrawSeries(canvas, plotArea, minY, maxY);
        }

        private void DrawTitles(ICanvas canvas, RectF dirtyRect, RectF plotArea)
        {
            canvas.FontColor = LabelColor;
            canvas.FontSize = 12;
            canvas.DrawString(
                "Odometer (km)",
                plotArea.Left,
                dirtyRect.Top + 8,
                plotArea.Width,
                18,
                HorizontalAlignment.Left,
                VerticalAlignment.Top);

            canvas.FontColor = MutedLabelColor;
            canvas.FontSize = 11;
            canvas.DrawString(
                "Date",
                plotArea.Left,
                plotArea.Bottom + 32,
                plotArea.Width,
                18,
                HorizontalAlignment.Right,
                VerticalAlignment.Top);
        }

        private void DrawGridAndLabels(ICanvas canvas, RectF plotArea, double minY, double maxY)
        {
            canvas.FontColor = MutedLabelColor;
            canvas.FontSize = 10;
            canvas.StrokeColor = GridLineColor;
            canvas.StrokeSize = 1;

            for (int i = 0; i < YTickCount; i++)
            {
                double value = minY + ((maxY - minY) * i / (YTickCount - 1));
                float y = MapY(value, plotArea, minY, maxY);

                canvas.DrawLine(plotArea.Left, y, plotArea.Right, y);
                canvas.DrawString(
                    FormatOdometerTick(value, maxY - minY),
                    plotArea.Left - LeftPadding + 2,
                    y - 9,
                    LeftPadding - 12,
                    18,
                    HorizontalAlignment.Right,
                    VerticalAlignment.Center);
            }
        }

        private void DrawAxes(ICanvas canvas, RectF plotArea)
        {
            canvas.StrokeColor = AxisColor;
            canvas.StrokeSize = 1.2f;
            canvas.DrawLine(plotArea.Left, plotArea.Top, plotArea.Left, plotArea.Bottom);
            canvas.DrawLine(plotArea.Left, plotArea.Bottom, plotArea.Right, plotArea.Bottom);
        }

        private void DrawSeries(ICanvas canvas, RectF plotArea, double minY, double maxY)
        {
            List<PointF> points = Entries
                .Select((entry, index) => new PointF(
                    MapX(entry, index, plotArea),
                    MapY(entry.Odometer, plotArea, minY, maxY)))
                .ToList();

            if (points.Count > 1)
            {
                PathF areaPath = new();
                areaPath.MoveTo(points[0].X, plotArea.Bottom);

                foreach (PointF point in points)
                {
                    areaPath.LineTo(point.X, point.Y);
                }

                areaPath.LineTo(points[^1].X, plotArea.Bottom);
                areaPath.Close();

                canvas.FillColor = LineColor.WithAlpha(0.14f);
                canvas.FillPath(areaPath);

                PathF linePath = new();
                linePath.MoveTo(points[0].X, points[0].Y);

                foreach (PointF point in points.Skip(1))
                {
                    linePath.LineTo(point.X, point.Y);
                }

                canvas.StrokeColor = LineColor;
                canvas.StrokeSize = 3;
                canvas.DrawPath(linePath);
            }

            foreach (PointF point in points)
            {
                canvas.FillColor = LineColor;
                canvas.FillCircle(point.X, point.Y, 4.5f);
                canvas.StrokeColor = Colors.White.WithAlpha(0.8f);
                canvas.StrokeSize = 1.4f;
                canvas.DrawCircle(point.X, point.Y, 4.5f);
            }
        }

        private void DrawDateLabels(ICanvas canvas, RectF plotArea)
        {
            canvas.FontColor = MutedLabelColor;
            canvas.FontSize = 10;
            canvas.StrokeColor = GridLineColor;
            canvas.StrokeSize = 1;

            foreach (int index in CreateDateLabelIndexes())
            {
                MileageEntry entry = Entries[index];
                float x = MapX(entry, index, plotArea);

                canvas.DrawLine(x, plotArea.Top, x, plotArea.Bottom);
                canvas.DrawString(
                    FormatDateLabel(entry.Timestamp),
                    x - 42,
                    plotArea.Bottom + 8,
                    84,
                    18,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Top);
            }
        }

        private void DrawEmptyState(ICanvas canvas, RectF plotArea)
        {
            canvas.FontColor = MutedLabelColor;
            canvas.FontSize = 13;
            canvas.DrawString(
                "No mileage entries yet",
                plotArea.Left,
                plotArea.Top,
                plotArea.Width,
                plotArea.Height,
                HorizontalAlignment.Center,
                VerticalAlignment.Center);
        }

        private (double Min, double Max) CreateOdometerRange()
        {
            double min = Entries.Min(static entry => entry.Odometer);
            double max = Entries.Max(static entry => entry.Odometer);
            double range = max - min;
            double padding = range > 0
                ? Math.Max(range * 0.12, 1)
                : Math.Max(max * 0.02, 10);

            return (Math.Max(0, min - padding), max + padding);
        }

        private float MapX(MileageEntry entry, int index, RectF plotArea)
        {
            if (Entries.Count == 1)
            {
                return plotArea.Center.X;
            }

            long minTicks = Entries[0].RecordedAtUtc.UtcDateTime.Ticks;
            long maxTicks = Entries[^1].RecordedAtUtc.UtcDateTime.Ticks;

            if (minTicks == maxTicks)
            {
                return plotArea.Left + (plotArea.Width * index / (Entries.Count - 1));
            }

            double percent = (entry.RecordedAtUtc.UtcDateTime.Ticks - minTicks) / (double)(maxTicks - minTicks);
            return plotArea.Left + (float)(plotArea.Width * percent);
        }

        private static float MapY(double odometer, RectF plotArea, double minY, double maxY)
        {
            double percent = (odometer - minY) / (maxY - minY);
            return plotArea.Bottom - (float)(plotArea.Height * percent);
        }

        private IReadOnlyList<int> CreateDateLabelIndexes()
        {
            return Entries.Count switch
            {
                1 => [0],
                2 => [0, 1],
                _ => [0, Entries.Count / 2, Entries.Count - 1]
            };
        }

        private static string FormatOdometerTick(double value, double range)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }

        private string FormatDateLabel(DateTime date)
        {
            DateTime first = Entries[0].Timestamp;
            DateTime last = Entries[^1].Timestamp;
            string format = (last - first).TotalDays > 365 ? "MMM yyyy" : "d MMM";
            return date.ToString(format, CultureInfo.CurrentCulture);
        }
    }
}
