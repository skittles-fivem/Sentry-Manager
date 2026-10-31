using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SentrySix.Core;

namespace SentrySix.Ui
{
    /// <summary>How an interface draws Core's <see cref="Rich"/> runs: the fonts and colours of code, links and muted text.</summary>
    public sealed class RichLook
    {
        public FontFamily Mono;
        /// <summary>CSS code { font-size: 0.92em } and the like (1 = the text's own size).</summary>
        public double CodeScale = 1;
        public string CodeBg;
        public string LinkFg = "accent";
        public string MutedFg = "muted";
        public FontWeight BoldWeight = FontWeights.Bold;
    }

    /// <summary>Core's <see cref="Rich"/> (the web's innerHTML with code, b, a, wbr) as TextBlock inlines.</summary>
    public static class RichText
    {
        /// <summary>A wrapping TextBlock with the runs; links call <paramref name="follow"/> with their href.</summary>
        public static TextBlock Block(Rich r, double size, FontWeight weight, string colorKey, RichLook look, Action<string> follow)
        {
            TextBlock t = U.Text("", size, weight, colorKey);
            Fill(t.Inlines, r, size, look, follow);
            return t;
        }

        public static void Fill(InlineCollection into, Rich r, double size, RichLook look, Action<string> follow)
        {
            if (r == null) return;
            foreach (RichRun x in r.Runs) into.Add(MakeInline(x, size, look, follow));
        }

        static Inline MakeInline(RichRun x, double size, RichLook look, Action<string> follow)
        {
            switch (x.Kind)
            {
                case RichKind.Break:
                    return new LineBreak();
                case RichKind.Code:
                case RichKind.Mono:
                {
                    Run c = new Run(Format.Breakable(x.Text));
                    if (look != null && look.Mono != null) c.FontFamily = look.Mono;
                    if (look != null && look.CodeScale != 1 && x.Kind == RichKind.Code) c.FontSize = size * look.CodeScale;
                    if (look != null && look.CodeBg != null && x.Kind == RichKind.Code) c.SetResourceReference(TextElement.BackgroundProperty, look.CodeBg);
                    return c;
                }
                case RichKind.Bold:
                {
                    Run b = new Run(x.Text);
                    b.FontWeight = look != null ? look.BoldWeight : FontWeights.Bold;
                    return b;
                }
                case RichKind.Muted:
                {
                    Run m = new Run(x.Text);
                    m.SetResourceReference(TextElement.ForegroundProperty, look != null ? look.MutedFg : "muted");
                    return m;
                }
                case RichKind.Link:
                {
                    Hyperlink h = new Hyperlink(new Run(x.Text));
                    h.SetResourceReference(TextElement.ForegroundProperty, look != null ? look.LinkFg : "accent");
                    h.TextDecorations = null;
                    h.Cursor = Cursors.Hand;
                    string href = x.Href;
                    h.Click += delegate { if (follow != null) follow(href); };
                    h.MouseEnter += delegate { h.TextDecorations = TextDecorations.Underline; };
                    h.MouseLeave += delegate { h.TextDecorations = null; };
                    return h;
                }
                default:
                    return new Run(x.Text);
            }
        }
    }
}
