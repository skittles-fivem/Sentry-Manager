using System;
using System.Collections.Generic;
using SentrySix.Core;
using SentrySix.Ui;

namespace SentrySix.Shell
{
    /// <summary>
    /// The server's answers as the shells' model, read the way the web shells
    /// read them (GET /api/vehicles, /api/notifications?open=1, /api/jobs?limit=50,
    /// /api/settings), so the live app and the render test mode's fixtures fill it alike.
    /// </summary>
    public static class ShellData
    {
        static string Plural(long n, string one)
        {
            return n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " " + (n == 1 ? one : one + "s");
        }

        /// <summary>{ vehicles: [...] }: name, share health dot, recordings line, Recycle Bin items.</summary>
        public static List<VehicleInfo> Vehicles(JsonValue reply)
        {
            List<VehicleInfo> list = new List<VehicleInfo>();
            foreach (JsonValue v in reply["vehicles"].Items)
            {
                VehicleInfo x = new VehicleInfo();
                x.Id = v["id"].Str;
                x.Name = v["name"].Str ?? "";
                JsonValue s = v["share"];
                // app.js pills(): no share, healthy (writable false: warn), else bad
                x.Dot = !s.Bool ? "" : s["healthy"].Bool ? (s["writable"].IsBool && !s["writable"].Bool ? "warn" : "ok") : "bad";
                x.Sub = v["inventory"].Bool ? Plural(v["inventory"]["recordings"].Long, "recording") : v["scanning"].Bool ? "scanning…" : "not scanned yet";
                x.BinItems = (int)v["binItems"].Long;
                list.Add(x);
            }
            return list;
        }

        /// <summary>{ notifications: [...] }.</summary>
        public static List<NoteInfo> Notes(JsonValue reply)
        {
            List<NoteInfo> list = new List<NoteInfo>();
            foreach (JsonValue n in reply["notifications"].Items)
            {
                NoteInfo x = new NoteInfo();
                x.Id = n["id"].Str;
                x.Title = n["title"].Str ?? "";
                x.Message = n["message"].Str ?? "";
                x.Level = n["level"].Str ?? "";
                x.Read = !n["readAt"].IsNull;
                x.Resolved = !n["resolvedAt"].IsNull;
                x.UpdatedAt = n["updatedAt"].Long;
                list.Add(x);
            }
            return list;
        }

        /// <summary>{ jobs: [...] }: running jobs, and whether one is a conversion.</summary>
        public static void Jobs(JsonValue reply, ShellModel m)
        {
            int running = 0;
            bool converting = false;
            foreach (JsonValue j in reply["jobs"].Items)
            {
                if (j["status"].Str != "running") continue;
                running++;
                if (j["type"].Str == "conversion") converting = true;
            }
            m.RunningJobs = running;
            m.Converting = converting;
        }

        /// <summary>/api/settings appearance: reduceEffects, clock24.</summary>
        public static void Settings(JsonValue reply, ShellModel m)
        {
            m.ReduceEffects = reply["appearance"]["reduceEffects"].Bool;
            m.Clock24 = reply["appearance"]["clock24"].Bool;
        }
    }
}
