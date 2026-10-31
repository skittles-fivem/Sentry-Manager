using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SentrySix.Core;

namespace SentrySix.Ui
{
    /// <summary>
    /// A UI pack's page drawn here, shaped as the packs' own page modules are
    /// (export default { mount(el, ctx) } returning { unmount, onTopics,
    /// onCar, onKey, keys }): Mount builds the page's element and loads what
    /// it shows (a throw is the pack's "could not load" box with Try again);
    /// the page then draws itself again as its web twin's draw() does.
    /// Logic stays in Core (Core/&lt;Pack&gt;/*.cs); this is the drawing.
    /// </summary>
    public interface IPackPage
    {
        /// <summary>mount(): build and load. Runs on the UI thread; awaits return to it.</summary>
        Task<FrameworkElement> Mount(PackPageContext ctx);
        /// <summary>unmount(): the page is gone (stop timers and polls).</summary>
        void Unmount();
        /// <summary>onTopics(topics): the event stream's "changed" topics (after the shell read vehicles and notifications again).</summary>
        void OnTopics(HashSet<string> topics);
        /// <summary>onCar(): another vehicle was picked. False: the page has no onCar (the host may mount it again).</summary>
        bool OnCar();
    }

    /// <summary>The second argument of a pack page's mount(): sub path, query, go, status, alive.</summary>
    public sealed class PackPageContext
    {
        public ShellModel Shell;
        public IJsonApi Api;
        public PageDef Def;
        /// <summary>The route's rest with its query ("vehicle/3", "?clear=1").</summary>
        public string Sub = "";
        /// <summary>The render test mode's fixture (null when live).</summary>
        public JsonValue Fixture;
        /// <summary>go(hash): "#/footage" or "footage".</summary>
        public Action<string> Go = delegate { };
        /// <summary>status(text): the shell's line for this page (only while it is still the page shown).</summary>
        public Action<string> Status = delegate { };
        /// <summary>alive(): this visit is still the page shown.</summary>
        public Func<bool> Alive = () => true;
        /// <summary>Animations off (the render test mode, Windows' setting, Settings > reduce effects).</summary>
        public bool Still;
    }

    /// <summary>
    /// Shows one <see cref="IPackPage"/> as a pack's router does (Horizon's
    /// main.js render()): the loading box until mount() is done, then the
    /// page; a failed mount shows the message with "Try again" (mount again);
    /// live topics go to the page's onTopics; a vehicle picked goes to onCar,
    /// or mounts the page again for the routes that redraw on a vehicle
    /// (<see cref="RemountOnCar"/>); when the shell shows another page,
    /// unmount(). <see cref="Root"/> is the shell's PageContent.
    /// </summary>
    public sealed class PackHost
    {
        readonly Func<IPackPage> make;
        readonly PackPageContext ctx;
        readonly Func<FrameworkElement> loading;
        readonly Func<string, Action, FrameworkElement> error;
        readonly ContentControl root = new ContentControl();
        readonly ShellModel shell;
        IPackPage page;
        int seq;

        // which host wrote the shell's status line last (a page closing clears only its own)
        sealed class Owner { public PackHost Host; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ShellModel, Owner> owners = new System.Runtime.CompilerServices.ConditionalWeakTable<ShellModel, Owner>();

        void SetStatus(string t)
        {
            if (shell == null) return;
            owners.GetOrCreateValue(shell).Host = this;
            shell.PageStatus = t ?? "";
        }
        bool closed;

        /// <summary>The routes mounted again when another vehicle is picked and the page has no onCar (main.js).</summary>
        public ISet<string> RemountOnCar = new HashSet<string>();

        public PackHost(Func<IPackPage> make, PackPageContext ctx, Func<FrameworkElement> loading, Func<string, Action, FrameworkElement> error)
        {
            this.make = make;
            this.ctx = ctx;
            this.loading = loading;
            this.error = error;
            shell = ctx.Shell;
            root.Focusable = false;
            root.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            if (shell != null)
            {
                Action<HashSet<string>> onChanged = OnChanged;
                PropertyChangedEventHandler onProp = delegate(object s, PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == "VehicleId") OnCarPicked();
                };
                PropertyChangedEventHandler gone = null;
                gone = delegate(object s, PropertyChangedEventArgs e)
                {
                    if (e.PropertyName != "PageContent" || shell.PageContent == root) return;
                    shell.PropertyChanged -= gone;
                    shell.Changed -= onChanged;
                    shell.PropertyChanged -= onProp;
                    Close();
                };
                shell.Changed += onChanged;
                shell.PropertyChanged += onProp;
                shell.PropertyChanged += gone;
            }
        }

        /// <summary>Starts the first mount (after RemountOnCar is set).</summary>
        public PackHost Start()
        {
            Mount();
            return this;
        }

        public FrameworkElement Root { get { return root; } }

        public IPackPage Page { get { return page; } }

        async void Mount()
        {
            if (closed) return;
            int mine = ++seq;
            if (page != null) { try { page.Unmount(); } catch (Exception) { } page = null; }
            SetStatus("");
            root.Content = loading();
            IPackPage p;
            try { p = make(); }
            catch (Exception ex) { root.Content = error(ex.Message, Mount); return; }
            page = p;
            PackPageContext c = new PackPageContext();
            c.Shell = ctx.Shell;
            c.Api = ctx.Api;
            c.Def = ctx.Def;
            c.Sub = ctx.Sub;
            c.Fixture = ctx.Fixture;
            c.Go = ctx.Go;
            c.Still = ctx.Still;
            c.Alive = () => mine == seq && !closed;
            c.Status = t => { if (mine == seq && !closed) SetStatus(t); };
            FrameworkElement el;
            try
            {
                el = await p.Mount(c);
            }
            catch (Exception ex)
            {
                if (mine != seq || closed) return;
                root.Content = error(string.IsNullOrEmpty(ex.Message) ? "This page could not load." : ex.Message, Mount);
                return;
            }
            if (mine != seq || closed) { try { p.Unmount(); } catch (Exception) { } return; }
            root.Content = el;
        }

        void OnChanged(HashSet<string> topics)
        {
            if (closed || page == null) return;
            try { page.OnTopics(topics); } catch (Exception) { }
        }

        void OnCarPicked()
        {
            if (closed || page == null) return;
            bool handled;
            try { handled = page.OnCar(); } catch (Exception) { handled = true; }
            if (!handled && ctx.Def != null && RemountOnCar.Contains(ctx.Def.Id)) Mount();
        }

        void Close()
        {
            if (closed) return;
            closed = true;
            seq++;
            Owner o;
            if (shell != null && owners.TryGetValue(shell, out o) && o.Host == this) shell.PageStatus = "";
            if (page != null) { try { page.Unmount(); } catch (Exception) { } }
        }
    }
}

namespace SentrySix.Ui
{
    /// <summary>
    /// Marks a pack page drawn here: [PackPage("horizon", "jobs")] on a class
    /// with a constructor taking nothing. The pack's interface finds its pages
    /// by this mark (and draws the others as the browser fallback), so a new
    /// page needs no line in a shared table.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class PackPageAttribute : Attribute
    {
        public PackPageAttribute(string pack, string route)
        {
            Pack = pack;
            Route = route;
        }

        /// <summary>"horizon", "flux", "aurora", "daylight".</summary>
        public string Pack { get; private set; }
        /// <summary>The page's route ("jobs"), or route/sub ("encoder/vehicles").</summary>
        public string Route { get; private set; }

        /// <summary>The pages of a pack drawn here, by route.</summary>
        public static Dictionary<string, Func<IPackPage>> Of(string pack)
        {
            Dictionary<string, Func<IPackPage>> d = new Dictionary<string, Func<IPackPage>>();
            foreach (Type t in typeof(PackPageAttribute).Assembly.GetTypes())
            {
                foreach (object a in t.GetCustomAttributes(typeof(PackPageAttribute), false))
                {
                    PackPageAttribute p = (PackPageAttribute)a;
                    if (p.Pack != pack || !typeof(IPackPage).IsAssignableFrom(t)) continue;
                    Type type = t;
                    d[p.Route] = () => (IPackPage)Activator.CreateInstance(type);
                }
            }
            return d;
        }
    }
}
