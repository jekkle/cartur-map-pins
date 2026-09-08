using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace CarturMapPins
{
    /// Side-car record of every pin this mod created, kept beside the config.
    ///
    /// It serves two purposes: it's the dedupe set (so re-approaching a deposit across sessions
    /// doesn't stack a second pin), and it's what the cleanup command uses to know which pins are
    /// ours.
    ///
    /// Note on why the record exists at all rather than tagging the pins themselves: the obvious
    /// tag would be PinData.m_ownerID, since it's one of the few fields Valheim persists. It must
    /// NOT be used - Minimap's pin render loop skips any pin whose m_ownerID != 0 unless
    /// shared-map fade is active, so tagged pins would be silently invisible while still piling up
    /// in the save file. m_author is similarly load-bearing.
    internal static class PinRecord
    {
        /// Key is "Category" or "Category:Subtype" (e.g. "Ore:Copper"). Keying dedupe on the
        /// subtype is what stops a copper pin from suppressing a tin node a few metres away,
        /// which a category-wide radius would otherwise do.
        private struct Entry
        {
            public string Key;
            public Vector3 Pos;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static string _path;

        public static int Count => Entries.Count;

        public static void Load(string configDir)
        {
            _path = Path.Combine(configDir, "com.jekkle.valheim.carturmappins.pins.txt");
            Entries.Clear();

            if (!File.Exists(_path))
                return;

            try
            {
                foreach (string line in File.ReadAllLines(_path))
                {
                    // key|x|y|z   (key is "Ore:Copper", "Dungeon", ...)
                    string[] parts = line.Split('|');
                    if (parts.Length != 4)
                        continue;
                    if (string.IsNullOrEmpty(parts[0]))
                        continue;
                    if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                        !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                        !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                        continue;

                    Entries.Add(new Entry { Key = parts[0], Pos = new Vector3(x, y, z) });
                }
                Plugin.Log.LogInfo($"Loaded {Entries.Count} previously placed pins from record.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not read pin record, starting empty: {e.Message}");
            }
        }

        /// True when a pin of this category already exists within `radius` - our own dedupe,
        /// because Minimap.HaveSimilarPin is private AND hardcoded to a 1m radius, which is far
        /// too tight for an ore field where deposits sit a few metres apart.
        public static bool Exists(string key, Vector3 pos, float radius)
        {
            // Records written before subtypes existed use the bare category ("Ore", "Pickable")
            // where we now write "Ore:Copper". The ore type can't be recovered from them, but a
            // legacy entry still means "we pinned something of this category here", so it counts
            // as a match - otherwise every previously-pinned node would pin a second time.
            string legacyKey = null;
            int colon = key.IndexOf(':');
            if (colon > 0)
                legacyKey = key.Substring(0, colon);

            float sqr = radius * radius;
            foreach (Entry e in Entries)
            {
                if (e.Key != key && (legacyKey == null || e.Key != legacyKey))
                    continue;
                Vector3 d = e.Pos - pos;
                if (d.x * d.x + d.z * d.z <= sqr)
                    return true;
            }
            return false;
        }

        private static bool _dirty;

        public static void Add(string key, Vector3 pos)
        {
            Entries.Add(new Entry { Key = key, Pos = pos });
            // Marked dirty rather than written immediately: walking into a dense area can place
            // pins several times a second, and rewriting the whole file per pin is needless disk
            // churn. Flush() is called from the throttled tick.
            _dirty = true;
        }

        /// Writes pending changes at most once per interval. Cheap no-op when nothing changed.
        public static void Flush()
        {
            if (!_dirty)
                return;
            _dirty = false;
            Save();
        }

        /// Removes every pin we created from the live map, and clears the record.
        /// Hand-placed pins are matched by position/type against the record, so they survive.
        public static int RemoveAll()
        {
            int removed = 0;
            Minimap map = Minimap.instance;

            if (map != null)
            {
                foreach (Entry e in Entries)
                {
                    Minimap.PinData pin = FindPinAt(map, e.Pos);
                    if (pin != null)
                    {
                        map.RemovePin(pin);
                        removed++;
                    }
                }
            }

            Entries.Clear();
            Save();
            return removed;
        }

        private static Minimap.PinData FindPinAt(Minimap map, Vector3 pos)
        {
            List<Minimap.PinData> pins = MinimapAccess.GetPins(map);
            if (pins == null)
                return null;

            foreach (Minimap.PinData pin in pins)
            {
                if (!pin.m_save)
                    continue;
                Vector3 d = pin.m_pos - pos;
                if (d.x * d.x + d.z * d.z < 0.25f)
                    return pin;
            }
            return null;
        }

        private static void Save()
        {
            if (string.IsNullOrEmpty(_path))
                return;

            try
            {
                var lines = new List<string>(Entries.Count);
                foreach (Entry e in Entries)
                {
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}",
                        e.Key, e.Pos.x, e.Pos.y, e.Pos.z));
                }
                File.WriteAllLines(_path, lines.ToArray());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not write pin record: {e.Message}");
            }
        }
    }
}
