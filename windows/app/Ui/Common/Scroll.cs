using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace SentrySix.Ui
{
    /// <summary>
    /// A page scroller with a thin overlay bar coloured from the theme
    /// ("scroll-thumb"): it takes no room from the page (the page is as wide
    /// as the window, as the web's with overlay scrollbars), and it is shown
    /// only while the page scrolls or the pointer is on it, then fades out,
    /// so at rest it does not sit over the phone-width top bar. A picture
    /// (the parity renders) shows no bar, as Playwright's captures.
    /// </summary>
    public static class Scroll
    {
        const string Template =
            "<ControlTemplate TargetType=\"ScrollViewer\">"
            + "<Grid><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>"
            + "<ScrollContentPresenter Grid.Column=\"0\" Grid.ColumnSpan=\"2\" CanContentScroll=\"{TemplateBinding CanContentScroll}\"/>"
            + "<ScrollBar x:Name=\"PART_VerticalScrollBar\" Grid.Column=\"1\" Width=\"8\" Opacity=\"0\" IsHitTestVisible=\"False\" Orientation=\"Vertical\" Value=\"{TemplateBinding VerticalOffset}\" Maximum=\"{TemplateBinding ScrollableHeight}\" ViewportSize=\"{TemplateBinding ViewportHeight}\" Visibility=\"{TemplateBinding ComputedVerticalScrollBarVisibility}\">"
            + "<ScrollBar.Template><ControlTemplate TargetType=\"ScrollBar\"><Track x:Name=\"PART_Track\" IsDirectionReversed=\"True\">"
            + "<Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType=\"Thumb\"><Border Margin=\"2,2,2,2\" CornerRadius=\"2\" Background=\"{DynamicResource scroll-thumb}\"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>"
            + "</Track></ControlTemplate></ScrollBar.Template></ScrollBar>"
            + "</Grid></ControlTemplate>";

        static ControlTemplate cached;

        public static ScrollViewer Page(UIElement content)
        {
            if (cached == null)
            {
                cached = (ControlTemplate)U.Xaml(Template.Replace("<ControlTemplate TargetType=\"ScrollViewer\">",
                    "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"ScrollViewer\">"));
            }
            ScrollViewer s = new ScrollViewer();
            s.Template = cached;
            s.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            s.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            s.Focusable = false;
            s.Content = content;
            Overlay(s);
            return s;
        }

        /// <summary>No bar shows at all (the render mode's still pictures).</summary>
        public static bool Still;

        static void Overlay(ScrollViewer s)
        {
            DispatcherTimer hide = new DispatcherTimer();
            hide.Interval = TimeSpan.FromMilliseconds(1000);
            ScrollBar bar = null;
            Action<bool> show = on =>
            {
                if (bar == null)
                {
                    bar = s.Template != null ? s.Template.FindName("PART_VerticalScrollBar", s) as ScrollBar : null;
                    if (bar == null) return;
                    // off the bar, or a drag let go: it fades a second later
                    MouseEventHandler later = (o, e) => { hide.Stop(); hide.Start(); };
                    bar.MouseLeave += later;
                    bar.LostMouseCapture += later;
                }
                if (on && !Still)
                {
                    bar.Opacity = 1;
                    bar.IsHitTestVisible = true;
                    hide.Stop();
                    hide.Start();
                }
                else if (!bar.IsMouseOver && !(Mouse.Captured is Thumb)) { bar.Opacity = 0; bar.IsHitTestVisible = false; hide.Stop(); }
            };
            hide.Tick += delegate { show(false); };
            // the page moved (wheel, keys, dragging, a link into view): not a new page's first layout
            s.ScrollChanged += (o, e) => { if (e.VerticalChange != 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0) show(true); };
            // the pointer near the right edge, as an overlay bar shows on hover
            s.PreviewMouseMove += (o, e) =>
            {
                if (s.ScrollableHeight > 0 && e.GetPosition(s).X >= s.ActualWidth - 12) show(true);
            };
            s.Unloaded += delegate { hide.Stop(); };
        }
    }
}
