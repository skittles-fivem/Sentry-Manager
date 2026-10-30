using System.Windows;
using System.Windows.Controls;

namespace SentrySix.Ui
{
    /// <summary>
    /// A page scroller with a thin overlay-style bar coloured from the theme
    /// ("scroll-thumb"), close to Chrome's dark-scheme scrollbar instead of
    /// WPF's light default.
    /// </summary>
    public static class Scroll
    {
        const string Template =
            "<ControlTemplate TargetType=\"ScrollViewer\">"
            + "<Grid><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>"
            + "<ScrollContentPresenter Grid.Column=\"0\" Grid.ColumnSpan=\"2\" CanContentScroll=\"{TemplateBinding CanContentScroll}\"/>"
            + "<ScrollBar x:Name=\"PART_VerticalScrollBar\" Grid.Column=\"1\" Width=\"10\" Orientation=\"Vertical\" Value=\"{TemplateBinding VerticalOffset}\" Maximum=\"{TemplateBinding ScrollableHeight}\" ViewportSize=\"{TemplateBinding ViewportHeight}\" Visibility=\"{TemplateBinding ComputedVerticalScrollBarVisibility}\">"
            + "<ScrollBar.Template><ControlTemplate TargetType=\"ScrollBar\"><Track x:Name=\"PART_Track\" IsDirectionReversed=\"True\">"
            + "<Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType=\"Thumb\"><Border Margin=\"2,2,2,2\" CornerRadius=\"3\" Background=\"{DynamicResource scroll-thumb}\"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>"
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
            return s;
        }
    }
}
