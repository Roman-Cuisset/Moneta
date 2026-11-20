using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Moneta.Core;

namespace Moneta.App;

public sealed class ReportPlots : FrameworkElement
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Brush[] SliceBrushes = [Brushes.MediumPurple, Brushes.DarkCyan, Brushes.IndianRed, Brushes.OliveDrab,
        Brushes.DodgerBlue, Brushes.DarkOrange, Brushes.MediumVioletRed, Brushes.SlateGray];
    private IReadOnlyList<ReportGroup> _groups = [];
    private Currency? _currency;
    private int _mode;

    public void SetData(IReadOnlyList<ReportGroup> groups, Currency currency, int mode)
    {
        _groups = groups;
        _currency = currency;
        _mode = mode;
        MinWidth = mode == 0 ? 500 : Math.Max(500, 105 + groups.Count * 70);
        Height = mode == 0 ? Math.Max(285, 58 + groups.Count * 27) : 310;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (_groups.Count == 0) return;
        var text = TryFindResource("TextBrush") as Brush ?? SystemColors.ControlTextBrush;
        var muted = TryFindResource("MutedBrush") as Brush ?? text;
        var accent = TryFindResource("AccentBrush") as Brush ?? Brushes.MediumPurple;
        if (_mode == 0) DrawDonut(drawing, text);
        else DrawColumns(drawing, text, muted, accent);
    }

    private void DrawDonut(DrawingContext drawing, Brush text)
    {
        var total = _groups.Sum(row => row.Total.Expense);
        if (total == 0) { Write(drawing, "Aucune dépense valorisée", 10, 20, text); return; }
        var center = new Point(113, 134);
        const double radius = 80;
        var angle = -Math.PI / 2;
        var active = _groups.Where(item => item.Total.Expense > 0).ToArray();
        for (var index = 0; index < active.Length; index++)
        {
            var row = active[index];
            var next = angle + (double)(row.Total.Expense / total) * (2 * Math.PI);
            var color = SliceBrushes[index % SliceBrushes.Length];
            if (active.Length == 1) drawing.DrawEllipse(null, new Pen(color, 32), center, radius, radius);
            else if (next > angle)
            {
                var arc = new StreamGeometry();
                using (var context = arc.Open())
                {
                    context.BeginFigure(PointAt(angle), false, false);
                    context.ArcTo(PointAt(next), new Size(radius, radius), 0, next - angle > Math.PI, SweepDirection.Clockwise, true, false);
                }
                drawing.DrawGeometry(null, new Pen(color, 32), arc);
            }
            var rowY = 36 + index * 27;
            drawing.DrawRoundedRectangle(color, null, new Rect(240, rowY + 5, 11, 11), 2, 2);
            Write(drawing, $"{row.Label} · {Number(row.Total.Expense)} {_currency!.Code}", 259, rowY, text, Math.Max(100, ActualWidth - 271));
            angle = next;
        }
        Point PointAt(double value) => new(center.X + radius * Math.Cos(value), center.Y + radius * Math.Sin(value));
    }

    private void DrawColumns(DrawingContext drawing, Brush text, Brush muted, Brush accent)
    {
        const double origin = 100, baseline = 248, height = 187;
        var spacing = Math.Clamp((ActualWidth - origin - 10) / _groups.Count, 70, 180);
        var expenses = _groups.Max(row => row.Total.Expense);
        var maximum = _mode == 1 ? expenses : Math.Max(expenses, _groups.Max(row => row.Total.Income));
        var costs = TryFindResource("NegativeBrush") as Brush ?? Brushes.IndianRed;
        var axis = new Pen(muted, 0.7);
        drawing.DrawLine(axis, new Point(origin, 61), new Point(origin, baseline));
        drawing.DrawLine(axis, new Point(origin, baseline), new Point(origin + spacing * _groups.Count, baseline));
        Write(drawing, _currency!.Code, 10, 25, text);
        Write(drawing, Number(maximum), 9, 55, text, 87);
        Write(drawing, "0", 9, baseline - 7, text);
        drawing.DrawRectangle(costs, null, new Rect(origin, 24, 12, 12));
        Write(drawing, "Dépenses", origin + 18, 21, text);
        if (_mode == 2)
        {
            drawing.DrawRectangle(accent, null, new Rect(origin + 103, 24, 12, 12));
            Write(drawing, "Recettes", origin + 120, 21, text);
        }
        for (var index = 0; index < _groups.Count; index++)
        {
            var group = _groups[index];
            var x = origin + 12 + index * spacing;
            DrawBar(group.Total.Expense, x, costs);
            if (_mode == 2) DrawBar(group.Total.Income, x + 20, accent);
            var key = DateOnly.ParseExact(group.Key + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            Write(drawing, key.ToString("MM/yy", French) + (group.Total.Missing > 0 ? " *" : ""), x - 5, baseline + 8, text);
        }
        if (maximum == 0) Write(drawing, "Aucun montant à représenter", origin + 25, 116, text);

        void DrawBar(decimal amount, double x, Brush brush)
        {
            if (amount == 0 || maximum == 0) return;
            var size = (double)(amount / maximum) * height;
            drawing.DrawRectangle(brush, null, new Rect(x, baseline - size, 16, size));
        }
    }

    private string Number(decimal amount) => amount.ToString($"N{_currency!.DecimalPlaces}", French);

    private void Write(DrawingContext drawing, string value, double x, double y, Brush brush, double max = 240)
    {
        var label = new FormattedText(value, French, FlowDirection.LeftToRight, Face, 13, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = max, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        drawing.DrawText(label, new Point(x, y));
    }
}
