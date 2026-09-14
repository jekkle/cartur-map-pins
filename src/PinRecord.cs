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

        /// Promotes a bare record ("Spawner") to a subtyped one ("Spawner:Boar") the first time
        /// the world is close enough to say what the thing actually is, and repoints its pin to
        /// the matching icon.
        ///
        /// Records written before subtypes existed carry only the category, so the upgrade
        /// migration could restore no more than the generic category icon - a boar spawner and a
        /// draugr pile both ended up on the summoning-circle glyph. The missing information is
        /// not recoverable from the record at any time; it exists only while the spawner itself is
        /// loaded, which is exactly when this runs.
        ///
        /// Only the icon is touched, never the name: a pin can be renamed by hand, and there is
        /// no way to tell a renamed pin from one still carrying our label.
        ///
        /// Returns true when an entry was promoted.
        public static bool Upgrade(PinCategory category, string subtype, Vector3 pos, float radius, Minimap.PinType wanted)
        {
            if (string.IsNullOrEmpty(subtype))
                return false;

            Minimap map = Minimap.instance;
            if (map == null)
                return false;

            string bareKey = category.ToString();
            float sqr = radius * radius;

            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Key != bareKey)
                    continue;   // already subtyped, or a different category

                Vector3 d = Entries[i].Pos - pos;
                if (d.x * d.x + d.z * d.z > sqr)
                    continue;

                // Only promote when the pin is actually there to repoint. A record whose pin has
                // gone stays bare, which keeps the record and the map saying the same thing.
                Minimap.PinData pin = FindPinAt(map, Entries[i].Pos);
                if (pin == null || !Repoint(map, pin, wanted))
                    return false;

                Entries[i] = new Entry { Key = bareKey + ":" + subtype, Pos = Entries[i].Pos };
                _dirty = true;
                map.SaveMapData();
                Plugin.Log.LogInfo($"Upgraded '{bareKey}' pin at {pos.x:F0},{pos.z:F0} to '{bareKey}:{subtype}'.");
                return true;
            }

            return false;
        }

        /// Drops records whose pin is no longer on the map, making those objects eligible to pin
        /// again. Deliberately manual: a missing pin means either the pin was lost before the
        /// character was saved, or the player deleted it on purpose, and nothing distinguishes
        /// the two - so resurrecting pins is never done to somebody's map without them asking.
        public static int ForgetMissing()
        {
            Minimap map = Minimap.instance;
            if (map == null)
                return 0;

            int before = Entries.Count;
            Entries.RemoveAll(e => FindPinAt(map, e.Pos) == null);
            int dropped = before - Entries.Count;
            if (dropped > 0)
                Save();
            return dropped;
        }

        /// Ticks or unticks our pin of this category near a position, and says whether anything
        /// changed - so the caller can log the moment rather than every time it looks.
        ///
        /// m_checked is vanilla's own greyed-out state, saved with the map and set by clicking a
        /// pin, so a dungeon the mod ticks looks exactly like one you ticked yourself.
        public static bool SetChecked(PinCategory category, Vector3 pos, float radius, bool value)
        {
            Minimap map = Minimap.instance;
            if (map == null)
                return false;

            string bareKey = category.ToString();
            float sqr = radius * radius;

            foreach (Entry e in Entries)
            {
                if (e.Key != bareKey && !e.Key.StartsWith(bareKey + ":", StringComparison.Ordinal))
                    continue;

                Vector3 d = e.Pos - pos;
                if (d.x * d.x + d.z * d.z > sqr)
                    continue;

                Minimap.PinData pin = FindPinAt(map, e.Pos);
                if (pin == null || pin.m_checked == value)
                    return false;

                // m_checked is what saves and what UpdatePins reads; the element is the tick
                // already drawn on screen, a GameObject rather than a graphic, so it is shown or
                // hidden rather than enabled.
                pin.m_checked = value;
                if (pin.m_checkedElement != null)
                    pin.m_checkedElement.SetActive(value);
                map.SaveMapData();
                return true;
            }

            return false;
        }

        /// Resolves the "$token" names left on pins by versions that wrote them raw.
        ///
        /// The map draws a pin's name exactly as stored, so those pins have been reading
        /// "$piece_chestwood" on the map itself, not only in the editor. Localize turns that into
        /// "Wood chest" in whatever language the player runs.
        ///
        /// Safe to run unasked, and safe to run on pins the mod did not place: a name containing a
        /// dollar sign is an unresolved token, and nobody types one. A name that Localize does not
        /// change is left exactly as it was.
        public static int LocalizeNames()
        {
            Minimap map = Minimap.instance;
            if (map == null)
                return 0;

            List<Minimap.PinData> pins = MinimapAccess.GetPins(map);
            if (pins == null)
                return 0;

            int changed = 0;
            foreach (Minimap.PinData pin in pins)
            {
                if (!pin.m_save || string.IsNullOrEmpty(pin.m_name) || pin.m_name.IndexOf('$') < 0)
                    continue;

                string resolved = Labels.Localize(pin.m_name);
                if (string.IsNullOrEmpty(resolved) || resolved == pin.m_name)
                    continue;

                pin.m_name = resolved;
                changed++;
            }

            if (changed > 0)
                map.SaveMapData();
            return changed;
        }

        /// Moves a pin still wearing its category's generic icon onto its own kind's icon.
        ///
        /// Narrower than RepointAllToCurrent on purpose, and safe to run unasked at every login:
        /// it only touches a pin whose icon is exactly the category default, so an icon somebody
        /// chose by hand is never overwritten. A pin recorded as "Ore:Copper" but still drawing
        /// the generic ore lump is one the mod could always have drawn better and never did.
        ///
        /// It is also what makes ore colouring reach old pins: the colour is keyed by the pin type
        /// that carries each ore's icon, so a pin on the generic icon has no ore colour to find.
        public static int AdoptSubtypeIcons()
        {
            Minimap map = Minimap.instance;
            if (map == null || !CustomIcons.Ready)
                return 0;

            int changed = 0;
            foreach (Entry e in Entries)
            {
                int colon = e.Key.IndexOf(':');
                if (colon <= 0)
                    continue;   // no kind recorded, so there is nothing better to move it to

                if (!Enum.TryParse(e.Key.Substring(0, colon), out PinCategory category))
                    continue;

                Plugin.CategorySettings settings = Plugin.SettingsFor(category);
                if (settings == null)
                    continue;

                string subtype = e.Key.Substring(colon + 1);
                Minimap.PinType generic = settings.ResolvedPinType;
                Minimap.PinType wanted = PinPlacer.IconFor(category, subtype, settings);
                if (wanted == generic)
                    continue;   // this kind has no icon of its own

                Minimap.PinData pin = FindPinAt(map, e.Pos);
                if (pin == null || pin.m_type != generic)
                    continue;   // gone, or already on something somebody chose

                if (Repoint(map, pin, wanted))
                    changed++;
            }

            if (changed > 0)
                map.SaveMapData();
            return changed;
        }

        /// Sets every pin we placed to the icon its category and kind say today.
        ///
        /// Unlike MigrateIcons this asks no questions: it does not care what a pin is currently
        /// wearing, so it also repairs pins left on a number whose meaning moved - which is what
        /// happens to anyone who ran a build from between two icon sheets. It is also the honest
        /// answer to "I changed the icon settings and want my existing pins to match".
        ///
        /// Only pins in the record, so hand-placed ones keep whatever they were given.
        public static int RepointAllToCurrent()
        {
            Minimap map = Minimap.instance;
            if (map == null || !CustomIcons.Ready)
                return 0;

            int changed = 0;
            foreach (Entry e in Entries)
            {
                int colon = e.Key.IndexOf(':');
                string categoryName = colon > 0 ? e.Key.Substring(0, colon) : e.Key;
                string subtype = colon > 0 ? e.Key.Substring(colon + 1) : null;

                if (!Enum.TryParse(categoryName, out PinCategory category))
                    continue;

                Plugin.CategorySettings settings = Plugin.SettingsFor(category);
                if (settings == null)
                    continue;

                Minimap.PinData pin = FindPinAt(map, e.Pos);
                if (pin == null)
                    continue;

                Minimap.PinType wanted = PinPlacer.IconFor(category, subtype, settings);
                if (pin.m_type == wanted || !Repoint(map, pin, wanted))
                    continue;
                changed++;
            }

            if (changed > 0)
                map.SaveMapData();
            return changed;
        }

        /// Where every recorded pin of a category sits. Copied into a list rather than yielded,
        /// because the caller removes entries as it goes.
        public static List<Vector3> PositionsOf(PinCategory category)
        {
            string bareKey = category.ToString();
            var found = new List<Vector3>();
            foreach (Entry e in Entries)
            {
                if (e.Key == bareKey || e.Key.StartsWith(bareKey + ":", StringComparison.Ordinal))
                    found.Add(e.Pos);
            }
            return found;
        }

        /// Removes our pin of this category near a position, and the record with it.
        ///
        /// Only ever called for something the world says is gone. The pin is matched the same way
        /// the migration matches: by position, and only pins the game saved - so a hand-placed pin
        /// sitting on top of a mined-out deposit is left alone unless it is the one we recorded.
        public static bool Forget(PinCategory category, Vector3 pos, float radius)
        {
            Minimap map = Minimap.instance;
            if (map == null)
                return false;

            string bareKey = category.ToString();
            float sqr = radius * radius;

            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].Key != bareKey && !Entries[i].Key.StartsWith(bareKey + ":", StringComparison.Ordinal))
                    continue;

                Vector3 d = Entries[i].Pos - pos;
                if (d.x * d.x + d.z * d.z > sqr)
                    continue;

                // No pin here means this record is not this world's. One record file serves every
                // world, so a copper deposit recorded in another save sits in the list wherever
                // you are - and dropping it would leave that world's pin unrecorded and liable to
                // be pinned a second time. Nothing to remove, so nothing is removed.
                Minimap.PinData pin = FindPinAt(map, Entries[i].Pos);
                if (pin == null)
                    return false;

                map.RemovePin(pin);
                map.SaveMapData();
                Entries.RemoveAt(i);
                _dirty = true;
                return true;
            }

            return false;
        }

        /// The subtype recorded for a pin of this category near a position, or null when the record
        /// there is a bare category with no subtype. Lets the looted-chest sweep tell a chest that
        /// is standing in for a ruin from an ordinary one.
        public static string SubtypeNear(PinCategory category, Vector3 pos, float radius)
        {
            string prefix = category.ToString() + ":";
            float sqr = radius * radius;
            foreach (Entry e in Entries)
            {
                if (!e.Key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                Vector3 d = e.Pos - pos;
                if (d.x * d.x + d.z * d.z <= sqr)
                    return e.Key.Substring(prefix.Length);
            }
            return null;
        }

        /// True when we have a pin of this category within `radius` of a position. Used to decide
        /// whether vanilla's own marker at the same spot is now a duplicate of ours.
        public static bool HasCategoryNear(PinCategory category, Vector3 pos, float radius)
        {
            string bareKey = category.ToString();
            float sqr = radius * radius;
            foreach (Entry e in Entries)
            {
                if (e.Key != bareKey && !e.Key.StartsWith(bareKey + ":", StringComparison.Ordinal))
                    continue;
                Vector3 d = e.Pos - pos;
                if (d.x * d.x + d.z * d.z <= sqr)
                    return true;
            }
            return false;
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

        /// Repairs spawner labels written by an older version, once, on the pins already sitting in
        /// the player's save. Those used to be named after the creature alone ("Skeleton") or after
        /// the spawner's tuning ("Skeleton Night Noarcher"), both of which read like the creature
        /// was standing there rather than spawning from there.
        ///
        /// The prefab is gone by the time a pin comes back from the save, but the old label still
        /// carries the creature's name, so the same word filtering that builds a new label repairs
        /// an old one.
        ///
        /// Only Spawner entries are considered, and that restriction is the whole safety of this:
        /// run over an Ore pin it would turn "Copper" into "Copper Spawner". Hand-placed pins are
        /// never touched - they aren't in the record.
        public static int RelabelSpawners()
        {
            Minimap map = Minimap.instance;
            if (map == null)
                return 0;

            int renamed = 0;
            foreach (Entry e in Entries)
            {
                if (!e.Key.StartsWith("Spawner", StringComparison.OrdinalIgnoreCase))
                    continue;

                Minimap.PinData pin = FindPinAt(map, e.Pos);
                if (pin == null || string.IsNullOrEmpty(pin.m_name))
                    continue;

                string repaired = Labels.CleanSpawnerLabel(pin.m_name);
                if (repaired == pin.m_name)
                    continue;

                pin.m_name = repaired;
                renamed++;
            }

            if (renamed > 0)
                map.SaveMapData();

            return renamed;
        }

        /// What 1.2.2 wrote into saved pins, by category and by dungeon/camp/pickable subtype.
        ///
        /// FROZEN HISTORICAL DATA - do not update these to follow the current defaults. They are
        /// what makes the migration below able to tell "this pin still carries the old sheet's
        /// icon" from "the player chose this". Changing a value here would make the migration
        /// either miss pins or overwrite deliberate choices.
        ///
        /// Categories whose 1.2.2 icon was -1 (BossAltar, Runestone) are deliberately absent:
        /// they resolved to a vanilla PinType, which the sheet swap never touched.
        /// Tomb and Dvergr Tower are 1.2.2 subtypes the current table no longer has - their pins
        /// still need migrating, and they correctly land on the category icon.
        private static readonly Dictionary<string, int> LegacyCategoryIcon = new Dictionary<string, int>
        {
            { "Ore", 47 }, { "Dungeon", 22 }, { "Camp", 34 }, { "Beehive", 6 },
            { "Chest", 45 }, { "Spawner", 48 }, { "Leviathan", 59 }, { "Trader", 15 },
            { "Wisp", 32 },
        };

        private static readonly Dictionary<string, int> LegacySubtypeIcon = new Dictionary<string, int>
        {
            { "Hildir Crypt", 22 }, { "Hildir Cave", 18 }, { "Sunken Crypt", 28 },
            { "Infested Citadel", 67 }, { "Infested Mine", 57 }, { "Frost Cave", 18 },
            { "Troll Cave", 50 }, { "Putrid Hole", 73 }, { "Tomb", 71 }, { "Crypt", 22 },
            { "Hildir Fortress", 16 }, { "Fuling Village", 77 }, { "Charred Fortress", 16 },
            { "Ashlands Ruin", 74 }, { "Abandoned Village", 79 }, { "Abandoned Farm", 41 },
            { "Greydwarf Camp", 13 }, { "Dvergr Tower", 67 }, { "Hildir Camp", 34 },
            { "Berries", 36 }, { "Mushrooms", 76 }, { "Crops", 37 }, { "HighValue", 21 },
            { "Junk", 0 }, { "Other", 0 },
            // Ore types are absent on purpose: 1.2.2 had no per-ore icon, so "Ore:Tin" used the
            // Ore category icon and must fall through to it.
        };

        /// The icon index 1.2.2 would have given a pin with this record key, or -1 for none.
        private static int LegacyIconFor(string key, out PinCategory category, out string subtype)
        {
            category = default;
            subtype = null;

            int colon = key.IndexOf(':');
            string categoryName = colon > 0 ? key.Substring(0, colon) : key;
            if (colon > 0)
                subtype = key.Substring(colon + 1);

            if (!Enum.TryParse(categoryName, out category))
                return -1;

            if (subtype != null && LegacySubtypeIcon.TryGetValue(subtype, out int bySubtype))
                return bySubtype;

            return LegacyCategoryIcon.TryGetValue(categoryName, out int byCategory) ? byCategory : -1;
        }

        /// 1.2.2's looted-chest icon. A chest pin can be sitting on either this or the normal
        /// chest icon, and which one it is says whether the chest was empty - information the
        /// record itself does not carry.
        private const int LegacyLootedChestIcon = 51;

        /// Repoints pins placed by 1.2.2 at the icons they mean under the current sheet.
        ///
        /// The sheet was replaced wholesale, so an index that meant "pickaxe" now means
        /// "shipwreck" - every pin 1.2.2 saved would otherwise show unrelated artwork.
        ///
        /// Self-limiting rather than version-stamped: a pin is only rewritten when it still
        /// carries exactly the icon 1.2.2 would have given it. That makes this idempotent (after
        /// the rewrite it no longer matches), safe across multiple worlds sharing one record
        /// (a config stamp would mark the first world done and leave the rest broken), and
        /// incapable of overwriting an icon the player picked themselves.
        ///
        /// Hand-placed pins are never touched: they aren't in the record. They are only counted,
        /// because their artwork has shifted too and there is no way to recover what was meant.
        public static int MigrateIcons()
        {
            Minimap map = Minimap.instance;
            if (map == null || !CustomIcons.Ready)
                return 0;

            List<Minimap.PinData> pins = MinimapAccess.GetPins(map);
            if (pins == null)
                return 0;

            int migrated = 0, notFound = 0, otherIcon = 0;
            var ours = new HashSet<Minimap.PinData>();

            foreach (Entry e in Entries)
            {
                int legacyIndex = LegacyIconFor(e.Key, out PinCategory category, out string subtype);
                if (legacyIndex < 0)
                    continue;

                Plugin.CategorySettings settings = Plugin.SettingsFor(category);
                if (settings == null)
                    continue;

                Minimap.PinData pin = FindPinAt(map, e.Pos);
                if (pin == null)
                {
#if DIAGNOSTICS
                    Plugin.Log.LogInfo($"  migrate: '{e.Key}' at {e.Pos.x:F0},{e.Pos.z:F0} - NO PIN FOUND within 0.5m");
#endif
                    notFound++;
                    continue;
                }

                ours.Add(pin);

                // A chest sitting on the old looted icon is one we knew was empty, so it has to
                // migrate to the current looted icon rather than the normal one.
                bool wasLooted = category == PinCategory.Chest &&
                                 pin.m_type == CustomIcons.LegacyTypeForIndex(LegacyLootedChestIcon);
                if (!wasLooted && pin.m_type != CustomIcons.LegacyTypeForIndex(legacyIndex))
                {
#if DIAGNOSTICS
                    Plugin.Log.LogInfo($"  migrate: '{e.Key}' at {e.Pos.x:F0},{e.Pos.z:F0} - on type {(int)pin.m_type}, 1.2.2 would have written {100 + legacyIndex} - left alone");
#endif
                    otherIcon++;
                    continue;   // already migrated, or the player chose this icon
                }

                Minimap.PinType wanted = wasLooted
                    ? CustomIcons.Resolve((int)Plugin.LootedChestIcon.Value, settings.ResolvedPinType)
                    : PinPlacer.IconFor(category, subtype, settings);

                if (wanted == pin.m_type)
                    continue;

                // Replaced rather than mutated: a pin's sprite is resolved once when it is
                // created and the field that forces a UI rebuild is private - the same reason
                // the looted-chest sweep re-adds instead of writing m_type.
                if (!Repoint(map, pin, wanted))
                    continue;
                migrated++;
            }

            if (migrated > 0)
                map.SaveMapData();

            // Pins carrying a custom icon that we did not place. Their artwork has shifted too,
            // but nothing records which icon was chosen, so say so rather than guess.
            int handPlaced = 0;
            foreach (Minimap.PinData pin in pins)
            {
                if (pin.m_save && CustomIcons.IsCustom(pin.m_type) && !ours.Contains(pin))
                    handPlaced++;
            }
            if (handPlaced > 0)
                Plugin.Log.LogInfo($"{handPlaced} pin(s) carry a custom icon but are not in our record - hand-placed, so their artwork shifted with the sheet and cannot be recovered.");

            Plugin.Log.LogInfo($"Icon migration detail: {migrated} repointed, {notFound} record(s) had no pin, " +
                               $"{otherIcon} already on another icon, {handPlaced} not ours.");
#if DIAGNOSTICS
            foreach (Minimap.PinData pin in pins)
            {
                if (pin.m_save && CustomIcons.IsCustom(pin.m_type) && !ours.Contains(pin))
                    Plugin.Log.LogInfo($"  not ours: type {(int)pin.m_type} '{pin.m_name}' at {pin.m_pos.x:F0},{pin.m_pos.z:F0}");
            }
#endif
            return migrated;
        }

        /// Changes a pin's icon without removing it.
        ///
        /// The obvious approach - RemovePin then AddPin - is what the looted-chest sweep used to
        /// do, on the reasoning that a pin's sprite is resolved once at creation and the rebuild
        /// flag is private. Both halves of that are true and the conclusion still doesn't follow:
        /// AddPin resolves GetSprite into PinData.m_icon, and m_type, m_icon and m_iconElement are
        /// all public, so the cached sprite can simply be replaced in place.
        ///
        /// That matters for other people's saves. PinData carries sixteen fields; re-adding
        /// carries five of them across and invents defaults for the rest - m_ownerID and m_author
        /// among them, and m_ownerID is load-bearing, since the render loop hides any pin whose
        /// owner is non-zero. Editing in place cannot lose a field nobody thought to copy, and
        /// there is no instant where the pin does not exist.
        ///
        /// Returns false when the sprite can't be resolved, leaving the pin exactly as it was
        /// rather than repointing m_type at an icon that would not render.
        internal static bool Repoint(Minimap map, Minimap.PinData pin, Minimap.PinType wanted)
        {
            Sprite sprite = SpriteFor(map, wanted);
            if (sprite == null)
                return false;

            pin.m_type = wanted;
            pin.m_icon = sprite;
            // Null until UpdatePins has built the pin's UI element; it reads m_icon when it does,
            // so there is nothing to refresh in that case.
            if (pin.m_iconElement != null)
                pin.m_iconElement.sprite = sprite;
            return true;
        }

        /// The same lookup Minimap.GetSprite does - m_icons is a public list, so this needs no
        /// reflection and picks up the custom types registered by CustomIcons for free.
        private static Sprite SpriteFor(Minimap map, Minimap.PinType type)
        {
            if (map.m_icons == null)
                return null;
            foreach (Minimap.SpriteData data in map.m_icons)
            {
                if (data.m_name == type)
                    return data.m_icon;
            }
            return null;
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
