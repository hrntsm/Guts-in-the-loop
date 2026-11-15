using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SequentialGallery;

internal partial class IntegratedHistoryView : UserControl {
    private sealed record Entry(bool Geometry, HistoryItem Item) { public string Key => (Geometry ? "g:" : "r:") + Item.Id; }
    private Entry[] entries = Array.Empty<Entry>();
    private IReadOnlyDictionary<int, GeometryCandidate?> sources = new Dictionary<int, GeometryCandidate?>();
    private readonly Dictionary<string, Border> cards = new();
    private readonly List<(int GeometryId, int RenderingId, Path Line)> handoffLines = new();
    private int? activeGeometry, activeRendering;
    private string? previewKey;
    private bool geometryCanResume, renderingCanResume, combined;
    private Point? panOrigin;
    private double panHorizontal, panVertical;
    public event Action<bool, int>? ResumeRequested;
    internal int HandoffCount => handoffLines.Count;
    internal int NodeCount => entries.Length;

    public IntegratedHistoryView() { InitializeComponent(); ApplyZoom(); }
    public void SetItems(IEnumerable<HistoryItem>? geometry, int? activeGeometry, bool geometryCanResume,
        IEnumerable<HistoryItem> rendering, int? activeRendering, bool renderingCanResume,
        IReadOnlyDictionary<int, GeometryCandidate?> sources, bool combined) {
        entries = (geometry ?? Enumerable.Empty<HistoryItem>()).Select(i => new Entry(true, i))
            .Concat(rendering.Select(i => new Entry(false, i))).ToArray();
        this.activeGeometry = activeGeometry; this.activeRendering = activeRendering;
        this.geometryCanResume = geometryCanResume; this.renderingCanResume = renderingCanResume;
        this.sources = sources; this.combined = combined;
        if (!entries.Any(e => e.Key == previewKey))
            previewKey = activeRendering is int r ? "r:" + r : activeGeometry is int g ? "g:" + g : entries.FirstOrDefault()?.Key;
        DrawGraph(); UpdatePreview();
    }
    public void Preview(bool geometry, int id) {
        string key = (geometry ? "g:" : "r:") + id;
        if (!entries.Any(e => e.Key == key)) return;
        previewKey = key; UpdatePreview();
    }
    public void ResumeSelected() {
        var entry = entries.FirstOrDefault(e => e.Key == previewKey);
        if (entry != null && (entry.Geometry ? geometryCanResume : renderingCanResume)) ResumeRequested?.Invoke(entry.Geometry, entry.Item.Id);
    }

    private void DrawGraph() {
        GraphCanvas.Children.Clear(); cards.Clear(); handoffLines.Clear();
        var positions = new Dictionary<string, Point>();
        double GeometryWidth = combined ? LaneWidth(true) : 0;
        double rightOffset = combined ? GeometryWidth + 130 : 0;
        double rightWidth = LaneWidth(false);
        PlaceLane(true, 0); PlaceLane(false, rightOffset);
        GraphCanvas.Width = rightOffset + rightWidth + 30;
        GraphCanvas.Height = positions.Count == 0 ? 350 : positions.Values.Max(p => p.Y) + 180;
        if (combined) {
            Header("GEOMETRY", 20, GeometryWidth);
            var separator = new Line { X1 = GeometryWidth + 65, X2 = GeometryWidth + 65, Y1 = 45, Y2 = GraphCanvas.Height, Stroke = Brushes.SlateGray, StrokeDashArray = new DoubleCollection { 3, 4 }, Opacity = .45 };
            GraphCanvas.Children.Add(separator);
        }
        Header("AI RENDERING", rightOffset + 20, rightWidth);
        foreach (var entry in entries) {
            string parentKey = (entry.Geometry ? "g:" : "r:") + entry.Item.ParentId;
            if (entry.Item.ParentId == null || !positions.TryGetValue(parentKey, out var parent)) continue;
            var child = positions[entry.Key];
            DrawLine(new Point(parent.X + 90, parent.Y + 135), new Point(child.X + 90, child.Y), false, Brushes.MediumPurple, entry.Item.EdgeLabel);
        }
        foreach (var entry in entries.Where(e => !e.Geometry && e.Item.ParentId == null)) {
            if (!sources.TryGetValue(entry.Item.Id, out var source) || source == null || !positions.TryGetValue("g:" + source.SourceNodeId, out var parent)) continue;
            var child = positions[entry.Key];
            var line = DrawLine(new Point(parent.X + 180, parent.Y + 68), new Point(child.X, child.Y + 68), true, Brushes.Goldenrod,
                $"Use shape\nR{source.Cell / source.Columns + 1} C{source.Cell % source.Columns + 1}");
            handoffLines.Add((source.SourceNodeId, entry.Item.Id, line));
        }
        foreach (var entry in entries) {
            var stack = new StackPanel();
            bool active = (entry.Geometry ? activeGeometry : activeRendering) == entry.Item.Id;
            stack.Children.Add(new TextBlock { Text = $"{(entry.Geometry ? "G" : "AI")}{entry.Item.Id + 1}  {entry.Item.Title}" + (active ? " · Active" : ""), Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(5), TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(new Image { Source = entry.Item.Image, Width = 168, Height = 92, Stretch = Stretch.Uniform });
            var button = new Button { Content = stack, Background = new SolidColorBrush(Color.FromRgb(34, 43, 54)), BorderThickness = new Thickness(0), Padding = new Thickness(0), Margin = new Thickness(0) };
            button.Click += (_, _) => Preview(entry.Geometry, entry.Item.Id);
            var card = new Border { Child = button, Width = 180, Height = 135, CornerRadius = new CornerRadius(5) };
            Canvas.SetLeft(card, positions[entry.Key].X); Canvas.SetTop(card, positions[entry.Key].Y);
            cards[entry.Key] = card; GraphCanvas.Children.Add(card);
        }

        double LaneWidth(bool geometry) {
            var lane = entries.Where(e => e.Geometry == geometry).ToArray();
            int MaxLeaves(Entry root) {
                var children = lane.Where(e => e.Item.ParentId == root.Item.Id).ToArray();
                return children.Length == 0 ? 1 : children.Sum(MaxLeaves);
            }
            return Math.Max(330, lane.Where(e => e.Item.ParentId == null).Select(e => MaxLeaves(e) * 200 + 30).DefaultIfEmpty(330).Max());
        }
        void PlaceLane(bool geometry, double offset) {
            var lane = entries.Where(e => e.Geometry == geometry).ToArray();
            var children = lane.ToLookup(e => e.Item.ParentId);
            double baseY = 65;
            foreach (var root in lane.Where(e => e.Item.ParentId == null)) {
                if (!geometry && sources.TryGetValue(root.Item.Id, out var source) && source != null && positions.TryGetValue("g:" + source.SourceNodeId, out var sourcePosition)) baseY = Math.Max(baseY, sourcePosition.Y);
                double nextLeaf = 0, bottom = baseY;
                double Place(Entry entry, int depth) {
                    var descendants = children[entry.Item.Id].ToArray();
                    double x;
                    if (descendants.Length == 0) x = 20 + nextLeaf++ * 200;
                    else {
                        var xs = descendants.Select(e => Place(e, depth + 1)).ToArray();
                        x = (xs[0] + xs[^1]) / 2;
                    }
                    double y = baseY + depth * 185; bottom = Math.Max(bottom, y);
                    positions[entry.Key] = new Point(offset + x, y); return x;
                }
                Place(root, 0); baseY = bottom + 190;
            }
        }
    }
    private void Header(string text, double x, double width) {
        var label = new TextBlock { Text = text, Foreground = Brushes.LightSteelBlue, FontSize = 17, FontWeight = FontWeights.SemiBold, Width = width - 20 };
        Canvas.SetLeft(label, x); Canvas.SetTop(label, 18); GraphCanvas.Children.Add(label);
    }
    private Path DrawLine(Point start, Point end, bool horizontal, Brush brush, string label) {
        var figure = new PathFigure { StartPoint = start };
        var first = horizontal ? new Point(start.X + 65, start.Y) : new Point(start.X, start.Y + 25);
        var second = horizontal ? new Point(end.X - 65, end.Y) : new Point(end.X, end.Y - 25);
        figure.Segments.Add(new BezierSegment(first, second, end, true));
        var path = new Path { Data = new PathGeometry(new[] { figure }), Stroke = brush, StrokeThickness = 2 };
        GraphCanvas.Children.Add(path);
        var text = new TextBlock { Text = label, FontSize = 10, Foreground = brush, Background = new SolidColorBrush(Color.FromRgb(16, 21, 28)), MaxWidth = 110, TextWrapping = TextWrapping.Wrap };
        Canvas.SetLeft(text, horizontal ? start.X + 8 : (start.X + end.X) / 2 + 5);
        Canvas.SetTop(text, horizontal ? (start.Y + end.Y) / 2 - 30 : (start.Y + end.Y) / 2 - 8);
        GraphCanvas.Children.Add(text); return path;
    }
    private void UpdatePreview() {
        var entry = entries.FirstOrDefault(e => e.Key == previewKey);
        GeometryCandidate? source = null, activeSource = null;
        if (entry != null && !entry.Geometry) sources.TryGetValue(entry.Item.Id, out source);
        if (activeRendering is int r) sources.TryGetValue(r, out activeSource);
        foreach (var pair in cards) {
            bool active = pair.Key == "g:" + activeGeometry || pair.Key == "r:" + activeRendering;
            bool origin = activeSource != null && pair.Key == "g:" + activeSource.SourceNodeId;
            bool previewOrigin = source != null && pair.Key == "g:" + source.SourceNodeId;
            pair.Value.BorderBrush = pair.Key == previewKey || previewOrigin && !origin ? Brushes.MediumPurple : origin ? Brushes.Goldenrod : active ? Brushes.MediumSeaGreen : Brushes.SlateGray;
            pair.Value.BorderThickness = new Thickness(pair.Key == previewKey || active || origin ? 3 : 1);
        }
        foreach (var link in handoffLines) {
            sources.TryGetValue(link.RenderingId, out var linkedSource);
            bool current = activeSource != null && linkedSource?.Key == activeSource.Key;
            bool preview = source != null && linkedSource?.Key == source.Key;
            link.Line.Stroke = preview && !current ? Brushes.MediumPurple : Brushes.Goldenrod;
            link.Line.StrokeThickness = current ? 4 : preview ? 3 : 2; link.Line.Opacity = current || preview ? 1 : .35;
        }
        PreviewTitle.Text = entry == null ? "Select a history node" : $"{(entry.Geometry ? "Geometry" : "AI")} #{entry.Item.Id + 1} · {entry.Item.Title}"
            + ((entry.Geometry ? activeGeometry : activeRendering) == entry.Item.Id ? " · Active" : " · Preview");
        PreviewImage.Source = entry?.Item.Image;
        PreviewDetails.Text = entry?.Item.Details ?? "Completed candidate grids will appear here.";
        if (entry != null && !entry.Geometry && source != null)
            PreviewDetails.Text = $"Source: geometry #{source.SourceNodeId + 1}, R{source.Cell / source.Columns + 1} C{source.Cell % source.Columns + 1}\n\n" + PreviewDetails.Text;
        ResumeButton.IsEnabled = entry != null && (entry.Geometry ? geometryCanResume : renderingCanResume);
    }
    private void ResumeClick(object sender, RoutedEventArgs e) => ResumeSelected();
    private void ZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (GraphCanvas != null) ApplyZoom(); }
    private void ApplyZoom() { GraphCanvas.LayoutTransform = new ScaleTransform(ZoomSlider.Value, ZoomSlider.Value); ZoomLabel.Text = $"{ZoomSlider.Value:P0}"; }
    private void GraphWheel(object sender, MouseWheelEventArgs e) { ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + (e.Delta > 0 ? .1 : -.1), ZoomSlider.Minimum, ZoomSlider.Maximum); e.Handled = true; }
    private void PanStart(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton != MouseButton.Middle && !(e.ChangedButton == MouseButton.Left && e.OriginalSource == GraphCanvas)) return;
        panOrigin = e.GetPosition(GraphScroll); panHorizontal = GraphScroll.HorizontalOffset; panVertical = GraphScroll.VerticalOffset; GraphScroll.CaptureMouse(); e.Handled = true;
    }
    private void PanMove(object sender, MouseEventArgs e) {
        if (panOrigin is not Point origin) return;
        var point = e.GetPosition(GraphScroll); GraphScroll.ScrollToHorizontalOffset(panHorizontal + origin.X - point.X); GraphScroll.ScrollToVerticalOffset(panVertical + origin.Y - point.Y);
    }
    private void PanEnd(object sender, MouseButtonEventArgs e) { if (panOrigin == null) return; panOrigin = null; GraphScroll.ReleaseMouseCapture(); e.Handled = true; }
}
