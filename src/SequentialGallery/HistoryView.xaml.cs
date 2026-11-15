using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SequentialGallery;

internal sealed record HistoryItem(int Id, int? ParentId, string Title, BitmapSource Image, string EdgeLabel, string Details);

internal partial class HistoryView : UserControl {
    private HistoryItem[] items = Array.Empty<HistoryItem>();
    private readonly Dictionary<int, Border> cards = new();
    private int? activeId, previewId;
    private bool canResume;
    private Point? panOrigin;
    private double panHorizontal, panVertical;
    public event Action<int>? ResumeRequested;
    public int? PreviewId => previewId;

    public HistoryView() { InitializeComponent(); ApplyZoom(); }

    public void SetItems(IEnumerable<HistoryItem> values, int? activeId, bool canResume) {
        items = values.ToArray();
        this.activeId = activeId;
        this.canResume = canResume;
        if (!items.Any(i => i.Id == previewId)) previewId = activeId ?? items.FirstOrDefault()?.Id;
        DrawGraph();
        UpdatePreview();
    }

    public void Preview(int id) {
        if (!items.Any(i => i.Id == id)) return;
        previewId = id;
        UpdatePreview();
    }

    public void ResumeSelected() {
        if (canResume && previewId is int id) ResumeRequested?.Invoke(id);
    }

    private void DrawGraph() {
        GraphCanvas.Children.Clear(); cards.Clear();
        var positions = new Dictionary<int, Point>();
        var children = items.ToLookup(i => i.ParentId);
        double nextRow = 0;
        double Place(HistoryItem item, int depth) {
            var descendants = children[item.Id].ToArray();
            double row;
            if (descendants.Length == 0) row = nextRow++;
            else {
                var rows = descendants.Select(child => Place(child, depth + 1)).ToArray();
                row = (rows[0] + rows[^1]) / 2;
            }
            positions[item.Id] = new Point(24 + depth * 300, 40 + row * 210);
            return row;
        }
        foreach (var root in items.Where(i => i.ParentId == null || !items.Any(p => p.Id == i.ParentId))) Place(root, 0);
        GraphCanvas.Width = positions.Count == 0 ? 400 : positions.Values.Max(p => p.X) + 250;
        GraphCanvas.Height = positions.Count == 0 ? 260 : positions.Values.Max(p => p.Y) + 200;
        foreach (var item in items.Where(i => i.ParentId is int && positions.ContainsKey(i.ParentId.Value))) {
            var parent = positions[item.ParentId!.Value];
            var child = positions[item.Id];
            var start = new Point(parent.X + 210, parent.Y + 80);
            var end = new Point(child.X, child.Y + 80);
            var figure = new PathFigure { StartPoint = start };
            figure.Segments.Add(new BezierSegment(new Point(start.X + 45, start.Y), new Point(end.X - 45, end.Y), end, true));
            GraphCanvas.Children.Add(new Path { Data = new PathGeometry(new[] { figure }), Stroke = Brushes.MediumPurple, StrokeThickness = 2 });
            var label = new TextBlock { Text = item.EdgeLabel, Foreground = Brushes.LightSteelBlue, FontSize = 10, Background = new SolidColorBrush(Color.FromRgb(16, 21, 28)), MaxWidth = 90, TextWrapping = TextWrapping.Wrap };
            Canvas.SetLeft(label, start.X + 5); Canvas.SetTop(label, (start.Y + end.Y) / 2 - 25); GraphCanvas.Children.Add(label);
        }
        foreach (var item in items) {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = $"#{item.Id + 1}  {item.Title}", Foreground = Brushes.White, Margin = new Thickness(6), FontWeight = FontWeights.SemiBold });
            content.Children.Add(new Image { Source = item.Image, Width = 190, Height = 120, Stretch = Stretch.Uniform, Margin = new Thickness(4) });
            var button = new Button { Content = content, Background = new SolidColorBrush(Color.FromRgb(34, 43, 54)), Padding = new Thickness(0), BorderThickness = new Thickness(0), Margin = new Thickness(0) };
            button.Click += (_, _) => Preview(item.Id);
            var card = new Border { Child = button, Width = 210, Height = 170, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(5) };
            Canvas.SetLeft(card, positions[item.Id].X); Canvas.SetTop(card, positions[item.Id].Y);
            cards[item.Id] = card; GraphCanvas.Children.Add(card);
        }
    }

    private void UpdatePreview() {
        foreach (var pair in cards) {
            pair.Value.BorderBrush = pair.Key == previewId ? Brushes.MediumPurple : pair.Key == activeId ? Brushes.MediumSeaGreen : Brushes.SlateGray;
            pair.Value.BorderThickness = new Thickness(pair.Key == activeId || pair.Key == previewId ? 3 : 1);
        }
        var item = items.FirstOrDefault(i => i.Id == previewId);
        PreviewTitle.Text = item == null ? "Select a history node" : $"#{item.Id + 1}  {item.Title}" + (item.Id == activeId ? " · Active" : " · Preview");
        PreviewImage.Source = item?.Image;
        PreviewDetails.Text = item?.Details ?? "Completed candidate grids will appear here.";
        ResumeButton.IsEnabled = canResume && item != null;
    }
    private void ResumeClick(object sender, RoutedEventArgs e) => ResumeSelected();
    private void ZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (GraphCanvas != null) ApplyZoom(); }
    private void ApplyZoom() {
        GraphCanvas.LayoutTransform = new ScaleTransform(ZoomSlider.Value, ZoomSlider.Value);
        ZoomLabel.Text = $"{ZoomSlider.Value:P0}";
    }
    private void GraphWheel(object sender, MouseWheelEventArgs e) {
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + (e.Delta > 0 ? .1 : -.1), ZoomSlider.Minimum, ZoomSlider.Maximum);
        e.Handled = true;
    }
    private void PanStart(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton != MouseButton.Middle && !(e.ChangedButton == MouseButton.Left && e.OriginalSource == GraphCanvas)) return;
        panOrigin = e.GetPosition(GraphScroll); panHorizontal = GraphScroll.HorizontalOffset; panVertical = GraphScroll.VerticalOffset;
        GraphScroll.CaptureMouse(); e.Handled = true;
    }
    private void PanMove(object sender, MouseEventArgs e) {
        if (panOrigin is not Point origin) return;
        var point = e.GetPosition(GraphScroll);
        GraphScroll.ScrollToHorizontalOffset(panHorizontal + origin.X - point.X);
        GraphScroll.ScrollToVerticalOffset(panVertical + origin.Y - point.Y);
    }
    private void PanEnd(object sender, MouseButtonEventArgs e) {
        if (panOrigin == null) return;
        panOrigin = null; GraphScroll.ReleaseMouseCapture(); e.Handled = true;
    }
}
