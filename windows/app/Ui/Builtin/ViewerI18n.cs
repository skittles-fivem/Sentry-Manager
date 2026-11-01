using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// i18n.js translatePage() on the Viewer's elements: every text, tooltip
    /// (the data-i18n-title) and plain string content found under the
    /// Viewer's root is put in the language by its English words
    /// (<see cref="StudioI18n.Word"/>). Each element keeps the English it was
    /// built with; words that are not Studio's (a clip name, a time, the
    /// languages' own names) stay as they are. A text the Viewer set itself
    /// since the last pass is taken as the element's new words, so the walk
    /// never puts back something stale. Use on the UI thread.
    /// </summary>
    public static class StudioTranslate
    {
        static readonly DependencyProperty EnglishProperty = DependencyProperty.RegisterAttached("StudioEnglish", typeof(string), typeof(StudioTranslate));
        static readonly DependencyProperty ShownProperty = DependencyProperty.RegisterAttached("StudioShown", typeof(string), typeof(StudioTranslate));
        static readonly DependencyProperty TipEnglishProperty = DependencyProperty.RegisterAttached("StudioTipEnglish", typeof(string), typeof(StudioTranslate));
        static readonly DependencyProperty TipShownProperty = DependencyProperty.RegisterAttached("StudioTipShown", typeof(string), typeof(StudioTranslate));

        /// <summary>The words under <paramref name="root"/> in <paramref name="lang"/>; how many changed.</summary>
        public static int Walk(DependencyObject root, string lang)
        {
            if (root == null) return 0;
            int n = 0;
            Visit(root, lang, ref n, 0);
            return n;
        }

        static void Visit(DependencyObject d, string lang, ref int n, int depth)
        {
            if (d == null || depth > 200) return;
            TextBlock tb = d as TextBlock;
            if (tb != null && Plain(tb)) Apply(tb, TextBlock.TextProperty, tb.Text, lang, EnglishProperty, ShownProperty, ref n);
            SpacedText st = d as SpacedText;
            if (st != null) Apply(st, SpacedText.TextProperty, st.Text, lang, EnglishProperty, ShownProperty, ref n);
            ContentControl cc = d as ContentControl;
            if (cc != null && cc.Content is string) Apply(cc, ContentControl.ContentProperty, (string)cc.Content, lang, EnglishProperty, ShownProperty, ref n);
            FrameworkElement fe = d as FrameworkElement;
            if (fe != null && fe.ToolTip is string) Apply(fe, FrameworkElement.ToolTipProperty, (string)fe.ToolTip, lang, TipEnglishProperty, TipShownProperty, ref n);

            // a popup's content (a select's list) is not in the visual tree
            Popup p = d as Popup;
            if (p != null && p.Child != null) Visit(p.Child, lang, ref n, depth + 1);
            if (!(d is Visual) && !(d is System.Windows.Media.Media3D.Visual3D)) return;
            int count;
            try { count = VisualTreeHelper.GetChildrenCount(d); }
            catch (Exception) { return; }
            for (int i = 0; i < count; i++) Visit(VisualTreeHelper.GetChild(d, i), lang, ref n, depth + 1);
        }

        /// <summary>A text of one run at most (Text = would drop a formatted text's runs).</summary>
        static bool Plain(TextBlock tb)
        {
            int runs = 0;
            foreach (Inline i in tb.Inlines)
            {
                if (!(i is Run)) return false;
                if (++runs > 1) return false;
            }
            return true;
        }

        static void Apply(DependencyObject d, DependencyProperty prop, string current, string lang, DependencyProperty engProp, DependencyProperty shownProp, ref int n)
        {
            if (string.IsNullOrEmpty(current)) return;
            string english = (string)d.GetValue(engProp);
            string shown = (string)d.GetValue(shownProp);
            // first seen, or the Viewer put new words in since: those are the element's words now
            if (english == null || current != shown) english = current;
            string want = StudioI18n.Word(lang, english) ?? english;
            if (want != current)
            {
                d.SetValue(prop, want);
                n++;
            }
            d.SetValue(engProp, english);
            d.SetValue(shownProp, want);
        }
    }
}
