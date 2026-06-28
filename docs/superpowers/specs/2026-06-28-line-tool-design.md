# Design Spec — Line Tool (Tier 1, feature 1 of the KillerPDF port)

**Date:** 2026-06-28
**Status:** Approved (design), proceeding to plan
**Program:** see `killerpdf-feature-port-program` memory; foundation refactor (sub-project R) already done.

## Goal

Add a straight-**Line** annotation tool to the Edit mode, in Scalpel's Clinical design language. Drag from A to B to draw a straight line; hold **Shift** to snap the angle to 0/45/90°. Reuses the existing color / width / opacity bar.

## Key decisions (user-confirmed)

- **Plain straight line** — no arrowheads, no dashes (YAGNI; those would be a larger feature).
- **Shift-to-snap** the endpoint angle to the nearest 0/45/90°.
- **In-XAML vector icon** — the ribbon button's icon is a WPF `Line` shape, not a Tabler font glyph (no font re-subsetting; the Tabler subset has no line glyph and we lack the full TTF source).
- **Shares Draw's style state** — the Line tool uses the same `_drawColor` / `_drawWidth` / `_drawOpacity` and the same settings bar as the freehand Draw tool ("same as Draw"). Not a separate palette.

## Approach: reuse `InkAnnotation`, add no new model type

A line is an `InkAnnotation` (`Models/EditingTypes.cs`) with **exactly 2 points**. This is the central simplification — these existing paths work **unchanged**:
- **Burn on save** (`MainWindow.SaveAnnotations.cs`, `case InkAnnotation`): the loop `DrawLine`s between consecutive points → for 2 points, one straight segment. No change.
- **Live render** (`MainWindow.AnnotationManagement.cs`, `case InkAnnotation`): renders a `Polyline` from the points → a straight segment for 2 points. No change.
- **Select/move/delete** (`MainWindow.Selection.cs`): `InkAnnotation` is already selectable/movable. No change.

Only two things are line-specific: the **enum value** `EditTool.Line` and the **drawing interaction** (2-point rubber-band vs. freehand point-collection).

## Components

### 1. `EditTool.Line` (`Models/EditingTypes.cs`)
Add `Line` to `enum EditTool { Select, Text, Highlight, Draw, Signature, Image, Crop, Line }` (append to preserve any ordinal assumptions).

### 2. Pure snap helper (`Services/LineSnap.cs`) — the only unit-tested logic
```csharp
namespace Scalpel.Services;
public static class LineSnap
{
    /// <summary>Snaps the line end so the segment start->end lies on the nearest
    /// 45-degree ray (0/45/90/135/180/225/270/315), preserving the drag length.
    /// Returns end unchanged when start==end.</summary>
    public static System.Windows.Point SnapEndpoint(System.Windows.Point start, System.Windows.Point end);
}
```
Algorithm: `dx=end.X-start.X, dy=end.Y-start.Y; len=hypot(dx,dy)`. If `len==0` return `end`. `ang=atan2(dy,dx); snapped=round(ang / (PI/4)) * (PI/4); return new Point(start.X + cos(snapped)*len, start.Y + sin(snapped)*len)`.

### 3. Drawing interaction (`MainWindow.CanvasInteraction.cs`) — new `case EditTool.Line` in 3 handlers
- **MouseLeftButtonDown:** `_activeInk = new InkAnnotation { PageIndex = pageIdx, StrokeWidth = _drawWidth };` `_activeInk.SetColor(<draw color with _drawOpacity>);` add start point; add a duplicate second point (so the preview `Polyline` has 2 points to update); create the `Polyline` preview exactly as the Draw case does; `CaptureMouse()`.
- **MouseMove (`case EditTool.Line` when preview/ink active):** compute `end = pos`; if `Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)` then `end = LineSnap.SnapEndpoint(_activeInk.Points[0], pos)`. Set `_activeInk.Points[1] = end` and the preview poly's second point to `end` (replace, do not append).
- **MouseLeftButtonUp (`case EditTool.Line`):** if distance(Points[0],Points[1]) > 3px, `AddAnnotation(_activeInk)` (commits + undo); else discard the preview. Release mouse. (Draw's commit guard `Points.Count > 2` would wrongly drop a 2-point line — Line has its own guard, so Draw is untouched.)

### 4. Tool selection & bars
- `MainWindow.ToolSelection.cs`: add `(ToolLineBtn, EditTool.Line)` to the SetTool button map; `CursorForTool`: `EditTool.Line => Cursors.Cross`; the settings-bar guard that currently shows the Draw bar for `Draw || Highlight` must also include `Line`.
- `MainWindow.DrawBar.cs`: every branch currently keyed `tool == EditTool.Draw` (color init/click, **size slider** show, opacity init/change) becomes `tool == EditTool.Draw || tool == EditTool.Line`, so Line gets color + width + opacity (Line shares Draw's `_drawColor`/`_drawWidth`/`_drawOpacity`).

### 5. Ribbon button (`MainWindow.xaml`, Edit-mode Tools group, immediately after `ToolDrawBtn`)
```xml
<Button x:Name="ToolLineBtn" Style="{StaticResource RibbonButton}" Click="ToolLine_Click" ToolTip="{DynamicResource Str_TT_LineTool}">
  <StackPanel>
    <Line X1="3" Y1="20" X2="20" Y2="3" StrokeThickness="2.2" Width="23" Height="23"
          Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}"
          StrokeStartLineCap="Round" StrokeEndLineCap="Round" HorizontalAlignment="Center"/>
    <TextBlock Text="{DynamicResource Str_Lbl_Line}" Margin="0,5,0,0" HorizontalAlignment="Center"/>
  </StackPanel>
</Button>
```
The `Stroke` binds to the ancestor Button's `Foreground` so it recolors to the accent when the tool is active (SetTool sets `Button.Foreground` via `SetResourceReference(..., "Accent")`), matching the glyph buttons whose label uses `TextElement.Foreground`.

### 6. `ToolLine_Click` handler
Add next to `ToolDraw_Click` (currently in `MainWindow.Outline.cs`): `private void ToolLine_Click(object s, RoutedEventArgs e) => SetTool(EditTool.Line);`

### 7. Context menu (`MainWindow.ContextMenu.cs`)
Add a "Line" item next to the existing Draw item, calling `SetTool(EditTool.Line)`, gated to the same mode/condition as the Draw item.

### 8. Localization (`Strings/*.xaml` ×9)
Add to every locale file:
- `Str_Lbl_Line` (button label, e.g. "Line")
- `Str_TT_LineTool` (tooltip, e.g. "Line Tool - drag to draw a straight line; Shift to snap angle")
English = real text; es/zh-TW/zh-CN/bn/tr-TR/he/ar/ru translated; brand tokens stay Latin. Every key must exist in every locale or the `DynamicResource` blanks out there.

### 9. Changelog (`Services/Changelog.cs`)
Prepend/extend the newest `Release` with a short user-facing bullet: a straight-line tool with Shift-to-snap.

## Testing

- **Unit (xUnit, `Scalpel.Tests`):** new `LineSnapTests` covering `SnapEndpoint`: pure-horizontal drag stays horizontal; pure-vertical stays vertical; exact-45° preserved; a drag a few degrees off horizontal rounds to horizontal; a drag near 45° rounds to 45°; length is preserved on snap; `start==end` returns `end`. Wire the new `Services/LineSnap.cs` into `Scalpel.Tests.csproj` via a `<Compile Include="..\Services\LineSnap.cs">` link (matching how other Services files are linked).
- **Build gate:** `~/.dotnet/dotnet.exe build` clean; full suite stays green (181 + new LineSnap tests).
- **Manual smoke (owed to user — GUI can't run headless):** select Line in Edit mode → drag a line (color/width/opacity from the bar apply) → Shift-drag snaps to H/V/45° → Save → reopen → line persists → Select tool can move it.

## Out of scope (YAGNI)
- No keyboard shortcut (tool-letter keys belong to the separate "missing keyboard shortcuts" Tier-1 feature).
- No arrowheads, dashes, or separate Line palette.
- No new annotation class.

## Definition of done
Line tool selectable in Edit mode and via context menu; draws a straight line with the Draw bar's color/width/opacity; Shift snaps to 0/45/90°; burns into the PDF and persists across save/reopen; `LineSnapTests` green; build clean; changelog + all 9 locales updated.
