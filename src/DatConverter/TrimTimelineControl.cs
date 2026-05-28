using System.ComponentModel;

namespace DatConverter;

public sealed class TrimTimelineControl : Control
{
    private TimeSpan? totalDuration;
    private TimeSpan current;
    private TimeSpan? start;
    private TimeSpan? end;
    private DragTarget dragTarget = DragTarget.None;

    public TrimTimelineControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 44;
        Cursor = Cursors.Hand;
    }

    public event EventHandler? CurrentChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool HasStartMarker => start.HasValue;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool HasEndMarker => end.HasValue;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TimeSpan Current
    {
        get => current;
        set
        {
            current = Clamp(value);
            Invalidate();
        }
    }

    public void SetTimeline(TimeSpan? duration)
    {
        totalDuration = duration;
        Current = current;
        Invalidate();
    }

    public void SetMarkers(TimeSpan? startOffset, TimeSpan? endOffset)
    {
        start = startOffset.HasValue ? Clamp(startOffset.Value) : null;
        end = endOffset.HasValue ? Clamp(endOffset.Value) : null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var track = GetTrackRectangle();
        using var trackBrush = new SolidBrush(Color.FromArgb(218, 222, 226));
        using var trimBrush = new SolidBrush(Color.FromArgb(122, 183, 255));
        using var currentBrush = new SolidBrush(Color.FromArgb(0, 120, 215));
        using var startBrush = new SolidBrush(Color.FromArgb(32, 150, 83));
        using var endBrush = new SolidBrush(Color.FromArgb(206, 84, 62));
        using var borderPen = new Pen(Color.FromArgb(185, 190, 196));

        e.Graphics.FillRectangle(trackBrush, track);
        e.Graphics.DrawRectangle(borderPen, track);

        if (start.HasValue && end.HasValue && end.Value > start.Value)
        {
            var x1 = OffsetToX(start.Value, track);
            var x2 = OffsetToX(end.Value, track);
            e.Graphics.FillRectangle(trimBrush, Rectangle.FromLTRB(Math.Min(x1, x2), track.Top, Math.Max(x1, x2), track.Bottom));
        }

        if (start.HasValue)
        {
            DrawMarker(e.Graphics, OffsetToX(start.Value, track), track, startBrush, "Start", above: true);
        }

        if (end.HasValue)
        {
            DrawMarker(e.Graphics, OffsetToX(end.Value, track), track, endBrush, "End", above: false);
        }

        DrawCurrent(e.Graphics, OffsetToX(current, track), track, currentBrush);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        dragTarget = HitTest(e.Location);
        UpdateFromMouse(e.X, dragTarget);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragTarget != DragTarget.None && e.Button == MouseButtons.Left)
        {
            UpdateFromMouse(e.X, dragTarget);
            return;
        }

        Cursor = HitTest(e.Location) == DragTarget.None ? Cursors.Hand : Cursors.SizeWE;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        dragTarget = DragTarget.None;
    }

    private void UpdateFromMouse(int x, DragTarget target)
    {
        var value = XToOffset(x, GetTrackRectangle());
        switch (target)
        {
            case DragTarget.Start:
                start = value;
                break;
            case DragTarget.End:
                end = value;
                break;
            default:
                current = value;
                CurrentChanged?.Invoke(this, EventArgs.Empty);
                break;
        }

        Invalidate();
    }

    private DragTarget HitTest(Point point)
    {
        var track = GetTrackRectangle();
        return DragTarget.Current;
    }

    private Rectangle GetTrackRectangle()
    {
        return new Rectangle(12, Height / 2 - 4, Math.Max(1, Width - 24), 8);
    }

    private int OffsetToX(TimeSpan offset, Rectangle track)
    {
        var durationSeconds = Math.Max(0.001, totalDuration?.TotalSeconds ?? 1);
        var ratio = Math.Clamp(offset.TotalSeconds / durationSeconds, 0, 1);
        return track.Left + (int)Math.Round(track.Width * ratio);
    }

    private TimeSpan XToOffset(int x, Rectangle track)
    {
        var durationSeconds = Math.Max(0.001, totalDuration?.TotalSeconds ?? 1);
        var ratio = Math.Clamp((x - track.Left) / (double)track.Width, 0, 1);
        return TimeSpan.FromSeconds(durationSeconds * ratio);
    }

    private TimeSpan Clamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return totalDuration.HasValue && value > totalDuration.Value ? totalDuration.Value : value;
    }

    private static void DrawCurrent(Graphics graphics, int x, Rectangle track, Brush brush)
    {
        using var pen = new Pen((brush as SolidBrush)?.Color ?? Color.Blue, 3);
        graphics.DrawLine(pen, x, track.Top - 11, x, track.Bottom + 11);
        var points = new[]
        {
            new Point(x - 7, track.Top - 15),
            new Point(x + 7, track.Top - 15),
            new Point(x, track.Top - 6)
        };
        graphics.FillPolygon(brush, points);
    }

    private static void DrawMarker(Graphics graphics, int x, Rectangle track, Brush brush, string label, bool above)
    {
        using var pen = new Pen((brush as SolidBrush)?.Color ?? Color.Black, 2);
        graphics.DrawLine(pen, x, track.Top - 13, x, track.Bottom + 13);
        var y = above ? track.Top - 27 : track.Bottom + 12;
        graphics.FillRectangle(brush, x - 18, y, 36, 14);
        using var font = new Font(FontFamily.GenericSansSerif, 7F);
        using var textBrush = new SolidBrush(Color.White);
        var size = graphics.MeasureString(label, font);
        graphics.DrawString(label, font, textBrush, x - size.Width / 2, y - 1);
    }

    private enum DragTarget
    {
        None,
        Current,
        Start,
        End
    }
}
