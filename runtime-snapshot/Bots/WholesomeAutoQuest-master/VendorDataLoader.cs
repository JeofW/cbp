using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.Combat.CombatRoutine;
using Styx.Logic.Profiles;
using Styx.WoWInternals.WoWObjects;

namespace WholesomeAQ
{
    public class VendorDataLoader
    {
        private VendorDatabase _database;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public VendorDatabase Database => _database;

        public bool Load()
        {
            if (_database != null)
                return true;

            string dataFile = FindDataFile();
            if (!File.Exists(dataFile))
                return false;

            string json = File.ReadAllText(dataFile);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            VendorDatabase db = new VendorDatabase();

            if (root.TryGetProperty("Vendors", out JsonElement vendors))
            {
                foreach (JsonElement ve in vendors.EnumerateArray())
                    db.Vendors.Add(ParseVendorEntry(ve));
            }

            _database = db;
            return true;
        }

        private static VendorEntry ParseVendorEntry(JsonElement ve)
        {
            VendorEntry v = new VendorEntry
            {
                Type = ve.GetProperty("Type").GetString() ?? "",
                Entry = ve.GetProperty("Entry").GetInt32(),
                Name = ve.GetProperty("Name").GetString() ?? "",
                X = ve.GetProperty("X").GetDouble(),
                Y = ve.GetProperty("Y").GetDouble(),
                Z = ve.GetProperty("Z").GetDouble(),
                Map = ve.GetProperty("Map").GetInt32()
            };
            if (ve.TryGetProperty("TrainClass", out JsonElement tc))
                v.TrainClass = tc.GetString();
            return v;
        }

        public List<VendorEntry> GetNearestVendors(LocalPlayer me, string type, int count = 5, HashSet<int> blacklist = null)
        {
            if (me == null) return new List<VendorEntry>();
            return SelectNearestVendors(me.MapId, me.Class, me.Location, type, count, blacklist);
        }

        private List<VendorEntry> SelectNearestVendors(uint map, WoWClass playerClass,
            WoWPoint origin, string type, int count, HashSet<int> blacklist)
        {
            if (_database == null || count <= 0 || !VendorTravelBackoff.IsFinite(origin))
                return new List<VendorEntry>();
            DateTime now = DateTime.UtcNow;
            string className = playerClass.ToString();
            return _database.Vendors
                .Where(v => v.Map == map && v.Type == type)
                .Where(v => type != "Train" || string.IsNullOrEmpty(v.TrainClass) || v.TrainClass == className)
                .Where(v => blacklist == null || !blacklist.Contains(v.Entry))
                .Where(v => !VendorSafetyPolicy.IsRejected(v.Entry))
                .Where(v => VendorTravelBackoff.IsFinite(new WoWPoint((float)v.X, (float)v.Y, (float)v.Z)))
                .Where(v => !VendorSafetyPolicy.Travel.IsDeferred(map, v.Entry,
                    new WoWPoint((float)v.X, (float)v.Y, (float)v.Z), now))
                .OrderBy(v => (v.X - origin.X) * (v.X - origin.X) + (v.Y - origin.Y) * (v.Y - origin.Y))
                .ThenBy(v => v.Entry)
                .Take(count)
                .ToList();
        }

        private static string FindDataFile()
        {
            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(asmDir))
            {
                string path = Path.Combine(asmDir, "vendor_data.json");
                if (File.Exists(path))
                    return path;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "Bots", "WholesomeAutoQuest", "vendor_data.json"),
                Path.Combine(baseDir, "Plugins", "WholesomeAutoQuester", "vendor_data.json"),
                Path.Combine(Environment.CurrentDirectory, "vendor_data.json")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(Environment.CurrentDirectory, "vendor_data.json");
        }
    }
}
