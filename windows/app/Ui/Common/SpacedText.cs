using System;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SentrySix.Ui
{
    /// <summary>
    /// Text with CSS letter-spacing (WPF's TextBlock has none) and an optional
    /// gradient fill over the text (CSS background-clip: text, Horizon's
    /// "chrome" lettering). Font, size, weight and colour inherit like a TextBlock's.
    /// </summary>
    public sealed class SpacedText : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(SpacedText),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        /// <summary>Letter spacing in em (CSS letter-spacing: 0.28em is 0.28).</summary>
        public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register("Spacing", typeof(double), typeof(SpacedText),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(SpacedText),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(SpacedText),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(SpacedText),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(SpacedText),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(SpacedText),
            new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public string Text { get { return (string)GetValue(TextProperty); } set { SetValue(TextProperty, value); } }
        public double Spacing { get { return (double)GetValue(SpacingProperty); } set { SetValue(SpacingProperty, value); } }
        public Brush Fill { get { return (Brush)GetValue(FillProperty); } set { SetValue(FillProperty, value); } }
        public Brush Foreground { get { return (Brush)GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }
        public FontFamily FontFamily { get { return (FontFamily)GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }
        public double FontSize { get { return (double)GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }
        public FontWeight FontWeight { get { return (FontWeight)GetValue(FontWeightProperty); } set { SetValue(FontWeightProperty, value); } }

        public SpacedText()
        {
            SnapsToDevicePixels = true;
        }

        public SpacedText(string text, double size, FontWeight weight, double spacing) : this()
        {
            Text = text;
            FontSize = size;
            FontWeight = weight;
            Spacing = spacing;
        }

        FormattedText Glyph(string ch, double dip)
        {
            Typeface face = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
            return new FormattedText(ch, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, FontSize, Brushes.Black, dip);
        }

        double Dip()
        {
            try { return VisualTreeHelper.GetDpi(this).PixelsPerDip; }
            catch (Exception) { return 1; }
        }

        protected override Size MeasureOverride(Size available)
        {
            string t = Text ?? "";
            double dip = Dip();
            double w = 0;
            foreach (char c in t) w += Glyph(c.ToString(), dip).WidthIncludingTrailingWhitespace + Spacing * FontSize;
            double h = Math.Ceiling(FontFamily.LineSpacing * FontSize);
            return new Size(Math.Max(0, Math.Ceiling(w)), h);
        }

        protected override void OnRender(DrawingContext dc)
        {
            string t = Text ?? "";
            if (t.Length == 0) return;
            double dip = Dip();
            GeometryGroup group = new GeometryGroup();
            group.FillRule = FillRule.Nonzero;
            double x = 0;
            foreach (char c in t)
            {
                FormattedText f = Glyph(c.ToString(), dip);
                group.Children.Add(f.BuildGeometry(new Point(x, 0)));
                x += f.WidthIncludingTrailingWhitespace + Spacing * FontSize;
            }
            // the gradient spans the element's box, as background-clip: text does
            dc.PushClip(new RectangleGeometry(new Rect(-4, -4, ActualWidth + 8, ActualHeight + 8)));
            Brush b = Fill ?? Foreground;
            LinearGradientBrush lg = Fill as LinearGradientBrush;
            if (lg != null && lg.MappingMode == BrushMappingMode.RelativeToBoundingBox)
            {
                LinearGradientBrush abs = lg.Clone();
                abs.MappingMode = BrushMappingMode.Absolute;
                abs.StartPoint = new Point(lg.StartPoint.X * ActualWidth, lg.StartPoint.Y * ActualHeight);
                abs.EndPoint = new Point(lg.EndPoint.X * ActualWidth, lg.EndPoint.Y * ActualHeight);
                b = abs;
            }
            dc.DrawGeometry(b, null, group);
            dc.Pop();
        }
    }
}
