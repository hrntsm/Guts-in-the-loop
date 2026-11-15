using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace SequentialGallery;

// Scale the complete scene, including hit targets, before scrolling its rendered extent.
[ContentProperty(nameof(Scene))]
internal sealed class ImageViewport : UserControl {
    private readonly Viewbox view = new() { Stretch = Stretch.Fill };
    internal readonly ScrollViewer Scroll = new() {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        CanContentScroll = false, Background = Brushes.Transparent
    };
    private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
    private double sceneWidth = 1024, sceneHeight = 1024, scale = 1;
    private Point? panOrigin;
    private double panHorizontal, panVertical;
    private Cursor? previousCursor;
    internal bool IsFit { get; private set; } = true;
    internal double Zoom => scale;
    public FrameworkElement? Scene { get => view.Child as FrameworkElement; set { EndPan(); view.Child = value; ResizeScene(); } }

    public ImageViewport() {
        var root = new DockPanel();
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 5), ToolTip = "Mouse wheel over image: zoom. Right drag: pan." };
        tools.Children.Add(new TextBlock { Text = "Image zoom", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        Add("Fit", FitToWindow); Add("100%", () => SetZoom(1)); Add("−", () => SetZoom(scale / 1.25)); Add("+", () => SetZoom(scale * 1.25));
        tools.Children.Add(label); DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        Scroll.Content = view; root.Children.Add(Scroll); Content = root;
        Scroll.SizeChanged += (_, _) => ResizeScene();
        Scroll.PreviewMouseWheel += (_, e) => {
            if (!IsImageSource(e.OriginalSource)) return;
            ZoomAt(scale * Math.Pow(1.25, e.Delta / 120.0), e.GetPosition(Scroll)); e.Handled = true;
        };
        Scroll.PreviewMouseDown += (_, e) => {
            if (e.ChangedButton == MouseButton.Right && IsImageSource(e.OriginalSource))
                e.Handled = BeginPan(e.GetPosition(Scroll));
        };
        Scroll.PreviewMouseMove += (_, e) => {
            if (panOrigin == null) return;
            if (e.RightButton != MouseButtonState.Pressed) { EndPan(); return; }
            PanTo(e.GetPosition(Scroll)); e.Handled = true;
        };
        Scroll.PreviewMouseUp += (_, e) => {
            if (e.ChangedButton != MouseButton.Right || panOrigin == null) return;
            EndPan(); e.Handled = true;
        };
        Scroll.LostMouseCapture += (_, _) => EndPan();
        Unloaded += (_, _) => EndPan();
        IsVisibleChanged += (_, _) => { if (!IsVisible) EndPan(); };
        Loaded += (_, _) => ResizeScene();
        void Add(string title, Action action) {
            var button = new Button { Content = title, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(1, 0, 1, 0),
                Background = new SolidColorBrush(Color.FromRgb(40, 51, 65)), Foreground = Brushes.White };
            button.Click += (_, _) => action(); tools.Children.Add(button);
        }
    }
    internal void SetSceneSize(double width, double height, bool reset = true) {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return;
        EndPan();
        sceneWidth = width; sceneHeight = height;
        if (reset) FitToWindow(); else ResizeScene();
    }
    internal void FitToWindow() {
        EndPan(); IsFit = true; ResizeScene(); Scroll.ScrollToHorizontalOffset(0); Scroll.ScrollToVerticalOffset(0);
    }
    internal void SetZoom(double value) {
        EndPan(); IsFit = false; scale = Math.Clamp(value, Math.Min(.01, scale), Math.Max(8, scale)); ResizeScene();
    }
    internal void ZoomAt(double value, Point position) {
        if (Scene == null) return;
        Scroll.UpdateLayout();
        var anchor = Scroll.TranslatePoint(position, Scene);
        SetZoom(value); Scroll.UpdateLayout();
        var moved = Scene.TranslatePoint(anchor, Scroll);
        Scroll.ScrollToHorizontalOffset(Scroll.HorizontalOffset + moved.X - position.X);
        Scroll.ScrollToVerticalOffset(Scroll.VerticalOffset + moved.Y - position.Y);
    }
    internal bool BeginPan(Point position) {
        EndPan();
        if (!Scroll.CaptureMouse()) return false;
        BeginPanCore(position); return true;
    }
    internal void BeginPanCore(Point position) {
        panOrigin = position; panHorizontal = Scroll.HorizontalOffset; panVertical = Scroll.VerticalOffset;
        previousCursor = Scroll.Cursor; Scroll.Cursor = Cursors.SizeAll;
    }
    internal void PanTo(Point position) {
        if (panOrigin is not Point origin) return;
        Scroll.ScrollToHorizontalOffset(panHorizontal + origin.X - position.X);
        Scroll.ScrollToVerticalOffset(panVertical + origin.Y - position.Y);
    }
    internal void EndPan() {
        if (panOrigin == null) return;
        panOrigin = null; Scroll.Cursor = previousCursor;
        if (Mouse.Captured == Scroll) Scroll.ReleaseMouseCapture();
    }
    private bool IsImageSource(object source) {
        for (var node = source as DependencyObject; node is Visual; node = VisualTreeHelper.GetParent(node)) {
            if (ReferenceEquals(node, Scene)) return true;
            if (ReferenceEquals(node, Scroll)) break;
        }
        return false;
    }
    private void ResizeScene() {
        if (Scene == null) return;
        Scroll.HorizontalScrollBarVisibility = Scroll.VerticalScrollBarVisibility = IsFit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Scene.Width = sceneWidth; Scene.Height = sceneHeight;
        if (IsFit && Scroll.ActualWidth > 1 && Scroll.ActualHeight > 1)
            scale = Math.Min((Scroll.ActualWidth - 1) / sceneWidth, (Scroll.ActualHeight - 1) / sceneHeight);
        view.Width = sceneWidth * scale; view.Height = sceneHeight * scale;
        label.Text = (IsFit ? "Fit · " : "") + scale.ToString("P0");
    }
}
