using System.Windows;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class LineSnapTests
    {
        const double Eps = 1e-6;

        [Fact]
        public void NearHorizontal_SnapsToHorizontal()
        {
            var e = LineSnap.SnapEndpoint(new Point(0, 0), new Point(100, 7));
            Assert.Equal(0, e.Y, 3);          // y flattens to start.Y
            Assert.True(e.X > 99);            // length preserved roughly along +x
        }

        [Fact]
        public void NearVertical_SnapsToVertical()
        {
            var e = LineSnap.SnapEndpoint(new Point(10, 10), new Point(13, 90));
            Assert.Equal(10, e.X, 3);
            Assert.True(e.Y > 89);
        }

        [Fact]
        public void Near45_SnapsTo45()
        {
            var e = LineSnap.SnapEndpoint(new Point(0, 0), new Point(100, 90));
            Assert.Equal(e.X, e.Y, 3);        // 45 degrees => dx == dy
        }

        [Fact]
        public void LengthPreserved_OnSnap()
        {
            var start = new Point(5, 5);
            var raw = new Point(105, 12);
            var e = LineSnap.SnapEndpoint(start, raw);
            double lenRaw = System.Math.Sqrt(100 * 100 + 7 * 7);
            double lenSnap = System.Math.Sqrt((e.X - 5) * (e.X - 5) + (e.Y - 5) * (e.Y - 5));
            Assert.Equal(lenRaw, lenSnap, 6);
        }

        [Fact]
        public void ZeroLength_ReturnsEndUnchanged()
        {
            var p = new Point(42, 42);
            var e = LineSnap.SnapEndpoint(p, p);
            Assert.Equal(p.X, e.X, 6);
            Assert.Equal(p.Y, e.Y, 6);
        }

        [Fact]
        public void ExactDiagonalDown_PreservedOctant()
        {
            // a drag up-and-left (dx<0, dy<0) snaps to the nearest diagonal, staying in that quadrant
            var e = LineSnap.SnapEndpoint(new Point(0, 0), new Point(-80, -70));
            Assert.True(e.X < 0 && e.Y < 0);
            Assert.Equal(System.Math.Abs(e.X), System.Math.Abs(e.Y), 3);
        }
    }
}