using System;
using System.Collections.Generic;
using System.Globalization;

namespace SentrySix.Core
{
    /// <summary>
    /// The dashboard's own Studio settings (GET /api/studio/settings, as
    /// script.js and settingsModal.js read them with getSetting).
    /// </summary>
    public sealed class DashboardSettings
    {
        /// <summary>state.ui.dashboardEnabled: getSetting('dashboardEnabled'), on unless saved as something other than true.</summary>
        public bool Enabled = true;
        /// <summary>state.ui.mapEnabled (updateVisualization does nothing while both overlays are off).</summary>
        public bool MapEnabled = true;
        /// <summary>useMetric: getSetting('useMetric') === true.</summary>
        public bool Metric;
        /// <summary>window.dashboardLayout: 'default' or 'compact' (getSetting('dashboardLayout') || 'default').</summary>
        public string Layout = "default";
        /// <summary>compactDashboardFixed: the compact dashboard sits on the front camera (getSetting(...) ?? true).</summary>
        public bool CompactFixed = true;
        /// <summary>window.updateAccelPedMode: 'solid', 'iconbar' or 'sidebar' (getSetting('accelPedMode') || 'iconbar').</summary>
        public string AccelMode = "iconbar";
        /// <summary>
        /// window._timeFormat || '12h': settingsModal.js sets it from
        /// #settingsTimeFormat, which Studio's index.html does not have, so
        /// the recording time is always 12 h there.
        /// </summary>
        public string TimeFormat = "12h";

        public const string DefaultLayout = "default";
        public const string CompactLayout = "compact";

        public bool Compact { get { return Layout == CompactLayout; } }

        /// <summary>
        /// updateDashboardVisibility: the floating dashboard is user-hidden
        /// in the compact layout or while the dashboard is off (or the
        /// profile has none: library.capabilities.dashboard false).
        /// </summary>
        public bool FloatingHidden(bool capability)
        {
            return Compact || !(Enabled && capability);
        }

        /// <summary>updateDashboardVisibility: the compact dashboard is shown (.visible) in its layout while the dashboard is on.</summary>
        public bool CompactShown(bool capability)
        {
            return Compact && Enabled && capability;
        }

        /// <summary>The settings as the Viewer reads them once its settings are in ({} when they could not be read).</summary>
        public static DashboardSettings From(JsonValue all)
        {
            DashboardSettings s = new DashboardSettings();
            JsonValue v = JsJson.Get(all, "dashboardEnabled");
            s.Enabled = v == null || (v.IsBool && v.Bool);
            v = JsJson.Get(all, "mapEnabled");
            s.MapEnabled = v == null || (v.IsBool && v.Bool);
            v = JsJson.Get(all, "useMetric");
            s.Metric = v != null && v.IsBool && v.Bool;
            v = JsJson.Get(all, "dashboardLayout");
            s.Layout = JsJson.Truthy(v) ? JsText(v) : DefaultLayout;
            v = JsJson.Get(all, "compactDashboardFixed");
            s.CompactFixed = v == null || v.IsNull || JsJson.Truthy(v);
            v = JsJson.Get(all, "accelPedMode");
            s.AccelMode = JsJson.Truthy(v) ? JsText(v) : "iconbar";
            return s;
        }

        static string JsText(JsonValue v)
        {
            return v.IsString || v.IsNumber || v.IsBool ? v.Str : "[object Object]";
        }
    }

    /// <summary>
    /// What the floating (#dashboardVis) and compact (#dashboardVisCompact)
    /// dashboards show, kept as script.js's updateVisualization,
    /// resetDashboardElements, setDashboardParked, steeringWheel.js,
    /// gforceMeter.js, compass.js and timeDisplay.js's updateRecordingTime
    /// leave the elements: their texts, classes and positions. The pages
    /// draw it; Core.Tests compare it with the web's elements.
    /// </summary>
    public sealed class DashboardView
    {
        public const double MpsToMph = 2.23694;
        public const double MpsToKmh = 3.6;

        // ---- texts (index.html's first values until the first update or reset)
        public string Speed = "0";
        public string Unit = "MPH";
        public string SpeedCompact = "0";
        public string UnitCompact = "MPH";
        public string Gear = "--";
        public string GearCompact = "--";
        public string Ap = "Manual";
        public string ApCompact = "Manual";
        public string Seq = "--";
        public string Lat = "--";
        public string Lon = "--";
        public string Heading = "--";
        public string RecordingTime = "--:--";
        public string GForceX = "0.0";
        public string GForceY = "0.0";
        public string Compass = "0°";

        // ---- classes
        public bool GearActive;
        public bool ApActive;
        public bool AutosteerActive;
        public bool BrakeActive;
        public bool AccelActive;
        public bool BlinkLeft, BlinkRight;
        public bool BlinkLeftPaused, BlinkRightPaused;
        /// <summary>The blink animation started again (a blinker came on): counts up each time.</summary>
        public int BlinkLeftRestarts, BlinkRightRestarts;
        /// <summary>The accelerator fill's clip-path inset from the top in percent (100: nothing filled); null while it was never set.</summary>
        public double? AccelTopInset;
        /// <summary>.telemetry-parked on both dashboards (PARKED replaces the compact one's gauges); null until setDashboardParked is first called.</summary>
        public bool Parked;
        /// <summary>.extra-data-container.expanded (the details under the instruments).</summary>
        public bool ExtraExpanded;
        /// <summary>gforceDot's class: null, "braking", "accelerating" or "cornering-hard".</summary>
        public string GForceDotClass;
        /// <summary>gforceX / gforceY's class: null, "positive", "negative" or "high".</summary>
        public string GForceXClass, GForceYClass;

        // ---- positions
        /// <summary>gforceDot's cx, cy and the three trail dots' (the SVG's 60 x 60 view box).</summary>
        public double DotX = 30, DotY = 30;
        public readonly double[] TrailX = { 30, 30, 30 };
        public readonly double[] TrailY = { 30, 30, 30 };
        /// <summary>compassNeedle's rotate(heading 30 30).</summary>
        public double Needle;
        /// <summary>The steering wheel (#steeringIcon, #steeringIconCompact) as drawn: rotate(angle deg).</summary>
        public double Steering;

        readonly Dictionary<string, string> dirty = new Dictionary<string, string>(StringComparer.Ordinal);
        bool prevLeft, prevRight;
        bool? lastParked;
        readonly List<double[]> history = new List<double[]>();
        string lastRenderKey;
        double? lastHeading;
        readonly SteeringWheel wheel = new SteeringWheel();

        /// <summary>steeringWheel.js's exponential tracking (the dashboards' wheel follows <see cref="SteeringWheel.Target"/>).</summary>
        public SteeringWheel Wheel { get { return wheel; } }

        /// <summary>telemetryDirty.changed(key, value): false when the value is the one last written.</summary>
        bool Changed(string key, string value)
        {
            string old;
            if (dirty.TryGetValue(key, out old) && old == value) return false;
            dirty[key] = value;
            return true;
        }

        /// <summary>telemetryDirty.reset() (a setting, the details toggle, the language changed).</summary>
        public void ResetDirty()
        {
            dirty.Clear();
        }

        /// <summary>
        /// updateVisualization(sei): what the record changes on the
        /// dashboards (each part only when its value changed since it was last
        /// written). Nothing without a record, for a profile without
        /// telemetry, or while the dashboard and the map are both off.
        /// </summary>
        public void Update(Sei sei, DashboardSettings s, bool playing, bool telemetry)
        {
            if (sei == null || !telemetry) return;
            bool dashOn = s.Enabled, mapOn = s.MapEnabled;
            if (!dashOn && !mapOn) return;
            double lat = Or0(sei.LatitudeDeg), lon = Or0(sei.LongitudeDeg), heading = Or0(sei.HeadingDeg);
            if (!dashOn) return;

            double mps = Math.Abs(Or0(sei.VehicleSpeedMps));
            double speed = s.Metric ? Format.Round(mps * MpsToKmh) : Format.Round(mps * MpsToMph);
            string unit = s.Metric ? "KM/H" : "MPH";
            if (Changed("dashboard.speed", Format.JsNum(speed) + "|" + unit))
            {
                Speed = SpeedCompact = Format.JsNum(speed);
                Unit = UnitCompact = unit;
            }

            int gear = sei.GearState;
            if (Changed("dashboard.gear", gear.ToString(CultureInfo.InvariantCulture)))
            {
                Gear = GearCompact = GearText(gear);
            }

            bool left = sei.BlinkerOnLeft, right = sei.BlinkerOnRight;
            if (Changed("dashboard.blinkers", (left ? "true" : "false") + "|" + (right ? "true" : "false") + "|" + (playing ? "true" : "false")))
            {
                if (left && !prevLeft) BlinkLeftRestarts++;
                if (right && !prevRight) BlinkRightRestarts++;
                prevLeft = left;
                prevRight = right;
                BlinkLeft = left;
                BlinkRight = right;
                BlinkLeftPaused = BlinkRightPaused = !playing;
            }

            double target = Or0(sei.SteeringWheelAngle);
            if (Changed("dashboard.steering", Format.JsNum(target)))
            {
                wheel.SmoothTo(target);
            }

            int ap = sei.AutopilotState;
            if (Changed("dashboard.autopilot", ap.ToString(CultureInfo.InvariantCulture)))
            {
                bool active = ap == 1 || ap == 2;
                AutosteerActive = ApActive = GearActive = active;
                Ap = ApCompact = ApText(ap);
            }

            bool brake = sei.BrakeApplied;
            if (Changed("dashboard.brake", brake ? "true" : "false"))
            {
                BrakeActive = brake;
            }

            double raw = Or0(sei.AcceleratorPedalPosition);
            double pct = AccelPercent(raw);
            bool pressed = pct > 5;
            double top = 100 - pct;
            if (Changed("dashboard.accelerator", (pressed ? "true" : "false") + "|" + Format.JsNum(top)))
            {
                AccelActive = pressed;
                AccelTopInset = top;
            }

            string seq = Format.JsNum(sei.FrameSeqNo);
            if (ExtraExpanded && Changed("dashboard.extra", seq + "|" + Format.JsNum(lat) + "|" + Format.JsNum(lon) + "|" + Format.JsNum(heading)))
            {
                Seq = seq;
                Lat = Fixed(lat, 6);
                Lon = Fixed(lon, 6);
                Heading = Fixed(heading, 1) + "°";
            }

            UpdateGForce(sei);
            UpdateCompass(sei);
        }

        /// <summary>Number(x || 0): NaN (and -0) read as 0.</summary>
        static double Or0(double x)
        {
            return double.IsNaN(x) || x == 0 ? 0 : x;
        }

        /// <summary>The gear's word: Park, Drive, Reverse, Neutral, else "--" (the pages show it in capitals).</summary>
        public static string GearText(int gear)
        {
            switch (gear)
            {
                case 0: return "Park";
                case 1: return "Drive";
                case 2: return "Reverse";
                case 3: return "Neutral";
                default: return "--";
            }
        }

        /// <summary>The Autopilot label (ui.dashboard.*): Self Driving, Autosteer, TACC, else Manual.</summary>
        public static string ApText(int ap)
        {
            if (ap == 1) return "Self Driving";
            if (ap == 2) return "Autosteer";
            if (ap == 3) return "TACC";
            return "Manual";
        }

        /// <summary>The pedal in percent: a value over 1 is a percent already, else a fraction; at most 100.</summary>
        public static double AccelPercent(double raw)
        {
            return raw > 1 ? Math.Min(100, raw) : Math.Min(100, raw * 100);
        }

        /// <summary>resetDashboardElements(): every gauge back to its empty state ("--", "No Data", the wheel straight).</summary>
        public void Reset(bool metric)
        {
            dirty.Clear();
            prevLeft = prevRight = false;
            Speed = SpeedCompact = "--";
            Unit = UnitCompact = metric ? "KM/H" : "MPH";
            Gear = GearCompact = "--";
            GearActive = false;
            BlinkLeft = BlinkRight = BlinkLeftPaused = BlinkRightPaused = false;
            wheel.Reset();
            AutosteerActive = false;
            Ap = "No Data";
            ApActive = false;
            BrakeActive = false;
            AccelActive = false;
            ApCompact = "Manual";
            Seq = Lat = Lon = Heading = "--";
            ResetGForce();
            ResetCompass();
        }

        /// <summary>setDashboardParked(parked) (idempotent; resetDashboardParkedState forgets the last value).</summary>
        public void SetParked(bool parked)
        {
            if (lastParked.HasValue && lastParked.Value == parked) return;
            lastParked = parked;
            Parked = parked;
        }

        /// <summary>resetDashboardAndMap's dashboard part: the parked state forgotten and cleared, the gauges reset.</summary>
        public void ResetAll(bool metric)
        {
            lastParked = null;
            SetParked(false);
            Reset(metric);
        }

        /// <summary>A telemetry tick's outcome (telemetryAnimationLoop / onMasterTimeUpdate): PARKED, the record, the reset.</summary>
        public void Apply(TelemetryStep step, DashboardSettings s, bool playing, bool telemetry)
        {
            if (step == null || !step.Ran) return;
            if (step.Parked.HasValue) SetParked(step.Parked.Value);
            if (step.Sei != null) Update(step.Sei, s, playing, telemetry);
            if (step.Reset) Reset(s.Metric);
        }

        /// <summary>#toggleExtra: the details open or close (the dirty values forgotten, so the next record fills them).</summary>
        public void ToggleExtra()
        {
            ExtraExpanded = !ExtraExpanded;
            dirty.Clear();
        }

        /// <summary>The metric toggle: the units on both dashboards at once.</summary>
        public void SetMetric(bool metric)
        {
            dirty.Clear();
            Unit = UnitCompact = metric ? "KM/H" : "MPH";
        }

        // ---- gforceMeter.js

        public const double Gravity = 9.81;
        public const double GForceScale = 25;
        const int HistoryMax = 3;

        void UpdateGForce(Sei sei)
        {
            double gX = sei.LinearAccelerationMps2X / Gravity;
            double gY = sei.LinearAccelerationMps2Y / Gravity;
            double cx = Math.Max(-2, Math.Min(2, gX));
            double cy = Math.Max(-2, Math.Min(2, gY));
            double dotX = 30 + (cx * GForceScale);
            double dotY = 30 - (cy * GForceScale);
            string key = Format.ToFixed(dotX, 1) + "," + Format.ToFixed(dotY, 1);
            bool settled = history.Count >= HistoryMax;
            if (settled) foreach (double[] p in history) if (p[0] != dotX || p[1] != dotY) { settled = false; break; }
            if (key == lastRenderKey && settled) return;
            lastRenderKey = key;
            history.Insert(0, new[] { dotX, dotY });
            if (history.Count > HistoryMax) history.RemoveAt(history.Count - 1);
            DotX = dotX;
            DotY = dotY;
            for (int i = 0; i < HistoryMax; i++)
            {
                if (history.Count <= i) continue;
                TrailX[i] = history[i][0] != 0 && !double.IsNaN(history[i][0]) ? history[i][0] : 30;
                TrailY[i] = history[i][1] != 0 && !double.IsNaN(history[i][1]) ? history[i][1] : 30;
            }
            if (gY < -0.3) GForceDotClass = "braking";
            else if (gY > 0.3) GForceDotClass = "accelerating";
            else if (Math.Abs(gX) > 0.5) GForceDotClass = "cornering-hard";
            else GForceDotClass = null;
            GForceX = Signed(gX);
            GForceXClass = ValueClass(gX);
            GForceY = Signed(gY);
            GForceYClass = ValueClass(gY);
        }

        static string Signed(double g)
        {
            return (g >= 0 ? "+" : "") + Format.ToFixed(g, 1);
        }

        static string ValueClass(double g)
        {
            if (Math.Abs(g) > 0.8) return "high";
            if (g > 0.2) return "positive";
            if (g < -0.2) return "negative";
            return null;
        }

        void ResetGForce()
        {
            lastRenderKey = null;
            DotX = DotY = 30;
            GForceDotClass = null;
            for (int i = 0; i < HistoryMax; i++) { TrailX[i] = 30; TrailY[i] = 30; }
            history.Clear();
            GForceX = GForceY = "0.0";
            GForceXClass = GForceYClass = null;
        }

        // ---- compass.js

        static readonly string[] Cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        void UpdateCompass(Sei sei)
        {
            double heading = sei.HeadingDeg;
            if (double.IsNaN(heading) || double.IsInfinity(heading)) heading = 0;
            heading = ((heading % 360) + 360) % 360;
            double rounded = Format.Round(heading * 10) / 10;
            if (lastHeading.HasValue && lastHeading.Value == rounded) return;
            lastHeading = rounded;
            Needle = heading;
            int index = (int)(Format.Round(heading / 45) % 8);
            Compass = Format.JsNum(Format.Round(heading)) + "° " + Cardinals[index];
        }

        void ResetCompass()
        {
            lastHeading = null;
            Needle = 0;
            Compass = "--";
        }

        // ---- timeDisplay.js updateRecordingTime

        /// <summary>
        /// updateRecordingTime: the wall clock of the playing moment, from the
        /// segment's first clip name (YYYY-MM-DD_HH-MM-SS) plus the whole
        /// seconds into it, wrapped at 24 h, as 12 h ("h:mm:ss AM") or 24 h
        /// ("HH:mm:ss"); unchanged (null) when the name has no time.
        /// </summary>
        public static string RecordingTimeOf(string fileName, double videoSeconds, string timeFormat)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d{4})-(\d{2})-(\d{2})_(\d{2})-(\d{2})-(\d{2})");
            if (!m.Success) return null;
            double vid = Math.Floor(double.IsNaN(videoSeconds) || videoSeconds == 0 ? 0 : videoSeconds);
            double total = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) * 3600 + int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture) + vid;
            total = total % 86400;
            int h = (int)Math.Floor(total / 3600);
            int mi = (int)Math.Floor((total % 3600) / 60);
            int s = (int)(total % 60);
            string mm = mi.ToString("00", CultureInfo.InvariantCulture), ss = s.ToString("00", CultureInfo.InvariantCulture);
            if (timeFormat == "24h") return h.ToString("00", CultureInfo.InvariantCulture) + ":" + mm + ":" + ss;
            string ampm = h >= 12 ? "PM" : "AM";
            int h12 = h % 12;
            if (h12 == 0) h12 = 12;
            return h12.ToString(CultureInfo.InvariantCulture) + ":" + mm + ":" + ss + " " + ampm;
        }

        /// <summary>The recording time for the segment's clip at the master's time (kept when there is none).</summary>
        public void UpdateRecordingTime(string fileName, double videoSeconds, string timeFormat)
        {
            string t = RecordingTimeOf(fileName, videoSeconds, timeFormat);
            if (t != null) RecordingTime = t;
        }

        // ---- numbers

        /// <summary>Number#toFixed(digits) for 0..6 digits (lat / lon use 6) from the double's exact value.</summary>
        public static string Fixed(double x, int digits)
        {
            if (digits <= 3) return Format.ToFixed(x, digits);
            if (double.IsNaN(x)) return "NaN";
            if (Math.Abs(x) >= 1e15) return Format.JsNum(x);
            string sign = "";
            if (x < 0) { sign = "-"; x = -x; }
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else mant |= 1L << 52;
            int e = exp - 1075;
            decimal scale = 1;
            for (int i = 0; i < digits; i++) scale *= 10;
            decimal m = mant * scale;
            decimal n;
            if (x == 0) n = 0;
            else if (e >= 0)
            {
                n = m;
                for (int i = 0; i < e; i++) n *= 2;
            }
            else
            {
                // x · 10^digits = m / 2^-e, rounded half up (exact at a tie: a power of two divides exactly)
                decimal div = 1;
                for (int i = 0; i < -e && i < 96; i++) div *= 2;
                decimal q = m / div;
                n = Math.Floor(q);
                if (q - n >= 0.5m) n += 1;
            }
            string s = n.ToString("0", CultureInfo.InvariantCulture);
            if (s.Length <= digits) s = new string('0', digits - s.Length + 1) + s;
            return sign + s.Substring(0, s.Length - digits) + "." + s.Substring(s.Length - digits);
        }
    }

    /// <summary>
    /// steeringWheel.js: the wheel follows its target with
    /// frame-rate-independent exponential tracking (45 per second, times the
    /// playback rate), written as rotate(angle) to a tenth of a degree, and
    /// stops once within 0.05 degrees (or when the video pauses).
    /// </summary>
    public sealed class SteeringWheel
    {
        public const double TrackingSpeed = 45;

        static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        double position, target, lastMs;
        double? lastApplied;

        /// <summary>performance.now() in ms (the tests set their own clock).</summary>
        public Func<double> Now = () => Clock.Elapsed.TotalMilliseconds;

        /// <summary>The animation runs (smoothSteeringTo started it; settling, stop or reset end it).</summary>
        public bool Running { get; private set; }
        public double Position { get { return position; } }
        public double Target { get { return target; } }
        /// <summary>The angle last written to the icons (Math.round(angle x 10) / 10; 0 after a reset).</summary>
        public double Shown { get { return lastApplied ?? 0; } }
        /// <summary>The icons' style.transform: null until first written, "rotate(0deg)" after a reset.</summary>
        public string Transform { get; private set; }

        /// <summary>smoothSteeringTo(angle): the new target; the animation starts if it is not running (its clock from now).</summary>
        public void SmoothTo(double angle)
        {
            target = angle;
            if (Running) return;
            Running = true;
            lastMs = Now();
        }

        /// <summary>One animation frame (requestAnimationFrame) at the playback rate.</summary>
        public void Frame(double rate)
        {
            if (!Running) return;
            double now = Now();
            double dt = Math.Min((now - lastMs) / 1000, 0.1);
            lastMs = now;
            double factor = 1 - Math.Exp(-TrackingSpeed * rate * dt);
            position += (target - position) * factor;
            Apply(position);
            if (Math.Abs(target - position) < 0.05)
            {
                position = target;
                Apply(position);
                Running = false;
            }
        }

        void Apply(double angle)
        {
            double rounded = Format.Round(angle * 10) / 10;
            if (lastApplied.HasValue && rounded == lastApplied.Value) return;
            lastApplied = rounded;
            Transform = "rotate(" + Format.JsNum(rounded) + "deg)";
        }

        /// <summary>stopSteeringAnimation (the video paused): the wheel stays where it is.</summary>
        public void Stop()
        {
            Running = false;
        }

        /// <summary>resetSteeringWheel: straight, nothing to follow.</summary>
        public void Reset()
        {
            Running = false;
            position = 0;
            target = 0;
            lastApplied = null;
            Transform = "rotate(0deg)";
        }

        /// <summary>The render test mode: the animation run to its end (the web's picture is taken long after).</summary>
        public void Settle()
        {
            if (!Running) return;
            position = target;
            Apply(position);
            Running = false;
        }
    }

    /// <summary>
    /// The blinkers' CSS animation (blink-signal 0.7 s linear infinite: lit
    /// for the first 57.14 %, then 0.15), started again when a blinker comes
    /// on and held while the video is paused (animation-play-state: paused).
    /// </summary>
    public sealed class BlinkClock
    {
        public const double PeriodMs = 700;
        public const double LitShare = 0.5714;

        double phase;
        int restarts = -1;

        /// <summary>The time since the last frame (ms), counted only while not paused; a new restart count starts the cycle again.</summary>
        public void Frame(double dtMs, bool paused, int restartCount)
        {
            if (restartCount != restarts) { restarts = restartCount; phase = 0; }
            else if (!paused) phase = (phase + Math.Max(0, dtMs)) % PeriodMs;
        }

        /// <summary>The blinker's opacity now: 1 while lit, else 0.15.</summary>
        public double Opacity { get { return phase / PeriodMs <= LitShare ? 1 : 0.15; } }
    }
}
