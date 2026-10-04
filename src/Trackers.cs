using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    /// Pins that follow a thing instead of marking a place: your boat, your cart, the tames you
    /// named, and anything you can ride.
    ///
    /// A moving pin must not go through PinRecord: that record is the "already pinned, do not pin
    /// again" set, keyed on position, and a boat would write a new entry every time it moved. So
    /// these pins are added with save:false, rebuilt each session, and never reach the dedupe path.
    ///
    /// Read off the installed assembly_valheim:
    ///  - Ship keeps a private static List&lt;Ship&gt; s_currentShips of every loaded boat.
    ///  - Vagon keeps a private static List&lt;Vagon&gt; m_instances, and its m_name is public. The
    ///    field initializer is the plain text "Wagon", not a token; whether the prefab overrides
    ///    it with "$piece_cart" was not read, so ScanCarts treats "Wagon" as unset.
    ///  - Tameable has no static list, so tames come from Character.GetAllCharacters(), which is
    ///    public and is what the mod's own safe-radius check already walks.
    ///  - Tameable.IsTamed(), GetText() (the player-given name, from ZDOVars.s_tamedName) and the
    ///    public m_saddle field are all the information needed to decide what is worth a pin.
    ///
    /// Limitation: a client only holds ZDOs for zones near it, so a boat moored across the ocean
    /// is not loaded and cannot be seen. That is why the last known position is written to disk -
    /// the pin stays where you left the thing, and starts moving again once the game loads it.
    internal static class Trackers
    {
        /// What kind of thing a tracked pin is following. Stored in the file, so the names matter.
        private enum Kind { Boat, Cart, Tame }

        private sealed class Tracked
        {
            public Kind Kind;

            /// Resolved when the thing is first seen and written to the file with it, so a boat that
            /// is no longer loaded keeps its icon instead of falling back to a generic one.
            public PinIcon Icon;

            public string Label;          // "$piece_cart", or the name the player typed
            public Vector3 Pos;
            public Minimap.PinData Pin;
            public bool SeenThisSession;

            /// The live object while its zone is loaded, so the pin can follow it every frame
            /// without re-walking the instance lists. Null once it unloads, which freezes the pin
            /// at the last place it was seen.
            public Transform Live;
        }

        /// Keyed by the object's ZDO id as text, which is stable across sessions and unique,
        /// unlike a position.
        private static readonly Dictionary<string, Tracked> Entries =
            new Dictionary<string, Tracked>(StringComparer.Ordinal);

        /// One row per kind of thing that can be tracked: the prefab the game spawns, the name
        /// shown in the settings, and the icon it gets unless the player picks another.
        ///
        /// These arrays are the single source of both the default and the setting - Plugin binds
        /// one config entry per row - so a kind cannot exist in the matcher without a switch in
        /// the menu, same as the subtype tables.
        internal struct IconKind
        {
            public string Prefab;    // matched against the live object's prefab name
            public string Display;   // the config key, and what the settings screen shows
            public PinIcon Default;
        }

        private static IconKind K(string prefab, string display, PinIcon icon) =>
            new IconKind { Prefab = prefab, Display = display, Default = icon };

        /// The five ship prefabs the game ships, read from its own asset bundle (every GameObject
        /// carrying a Ship component). The Ashlands ship and the trailership start on the longship
        /// glyph because there is no art that tells them apart - a shared icon beats a wrong one.
        internal static readonly IconKind[] BoatKinds =
        {
            K("Raft", "Raft", PinIcon.UtilRaft),
            K("Karve", "Karve", PinIcon.UtilKarve),
            K("VikingShip", "Longship", PinIcon.UtilLongship),
            K("VikingShip_Ashlands", "Longship (Ashlands)", PinIcon.UtilLongship),
            K("Trailership", "Trailership", PinIcon.UtilLongship),
        };

        /// The same for the prefabs that actually carry a Tameable component.
        /// Lox uses spawner_lox because that icon is a lox head portrait, not a den; the
        /// "spawner_" in its name is historical.
        internal static readonly IconKind[] TameKinds =
        {
            K("Boar", "Boar", PinIcon.CreatureBoar),
            K("Wolf", "Wolf", PinIcon.CreatureWolf),
            K("Lox", "Lox", PinIcon.SpawnerLox),
            K("Asksvin", "Asksvin", PinIcon.CreatureAsksvin),
            K("Moose", "Moose", PinIcon.CreatureMoose),
            K("Hen", "Hen", PinIcon.CreatureHen),
        };

        private static readonly FieldInfo VagonInstances =
            AccessTools.Field(typeof(Vagon), "m_instances");

        private static readonly FieldInfo PinUpdateRequired =
            AccessTools.Field(typeof(Minimap), "m_pinUpdateRequired");

        private static string _configDir;
        private static string _path;
        private static bool _loaded;
        private static bool _dirty;
        private static float _timer;

        /// How often to look for things that have appeared or vanished. Pins move every frame
        /// regardless - see Follow.
        private const float Interval = 0.5f;

        public static void Load(string configDir)
        {
            _configDir = configDir;
            Unload();
        }

        /// Dropped when a world is torn down, like PinRecord: the next world must not inherit this
        /// one's boats, and the pins belong to a Minimap that no longer exists.
        public static void Unload()
        {
            Entries.Clear();
            _path = null;
            _loaded = false;
            _dirty = false;
            _timer = 0f;
        }

        public static void Tick(float dt)
        {
            if (Minimap.instance == null || Player.m_localPlayer == null)
                return;

            if (!Plugin.TrackBoats.Value && !Plugin.TrackCarts.Value && !Plugin.TrackTames.Value)
            {
                DropAllPins();
                return;
            }

            EnsureLoaded();
            if (!_loaded)
                return;

            // Finding what exists walks every loaded character and both instance lists, and the
            // answer does not change between frames, so it runs on a timer. Following something
            // already found is one transform read per object and must run every frame, or a boat
            // under sail visibly lags its own pin.
            _timer += dt;
            if (_timer >= Interval)
            {
                _timer = 0f;

                foreach (Tracked t in Entries.Values)
                {
                    t.SeenThisSession = false;
                    t.Live = null;
                }

                ScanShips();
                ScanCarts();
                ScanTames();
                Sweep();

                if (_dirty)
                    Save();
            }

            Follow();
            Draw();
        }

        /// Copies each live object's position onto its record, every frame.
        ///
        /// Just a transform read per tracked thing, and there are only a handful. A Transform
        /// destroyed since the last scan compares equal to null through Unity's operator, which is
        /// the "it unloaded" case - the record keeps its last position and the pin stops moving.
        private static void Follow()
        {
            foreach (Tracked t in Entries.Values)
            {
                if (t.Live == null)
                    continue;

                Vector3 pos = t.Live.position;

                // The save file only needs where it ended up, so the dirty flag uses a coarse
                // threshold while the pin gets the exact spot.
                if ((t.Pos - pos).sqrMagnitude > 4f)
                    _dirty = true;

                t.Pos = pos;
            }
        }

        /// Boats and carts are built pieces, and a piece's ZDO carries its builder's player id in
        /// ZDOVars.s_creator, so "your boat" is the one whose creator is you. Otherwise every
        /// loaded boat and cart in range, other players' included, would get a pin.
        private static bool IsMine(ZNetView nview)
        {
            Player player = Player.m_localPlayer;
            return player != null && nview != null && nview.IsValid() &&
                   nview.GetZDO().GetLong(ZDOVars.s_creator, 0L) == player.GetPlayerID();
        }

        private static void ScanShips()
        {
            if (!Plugin.TrackBoats.Value)
                return;

            // Ship.Instances, NOT Ship.s_currentShips.
            //
            // s_currentShips is Added in OnTriggerEnter and Removed in OnTriggerExit, so it holds
            // only the boats the player is physically standing on (GetLocalShip() returns its last
            // element). Tracking that would pin a boat only while you are aboard, the opposite of
            // the feature. Ship.Instances is filled in OnEnable and emptied in OnDisable, so it is
            // every loaded ship, and it is public so nothing here needs reflection.
            foreach (IMonoUpdater updater in Ship.Instances)
            {
                var ship = updater as Ship;
                if (ship == null || !IsMine(ship.GetComponent<ZNetView>()))
                    continue;
                // The boat's own piece name - "$ship_karve" - so it reads in the player's language
                // and says which boat it is. Ships are built pieces, so this is always present.
                Piece piece = ship.GetComponent<Piece>();
                string label = piece != null && !string.IsNullOrEmpty(piece.m_name)
                    ? piece.m_name
                    : Labels.Prettify(Utils.GetPrefabName(ship.gameObject));
                Note(Kind.Boat, Plugin.TrackedIconFor(Utils.GetPrefabName(ship.gameObject), PinIcon.UtilLongship),
                     ship.GetComponent<ZNetView>(), ship.transform, ship.transform.position, label);
            }
        }

        private static void ScanCarts()
        {
            if (!Plugin.TrackCarts.Value)
                return;
            if (!(VagonInstances?.GetValue(null) is List<Vagon> carts))
                return;

            foreach (Vagon cart in carts)
            {
                if (cart == null || !IsMine(cart.GetComponent<ZNetView>()))
                    continue;
                Note(Kind.Cart, Plugin.CartIcon.Value, cart.GetComponent<ZNetView>(), cart.transform,
                     cart.transform.position,
                     !string.IsNullOrEmpty(cart.m_name) && cart.m_name != "Wagon" ? cart.m_name : "$piece_cart");
            }
        }

        /// A tame is worth a pin if you named it or you can ride it.
        ///
        /// Not every tame: a boar pen holds dozens and pinning all of them would bury the map.
        /// Naming one is the player saying it is not livestock, and a saddle says the same thing.
        private static void ScanTames()
        {
            if (!Plugin.TrackTames.Value)
                return;

            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsDead())
                    continue;

                Tameable tame = c.GetComponent<Tameable>();
                if (tame == null || !tame.IsTamed())
                    continue;

                string given = tame.GetText();
                bool rideable = tame.m_saddle != null;
                if (string.IsNullOrEmpty(given) && !rideable)
                    continue;

                // A given name is plain text the player typed; the species name is a "$token".
                // Both go through Localize on the way to the pin, which only rewrites what follows
                // a '$' and returns anything else untouched. The species fallback keeps a saddled
                // but unnamed Lox readable.
                string label = !string.IsNullOrEmpty(given) ? given : c.m_name;
                Note(Kind.Tame, Plugin.TrackedIconFor(Utils.GetPrefabName(c.gameObject), PinIcon.UtilStar),
                     c.GetComponent<ZNetView>(), c.transform, c.transform.position, label);
            }
        }

        private static void Note(Kind kind, PinIcon icon, ZNetView nview, Transform live, Vector3 pos, string label)
        {
            if (nview == null || !nview.IsValid())
                return;

            string key = nview.GetZDO().m_uid.ToString();
            if (!Entries.TryGetValue(key, out Tracked t))
            {
                t = new Tracked { Kind = kind, Pos = pos, Icon = icon };
                Entries[key] = t;
                _dirty = true;
            }

            t.SeenThisSession = true;
            t.Kind = kind;
            t.Icon = icon;
            t.Live = live;

            if (t.Label != label)
            {
                t.Label = label;
                _dirty = true;
            }
        }

        /// Forgets things that are genuinely gone rather than merely out of range.
        ///
        /// Not loaded is not destroyed - most of the map is not loaded at any moment. Absence is
        /// proof only when standing where the thing should be and not finding it, so that is the
        /// only case that removes an entry.
        private static void Sweep()
        {
            Vector3 here = Player.m_localPlayer.transform.position;
            float forget = Plugin.TrackForgetRadius.Value;
            float sqr = forget * forget;

            List<string> gone = null;
            foreach (KeyValuePair<string, Tracked> pair in Entries)
            {
                Tracked t = pair.Value;
                if (t.SeenThisSession)
                    continue;

                Vector3 d = t.Pos - here;
                if (d.x * d.x + d.z * d.z > sqr)
                    continue;       // out of range - no evidence either way, keep it

                (gone ?? (gone = new List<string>())).Add(pair.Key);
            }

            if (gone == null)
                return;

            foreach (string key in gone)
            {
                if (Entries.TryGetValue(key, out Tracked t))
                {
                    RemovePin(t);
                    Plugin.Log.LogInfo($"Tracked {t.Kind} '{t.Label}' is gone from {t.Pos.x:F0},{t.Pos.z:F0} - forgetting it.");
                }
                Entries.Remove(key);
                _dirty = true;
            }
        }

        private static void Draw()
        {
            Minimap map = Minimap.instance;
            bool moved = false;

            foreach (Tracked t in Entries.Values)
            {
                if (!EnabledFor(t.Kind))
                {
                    if (t.Pin != null)
                    {
                        RemovePin(t);
                        moved = true;
                    }
                    continue;
                }

                if (t.Pin == null)
                {
                    // save:false keeps the pin in memory only, so nothing here can grow the save file.
                    t.Pin = MinimapAccess.AddPinKeepFilter(map, t.Pos, IconFor(t), Labels.ForPin(t.Label), save: false);
                    moved = true;
                    continue;
                }

                // A pin carries its icon in its type, fixed at AddPin, so a changed Boat/Tame/Cart
                // icon setting only reaches a pin already on the map if it is re-added.
                Minimap.PinType wanted = IconFor(t);
                if (t.Pin.m_type != wanted)
                {
                    RemovePin(t);
                    t.Pin = MinimapAccess.AddPinKeepFilter(map, t.Pos, wanted, Labels.ForPin(t.Label), save: false);
                    moved = true;
                    continue;
                }

                // Compared on the ground plane with a metre of slack. A boat bobbing or a tame
                // shuffling changes m_pos every frame, which would set the update flag every frame
                // and make the map rebuild all its pins; height means nothing on a 2D map.
                float dx = t.Pin.m_pos.x - t.Pos.x;
                float dz = t.Pin.m_pos.z - t.Pos.z;
                if (dx * dx + dz * dz > 1f)
                {
                    t.Pin.m_pos = t.Pos;
                    moved = true;
                }
            }

            // Moving m_pos alone changes nothing on screen: UpdatePins only recomputes a pin's
            // screen position when the map asks it to, and this is the flag that asks.
            if (moved)
                PinUpdateRequired?.SetValue(map, true);
        }

        private static bool EnabledFor(Kind kind)
        {
            switch (kind)
            {
                case Kind.Boat: return Plugin.TrackBoats.Value;
                case Kind.Cart: return Plugin.TrackCarts.Value;
                default: return Plugin.TrackTames.Value;
            }
        }

        private static Minimap.PinType IconFor(Tracked t) =>
            CustomIcons.Resolve((int)t.Icon, Minimap.PinType.Icon3);

        private static void RemovePin(Tracked t)
        {
            if (t.Pin != null && Minimap.instance != null)
                Minimap.instance.RemovePin(t.Pin);
            t.Pin = null;
        }

        private static void DropAllPins()
        {
            foreach (Tracked t in Entries.Values)
                RemovePin(t);
        }

        /// Same key as PinRecord: one world and one character per file. Two characters in one
        /// world do not share a boat pin, and two worlds must never share anything.
        private static void EnsureLoaded()
        {
            if (_loaded || string.IsNullOrEmpty(_configDir))
                return;

            World world = ZNet.World;
            PlayerProfile profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            if (world == null || profile == null || string.IsNullOrEmpty(profile.m_filename))
                return;

            string key = $"{Sanitize(world.m_name)}-{world.m_uid}.{Sanitize(profile.m_filename)}";
            _path = Path.Combine(_configDir, $"com.jekkle.valheim.carturmappins.{key}.tracked.txt");
            _loaded = true;
            Entries.Clear();
            ReadFile();
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "unknown";
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        /// "zdoid|kind|icon|x|z|label" per line. A line hand-edited into nonsense is skipped
        /// rather than throwing.
        private static void ReadFile()
        {
            if (!File.Exists(_path))
                return;

            try
            {
                foreach (string line in File.ReadAllLines(_path))
                {
                    string[] parts = line.Split(new[] { '|' }, 6);
                    if (parts.Length < 6)
                        continue;
                    if (!Enum.TryParse(parts[1], out Kind kind))
                        continue;
                    if (!Enum.TryParse(parts[2], out PinIcon icon))
                        icon = PinIcon.UtilStar;
                    if (!float.TryParse(parts[3], out float x) || !float.TryParse(parts[4], out float z))
                        continue;

                    Entries[parts[0]] = new Tracked
                    {
                        Kind = kind,
                        Icon = icon,
                        Pos = new Vector3(x, 0f, z),
                        Label = parts[5],
                    };
                }

                Plugin.Log.LogInfo($"Tracked pins: {Entries.Count} remembered from the last session.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not read {_path}: {e.Message}. Tracked pins start empty.");
                Entries.Clear();
            }
        }

        private static void Save()
        {
            _dirty = false;
            if (string.IsNullOrEmpty(_path))
                return;

            try
            {
                var lines = new List<string>(Entries.Count);
                foreach (KeyValuePair<string, Tracked> pair in Entries)
                {
                    Tracked t = pair.Value;
                    lines.Add($"{pair.Key}|{t.Kind}|{t.Icon}|{t.Pos.x:F1}|{t.Pos.z:F1}|{t.Label}");
                }
                File.WriteAllLines(_path, lines.ToArray());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not write {_path}: {e.Message}");
            }
        }
    }
}
