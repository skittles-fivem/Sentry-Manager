using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SentrySix.Core
{
    /// <summary>
    /// JavaScript's string rules where the Viewer's scan relies on them:
    /// String.prototype.toLowerCase (full mapping: U+0130 becomes "i" and a
    /// combining dot, a final capital sigma a final small sigma), the default
    /// Array sort of strings (UTF-16 code units) and a stable sort.
    /// </summary>
    public static class JsText
    {
        /// <summary>s.toLowerCase() (throws on null, as calling it on undefined does).</summary>
        public static string Lower(string s)
        {
            if (s == null) throw new NullReferenceException("Cannot read properties of undefined (reading 'toLowerCase')");
            StringBuilder b = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                string r = null;
                if (c == '\u0130') r = "i\u0307";
                else if (c == '\u03A3') r = FinalSigma(s, i) ? "\u03C2" : "\u03C3";
                else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    string pair = s.Substring(i, 2);
                    string low = pair.ToLowerInvariant();
                    if (b == null && low != pair) { b = new StringBuilder(s.Length); b.Append(s, 0, i); }
                    if (b != null) b.Append(low);
                    i++;
                    continue;
                }
                else
                {
                    char l = char.ToLowerInvariant(c);
                    if (l != c) r = l.ToString();
                }
                if (r == null) { if (b != null) b.Append(c); continue; }
                if (b == null) { b = new StringBuilder(s.Length + 4); b.Append(s, 0, i); }
                b.Append(r);
            }
            return b == null ? s : b.ToString();
        }

        // Unicode's Final_Sigma: a cased letter before it, none after it (case-ignorable marks skipped)
        static bool FinalSigma(string s, int at)
        {
            bool before = false;
            for (int i = at - 1; i >= 0; i--)
            {
                if (Ignorable(s[i])) continue;
                before = Cased(s[i]);
                break;
            }
            if (!before) return false;
            for (int i = at + 1; i < s.Length; i++)
            {
                if (Ignorable(s[i])) continue;
                return !Cased(s[i]);
            }
            return true;
        }

        static bool Cased(char c)
        {
            UnicodeCategory k = char.GetUnicodeCategory(c);
            return k == UnicodeCategory.UppercaseLetter || k == UnicodeCategory.LowercaseLetter || k == UnicodeCategory.TitlecaseLetter;
        }

        static bool Ignorable(char c)
        {
            UnicodeCategory k = char.GetUnicodeCategory(c);
            return k == UnicodeCategory.NonSpacingMark || k == UnicodeCategory.EnclosingMark || k == UnicodeCategory.Format
                || k == UnicodeCategory.ModifierLetter || k == UnicodeCategory.ModifierSymbol
                || c == '\'' || c == '.' || c == ':' || c == '\u00B7' || c == '\u2019';
        }

        /// <summary>A stable sort (Array.prototype.sort is stable; List.Sort is not).</summary>
        public static List<T> StableSort<T>(IList<T> items, Comparison<T> compare)
        {
            List<T> list = new List<T>(items);
            // insertion into runs merged pairwise: a plain merge sort
            if (list.Count < 2) return list;
            T[] a = list.ToArray();
            T[] tmp = new T[a.Length];
            for (int width = 1; width < a.Length; width *= 2)
            {
                for (int lo = 0; lo < a.Length; lo += 2 * width)
                {
                    int mid = Math.Min(lo + width, a.Length), hi = Math.Min(lo + 2 * width, a.Length);
                    int i = lo, j = mid, k = lo;
                    while (i < mid && j < hi) tmp[k++] = compare(a[j], a[i]) < 0 ? a[j++] : a[i++];
                    while (i < mid) tmp[k++] = a[i++];
                    while (j < hi) tmp[k++] = a[j++];
                }
                T[] swap = a; a = tmp; tmp = swap;
            }
            return new List<T>(a);
        }

        /// <summary>The default sort of strings (code units), then reversed: Array.from(set).sort().reverse().</summary>
        public static List<string> SortDescending(IEnumerable<string> items)
        {
            List<string> list = StableSort(new List<string>(items), string.CompareOrdinal);
            list.Reverse();
            return list;
        }

        // CLDR root collation of ASCII (Chrome's localeCompare): the controls but tab to CR are
        // ignored; then white space, punctuation and symbols in this order, digits, letters
        const string Primary = "\t\n\u000B\f\r _-,;:!?.'\"()[]{}@*/\\&#%`^+<=>|~$0123456789abcdefghijklmnopqrstuvwxyz";

        static int Weight(char c)
        {
            if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
            return Primary.IndexOf(c);
        }

        /// <summary>
        /// a.localeCompare(b) as Chrome orders ASCII text (CLDR root: letters
        /// alike in either case first, then lower case before upper case; the
        /// other controls ignored). Text with other characters is compared by
        /// .NET's invariant culture (not the same as ICU's for every script).
        /// </summary>
        public static int LocaleCompare(string a, string b)
        {
            a = a ?? "undefined";
            b = b ?? "undefined";
            foreach (char c in a) if (c > 0x7F) return Math.Sign(string.Compare(a, b, StringComparison.InvariantCulture));
            foreach (char c in b) if (c > 0x7F) return Math.Sign(string.Compare(a, b, StringComparison.InvariantCulture));
            List<int> pa = new List<int>(), pb = new List<int>();
            List<bool> ua = new List<bool>(), ub = new List<bool>();
            foreach (char c in a) { int w = Weight(c); if (w >= 0) { pa.Add(w); ua.Add(c >= 'A' && c <= 'Z'); } }
            foreach (char c in b) { int w = Weight(c); if (w >= 0) { pb.Add(w); ub.Add(c >= 'A' && c <= 'Z'); } }
            for (int i = 0; i < Math.Min(pa.Count, pb.Count); i++) if (pa[i] != pb[i]) return pa[i] < pb[i] ? -1 : 1;
            if (pa.Count != pb.Count) return pa.Count < pb.Count ? -1 : 1;
            for (int i = 0; i < ua.Count; i++) if (ua[i] != ub[i]) return ua[i] ? 1 : -1;
            return 0;
        }

        /// <summary>encodeURIComponent(s): UTF-8, A-Z a-z 0-9 - _ . ! ~ * ' ( ) kept (a lone surrogate throws URIError).</summary>
        public static string EncodeUriComponent(string s)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || "-_.!~*'()".IndexOf(c) >= 0) { b.Append(c); continue; }
                int cp = c;
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) throw new FormatException("URI malformed");
                    cp = char.ConvertToUtf32(c, s[i + 1]);
                    i++;
                }
                else if (char.IsLowSurrogate(c)) throw new FormatException("URI malformed");
                foreach (byte x in Encoding.UTF8.GetBytes(char.ConvertFromUtf32(cp))) b.Append('%').Append(x.ToString("X2", CultureInfo.InvariantCulture));
            }
            return b.ToString();
        }

        /// <summary>ASCII digits at s[from .. from + n).</summary>
        public static bool Digits(string s, int from, int n)
        {
            if (from < 0 || from + n > s.Length) return false;
            for (int i = from; i < from + n; i++) if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }

        /// <summary>/^\d{4}-\d{2}-\d{2}$/ (a day folder's name).</summary>
        public static bool IsDay(string s)
        {
            return s != null && s.Length == 10 && DayAt(s, 0);
        }

        /// <summary>/^(\d{4}-\d{2}-\d{2})_/ : the day it starts with, else null.</summary>
        public static string DayPrefix(string s)
        {
            if (s == null || s.Length < 11 || !DayAt(s, 0) || s[10] != '_') return null;
            return s.Substring(0, 10);
        }

        /// <summary>/(\d{4})(\d{2})(\d{2})/ : the first eight digits in a row, as YYYY-MM-DD, else null.</summary>
        public static string CompactDay(string s)
        {
            if (s == null) return null;
            for (int i = 0; i + 8 <= s.Length; i++)
                if (Digits(s, i, 8)) return s.Substring(i, 4) + "-" + s.Substring(i + 4, 2) + "-" + s.Substring(i + 6, 2);
            return null;
        }

        static bool DayAt(string s, int i)
        {
            return Digits(s, i, 4) && s[i + 4] == '-' && Digits(s, i + 5, 2) && s[i + 7] == '-' && Digits(s, i + 8, 2);
        }

        /// <summary>A line terminator, which a regular expression's . does not match.</summary>
        public static bool LineBreak(char c)
        {
            return c == '\n' || c == '\r' || c == (char)0x2028 || c == (char)0x2029;
        }

        /// <summary>ASCII-only case-insensitive equality (a /i regular expression on ASCII letters).</summary>
        public static bool AsciiEq(string s, int from, string ascii)
        {
            if (from < 0 || from + ascii.Length > s.Length) return false;
            for (int i = 0; i < ascii.Length; i++)
            {
                char a = s[from + i], b = ascii[i];
                if (a >= 'A' && a <= 'Z') a = (char)(a + 32);
                if (b >= 'A' && b <= 'Z') b = (char)(b + 32);
                if (a != b) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// JavaScript's Date where the Viewer's scan uses it: new Date(y, m, d,
    /// h, mi, s) in the PC's time zone (months and days roll over, years 0
    /// to 99 are 1900 to 1999; a time skipped or repeated by a clock change
    /// takes the offset from before the change, as the language says) and
    /// Date.parse of event.json's timestamp (the ISO form; without a zone a
    /// date-time is local, a date alone UTC).
    /// </summary>
    public static class JsDate
    {
        /// <summary>new Date(y, m, d, h, mi, s, 0).getTime() in that zone (NaN out of range).</summary>
        public static double Local(double y, double m, double d, double h, double mi, double s, TimeZoneInfo zone)
        {
            if (y >= 0 && y <= 99) y += 1900;
            double local = MakeDate(y, m, d, h, mi, s, 0);
            return FromLocal(local, zone);
        }

        /// <summary>A local time (ms since 1970 on the wall clock) to UTC ms in that zone.</summary>
        public static double FromLocal(double local, TimeZoneInfo zone)
        {
            if (double.IsNaN(local) || Math.Abs(local) > 8.64e15) return double.NaN;
            zone = zone ?? TimeZoneInfo.Local;
            DateTime wall;
            try
            {
                wall = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMilliseconds(local);
            }
            catch (ArgumentOutOfRangeException)
            {
                return local - zone.BaseUtcOffset.TotalMilliseconds;
            }
            TimeSpan offset;
            if (zone.IsAmbiguousTime(wall) || zone.IsInvalidTime(wall))
            {
                // the offset in force before the change (clock changes are months apart)
                DateTime before = wall.Ticks > TimeSpan.TicksPerDay ? wall.AddDays(-1) : wall;
                offset = zone.GetUtcOffset(before);
            }
            else offset = zone.GetUtcOffset(wall);
            double t = local - offset.TotalMilliseconds;
            return Math.Abs(t) > 8.64e15 ? double.NaN : t;
        }

        /// <summary>MakeDate(MakeDay(y, m, d), MakeTime(h, mi, s, ms)) on the UTC time line.</summary>
        public static double MakeDate(double y, double m, double d, double h, double mi, double s, double ms)
        {
            if (double.IsNaN(y) || double.IsNaN(m) || double.IsNaN(d)) return double.NaN;
            double ym = y + Math.Floor(m / 12);
            double mn = m - Math.Floor(m / 12) * 12;
            if (Math.Abs(ym) > 400000) return double.NaN;
            double days = DaysFromCivil((long)ym, (int)mn + 1, 1) + Math.Truncate(d) - 1;
            return days * 86400000.0 + Math.Truncate(h) * 3600000.0 + Math.Truncate(mi) * 60000.0 + Math.Truncate(s) * 1000.0 + Math.Truncate(ms);
        }

        // days from 1970-01-01 to y-m-d (proleptic Gregorian)
        static long DaysFromCivil(long y, int m, int d)
        {
            y -= m <= 2 ? 1 : 0;
            long era = (y >= 0 ? y : y - 399) / 400;
            long yoe = y - era * 400;
            long doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
            long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
            return era * 146097 + doe - 719468;
        }

        /// <summary>
        /// Date.parse(s): the date time string format (YYYY, YYYY-MM,
        /// YYYY-MM-DD, then THH:mm, :ss, .sss, Z or ±HH:mm); a date alone is
        /// UTC, a date-time without a zone local. Other text is read the way
        /// .NET reads it (the browsers' own fallbacks differ among themselves).
        /// </summary>
        public static double Parse(string s, TimeZoneInfo zone)
        {
            if (s == null) return double.NaN;
            double t;
            if (TryIso(s, zone, out t)) return t;
            if (IsoShaped(s)) return double.NaN;
            DateTimeOffset o;
            string trimmed = s.Trim();
            if (trimmed.Length == 0) return double.NaN;
            bool zoned = trimmed.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || trimmed.IndexOf("GMT", StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.IndexOf("UTC", StringComparison.OrdinalIgnoreCase) >= 0 || HasOffset(trimmed);
            DateTime dt;
            if (!zoned && DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return FromLocal((dt - new DateTime(1970, 1, 1)).TotalMilliseconds, zone);
            if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out o))
                return (o.UtcDateTime - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            return double.NaN;
        }

        static bool HasOffset(string s)
        {
            int i = Math.Max(s.LastIndexOf('+'), s.LastIndexOf('-'));
            return i > 10 && s.Length - i >= 5 && JsText.Digits(s, i + 1, 2);
        }

        // starts like the ISO form (YYYY-MM...): V8 does not fall back for those when they fail
        static bool IsoShaped(string s)
        {
            return s.Length >= 7 && JsText.Digits(s, 0, 4) && s[4] == '-' && JsText.Digits(s, 5, 2) && (s.Length == 7 || s[7] == '-' || s[7] == 'T');
        }

        static bool TryIso(string s, TimeZoneInfo zone, out double t)
        {
            t = double.NaN;
            int i = 0;
            double year;
            if (s.Length >= 7 && (s[0] == '+' || s[0] == '-') && JsText.Digits(s, 1, 6))
            {
                year = int.Parse(s.Substring(1, 6), CultureInfo.InvariantCulture);
                if (s[0] == '-') { if (year == 0) return false; year = -year; }
                i = 7;
            }
            else if (JsText.Digits(s, 0, 4)) { year = int.Parse(s.Substring(0, 4), CultureInfo.InvariantCulture); i = 4; }
            else return false;
            int month = 1, day = 1;
            if (i < s.Length && s[i] == '-')
            {
                if (!JsText.Digits(s, i + 1, 2)) return false;
                month = int.Parse(s.Substring(i + 1, 2), CultureInfo.InvariantCulture);
                i += 3;
                if (i < s.Length && s[i] == '-')
                {
                    if (!JsText.Digits(s, i + 1, 2)) return false;
                    day = int.Parse(s.Substring(i + 1, 2), CultureInfo.InvariantCulture);
                    i += 3;
                }
            }
            // (V8 takes any day up to 31 in any month: February 30 is March 2)
            if (month < 1 || month > 12 || day < 1 || day > 31) return false;
            if (i == s.Length)
            {
                t = MakeDate(year, month - 1, day, 0, 0, 0, 0);
                return true;
            }
            if (s[i] != 'T') return false;
            if (!JsText.Digits(s, i + 1, 2) || i + 3 >= s.Length || s[i + 3] != ':' || !JsText.Digits(s, i + 4, 2)) return false;
            int hour = int.Parse(s.Substring(i + 1, 2), CultureInfo.InvariantCulture);
            int minute = int.Parse(s.Substring(i + 4, 2), CultureInfo.InvariantCulture);
            i += 6;
            int second = 0;
            double ms = 0;
            if (i < s.Length && s[i] == ':')
            {
                if (!JsText.Digits(s, i + 1, 2)) return false;
                second = int.Parse(s.Substring(i + 1, 2), CultureInfo.InvariantCulture);
                i += 3;
                if (i < s.Length && s[i] == '.')
                {
                    int j = i + 1;
                    while (j < s.Length && s[j] >= '0' && s[j] <= '9') j++;
                    if (j == i + 1) return false;
                    string frac = s.Substring(i + 1, Math.Min(3, j - i - 1)).PadRight(3, '0');
                    ms = int.Parse(frac, CultureInfo.InvariantCulture);
                    i = j;
                }
            }
            if (hour > 24 || minute > 59 || second > 59 || (hour == 24 && (minute != 0 || second != 0 || ms != 0))) return false;
            double local = MakeDate(year, month - 1, day, hour, minute, second, ms);
            if (i == s.Length) { t = FromLocal(local, zone); return !double.IsNaN(t); }
            if (s[i] == 'Z' && i + 1 == s.Length) { t = local; return true; }
            if ((s[i] == '+' || s[i] == '-') && i + 6 == s.Length && JsText.Digits(s, i + 1, 2) && s[i + 3] == ':' && JsText.Digits(s, i + 4, 2))
            {
                int oh = int.Parse(s.Substring(i + 1, 2), CultureInfo.InvariantCulture);
                int om = int.Parse(s.Substring(i + 4, 2), CultureInfo.InvariantCulture);
                if (oh > 23 || om > 59) return false;
                double off = (oh * 60 + om) * 60000.0;
                t = s[i] == '+' ? local - off : local + off;
                return true;
            }
            return false;
        }
    }

    /// <summary>A dashcam file name read by its profile (src/shared/dashcamProfiles.mjs parseFilename).</summary>
    public sealed class DashcamName
    {
        public string ProfileId;
        /// <summary>YYYY-MM-DD_HH-MM-SS: the minute the clip starts.</summary>
        public string TimestampKey;
        public string Date;
        public string Camera;
    }

    /// <summary>What a dashcam profile allows (capabilities).</summary>
    public sealed class DashcamCapabilities
    {
        public bool Telemetry, Gps, Dashboard, Map, Export, DriveMatching;
    }

    /// <summary>One dashcam profile of src/shared/dashcamProfiles.mjs (Tesla, GM Surround Vision).</summary>
    public sealed class DashcamProfile
    {
        public string Id;
        public string DisplayName;
        public string CollectionLabel;
        public string CustomCollectionLabel;
        public bool NormalizeCollectionTag;
        public bool LocalizedCameraLabels;
        public string DefaultLooseSourceKind;
        public string LayoutId;
        public double DefaultSegmentDurationSeconds;
        public double ExportFps;
        public string[] ExportCameraOrder;
        public DashcamCapabilities Capabilities;
        /// <summary>camera id to its label, in the profile's order.</summary>
        public KeyValuePair<string, string>[] Cameras;
        public LayoutSlot[] LayoutSlots;
        public string[] MirroredCameras;
        /// <summary>Tesla: the folders a source may start at (sourceRules.roots).</summary>
        public string[] Roots;
        /// <summary>GM: the phone's recorder folder (sourceRules.rawAndroidPath) and the Continuous folder.</summary>
        public string[] RawAndroidPath;
        public string ContinuousFolder;

        internal Func<string, DashcamName> ParseFilename;

        public DashcamName Parse(string filename) { return ParseFilename(filename); }

        /// <summary>profile.cameras[cameraId], else null.</summary>
        public string Camera(string id)
        {
            foreach (KeyValuePair<string, string> kv in Cameras) if (kv.Key == id) return kv.Value;
            return null;
        }
    }

    /// <summary>
    /// src/shared/dashcamProfiles.mjs and teslaFilename.mjs: the two dashcam
    /// profiles, their file names (YYYY-MM-DD_HH-MM-SS-&lt;camera&gt;.mp4;
    /// FRONT|LEFT|RIGHT|REAR|INTERIOR_YYYY_MM_DD_T_HH_MM_SS.mp4), and the
    /// camera words of teslaCamIndex.js.
    /// </summary>
    public static class Dashcam
    {
        public const string TeslaId = "tesla";
        public const string GmcId = "gm_surroundvision";

        public static readonly DashcamProfile Tesla = new DashcamProfile
        {
            Id = TeslaId,
            DisplayName = "Tesla",
            CollectionLabel = "TeslaCam",
            CustomCollectionLabel = "Custom",
            NormalizeCollectionTag = false,
            LocalizedCameraLabels = true,
            DefaultLooseSourceKind = "tesla",
            LayoutId = "six_default",
            DefaultSegmentDurationSeconds = 60,
            ExportFps = 36,
            ExportCameraOrder = new[] { "left_pillar", "front", "right_pillar", "left_repeater", "back", "right_repeater" },
            Capabilities = new DashcamCapabilities { Telemetry = true, Gps = true, Dashboard = true, Map = true, Export = true, DriveMatching = true },
            Cameras = new[]
            {
                new KeyValuePair<string, string>("front", "Front"),
                new KeyValuePair<string, string>("back", "Back"),
                new KeyValuePair<string, string>("left_repeater", "Left Repeater"),
                new KeyValuePair<string, string>("right_repeater", "Right Repeater"),
                new KeyValuePair<string, string>("left_pillar", "Left Pillar"),
                new KeyValuePair<string, string>("right_pillar", "Right Pillar")
            },
            LayoutSlots = new[]
            {
                new LayoutSlot("tl", "left_pillar"), new LayoutSlot("tc", "front"), new LayoutSlot("tr", "right_pillar"),
                new LayoutSlot("bl", "left_repeater"), new LayoutSlot("bc", "back"), new LayoutSlot("br", "right_repeater")
            },
            MirroredCameras = new[] { "back", "left_repeater", "right_repeater" },
            Roots = new[] { "TeslaCam", "RecentClips", "SentryClips", "SavedClips" },
            ParseFilename = ParseTesla
        };

        public static readonly DashcamProfile Gmc = new DashcamProfile
        {
            Id = GmcId,
            DisplayName = "GM Surround Vision",
            CollectionLabel = "Continuous",
            CustomCollectionLabel = "Continuous",
            NormalizeCollectionTag = true,
            LocalizedCameraLabels = false,
            DefaultLooseSourceKind = "gmc-raw-flat",
            LayoutId = "gmc_surroundvision",
            DefaultSegmentDurationSeconds = 300,
            ExportFps = 30,
            ExportCameraOrder = new[] { "left", "front", "right", "interior", "rear" },
            Capabilities = new DashcamCapabilities(),
            Cameras = new[]
            {
                new KeyValuePair<string, string>("left", "Left"),
                new KeyValuePair<string, string>("front", "Front"),
                new KeyValuePair<string, string>("right", "Right"),
                new KeyValuePair<string, string>("rear", "Rear"),
                new KeyValuePair<string, string>("interior", "Interior")
            },
            LayoutSlots = new[]
            {
                new LayoutSlot("tl", "left"), new LayoutSlot("tc", "front"), new LayoutSlot("tr", "right"),
                new LayoutSlot("bl", "interior"), new LayoutSlot("bc", "rear"), new LayoutSlot("br", null)
            },
            MirroredCameras = new string[0],
            RawAndroidPath = new[] { "Android", "media", "com.gm.ultifi.gmconnectedcameraservice", "Recordings", "SurroundVisionRecorder" },
            ContinuousFolder = "Continuous",
            ParseFilename = ParseGmc
        };

        /// <summary>DASHCAM_PROFILES in their order.</summary>
        public static readonly DashcamProfile[] All = { Tesla, Gmc };

        /// <summary>getDashcamProfile(id): the profile, Tesla when unknown.</summary>
        public static DashcamProfile Get(string id)
        {
            foreach (DashcamProfile p in All) if (p.Id == id) return p;
            return Tesla;
        }

        /// <summary>normalizeTeslaCamera: lower case; left / right are the repeaters.</summary>
        public static string NormalizeTeslaCamera(string raw)
        {
            string camera = JsText.Lower(raw ?? "");
            if (camera == "left") return "left_repeater";
            if (camera == "right") return "right_repeater";
            return camera;
        }

        /// <summary>parseTeslaFilename: /^(\d{4}-\d{2}-\d{2})_(\d{2}-\d{2}-\d{2})-(.+)\.mp4$/i.</summary>
        public static DashcamName ParseTesla(string filename)
        {
            string s = filename ?? "";
            if (s.Length < 25) return null;
            if (JsText.DayPrefix(s) == null) return null;
            if (!JsText.Digits(s, 11, 2) || s[13] != '-' || !JsText.Digits(s, 14, 2) || s[16] != '-' || !JsText.Digits(s, 17, 2) || s[19] != '-') return null;
            if (s[s.Length - 4] != '.' || !JsText.AsciiEq(s, s.Length - 3, "mp4")) return null;
            string camera = s.Substring(20, s.Length - 24);
            foreach (char c in camera) if (JsText.LineBreak(c)) return null;
            DashcamName n = new DashcamName();
            n.ProfileId = TeslaId;
            n.Date = s.Substring(0, 10);
            n.TimestampKey = n.Date + "_" + s.Substring(11, 8);
            n.Camera = NormalizeTeslaCamera(camera);
            return n;
        }

        static readonly string[] GmcCameras = { "FRONT", "LEFT", "RIGHT", "REAR", "INTERIOR" };

        /// <summary>parseGmcFilename: /^(FRONT|LEFT|RIGHT|REAR|INTERIOR)_(\d{4})_(\d{2})_(\d{2})_T_(\d{2})_(\d{2})_(\d{2})\.mp4$/i.</summary>
        public static DashcamName ParseGmc(string filename)
        {
            string s = filename ?? "";
            int u = s.IndexOf('_');
            if (u <= 0) return null;
            string raw = null;
            foreach (string c in GmcCameras) if (c.Length == u && JsText.AsciiEq(s, 0, c)) { raw = c; break; }
            if (raw == null) return null;
            int i = u;
            // _YYYY_MM_DD_T_HH_MM_SS.mp4
            if (s.Length != i + 26) return null;
            if (s[i] != '_' || !JsText.Digits(s, i + 1, 4) || s[i + 5] != '_' || !JsText.Digits(s, i + 6, 2) || s[i + 8] != '_' || !JsText.Digits(s, i + 9, 2)
                || s[i + 11] != '_' || !JsText.AsciiEq(s, i + 12, "T") || s[i + 13] != '_' || !JsText.Digits(s, i + 14, 2) || s[i + 16] != '_'
                || !JsText.Digits(s, i + 17, 2) || s[i + 19] != '_' || !JsText.Digits(s, i + 20, 2) || s[i + 22] != '.' || !JsText.AsciiEq(s, i + 23, "mp4"))
                return null;
            string year = s.Substring(i + 1, 4), month = s.Substring(i + 6, 2), day = s.Substring(i + 9, 2);
            string hour = s.Substring(i + 14, 2), minute = s.Substring(i + 17, 2), second = s.Substring(i + 20, 2);
            DashcamName n = new DashcamName();
            n.ProfileId = GmcId;
            n.TimestampKey = year + "-" + month + "-" + day + "_" + hour + "-" + minute + "-" + second;
            n.Date = year + "-" + month + "-" + day;
            n.Camera = JsText.Lower(raw);
            return n;
        }

        /// <summary>parseDashcamFilename: event.mp4 is never a camera's clip; then each profile in turn.</summary>
        public static DashcamName Parse(string filename)
        {
            string name = filename ?? "";
            if (JsText.Lower(name) == "event.mp4") return null;
            foreach (DashcamProfile p in All)
            {
                DashcamName n = p.Parse(name);
                if (n != null) return n;
            }
            return null;
        }

        /// <summary>getCameraLabel(profileId, cameraId): the profile's label, else the id.</summary>
        public static string CameraLabel(string profileId, string cameraId)
        {
            string label = Get(profileId).Camera(cameraId);
            return !string.IsNullOrEmpty(label) ? label : cameraId ?? "";
        }

        /// <summary>teslaCamIndex.js normalizeCamera.</summary>
        public static string NormalizeCamera(string raw)
        {
            string c = JsText.Lower(raw ?? "");
            if (c == "front") return "front";
            if (c == "back") return "back";
            if (c == "left_repeater" || c == "left") return "left_repeater";
            if (c == "right_repeater" || c == "right") return "right_repeater";
            if (c == "left_pillar") return "left_pillar";
            if (c == "right_pillar") return "right_pillar";
            return c.Length > 0 ? c : "unknown";
        }

        /// <summary>teslaCamIndex.js cameraLabel: Studio's words (ui.cameras.*) for Tesla, the profile's labels otherwise.</summary>
        public static string StudioCameraLabel(string camera, string profileId)
        {
            if (!Get(profileId).LocalizedCameraLabels) return CameraLabel(profileId, camera);
            switch (camera)
            {
                case "front": return "Front";
                case "back": return "Back";
                case "left_repeater": return "Left Repeater";
                case "right_repeater": return "Right Repeater";
                case "left_pillar": return "Left Pillar";
                case "right_pillar": return "Right Pillar";
                default: return camera;
            }
        }
    }
}
