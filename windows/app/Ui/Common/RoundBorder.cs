using System;
using System.Windows;
using System.Windows.Controls;

namespace SentrySix.Ui
{
    /// <summary>
    /// A Border whose corners are rounded as a browser rounds them: a radius
    /// larger than half the box (CSS's 999px "pill") stops at half its
    /// shorter side, so the ends are half circles. WPF's own Border instead
    /// scales such radii to the box, which draws an oval.
    /// </summary>
    public class RoundBorder : Border
    {
        CornerRadius asked;
        bool fitting;

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == CornerRadiusProperty && !fitting) asked = (CornerRadius)e.NewValue;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double most = Math.Min(finalSize.Width, finalSize.Height) / 2;
            CornerRadius fit = new CornerRadius(
                Math.Min(asked.TopLeft, most), Math.Min(asked.TopRight, most),
                Math.Min(asked.BottomRight, most), Math.Min(asked.BottomLeft, most));
            if (fit != CornerRadius)
            {
                fitting = true;
                CornerRadius = fit;
                fitting = false;
            }
            return base.ArrangeOverride(finalSize);
        }
    }
}
