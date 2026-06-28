using System;
using System.Windows;

namespace Scalpel.Services
{
    /// <summary>Angle-snapping for the straight-line tool.</summary>
    public static class LineSnap
    {
        /// <summary>Snaps <paramref name="end"/> so the segment start->end lies on the
        /// nearest 45-degree ray, preserving the drag length. Returns end unchanged when
        /// start == end.</summary>
        public static Point SnapEndpoint(Point start, Point end)
        {
            double dx = end.X - start.X, dy = end.Y - start.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return end;
            double step = Math.PI / 4.0;
            double snapped = Math.Round(Math.Atan2(dy, dx) / step) * step;
            return new Point(start.X + Math.Cos(snapped) * len, start.Y + Math.Sin(snapped) * len);
        }
    }
}
