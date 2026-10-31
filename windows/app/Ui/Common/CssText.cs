using System.Windows;
using System.Windows.Controls;

namespace SentrySix.Ui
{
    /// <summary>
    /// A TextBlock with CSS's unitless line-height: each line is FontSize x
    /// <see cref="LineFactorProperty"/> tall (body { font: 14px/1.5 } gives
    /// 21 px lines at 14 px, 18.75 at 12.5), set on an interface's root and
    /// inherited by every text in it, as the browser's line-height is. With no
    /// factor (NaN, the default) the font's own line spacing is used. A
    /// LineHeight set on the text itself (a .card h2 + p's 1.6) wins.
    /// </summary>
    public class CssText : TextBlock
    {
        public static readonly DependencyProperty LineFactorProperty = DependencyProperty.RegisterAttached("LineFactor", typeof(double), typeof(CssText),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.Inherits, Changed));
        public static double GetLineFactor(DependencyObject e) { return (double)e.GetValue(LineFactorProperty); }
        public static void SetLineFactor(DependencyObject e, double v) { e.SetValue(LineFactorProperty, v); }

        static CssText()
        {
            FontSizeProperty.OverrideMetadata(typeof(CssText), new FrameworkPropertyMetadata(Changed));
            LineHeightProperty.OverrideMetadata(typeof(CssText), new FrameworkPropertyMetadata(Set));
            LineStackingStrategyProperty.OverrideMetadata(typeof(CssText), new FrameworkPropertyMetadata(Set));
        }

        bool own = true, applying;

        static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            CssText t = d as CssText;
            if (t != null && t.own) t.Apply();
        }

        // a line height of the text's own: it stays
        static void Set(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            CssText t = d as CssText;
            if (t != null && !t.applying && DependencyPropertyHelper.GetValueSource(d, e.Property).BaseValueSource == BaseValueSource.Local) t.own = false;
        }

        void Apply()
        {
            double f = GetLineFactor(this);
            applying = true;
            try
            {
                if (double.IsNaN(f) || f <= 0)
                {
                    ClearValue(LineHeightProperty);
                    ClearValue(LineStackingStrategyProperty);
                }
                else
                {
                    LineHeight = FontSize * f;
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                }
            }
            finally { applying = false; }
        }
    }
}
