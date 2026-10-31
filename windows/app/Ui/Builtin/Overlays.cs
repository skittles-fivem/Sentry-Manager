using System;
using System.Windows;
using System.Windows.Controls;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// The toasts and dialogs in the built-in look (web/shell.css .toast and
    /// dialog), over any interface's window: they carry the built-in theme
    /// themselves, so a pack's own theme keys do not change them.
    /// </summary>
    public static class Overlays
    {
        public static ToastLook ToastK()
        {
            ToastLook k = new ToastLook();
            k.Font = U.Inter;
            return k;
        }

        public static DialogLook DialogK()
        {
            DialogLook k = new DialogLook();
            k.Font = U.Inter;
            k.Mono = U.JetBrains;
            k.Btn = Look.BtnK(BtnKind.Plain);
            k.Solid = Look.BtnK(BtnKind.Solid);
            k.Danger = Look.BtnK(BtnKind.Danger);
            k.Input = Look.InputK();
            return k;
        }

        /// <summary>Puts the toast and dialog layers over <paramref name="root"/>'s content (taken from where they were before).</summary>
        public static void Attach(Grid root, FrameworkElement toasts, FrameworkElement dialogs)
        {
            foreach (FrameworkElement layer in new[] { dialogs, toasts })
            {
                Panel old = layer.Parent as Panel;
                if (old != null) old.Children.Remove(layer);
                if (layer.Resources == null || layer.Resources.Count == 0) layer.Resources = Interfaces.Builtin.Theme();
                Grid.SetRowSpan(layer, 99);
                Grid.SetColumnSpan(layer, 99);
                root.Children.Add(layer);
            }
        }
    }
}
