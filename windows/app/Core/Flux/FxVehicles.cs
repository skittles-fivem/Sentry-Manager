using System;
using System.Collections.Generic;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Flux
{
    /// <summary>
    /// Flux's Encoding per vehicle page (examples/ui-packs/flux/web/flux/pages/vehicles.js):
    /// the head ("What each vehicle converts with: …" and Open the Encoder),
    /// then the shared web/vehicleEncoding.js view mounted with the pack's
    /// options (no intro, Flux's classes: .card cards, .btn.primary buttons,
    /// .input fields, .field labels, .check boxes; its toasts as Flux's; its
    /// question as Flux's "Replace originals?" dialog, danger). Everything the
    /// view works out (the cards, the editor, the checks, the requests) is
    /// Core's VehicleEncodingView, exactly as the built-in and Horizon pages
    /// use it; this adds the pack's words and options. Checked by
    /// test/windowsNativeFluxVehicles.test.js and Core.Tests FluxVehiclesTests.
    /// </summary>
    public static class FxVehicles
    {
        public const string Title = "Encoding per vehicle";
        public const string Lead = "What each vehicle converts with: codec, size, output, verification, clip groups, devices and automatic conversion.";
        public const string OpenEncoder = "Open the Encoder";
        public const string EncoderHref = "#/encoder";
        /// <summary>The pack's confirm(text): confirmDialog({ title, body: &lt;p&gt;text&lt;/p&gt;, ok, danger }).</summary>
        public const string AskTitle = "Replace originals?";
        public const string AskYes = "Replace originals";

        /// <summary>The view's options: toast(text, bad) as Flux's toast, confirm(text) as Flux's dialog (for Forget too, as the pack's).</summary>
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
            a.No = "Cancel";
            a.Danger = true;
            return a;
        }

        /// <summary>params?.get?.('edit') || null: the vehicle whose editor opens.</summary>
        public static string Edit(string sub)
        {
            return HzVehicles.Edit(sub);
        }

        /// <summary>onTopics: "vehicles" refreshes the view (an open editor is left alone; not while the reader is in the page).</summary>
        public static bool Redraws(ICollection<string> topics)
        {
            return topics != null && topics.Contains("vehicles");
        }

        /// <summary>
        /// The page as test/windowsNativeFluxVehicles.test.js reads the web's:
        /// [head] Encoding per vehicle [p] the lead [a] Open the Encoder, then
        /// the view as VehicleEncodingView.Outline writes it, without its
        /// intro (intro: false) and with the pack's primary button
        /// (.btn.primary).
        /// </summary>
        public static List<string> Outline(List<VeCard> cards, string loadError)
        {
            List<string> o = new List<string>();
            o.Add("[head]"); o.Add(Title);
            o.Add("[p]"); o.Add(Lead);
            o.Add("[a]"); o.Add(OpenEncoder);
            List<string> ve = VehicleEncodingView.Outline(cards ?? new List<VeCard>(), loadError);
            int from = 0;
            if (loadError == null && ve.Count >= 4 && ve[0] == "[intro]") from = 4;
            for (int i = from; i < ve.Count; i++)
            {
                string t = ve[i];
                if (t.StartsWith("[btn solid")) t = "[btn primary" + t.Substring("[btn solid".Length);
                o.Add(t);
            }
            return o;
        }
    }
}
