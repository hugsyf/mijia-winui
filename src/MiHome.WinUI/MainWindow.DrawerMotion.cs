using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace MiHome.WinUI;

public sealed partial class MainWindow
{
    private Storyboard? drawerStoryboard;
    private TaskCompletionSource<bool>? drawerMotionCompletion;
    private int drawerMotionGeneration;

    private void OnDrawerScrimTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
    { args.Handled = true; Back(); }

    private void CancelDrawerMotion()
    {
        drawerStoryboard?.Stop(); drawerStoryboard = null;
        drawerMotionCompletion?.TrySetResult(false); drawerMotionCompletion = null;
    }
    private void ShowDrawerAnimated()
    {
        drawerMotionGeneration++; CancelDrawerMotion();
        DrawerPane.IsHitTestVisible = DrawerScrim.IsHitTestVisible = true;
        DetailView.ChangeView(null, 0, null, true);
        _ = AnimateDrawerAsync(Math.Max(0, DrawerPane.Width), 0, 0, 1, 0, 1);
    }
    private void ResizeDrawerAnimated(double previousWidth)
    {
        drawerMotionGeneration++; CancelDrawerMotion();
        _ = AnimateDrawerAsync(0, 0, 1, 1, 1, 1, previousWidth, DrawerPane.Width);
    }
    private async void HideDrawerAnimated()
    {
        var generation = ++drawerMotionGeneration;
        var offset = DrawerTranslation.X;
        var width = DrawerPane.ActualWidth;
        CancelDrawerMotion();
        if(width>0)DrawerPane.Width=width;
        DrawerPane.IsHitTestVisible = DrawerScrim.IsHitTestVisible = false;
        if (DrawerPane.Visibility == Visibility.Visible)
            await AnimateDrawerAsync(offset, Math.Max(0, DrawerPane.Width), 1, 0, 1, 0);
        // A newly opened device owns these shared controls. An old completion
        // must never collapse or clear the new panel.
        if (generation != drawerMotionGeneration || detailOpen) return;
        DrawerPane.Visibility = DrawerScrim.Visibility = Visibility.Collapsed;
        DrawerColumn.Width = new GridLength(0);
        DeviceListHost.Visibility = Visibility.Visible;
        CardAppearance.Visibility = Visibility.Collapsed;
        genericPanel = null; specializedPanel = null;
        GenericContent.Content = PrimaryControls.Content = SpecializedContent.Content = null;
        DetailView.Visibility = Visibility.Collapsed;
    }
    private Task AnimateDrawerAsync(double fromX, double toX, double fromOpacity, double toOpacity,
        double fromScrim, double toScrim, double? fromWidth = null, double? toWidth = null)
    {
        DrawerTranslation.X = toX; DrawerPane.Opacity = toOpacity; DrawerScrim.Opacity = toScrim;
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) return Task.CompletedTask;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var story = new Storyboard();
        void Add(DependencyObject target, string property, double from, double to, bool dependent = false)
        {
            var animation = new DoubleAnimation
            {
                From = from, To = to, Duration = TimeSpan.FromMilliseconds(toOpacity == 0 ? 160 : 220),
                EnableDependentAnimation = dependent, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property); story.Children.Add(animation);
        }
        Add(DrawerTranslation, "X", fromX, toX);
        Add(DrawerPane, "Opacity", fromOpacity, toOpacity);
        Add(DrawerScrim, "Opacity", fromScrim, toScrim);
        if (fromWidth is > 0 && toWidth is > 0) Add(DrawerPane, "Width", fromWidth.Value, toWidth.Value, true);
        story.Completed += (_, _) =>
        {
            if (ReferenceEquals(drawerStoryboard, story)) { drawerStoryboard = null; drawerMotionCompletion = null; }
            story.Stop(); completion.TrySetResult(true);
        };
        drawerStoryboard = story; drawerMotionCompletion = completion;
        story.Begin(); return completion.Task;
    }
    private void OnDrawerPaneSizeChanged(object sender, SizeChangedEventArgs args) =>
        DrawerPane.Clip = new RectangleGeometry { Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height) };
}
