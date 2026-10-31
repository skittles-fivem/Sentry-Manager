using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SentrySix.Core.Horizon
{
    /// <summary>
    /// Horizon's Encoding per vehicle page (examples/ui-packs/horizon/web/hz/pages/vehicles.js):
    /// the title strip ("What each vehicle converts with: …" and Open the
    /// Encoder), then the shared web/vehicleEncoding.js view mounted with
    /// the pack's options (no intro, Horizon's classes: panel hud cards,
    /// .btn go buttons, .in fields, .fld labels, .chk boxes; its toasts as
    /// Horizon's; its question as Horizon's "Replace originals?" dialog,
    /// danger). Everything the view works out (the cards, the editor, the
    /// checks, the requests) is Core's VehicleEncodingView, exactly as the
    /// built-in page uses it; this adds the pack's words and options.
    /// Checked by test/windowsNativeHorizonVehicles.test.js and Core.Tests
    /// HorizonVehiclesTests.
    /// </summary>
    public static class HzVehicles
    {
        public const string Title = "Encoding per vehicle";
        public const string Lead = "What each vehicle converts with: codec, size, output, verification, clip groups, devices and automatic conversion.";
        public const string OpenEncoder = "Open the Encoder";
        public const string EncoderHref = "#/encoder";
        public const string Status = "Saved settings per vehicle";
        /// <summary>The pack's confirm(text): ask({ title, body: &lt;p&gt;text&lt;/p&gt;, yes, danger }).</summary>
        public const string AskTitle = "Replace originals?";
        public const string AskYes = "Replace originals";

        /// <summary>The view's options: toast(text, bad) as Horizon's toast, confirm(text) as Horizon's ask (for Forget too, as the pack's).</summary>
        public static VeUi Ui(HzUi ui)
        {
            VeUi v = new VeUi();
            v.Toast = (t, bad) => ui.Toast(t, bad ? "bad" : "ok");
            v.Confirm = t => ui.Ask(Ask(t));
            return v;
        }

        public static HzAsk Ask(string text)
        {
            HzAsk a = new HzAsk();
            a.Title = AskTitle;
            a.Para("", new Rich().Text(text ?? ""));
            a.Yes = AskYes;
            a.Danger = true;
            return a;
        }

        /// <summary>params?.get?.('edit') || null: the vehicle whose editor opens.</summary>
        public static string Edit(string sub)
        {
            string e = HzRoute.Param(sub, "edit");
            return string.IsNullOrEmpty(e) ? null : e;
        }

        /// <summary>onTopics: "vehicles" refreshes the view (an open editor is left alone; not while the reader is in the page).</summary>
        public static bool Redraws(ICollection<string> topics)
        {
            return topics != null && topics.Contains("vehicles");
        }

        /// <summary>
        /// The page as test/windowsNativeHorizonVehicles.test.js reads the
        /// web's: [title] Encoding per vehicle [p] the lead [a] Open the
        /// Encoder, then the view as VehicleEncodingView.Outline writes it,
        /// without its intro (intro: false) and with the pack's primary
        /// button (.btn.go).
        /// </summary>
        public static List<string> Outline(List<VeCard> cards, string loadError)
        {
            List<string> o = new List<string>();
            o.Add("[title]"); o.Add(Title);
            o.Add("[p]"); o.Add(Lead);
            o.Add("[a]"); o.Add(OpenEncoder);
            List<string> ve = VehicleEncodingView.Outline(cards ?? new List<VeCard>(), loadError);
            int from = 0;
            if (loadError == null && ve.Count >= 4 && ve[0] == "[intro]") from = 4;
            for (int i = from; i < ve.Count; i++)
            {
                string t = ve[i];
                if (t.StartsWith("[btn solid")) t = "[btn go" + t.Substring("[btn solid".Length);
                o.Add(t);
            }
            return o;
        }
    }
}
