using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SentrySix.Core
{
    public enum RichKind { Text, Code, Bold, Link, Muted, Mono, Break }

    public sealed class RichRun
    {
        public RichKind Kind;
        public string Text;
        /// <summary>A link's target (a route such as "#/settings", or a URL).</summary>
        public string Href;

        public override string ToString()
        {
            return Kind + ":" + Text + (Href != null ? "->" + Href : "");
        }
    }

    /// <summary>
    /// Text the web writes as HTML (innerHTML with &lt;code&gt;, &lt;b&gt;,
    /// &lt;wbr&gt;, links): as runs, so an interface draws them as inline runs
    /// (code in the monospace font, bold, links) rather than as plain text.
    /// </summary>
    public sealed class Rich
    {
        readonly List<RichRun> runs = new List<RichRun>();

        public IList<RichRun> Runs { get { return runs; } }

        public Rich Add(RichKind kind, string text)
        {
            return Add(kind, text, null);
        }

        public Rich Add(RichKind kind, string text, string href)
        {
            if (kind != RichKind.Break && string.IsNullOrEmpty(text)) return this;
            RichRun last = runs.Count > 0 ? runs[runs.Count - 1] : null;
            if (last != null && last.Kind == kind && kind != RichKind.Link && kind != RichKind.Break) last.Text += text;
            else
            {
                RichRun r = new RichRun();
                r.Kind = kind;
                r.Text = text ?? "";
                r.Href = href;
                runs.Add(r);
            }
            return this;
        }

        public Rich Text(string s) { return Add(RichKind.Text, s); }
        public Rich Code(string s) { return Add(RichKind.Code, s); }
        public Rich Bold(string s) { return Add(RichKind.Bold, s); }

        public Rich Append(Rich other)
        {
            if (other != null) foreach (RichRun r in other.runs) Add(r.Kind, r.Text, r.Href);
            return this;
        }

        /// <summary>The words alone (what textContent would give, &lt;wbr&gt; dropped).</summary>
        public string Plain
        {
            get
            {
                StringBuilder b = new StringBuilder();
                foreach (RichRun r in runs) b.Append(r.Kind == RichKind.Break ? "\n" : r.Text);
                return b.ToString();
            }
        }

        public override string ToString()
        {
            return Plain;
        }

        static readonly Regex Tag = new Regex("<(/?)([a-zA-Z0-9]+)([^>]*)>", RegexOptions.CultureInvariant);
        static readonly Regex HrefRe = new Regex("href=\"([^\"]*)\"", RegexOptions.CultureInvariant);
        static readonly Regex ClassRe = new Regex("class=\"([^\"]*)\"", RegexOptions.CultureInvariant);

        /// <summary>
        /// The small HTML the pages write: code, b/strong, a href, span
        /// class="mono"/"muted", wbr (dropped), br; entities decoded; any other
        /// tag keeps only its text.
        /// </summary>
        public static Rich FromHtml(string html)
        {
            Rich r = new Rich();
            if (string.IsNullOrEmpty(html)) return r;
            Stack<RichKind> kinds = new Stack<RichKind>();
            Stack<string> hrefs = new Stack<string>();
            Stack<string> open = new Stack<string>();
            int at = 0;
            foreach (Match m in Tag.Matches(html))
            {
                if (m.Index > at) Emit(r, kinds, hrefs, html.Substring(at, m.Index - at));
                at = m.Index + m.Length;
                string name = m.Groups[2].Value.ToLowerInvariant();
                bool closing = m.Groups[1].Value == "/";
                if (name == "wbr") continue;
                if (name == "br") { r.Add(RichKind.Break, ""); continue; }
                if (closing)
                {
                    // pop to the matching open tag
                    while (open.Count > 0)
                    {
                        string o = open.Pop();
                        kinds.Pop();
                        hrefs.Pop();
                        if (o == name) break;
                    }
                    continue;
                }
                RichKind k = kinds.Count > 0 ? kinds.Peek() : RichKind.Text;
                string href = hrefs.Count > 0 ? hrefs.Peek() : null;
                string attrs = m.Groups[3].Value;
                if (name == "code") k = RichKind.Code;
                else if (name == "b" || name == "strong") k = RichKind.Bold;
                else if (name == "a")
                {
                    Match h = HrefRe.Match(attrs);
                    k = RichKind.Link;
                    href = h.Success ? WebUtility.HtmlDecode(h.Groups[1].Value) : "";
                }
                else if (name == "span")
                {
                    Match c = ClassRe.Match(attrs);
                    string cls = c.Success ? " " + c.Groups[1].Value + " " : "";
                    if (cls.Contains(" mono ")) k = RichKind.Mono;
                    else if (cls.Contains(" muted ")) k = RichKind.Muted;
                }
                if (attrs.TrimEnd().EndsWith("/", StringComparison.Ordinal)) continue;
                open.Push(name);
                kinds.Push(k);
                hrefs.Push(href);
            }
            if (at < html.Length) Emit(r, kinds, hrefs, html.Substring(at));
            return r;
        }

        static void Emit(Rich r, Stack<RichKind> kinds, Stack<string> hrefs, string raw)
        {
            string text = WebUtility.HtmlDecode(raw);
            RichKind k = kinds.Count > 0 ? kinds.Peek() : RichKind.Text;
            string href = hrefs.Count > 0 ? hrefs.Peek() : null;
            r.Add(k, text, k == RichKind.Link ? href : null);
        }
    }
}
