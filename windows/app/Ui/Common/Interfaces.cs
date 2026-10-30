using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SentrySix.Ui
{
    /// <summary>
    /// The interfaces this app draws natively, and which one the server has
    /// chosen (GET /api/ui-packs: { active: id | null, packs: [{ id, name, builtin }] };
    /// the event stream's "changed" with topic "interface" announces a switch).
    /// </summary>
    public static class Interfaces
    {
        static List<IInterface> all;

        public static IReadOnlyList<IInterface> All
        {
            get
            {
                if (all == null)
                    all = new List<IInterface>
                    {
                        new Builtin.BuiltinInterface(), new Horizon.HorizonInterface(), new Flux.FluxInterface(),
                        new Aurora.AuroraInterface(), new Daylight.DaylightInterface()
                    };
                return all;
            }
        }

        public static IInterface Get(string id)
        {
            foreach (IInterface i in All) if (i.Id == id) return i;
            return null;
        }

        public static IInterface Builtin { get { return All[0]; } }

        /// <summary>
        /// The interface for the server's active pack: null (no pack) is the
        /// built-in one; a shipped pack (builtin: true in the list) is its
        /// native twin; anything else (an uploaded pack) is null: the app shows
        /// a notice that opens it in the browser.
        /// </summary>
        public static IInterface ForPack(string activeId, bool shippedPack)
        {
            if (string.IsNullOrEmpty(activeId)) return Builtin;
            if (!shippedPack) return null;
            return Get(activeId);
        }

        /// <summary>
        /// Before sign-in the pack list is not readable (it needs a session),
        /// but the public /login page is the active pack's own sign-in page:
        /// its words tell which interface it is. Returns an interface id,
        /// "uploaded" for an unknown page, or null when it cannot tell.
        /// </summary>
        public static string SniffLogin(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            if (html.Contains("Sign in · Horizon") || html.Contains("class=\"chrome\">HORIZON")) return "horizon";
            if (html.Contains("Sign in · Flux") || html.Contains("flux --connect")) return "flux";
            if (html.Contains("<span>Aurora</span>") && html.Contains("signin-card")) return "aurora";
            if (html.Contains("Your Tesla footage, looked after")) return "daylight";
            if (html.Contains("Use the account for this Sentry Six Manager.") && html.Contains("class=\"login\"")) return "builtin";
            return "uploaded";
        }

        /// <summary>The notice for an uploaded pack (a web page only): drawn in the built-in look, with a button to the browser.</summary>
        public static FrameworkElement UploadedNotice(string packName, Action openInBrowser)
        {
            IInterface b = Builtin;
            Grid root = new Grid();
            root.Resources = b.Theme();
            root.SetResourceReference(Panel.BackgroundProperty, "bg");
            root.SetResourceReference(TextElement.FontFamilyProperty, "font");
            root.SetResourceReference(TextElement.ForegroundProperty, "text");
            TextElement.SetFontSize(root, 14);
            StackPanel s = new StackPanel();
            s.Children.Add(U.Text("This interface is a web page", 20, FontWeights.Bold, "text"));
            TextBlock p = U.Text("Sentry Six Manager uses the interface “" + (packName ?? "uploaded") + "”, an interface pack uploaded in Settings > Interface. "
                + "Uploaded packs are web pages, so this app cannot draw them; they open in the browser. "
                + "The built-in interface and the packs that come with the app (Horizon, Flux, Aurora, Daylight) open here.", 15.5, FontWeights.Normal, "text-2");
            p.LineHeight = 15.5 * 1.6;
            p.Margin = new Thickness(0, 12, 0, 20);
            s.Children.Add(p);
            BtnLook k = new BtnLook();
            k.Bg = "accent"; k.Fg = "accent-ink"; k.Border = Brushes.Transparent; k.Font = U.Inter;
            Button open = U.Button("Open it in the browser", k);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Click += delegate { openInBrowser(); };
            s.Children.Add(open);
            Border card = U.Box(s, "panel", "border", 1, new CornerRadius(16), new Thickness(28));
            card.MaxWidth = 620;
            card.VerticalAlignment = VerticalAlignment.Center;
            card.Margin = new Thickness(16);
            root.Children.Add(card);
            return root;
        }
    }
}
