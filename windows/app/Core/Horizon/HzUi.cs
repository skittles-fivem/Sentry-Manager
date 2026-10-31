using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SentrySix.Core.Horizon
{
    /// <summary>One paragraph of a Horizon dialog's body: its text (bold, code runs) and class ("dim", "dim small", "").</summary>
    public sealed class HzPara
    {
        public Rich Text = new Rich();
        public string Class = "";

        public HzPara() { }

        public HzPara(string cls, Rich text)
        {
            Class = cls ?? "";
            Text = text ?? new Rich();
        }
    }

    /// <summary>core.js ask({ title, body, yes, no, danger }): a dialog with Cancel and the yes button (.go, or .danger).</summary>
    public sealed class HzAsk
    {
        public string Title = "";
        public List<HzPara> Body = new List<HzPara>();
        public string Yes = "OK";
        public string No = "Cancel";
        public bool Danger;

        public HzAsk Para(string cls, Rich text)
        {
            Body.Add(new HzPara(cls, text));
            return this;
        }

        /// <summary>
        /// The dialog as test/nativePackHarness.js reads the web's: the title,
        /// the body's text runs, then [btn no] No [btn yes go|danger] Yes.
        /// </summary>
        public List<string> Outline()
        {
            List<string> o = new List<string>();
            Add(o, Title);
            foreach (HzPara p in Body) foreach (RichRun r in p.Text.Runs) Add(o, r.Text);
            o.Add("[btn no]");
            Add(o, No);
            o.Add(Danger ? "[btn yes danger]" : "[btn yes go]");
            Add(o, Yes);
            return o;
        }

        static void Add(List<string> o, string s)
        {
            string t = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim();
            if (t.Length > 0) o.Add(t);
        }
    }

    /// <summary>core.js askText({ title, label, value, yes, type, body, max }).</summary>
    public sealed class HzAskText
    {
        public string Title = "", Label = "", Value = "", Yes = "Save", Type = "text";
        public List<HzPara> Body = new List<HzPara>();
        public int Max = 200;
    }

    /// <summary>
    /// What a Horizon page needs from the window: core.js toast(text, tone),
    /// ask(), askText(), the router's go() and status() (the radio's sub line).
    /// The tests record them; the app draws them in Horizon's look.
    /// </summary>
    public sealed class HzUi
    {
        /// <summary>toast(text, tone): tone "ok" (green, 4 s) or "bad" (red, 8 s).</summary>
        public Action<string, string> Toast = delegate { };
        /// <summary>ask(): true for the yes button.</summary>
        public Func<HzAsk, Task<bool>> Ask = a => Task.FromResult(false);
        /// <summary>askText(): the text, or null when cancelled.</summary>
        public Func<HzAskText, Task<string>> AskText = a => Task.FromResult<string>(null);
        /// <summary>go("#/footage"): another page.</summary>
        public Action<string> Go = delegate { };
        /// <summary>status(text): the radio display's sub line ("" for the vehicle's name).</summary>
        public Action<string> Status = delegate { };

        /// <summary>
        /// act(btn, fn): runs fn (the caller disables its button meanwhile);
        /// a failure becomes a red toast with its message (not for "Signed out").
        /// </summary>
        public async Task Act(Func<Task> fn)
        {
            Exception failed = null;
            try { await fn(); }
            catch (Exception ex) { failed = ex; }
            if (failed != null && failed.Message != "Signed out") Toast(failed.Message, "bad");
        }

        public async Task<T> Act<T>(Func<Task<T>> fn)
        {
            Exception failed = null;
            T v = default(T);
            try { v = await fn(); }
            catch (Exception ex) { failed = ex; }
            if (failed != null && failed.Message != "Signed out") Toast(failed.Message, "bad");
            return v;
        }
    }
}
