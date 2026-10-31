using System;
using System.Windows;
using SentrySix.Core;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Horizon
{
    /// <summary>What every Horizon page reads from its context: core.js car(), Date.now(), app.power, the page's frame, its window.</summary>
    public static class HzPages
    {
        /// <summary>core.js car(): the vehicle shown, null for none.</summary>
        public static HzCar Car(PackPageContext c)
        {
            VehicleInfo v = c != null && c.Shell != null ? c.Shell.Current : null;
            return v == null ? null : new HzCar(v.Id, v.Name);
        }

        /// <summary>Date.now(): the shell's clock (the render test mode's fixture time, read as UTC).</summary>
        public static long Now(PackPageContext c)
        {
            if (c == null || c.Shell == null) return Format.NowMs();
            DateTime t = c.Shell.Now();
            DateTime utc = t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc);
            return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        /// <summary>app.power?.currency (null for none: the pages use "$").</summary>
        public static string Currency(PackPageContext c)
        {
            return c != null && c.Shell != null && c.Shell.Power != null ? c.Shell.Power["currency"].Str : null;
        }

        /// <summary>The page's toasts and dialogs: the Horizon shell's own, else the window's.</summary>
        public static HzUi Ui(PackPageContext c)
        {
            HzOverlay o = c != null ? HzOverlay.Of(c.Shell) : null;
            return o != null ? o.UiFor(c) : HzOverlay.Fallback(c);
        }

        /// <summary>The page's sections in .view's grid: gap 16 (12 under 1024).</summary>
        public static Flex Page(params UIElement[] sections)
        {
            Flex f = Flex.Col(16, sections);
            U.OnWindowWidth(f, w => f.Gap = w <= 1023 ? 12 : 16);
            return f;
        }
    }
}
