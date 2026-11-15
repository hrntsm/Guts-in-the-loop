using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace SequentialGallery;

internal partial class IntegratedWindow : Window {
    internal GeometryPane? Geometry { get; }
    internal RenderingPane Rendering { get; }
    private bool closing, closed, refreshing, handlingClose, closeScheduled;

    public IntegratedWindow(Func<IGeometryHost>? createGeometryHost, TimeSpan? lastSolution,
        Func<byte[]> captureReference, Func<RenderingApi>? createApi = null) {
        InitializeComponent();
        if (createGeometryHost != null) {
            Geometry = new GeometryPane(createGeometryHost, lastSolution);
            GeometrySlot.Content = Geometry;
            Geometry.StateChanged += Refresh;
            Geometry.HandoffRequested += Handoff;
            Geometry.Finished += RequestClose;
        } else {
            GeometrySlot.Visibility = PaneSplitter.Visibility = Visibility.Collapsed;
            GeometryColumn.Width = PaneGutter.Width = new GridLength(0);
            Width = 1150;
        }
        Rendering = new RenderingPane(captureReference, createApi, integratedMode: Geometry != null);
        RenderingSlot.Content = Rendering;
        Rendering.StateChanged += Refresh;
        Rendering.CloseRequested += RequestClose;
        Rendering.Finished += RequestClose;
        if (Geometry != null) Rendering.ApplyGeometryForFinish = Geometry.ApplyCandidateAsync;
        History.ResumeRequested += ResumeHistory;
        Closing += ClosingWindow;
        Closed += (_, _) => { closed = true; Geometry?.Shutdown(); Rendering.Shutdown(); };
        ModeLabel.Text = Geometry == null ? "AI rendering only · fixed viewport reference"
            : "Geometry on the left · AI rendering on the right · select a shape and choose Use this shape";
        var workArea = SystemParameters.WorkArea;
        double availableWidth = Math.Max(1, workArea.Width - 24), availableHeight = Math.Max(1, workArea.Height - 24);
        MinWidth = Math.Min(MinWidth, availableWidth); MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Clamp(Width, MinWidth, availableWidth); Height = Math.Clamp(Height, MinHeight, availableHeight);
        Refresh();
    }

    private async void Handoff(GeometryCandidate source) {
        if (closing || Rendering.IsBusy || Geometry?.IsBusy == true) return;
        try { await Rendering.UseShapeAsync(source); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Could not use shape", MessageBoxButton.OK, MessageBoxImage.Error); }
        Refresh();
    }

    private void ModeChanged(object sender, SelectionChangedEventArgs e) {
        if (e.Source == ModeTabs && Rendering != null) Refresh();
    }
    private void Refresh() {
        if (refreshing || closed || Rendering == null) return;
        refreshing = true;
        try {
            Geometry?.SetPeerBusy(Rendering.IsBusy);
            Rendering.SetPeerBusy(Geometry?.IsBusy == true);
            History.SetItems(Geometry?.HistoryItems, Geometry?.ActiveHistoryNodeId,
                Geometry != null && Geometry.CanResumeHistory && !closing,
                Rendering.HistoryItems, Rendering.ActiveHistoryNodeId,
                Rendering.CanResume && !closing, Rendering.HistorySources, Geometry != null);
            if (closing && !handlingClose && !closeScheduled && Geometry?.IsBusy != true && !Rendering.IsBusy) {
                closeScheduled = true;
                Dispatcher.BeginInvoke(new Action(() => { closeScheduled = false; if (!closed) Close(); }));
            }
        } finally { refreshing = false; }
    }
    private void ResumeHistory(bool geometry, int id) {
        if (closing) return;
        if (geometry) {
            if (Geometry == null || !Geometry.CanResumeHistory) return;
            Geometry.ResumeHistory(id);
        } else {
            if (Rendering.IsBusy) return;
            Rendering.ResumeHistory(id);
        }
        ModeTabs.SelectedIndex = 0;
        Refresh();
    }
    private void ClosingWindow(object? sender, CancelEventArgs e) {
        handlingClose = true;
        try {
            closing = true;
            ModeLabel.Text = "Closing after active work stops. Late image responses will be ignored.";
            Geometry?.Cancel(); Rendering.Cancel();
            if (Geometry?.IsBusy == true || Rendering.IsBusy) e.Cancel = true;
        } finally { handlingClose = false; }
        if (e.Cancel) Refresh();
    }
    private void CloseClick(object sender, RoutedEventArgs e) => RequestClose();
    internal void RequestClose() { if (!closed) Close(); }
}
