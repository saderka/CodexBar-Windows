using System.Drawing;

namespace CodexBar.Core;

public static class OverlayPlacement
{
    public static Point Restore(Point? saved, Size overlay, IReadOnlyList<Rectangle> workAreas)
    {
        if (workAreas.Count == 0) return saved ?? Point.Empty;
        var area = workAreas[0];
        if (saved is { } position)
        {
            var bounds = new Rectangle(position, overlay);
            var intersections = workAreas.Select(a => (Area: a, Intersection: Rectangle.Intersect(a, bounds)))
                .OrderByDescending(a => (long)a.Intersection.Width * a.Intersection.Height).First();
            if (intersections.Intersection.Width > 0 && intersections.Intersection.Height > 0) area = intersections.Area;
            else saved = null;
        }
        var desired = saved ?? new Point(area.Right - overlay.Width - 24, area.Bottom - overlay.Height - 24);
        return new(Math.Clamp(desired.X, area.Left, Math.Max(area.Left, area.Right - overlay.Width)),
            Math.Clamp(desired.Y, area.Top, Math.Max(area.Top, area.Bottom - overlay.Height)));
    }
}
