using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SentrySix.Core
{
    /// <summary>A slot of a multi-camera layout: the slot (tl tc tr / bl bc br), its camera and the layout's label.</summary>
    public sealed class MultiSlot
    {
        public string Slot;
        /// <summary>The camera id; null for an empty slot, or when a custom order holds something that is not text.</summary>
        public string Camera;
        /// <summary>The layout's label (MULTI_LAYOUTS' label; the effective slot's label after a custom order).</summary>
        public string Label;
        /// <summary>A custom order's value for this slot as saved (null: the layout's own camera; <see cref="JsJson.Undefined"/>: undefined).</summary>
        public JsonValue Raw;

        public MultiSlot Copy() { return new MultiSlot { Slot = Slot, Camera = Camera, Label = Label, Raw = Raw }; }

        public LayoutSlot ToLayoutSlot() { return new LayoutSlot(Slot, Camera); }
    }

    /// <summary>A multi-camera layout (lib/multiLayouts.js): its name, columns and six slots.</summary>
    public sealed class MultiLayout
    {
        public string Id;
        public string Name;
        public int Columns;
        public List<MultiSlot> Slots = new List<MultiSlot>();
    }

    /// <summary>
    /// lib/multiLayouts.js MULTI_LAYOUTS: the Tesla six-camera layout (the
    /// profile's layoutSlots, "Default"), the HW3 four-camera layout (two
    /// columns, the centre slots empty) and GM Surround Vision's.
    /// </summary>
    public static class MultiLayouts
    {
        public const string DefaultId = "six_default";
        public const string TeslaHw3Id = "tesla_hw3";
        public const string GmId = "gm_surroundvision";

        static readonly MultiLayout[] all = { ProfileLayout(DefaultId, Dashcam.Tesla, "Default"), Hw3(), ProfileLayout(GmId, Dashcam.Gmc, "GM Surround Vision") };

        static MultiLayout ProfileLayout(string id, DashcamProfile p, string name)
        {
            MultiLayout l = new MultiLayout { Id = id, Name = name, Columns = 3 };
            foreach (LayoutSlot s in p.LayoutSlots)
                l.Slots.Add(new MultiSlot { Slot = s.Slot, Camera = s.Camera, Label = s.Camera != null ? p.Camera(s.Camera) : "" });
            return l;
        }

        static MultiLayout Hw3()
        {
            MultiLayout l = new MultiLayout { Id = TeslaHw3Id, Name = "HW3", Columns = 2 };
            string[,] slots = { { "tl", "front" }, { "tc", null }, { "tr", "back" }, { "bl", "left_repeater" }, { "bc", null }, { "br", "right_repeater" } };
            for (int i = 0; i < 6; i++)
            {
                string cam = slots[i, 1];
                l.Slots.Add(new MultiSlot { Slot = slots[i, 0], Camera = cam, Label = cam != null ? Dashcam.Tesla.Camera(cam) : "" });
            }
            return l;
        }

        /// <summary>The layouts in their order.</summary>
        public static IList<MultiLayout> All { get { return all; } }

        /// <summary>MULTI_LAYOUTS[id], or null.</summary>
        public static MultiLayout Get(string id)
        {
            foreach (MultiLayout l in all) if (l.Id == id) return l;
            return null;
        }

        /// <summary>setMultiLayout's choice: the layout when there is one by that id, else six_default (GM's profile names "gmc_surroundvision", which is not one: its footage gets the six-camera layout, as on the web).</summary>
        public static string Resolve(string id)
        {
            return Get(id) != null ? id : DefaultId;
        }
    }

    /// <summary>ui/playbackLayout.js getTeslaHardware / setTeslaHardware / teslaLayoutId and script.js autoTeslaHardware.</summary>
    public static class TeslaHardware
    {
        public const string Key = "sentrySix.playback.teslaHardware";

        /// <summary>The words of the HW4 / HW3 switch's title.</summary>
        public const string Title = "HW4: six cameras. HW3: four cameras (HW3 cars have no pillar cameras).";

        /// <summary>The stored mode: hw3, else hw4.</summary>
        public static string Read(string stored) { return stored == "hw3" ? "hw3" : "hw4"; }

        /// <summary>setTeslaHardware's value.</summary>
        public static string Normalize(string mode) { return mode == "hw3" ? "hw3" : "hw4"; }

        public static string LayoutId(string mode) { return mode == "hw3" ? MultiLayouts.TeslaHw3Id : MultiLayouts.DefaultId; }

        /// <summary>
        /// autoTeslaHardware(groups): for Tesla footage, HW4 when a group has a
        /// pillar camera, else HW3; null when nothing changes (not Tesla, no
        /// groups, or that mode and its layout are already the ones).
        /// </summary>
        public static string Auto(string profileId, IList<ICollection<string>> groupCameras, string storedMode, string layoutId)
        {
            if ((string.IsNullOrEmpty(profileId) ? Dashcam.TeslaId : profileId) != Dashcam.TeslaId || groupCameras == null || groupCameras.Count == 0) return null;
            bool pillars = false;
            foreach (ICollection<string> g in groupCameras)
                if (g != null && (g.Contains("left_pillar") || g.Contains("right_pillar"))) { pillars = true; break; }
            string mode = pillars ? "hw4" : "hw3";
            if (mode == Read(storedMode) && layoutId == LayoutId(mode)) return null;
            return mode;
        }
    }

    /// <summary>The share of each column and row of the grid.</summary>
    public sealed class GridTracks
    {
        public double[] Columns;
        public double[] Rows;

        public GridTracks Copy() { return new GridTracks { Columns = (double[])Columns.Clone(), Rows = (double[])Rows.Clone() }; }
    }

    /// <summary>
    /// ui/playbackLayout.js createGridResizer's sizes: the dividers' shares per
    /// layout (localStorage sentrySix.playback.gridSizes; this PC's prefs
    /// here), equal shares when none are saved or they do not fit the
    /// layout, a divider's drag (each side keeps 12 % of the pair) and
    /// double-click's reset.
    /// </summary>
    public sealed class GridSizes
    {
        public const string Key = "sentrySix.playback.gridSizes";
        public const double MinShare = 0.12;

        JsonValue sizes;

        /// <param name="stored">The saved text (null: none).</param>
        public GridSizes(string stored)
        {
            sizes = JsJson.ReadStored(stored);
        }

        /// <summary>tracksFor(layoutId, columns).</summary>
        public GridTracks For(string layoutId, int columns)
        {
            JsonValue saved = JsJson.Get(sizes, layoutId);
            if (!JsJson.Truthy(saved)) saved = null;
            GridTracks t = new GridTracks();
            JsonValue c = JsJson.Get(saved, "columns"), r = JsJson.Get(saved, "rows");
            if (Valid(c, columns)) t.Columns = Normalize(c);
            else
            {
                t.Columns = new double[columns];
                for (int i = 0; i < columns; i++) t.Columns[i] = 1.0 / columns;
            }
            t.Rows = Valid(r, 2) ? Normalize(r) : new[] { 0.5, 0.5 };
            return t;
        }

        /// <summary>current().columns: data-columns 2 is two, anything else three.</summary>
        public static int ColumnsOf(int dataColumns) { return dataColumns == 2 ? 2 : 3; }

        static bool Valid(JsonValue v, int count)
        {
            if (v == null || !v.IsArray || v.Count != count) return false;
            foreach (JsonValue x in v.Items) if (!JsJson.FiniteNumber(x) || !(x.Num > 0)) return false;
            return true;
        }

        static double[] Normalize(JsonValue v)
        {
            double total = 0;
            foreach (JsonValue x in v.Items) total += x.Num;
            double[] r = new double[v.Count];
            for (int i = 0; i < r.Length; i++) r[i] = v[i].Num / total;
            return r;
        }

        /// <summary>
        /// onMove: the divider between track index and index + 1 moved by
        /// moved (px) of extent (the grid's width or height); the pair keeps
        /// its sum, each side at least 12 % of it.
        /// </summary>
        public static GridTracks Drag(GridTracks start, bool columns, int index, double movedPx, double extentPx)
        {
            double[] a = columns ? start.Columns : start.Rows;
            double pair = a[index] + a[index + 1];
            double delta = movedPx / extentPx;
            double minimum = MinShare * pair;
            double first = Math.Min(pair - minimum, Math.Max(minimum, a[index] + delta));
            GridTracks t = start.Copy();
            double[] b = columns ? t.Columns : t.Rows;
            b[index] = first;
            b[index + 1] = pair - first;
            return t;
        }

        /// <summary>save(layoutId, tracks): the text to store.</summary>
        public string Save(string layoutId, GridTracks t)
        {
            List<JsonValue> c = new List<JsonValue>(), r = new List<JsonValue>();
            foreach (double d in t.Columns) c.Add(JsonValue.Number(d));
            foreach (double d in t.Rows) r.Add(JsonValue.Number(d));
            sizes = JsJson.Spread(sizes).With(layoutId, JsonValue.Object("columns", JsonValue.Array(c), "rows", JsonValue.Array(r)));
            return JsJson.Stringify(sizes);
        }

        /// <summary>reset(): the layout's sizes forgotten; the text to store.</summary>
        public string Reset(string layoutId)
        {
            sizes = JsJson.Without(JsJson.Spread(sizes), layoutId);
            return JsJson.Stringify(sizes);
        }
    }

    /// <summary>A camera's place in a free layout: shares of the view.</summary>
    public sealed class FreeBox
    {
        public double X, Y, W, H;
        /// <summary>The saved object it came from (its other keys are written back as they were), or null.</summary>
        public JsonValue Raw;

        public FreeBox Copy() { return new FreeBox { X = X, Y = Y, W = W, H = H, Raw = Raw }; }

        public JsonValue ToJson()
        {
            if (Raw == null) return JsonValue.Object("x", JsonValue.Number(X), "y", JsonValue.Number(Y), "w", JsonValue.Number(W), "h", JsonValue.Number(H));
            JsonValue o = JsJson.Spread(Raw);
            return o.With("x", JsonValue.Number(X)).With("y", JsonValue.Number(Y)).With("w", JsonValue.Number(W)).With("h", JsonValue.Number(H));
        }
    }

    /// <summary>A layout's free positions: each slot's box and the order they are stacked in (last on top).</summary>
    public sealed class FreeLayout
    {
        public readonly OrderedMap<FreeBox> Boxes = new OrderedMap<FreeBox>();
        /// <summary>The slots bottom to top (as saved: other values are kept).</summary>
        public List<JsonValue> Order = new List<JsonValue>();

        /// <summary>order.indexOf(slot).</summary>
        public int Depth(string slot)
        {
            for (int i = 0; i < Order.Count; i++) if (Order[i].IsString && Order[i].Str == slot) return i;
            return -1;
        }

        public JsonValue ToJson()
        {
            JsonValue boxes = JsonValue.Object();
            foreach (KeyValuePair<string, FreeBox> kv in Boxes.Entries) boxes = boxes.With(kv.Key, kv.Value.ToJson());
            return JsonValue.Object("boxes", boxes, "order", JsonValue.Array(Order));
        }
    }

    /// <summary>A tile's place in the grid or free layout (CSS pixels from the grid's top left).</summary>
    public struct TileBox
    {
        public double Left, Top, Width, Height;
        public TileBox(double l, double t, double w, double h) { Left = l; Top = t; Width = w; Height = h; }
        public double Right { get { return Left + Width; } }
        public double Bottom { get { return Top + Height; } }
    }

    /// <summary>
    /// ui/playbackLayout.js's free layout: a camera's corner drag resizes it,
    /// its move grip moves it (the first such drag switches the layout to free
    /// positions, starting from where the cameras are), remembered per layout
    /// (localStorage sentrySix.playback.freeLayouts), each camera at least 8 %
    /// of the view; double-click on a corner goes back to the grid.
    /// </summary>
    public sealed class FreeLayouts
    {
        public const string Key = "sentrySix.playback.freeLayouts";
        public const double Min = 0.08;

        JsonValue all;

        public FreeLayouts(string stored)
        {
            all = JsJson.ReadStored(stored);
        }

        static bool ValidBox(JsonValue b)
        {
            if (!JsJson.Truthy(b)) return false;
            JsonValue x = JsJson.Get(b, "x"), y = JsJson.Get(b, "y"), w = JsJson.Get(b, "w"), h = JsJson.Get(b, "h");
            if (!JsJson.FiniteNumber(x) || !JsJson.FiniteNumber(y) || !JsJson.FiniteNumber(w) || !JsJson.FiniteNumber(h)) return false;
            return w.Num >= Min && h.Num >= Min && x.Num >= 0 && y.Num >= 0 && x.Num + w.Num <= 1.0001 && y.Num + h.Num <= 1.0001;
        }

        /// <summary>freeFor(layoutId): the saved layout's valid boxes, or null (none, or none valid: the grid).</summary>
        public FreeLayout For(string layoutId)
        {
            JsonValue saved = JsJson.Get(all, layoutId);
            if (!JsJson.Truthy(saved) || !(saved.IsObject || saved.IsArray)) return null;
            JsonValue boxes = JsJson.Get(saved, "boxes");
            if (!JsJson.Truthy(boxes)) return null;
            FreeLayout l = new FreeLayout();
            foreach (KeyValuePair<string, JsonValue> kv in JsJson.Entries(boxes))
            {
                if (!ValidBox(kv.Value)) continue;
                l.Boxes.Set(kv.Key, new FreeBox { X = kv.Value["x"].Num, Y = kv.Value["y"].Num, W = kv.Value["w"].Num, H = kv.Value["h"].Num, Raw = kv.Value });
            }
            if (l.Boxes.Count == 0) return null;
            JsonValue order = JsJson.Get(saved, "order");
            if (order != null && order.IsArray) l.Order = new List<JsonValue>(order.Items);
            return l;
        }

        /// <summary>captureGrid(): every shown tile's place (in their order) as a share of the grid.</summary>
        public static FreeLayout Capture(IList<KeyValuePair<string, TileBox>> shown, double gridWidth, double gridHeight)
        {
            FreeLayout l = new FreeLayout();
            foreach (KeyValuePair<string, TileBox> kv in shown)
                l.Boxes.Set(kv.Key, new FreeBox { X = kv.Value.Left / gridWidth, Y = kv.Value.Top / gridHeight, W = kv.Value.Width / gridWidth, H = kv.Value.Height / gridHeight });
            foreach (string k in l.Boxes.Keys) l.Order.Add(JsonValue.String(k));
            return l;
        }

        /// <summary>startFreeDrag: the layout to drag in (the saved one, else the grid as it is) with the slot brought to the top; null when the slot has no box.</summary>
        public FreeLayout Begin(string layoutId, string slot, Func<FreeLayout> capture)
        {
            FreeLayout l = For(layoutId) ?? capture();
            if (!l.Boxes.Has(slot)) return null;
            List<JsonValue> order = new List<JsonValue>();
            foreach (JsonValue o in l.Order) if (!(o.IsString && o.Str == slot)) order.Add(o);
            order.Add(JsonValue.String(slot));
            l.Order = order;
            return l;
        }

        /// <summary>onFreeMove: the box from the drag's start moved or resized by (dx, dy) shares of the grid.</summary>
        public static FreeBox Moved(FreeBox start, bool resize, double dx, double dy)
        {
            FreeBox n = start.Copy();
            if (resize)
            {
                n.W = Math.Min(1 - start.X, Math.Max(Min, start.W + dx));
                n.H = Math.Min(1 - start.Y, Math.Max(Min, start.H + dy));
            }
            else
            {
                n.X = Math.Min(1 - start.W, Math.Max(0, start.X + dx));
                n.Y = Math.Min(1 - start.H, Math.Max(0, start.Y + dy));
            }
            return n;
        }

        /// <summary>saveFree(layoutId, layout): the text to store.</summary>
        public string Save(string layoutId, FreeLayout l)
        {
            all = JsJson.Spread(all).With(layoutId, l.ToJson());
            return JsJson.Stringify(all);
        }

        /// <summary>resetFree(): back to the grid; the text to store.</summary>
        public string Reset(string layoutId)
        {
            all = JsJson.Without(JsJson.Spread(all), layoutId);
            return JsJson.Stringify(all);
        }
    }

    /// <summary>
    /// The grid's geometry as Chrome lays out .multi-cam-grid: padding 10,
    /// gap 10, the columns' and rows' fr shares of what is left (the tiles
    /// are inline-size containers, so their content never widens a column),
    /// two columns with the centre tiles not shown; in a free layout each
    /// tile at its box (a tile without one at 0, 0, 33 % x 50 %).
    /// </summary>
    public static class GridGeometry
    {
        public const double Padding = 10;
        public const double Gap = 10;
        public static readonly string[] Slots = { "tl", "tc", "tr", "bl", "bc", "br" };

        /// <summary>The slots shown with that many columns (tc and bc are display: none with two).</summary>
        public static bool Shown(string slot, int columns)
        {
            return columns == 3 || (slot != "tc" && slot != "bc");
        }

        /// <summary>Each shown slot's place in the grid (tiles placed in order, row by row).</summary>
        public static List<KeyValuePair<string, TileBox>> Grid(double width, double height, GridTracks t)
        {
            int cols = t.Columns.Length;
            double[] xs = Tracks(width, t.Columns), ys = Tracks(height, t.Rows);
            double[] ws = Sizes(width, t.Columns), hs = Sizes(height, t.Rows);
            List<KeyValuePair<string, TileBox>> list = new List<KeyValuePair<string, TileBox>>();
            int n = 0;
            foreach (string s in Slots)
            {
                if (!Shown(s, cols)) continue;
                int c = n % cols, r = Math.Min(n / cols, ys.Length - 1);
                list.Add(new KeyValuePair<string, TileBox>(s, new TileBox(xs[c], ys[r], ws[c], hs[r])));
                n++;
            }
            return list;
        }

        static double[] Sizes(double extent, double[] shares)
        {
            double free = Math.Max(0, extent - 2 * Padding - (shares.Length - 1) * Gap);
            double sum = 0;
            foreach (double s in shares) sum += s;
            // (fr shares that add up to less than 1 are taken as they are, as CSS does)
            double scale = sum < 1 ? 1 : sum;
            double[] r = new double[shares.Length];
            for (int i = 0; i < r.Length; i++) r[i] = free * shares[i] / scale;
            return r;
        }

        static double[] Tracks(double extent, double[] shares)
        {
            double[] s = Sizes(extent, shares);
            double[] r = new double[s.Length];
            double at = Padding;
            for (int i = 0; i < s.Length; i++) { r[i] = at; at += s[i] + Gap; }
            return r;
        }

        /// <summary>The dividers' centres: between the first row's tiles (columns) and between the rows.</summary>
        public static void Dividers(double width, double height, GridTracks t, out double[] columnsAt, out double rowAt)
        {
            List<KeyValuePair<string, TileBox>> g = Grid(width, height, t);
            int cols = t.Columns.Length;
            columnsAt = new double[cols - 1];
            for (int i = 0; i < cols - 1; i++) columnsAt[i] = (g[i].Value.Right + g[i + 1].Value.Left) / 2;
            rowAt = (g[0].Value.Bottom + g[cols].Value.Top) / 2;
        }

        /// <summary>A tile's place in a free layout (its box, else the stylesheet's 0, 0, 0.33, 0.5).</summary>
        public static TileBox Free(double width, double height, FreeBox b)
        {
            if (b == null) return new TileBox(0, 0, 0.33 * width, 0.5 * height);
            return new TileBox(b.X * width, b.Y * height, b.W * width, b.H * height);
        }
    }

    /// <summary>
    /// createGridResizer's state for one grid: the active layout's divider
    /// sizes, its free layout when it has one (painted), and the drags that
    /// change them; what is to be stored goes to <c>store(key, text)</c>.
    /// The grid on screen draws <see cref="Boxes"/> and <see cref="ZIndex"/>.
    /// </summary>
    public sealed class GridLayoutState
    {
        readonly GridSizes sizes;
        readonly FreeLayouts free;
        readonly Action<string, string> store;
        // a divider's drag
        GridTracks dragFrom, dragLatest;
        string dragLayout;
        bool dragColumns;
        int dragIndex;
        double dragStart, dragExtent;
        // a free drag
        FreeLayout freeDrag;
        FreeBox freeStart;
        string freeSlot, freeLayout;
        bool freeResize, freeMoved;
        double freeX, freeY, freeW, freeH;

        public GridLayoutState(string sizesStored, string freeStored, Action<string, string> store)
        {
            sizes = new GridSizes(sizesStored);
            free = new FreeLayouts(freeStored);
            this.store = store;
            LayoutId = MultiLayouts.DefaultId;
            Columns = 3;
            Tracks = sizes.For(LayoutId, 3);
        }

        public string LayoutId { get; private set; }
        /// <summary>data-columns: 2 or 3.</summary>
        public int Columns { get; private set; }
        /// <summary>The tracks drawn now (a divider's drag shows its own).</summary>
        public GridTracks Tracks { get; private set; }
        /// <summary>The free layout drawn (null: the grid).</summary>
        public FreeLayout Painted { get; private set; }
        /// <summary>.grid-resizing: a divider or a camera's grip is being dragged.</summary>
        public bool Resizing { get; private set; }

        void Store(string key, string text) { if (store != null) store(key, text); }

        /// <summary>apply() for the layout and its data-columns.</summary>
        public void Apply(string layoutId, int dataColumns)
        {
            LayoutId = layoutId;
            Columns = GridSizes.ColumnsOf(dataColumns);
            Tracks = sizes.For(LayoutId, Columns);
            Painted = free.For(LayoutId);
        }

        /// <summary>Each shown tile's place now (the grid, or the free layout).</summary>
        public List<KeyValuePair<string, TileBox>> Boxes(double width, double height)
        {
            if (Painted == null) return GridGeometry.Grid(width, height, Tracks);
            List<KeyValuePair<string, TileBox>> r = new List<KeyValuePair<string, TileBox>>();
            foreach (string s in GridGeometry.Slots)
                if (GridGeometry.Shown(s, Tracks.Columns.Length))
                    r.Add(new KeyValuePair<string, TileBox>(s, GridGeometry.Free(width, height, Painted.Boxes.Has(s) ? Painted.Boxes.Get(s) : null)));
            return r;
        }

        /// <summary>A tile's z-index in the free layout (1 + its place in the order, 1 when not in it); 0 (auto) without a box.</summary>
        public int ZIndex(string slot)
        {
            if (Painted == null || !Painted.Boxes.Has(slot)) return 0;
            int d = Painted.Depth(slot);
            return d >= 0 ? 1 + d : 1;
        }

        /// <summary>startDrag on a divider (columns: between column index and index + 1) at a pointer position, of the grid's width or height.</summary>
        public void BeginDivider(bool columns, int index, double at, double extent)
        {
            dragFrom = sizes.For(LayoutId, Columns);
            dragLayout = LayoutId;
            dragColumns = columns;
            dragIndex = index;
            dragStart = at;
            dragExtent = extent;
            dragLatest = null;
            Resizing = true;
        }

        public void MoveDivider(double at)
        {
            if (dragFrom == null) return;
            dragLatest = GridSizes.Drag(dragFrom, dragColumns, dragIndex, at - dragStart, dragExtent);
            Tracks = dragLatest;
        }

        public void EndDivider()
        {
            if (dragFrom == null) return;
            Resizing = false;
            if (dragLatest != null) Store(GridSizes.Key, sizes.Save(dragLayout, dragLatest));
            dragFrom = null;
            dragLatest = null;
        }

        /// <summary>reset() (a divider's double-click).</summary>
        public void ResetSizes()
        {
            Store(GridSizes.Key, sizes.Reset(LayoutId));
            Apply(LayoutId, Columns);
        }

        /// <summary>startFreeDrag on a tile's corner (resize) or move grip, at a pointer position in a grid of that size. False when nothing starts.</summary>
        public bool BeginFree(string slot, bool resize, double x, double y, double width, double height)
        {
            FreeLayout l = free.Begin(LayoutId, slot, () => FreeLayouts.Capture(Boxes(width, height), width, height));
            if (l == null) return false;
            freeDrag = l;
            freeSlot = slot;
            freeLayout = LayoutId;
            freeResize = resize;
            freeMoved = false;
            freeStart = l.Boxes.Get(slot).Copy();
            freeX = x;
            freeY = y;
            freeW = width;
            freeH = height;
            Painted = l;
            Resizing = true;
            return true;
        }

        public void MoveFree(double x, double y)
        {
            if (freeDrag == null) return;
            freeDrag.Boxes.Set(freeSlot, FreeLayouts.Moved(freeStart, freeResize, (x - freeX) / freeW, (y - freeY) / freeH));
            freeMoved = true;
        }

        public void EndFree()
        {
            if (freeDrag == null) return;
            Resizing = false;
            if (freeMoved) Store(FreeLayouts.Key, free.Save(freeLayout, freeDrag));
            else if (free.For(freeLayout) == null) Painted = null; // a plain click changes nothing
            freeDrag = null;
        }

        /// <summary>resetFree() (a corner's double-click): back to the grid.</summary>
        public void ResetFree()
        {
            Store(FreeLayouts.Key, free.Reset(LayoutId));
            Painted = null;
            Apply(LayoutId, Columns);
        }
    }

    /// <summary>
    /// features/cameraRearrange.js: a tile dragged onto another swaps their
    /// cameras; the custom orders are kept per profile (and for Tesla's HW3
    /// layout on its own) in Studio's setting customCameraOrdersByProfile;
    /// getEffectiveSlots, updateTileLabels' words and resetCameraOrder.
    /// </summary>
    public sealed class CameraOrders
    {
        public const string Setting = "customCameraOrdersByProfile";
        public const string LegacySetting = "customCameraOrder";
        /// <summary>ui.notifications.cameraOrderReset.</summary>
        public const string ResetNotice = "Camera order reset to default";

        JsonValue orders = JsonValue.Object();
        // the saved orders were an array: JSON.stringify writes its items only (the profiles' orders kept on it are not saved)
        int arrayLength = -1;

        /// <summary>
        /// initCustomCameraOrder: the saved orders when they are an object (or
        /// array), else the legacy Tesla order (then saved under the new
        /// setting: <paramref name="save"/>). null: the setting is not there.
        /// </summary>
        public static CameraOrders Load(JsonValue byProfile, JsonValue legacy, out bool save)
        {
            CameraOrders o = new CameraOrders();
            save = false;
            if (JsJson.Truthy(byProfile) && (byProfile.IsObject || byProfile.IsArray))
            {
                o.orders = JsJson.Spread(byProfile);
                if (byProfile.IsArray) o.arrayLength = byProfile.Count;
            }
            else if (JsJson.Truthy(legacy))
            {
                o.orders = JsonValue.Object("tesla", legacy);
                save = true;
            }
            return o;
        }

        /// <summary>getOrderKey(): the profile; for Tesla's other layouts "tesla:&lt;layout&gt;".</summary>
        public static string OrderKey(string profileId, string layoutId)
        {
            string p = string.IsNullOrEmpty(profileId) ? Dashcam.TeslaId : profileId;
            return !string.IsNullOrEmpty(layoutId) && layoutId != MultiLayouts.DefaultId && p == Dashcam.TeslaId ? p + ":" + layoutId : p;
        }

        /// <summary>getCustomCameraOrder(): the order for this profile and layout when it is an object, else null.</summary>
        JsonValue Custom(string key)
        {
            JsonValue c = JsJson.Get(orders, key);
            return c != null && c.IsObject ? c : null;
        }

        /// <summary>Whether a custom order is saved for this profile and layout.</summary>
        public bool HasCustom(string profileId, string layoutId)
        {
            return Custom(OrderKey(profileId, layoutId)) != null;
        }

        /// <summary>getEffectiveSlots(): the layout's slots with the custom order's cameras.</summary>
        public List<MultiSlot> Effective(string profileId, string layoutId)
        {
            MultiLayout layout = MultiLayouts.Get(layoutId) ?? MultiLayouts.Get(MultiLayouts.DefaultId);
            JsonValue custom = Custom(OrderKey(profileId, layoutId));
            List<MultiSlot> r = new List<MultiSlot>();
            foreach (MultiSlot s in layout.Slots)
            {
                MultiSlot e = s.Copy();
                if (custom != null && custom.Has(s.Slot))
                {
                    JsonValue v = custom[s.Slot];
                    e.Raw = v;
                    e.Camera = JsJson.IsText(v) ? v.Str : null;
                    MultiSlot original = null;
                    foreach (MultiSlot b in layout.Slots)
                        if (JsJson.IsText(v) ? b.Camera == v.Str : (v.IsNull && b.Camera == null)) { original = b; break; }
                    string label = original != null ? original.Label : null;
                    e.Label = !string.IsNullOrEmpty(label) ? label : JsString(v);
                }
                r.Add(e);
            }
            return r;
        }

        /// <summary>Overlay's value as textContent shows it (null and undefined: empty).</summary>
        static string JsString(JsonValue v)
        {
            if (v == null || ReferenceEquals(v, JsJson.Undefined) || v.IsNull) return "";
            return StudioFrames.JsString(v);
        }

        /// <summary>updateTileLabels' words for a slot: getCameraTranslation(camera).</summary>
        public static string TileLabel(string profileId, MultiSlot s)
        {
            if (ReferenceEquals(s.Raw, JsJson.Undefined) || (s.Raw != null && s.Raw.IsNull)) return "";
            if (s.Raw == null || s.Raw.IsString) return Dashcam.StudioCameraLabel(s.Camera, profileId) ?? "";
            DashcamProfile p = Dashcam.Get(profileId);
            if (p.LocalizedCameraLabels) return JsString(s.Raw);
            // getCameraLabel: profile.cameras[camera] || camera || ''
            string key = JsString(s.Raw);
            string label = p.Camera(key);
            if (!string.IsNullOrEmpty(label)) return label;
            return JsJson.Truthy(s.Raw) ? key : "";
        }

        /// <summary>
        /// The drop of the camera in slot dragged onto slot target: their
        /// cameras swap (the custom order made from the slots shown when there
        /// was none). False when nothing changed (the same slot, or one that
        /// is not in the layout).
        /// </summary>
        public bool Drop(string profileId, string layoutId, string dragged, string target)
        {
            if (dragged == null || target == dragged) return false;
            List<MultiSlot> eff = Effective(profileId, layoutId);
            MultiSlot src = eff.Find(s => s.Slot == dragged), dst = eff.Find(s => s.Slot == target);
            if (src == null || dst == null) return false;
            string key = OrderKey(profileId, layoutId);
            JsonValue saved = JsJson.Get(orders, key);
            // (an order saved as text, a number or true: the web's drop throws setting a slot on it, and nothing changes)
            if (JsJson.Truthy(saved) && !saved.IsObject && !saved.IsArray) return false;
            JsonValue custom = Custom(key);
            if (custom == null)
            {
                custom = JsonValue.Object();
                foreach (MultiSlot s in eff) custom = JsJson.Set(custom, s.Slot, s.Raw ?? (s.Camera != null ? JsonValue.String(s.Camera) : JsonValue.Null));
            }
            JsonValue a = custom.Has(dragged) ? custom[dragged] : JsJson.Undefined;
            JsonValue b = custom.Has(target) ? custom[target] : JsJson.Undefined;
            custom = JsJson.Set(JsJson.Set(custom, dragged, b), target, a);
            orders = JsJson.Set(orders, key, custom);
            return true;
        }

        /// <summary>resetCameraOrder(): this profile and layout's order forgotten.</summary>
        public void Reset(string profileId, string layoutId)
        {
            orders = JsJson.Without(orders, OrderKey(profileId, layoutId));
        }

        /// <summary>The setting's value as saveCustomCameraOrder sends it (undefined values left out, as JSON.stringify does).</summary>
        public JsonValue ToSetting()
        {
            if (arrayLength >= 0)
            {
                List<JsonValue> items = new List<JsonValue>();
                for (int i = 0; i < arrayLength; i++)
                {
                    JsonValue v = JsJson.Get(orders, i.ToString(CultureInfo.InvariantCulture));
                    items.Add(v == null || ReferenceEquals(v, JsJson.Undefined) ? JsonValue.Null : v);
                }
                return JsonValue.Parse(JsJson.Stringify(JsonValue.Array(items)));
            }
            return JsonValue.Parse(JsJson.Stringify(orders));
        }
    }

    /// <summary>
    /// zoomPan.js applyMirrorTransforms / shouldMirrorCamera: with Studio's
    /// Mirror Cameras setting on (not saved as false), the profile's mirrored
    /// cameras (Tesla's back and repeaters) are flipped (scaleX(-1)).
    /// </summary>
    public static class CameraMirror
    {
        public const string Setting = "mirrorCameras";

        /// <summary>The setting's value: on unless it is false.</summary>
        public static bool On(JsonValue saved) { return !(saved != null && saved.IsBool && !saved.Bool); }

        public static bool Mirrored(bool on, string profileId, string camera)
        {
            if (string.IsNullOrEmpty(camera) || !on) return false;
            return Array.IndexOf(Dashcam.Get(profileId).MirroredCameras, camera) >= 0;
        }
    }

    /// <summary>
    /// eventMarkers.js updateEventCameraHighlight: a Sentry or Saved event's
    /// camera (event.json's camera index: 0-2 front, 3 left pillar, 4 right
    /// pillar, 5 left repeater, 6 right repeater, 7 back) glows red (Sentry)
    /// or yellow (Saved) when Sentry Camera Glow / Saved Camera Glow is on.
    /// </summary>
    public static class EventCameraGlow
    {
        public const string SentrySetting = "sentryCameraHighlight";
        public const string SavedSetting = "savedCameraHighlight";
        public const string Sentry = "event-camera-highlight-sentry";
        public const string Saved = "event-camera-highlight-saved";

        static string CameraOf(string index)
        {
            switch (index)
            {
                case "0": case "1": case "2": return "front";
                case "3": return "left_pillar";
                case "4": return "right_pillar";
                case "5": return "left_repeater";
                case "6": return "right_repeater";
                case "7": return "back";
                default: return null;
            }
        }

        /// <summary>The slot that glows and its class, or null.</summary>
        /// <param name="groupMetas">The collection's groups' event.json (null: not read).</param>
        /// <param name="byKey">eventMetaByKey.get("tag/eventId"), or null.</param>
        public static KeyValuePair<string, string>? Of(string tag, string eventId, IEnumerable<JsonValue> groupMetas, Func<string, JsonValue> byKey, bool sentryOn, bool savedOn, IList<MultiSlot> effective)
        {
            string t = JsText.Lower(tag ?? "");
            if (t != "savedclips" && t != "sentryclips") return null;
            bool sentry = t == "sentryclips";
            if (!(sentry ? sentryOn : savedOn)) return null;
            JsonValue meta = null;
            if (groupMetas != null)
                foreach (JsonValue m in groupMetas) if (JsJson.Truthy(m)) { meta = m; break; }
            if (meta == null && !string.IsNullOrEmpty(tag) && !string.IsNullOrEmpty(eventId) && byKey != null)
                meta = byKey(tag + "/" + eventId);
            JsonValue cam = JsJson.Get(meta, "camera");
            if (!JsJson.Truthy(cam) && !(cam != null && cam.IsNumber && cam.Num == 0)) return null;
            string name = CameraOf(StudioFrames.JsString(cam));
            if (name == null) return null;
            foreach (MultiSlot s in effective)
                if (s.Camera == name && (s.Raw == null || s.Raw.IsString))
                    return new KeyValuePair<string, string>(s.Slot, sentry ? Sentry : Saved);
            return null;
        }
    }

    /// <summary>
    /// What JavaScript does with the JSON these settings are kept in: reading
    /// a stored value (JSON.parse, the fallback {} when there is none or it is
    /// not JSON), property lookups on what it parsed, object spread and
    /// JSON.stringify (integer keys first, numbers as JavaScript prints them,
    /// undefined left out).
    /// </summary>
    public static class JsJson
    {
        /// <summary>undefined (a key that was given undefined: JSON.stringify leaves it out).</summary>
        public static readonly JsonValue Undefined = JsonValue.String("\u0000undefined");

        /// <summary>readJson(key, {}): the parsed value; {} when nothing is stored, it is not JSON, or it is null (where the web's script would stop).</summary>
        public static JsonValue ReadStored(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return JsonValue.Object();
            JsonValue v;
            try { v = JsonValue.Parse(stored); }
            catch (Exception) { return JsonValue.Object(); }
            return v == null || v.IsNull ? JsonValue.Object() : v;
        }

        /// <summary>A string value (not undefined).</summary>
        public static bool IsText(JsonValue v)
        {
            return v != null && v.IsString && !ReferenceEquals(v, Undefined);
        }

        public static bool Truthy(JsonValue v)
        {
            if (v == null || ReferenceEquals(v, Undefined)) return false;
            if (v.IsNumber) return v.Num != 0 && !double.IsNaN(v.Num);
            return v.Bool;
        }

        public static bool FiniteNumber(JsonValue v)
        {
            return v != null && v.IsNumber && !double.IsNaN(v.Num) && !double.IsInfinity(v.Num);
        }

        /// <summary>value[key] on what JSON.parse gave: an object's own property, an array's item; null (undefined) otherwise.</summary>
        public static JsonValue Get(JsonValue v, string key)
        {
            if (v == null || key == null) return null;
            if (v.IsObject) return v.Has(key) ? v[key] : null;
            if (v.IsArray)
            {
                int i;
                if (IsIndex(key, out i) && i < v.Count) return v[i];
                return null;
            }
            if (v.IsString && !ReferenceEquals(v, Undefined))
            {
                int i;
                if (IsIndex(key, out i) && i < v.Str.Length) return JsonValue.String(v.Str.Substring(i, 1));
            }
            return null;
        }

        static bool IsIndex(string key, out int i)
        {
            i = -1;
            if (key.Length == 0 || key.Length > 10) return false;
            if (key.Length > 1 && key[0] == '0') return false;
            foreach (char c in key) if (c < '0' || c > '9') return false;
            long l = long.Parse(key, CultureInfo.InvariantCulture);
            if (l >= 4294967295L || l > int.MaxValue) return false;
            i = (int)l;
            return true;
        }

        /// <summary>Object.entries(v): an object's keys in JavaScript's order, an array's or a string's indexes.</summary>
        public static List<KeyValuePair<string, JsonValue>> Entries(JsonValue v)
        {
            List<KeyValuePair<string, JsonValue>> r = new List<KeyValuePair<string, JsonValue>>();
            if (v == null || ReferenceEquals(v, Undefined)) return r;
            if (v.IsObject)
            {
                List<KeyValuePair<string, JsonValue>> ints = new List<KeyValuePair<string, JsonValue>>();
                foreach (KeyValuePair<string, JsonValue> kv in v.Props)
                {
                    int i;
                    if (IsIndex(kv.Key, out i)) ints.Add(kv);
                    else r.Add(kv);
                }
                ints.Sort((a, b) => long.Parse(a.Key, CultureInfo.InvariantCulture).CompareTo(long.Parse(b.Key, CultureInfo.InvariantCulture)));
                ints.AddRange(r);
                return ints;
            }
            if (v.IsArray)
            {
                for (int i = 0; i < v.Count; i++) r.Add(new KeyValuePair<string, JsonValue>(i.ToString(CultureInfo.InvariantCulture), v[i]));
                return r;
            }
            if (v.IsString)
                for (int i = 0; i < v.Str.Length; i++) r.Add(new KeyValuePair<string, JsonValue>(i.ToString(CultureInfo.InvariantCulture), JsonValue.String(v.Str.Substring(i, 1))));
            return r;
        }

        /// <summary>{ ...v }.</summary>
        public static JsonValue Spread(JsonValue v)
        {
            JsonValue o = JsonValue.Object();
            foreach (KeyValuePair<string, JsonValue> kv in Entries(v)) o = Set(o, kv.Key, kv.Value);
            return o;
        }

        /// <summary>o[key] = value (in place of an existing key, else last).</summary>
        public static JsonValue Set(JsonValue o, string key, JsonValue value)
        {
            return o.With(key, value);
        }

        /// <summary>delete o[key] on a copy.</summary>
        public static JsonValue Without(JsonValue o, string key)
        {
            JsonValue r = JsonValue.Object();
            foreach (KeyValuePair<string, JsonValue> kv in o.Props) if (kv.Key != key) r = r.With(kv.Key, kv.Value);
            return r;
        }

        /// <summary>JSON.stringify(v).</summary>
        public static string Stringify(JsonValue v)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, v);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, JsonValue v)
        {
            if (v == null || ReferenceEquals(v, Undefined) || v.IsNull) { sb.Append("null"); return; }
            switch (v.Kind)
            {
                case JsonKind.Bool: sb.Append(v.Bool ? "true" : "false"); return;
                case JsonKind.Number:
                    if (double.IsNaN(v.Num) || double.IsInfinity(v.Num)) sb.Append("null");
                    else sb.Append(Format.JsNum(v.Num));
                    return;
                case JsonKind.String: Quote(sb, v.Str); return;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < v.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Write(sb, v[i]);
                    }
                    sb.Append(']');
                    return;
                default:
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, JsonValue> kv in Entries(v))
                    {
                        if (ReferenceEquals(kv.Value, Undefined)) continue;
                        if (!first) sb.Append(',');
                        first = false;
                        Quote(sb, kv.Key);
                        sb.Append(':');
                        Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    return;
            }
        }

        /// <summary>JSON.stringify's QuoteJSONString (lone surrogates as \u escapes).</summary>
        static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); continue;
                    case '\\': sb.Append("\\\\"); continue;
                    case '\b': sb.Append("\\b"); continue;
                    case '\f': sb.Append("\\f"); continue;
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\t': sb.Append("\\t"); continue;
                }
                if (c < 0x20) { sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); continue; }
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[i + 1]); i++; continue; }
                if (char.IsSurrogate(c)) { sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); continue; }
                sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
