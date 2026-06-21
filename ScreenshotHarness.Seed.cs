#if DEBUG
using System.Collections.Generic;
using System.Windows;

namespace Scalpel
{
    public partial class MainWindow
    {
        partial void SeedShotAnnotations(AppMode mode, int page)
        {
            // Canvas-space coordinates (top-left origin) over the displayed page.
            if (mode == AppMode.Edit)
            {
                AddAnnotation(new HighlightAnnotation
                {
                    PageIndex = page,
                    Bounds = new Rect(150, 150, 360, 22),
                });
                AddAnnotation(new TextAnnotation
                {
                    PageIndex = page,
                    Position = new Point(160, 250),
                    Content = "Review this section",
                    FontSize = 16,
                });
                AddAnnotation(new InkAnnotation
                {
                    PageIndex = page,
                    StrokeWidth = 3,
                    Points =
                    [
                        new(160, 310), new(200, 290), new(240, 318), new(280, 290), new(320, 314),
                    ],
                });
            }
            else if (mode == AppMode.Sign)
            {
                AddAnnotation(new SignatureAnnotation
                {
                    PageIndex = page,
                    Position = new Point(330, 120),
                    Scale = 1.3,
                    Strokes =
                    [
                        // Flowing cursive-style signature stroke.
                        [
                            new(10, 70), new(35, 18), new(60, 72), new(95, 22), new(130, 66),
                            new(165, 26), new(205, 70), new(245, 28), new(285, 62), new(325, 40),
                        ],
                        // Underline flourish.
                        [ new(5, 98), new(335, 92) ],
                    ],
                });
            }

            // Redraw the page so the freshly-added overlays appear before capture.
            RenderPage(page);
        }
    }
}
#endif
