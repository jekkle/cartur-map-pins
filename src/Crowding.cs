using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// Decides how big each pin is drawn and whether it is drawn at all.
    ///
    /// Three inputs, read once per pin update:
    ///   - how many pins share a patch of ground (crowding),
    ///   - how far the map is zoomed out,
    ///   - how far the pin is from the player.
    ///
    /// Everything here is decided in WORLD metres, not screen pixels, and over every pin rather
    /// than only the ones currently on screen. That is the whole reason the answers hold still.
    ///
    /// The first version measured crowding between icons where they had landed on screen, which
    /// seemed obviously right - it is what the eye sees. It made the map re-roll its decisions
    /// constantly: a cell boundary is fixed to the screen, so panning one pixel slides every pin
    /// towards a different cell, and a pin crossing one changes what its neighbours are worth.
    /// Pins also lose their icons the moment they scroll off, so the set being measured changed
    /// every time the view moved. Between the two, no two looks at the same map agreed.
    ///
    /// A patch of ground does not move when you drag the map, and a pin's position in the world
    /// does not depend on whether it is currently visible. So the only thing that changes an
    /// answer now is the zoom, which is the one time a change is wanted.
    ///
    /// Never below half size. Past that a pin stops being readable, and a cluster of unreadable
    /// pins is worse than an overlapping one.
    internal static class Crowding
    {
        /// Used only until a real icon has been seen. 32px is what the large map measured, but any
        /// UI scale moves it, so it is read off a live icon rather than trusted.
        private const float FallbackIconPixels = 32f;

        private const float Floor = 0.5f;

        private static float _iconPixels = FallbackIconPixels;
        private static float _cellMetres = 1f;
        private static float _zoom = 1f;

        /// Reused rather than allocated per pass: this runs on every pin update, and a dictionary
        /// a frame is the kind of thing that shows up as stutter half an hour later.
        private static readonly Dictionary<long, int> Counts = new Dictionary<long, int>();

        /// How many pins on the whole map share each name. A name appearing once says more than
        /// one appearing forty times, which is what decides who keeps their size in a cluster.
        private static readonly Dictionary<string, int> NameCounts = new Dictionary<string, int>();

        /// The rarest pin in each cell, which is the one that keeps full size.
        private static readonly Dictionary<long, Minimap.PinData> Winners =
            new Dictionary<long, Minimap.PinData>();

        /// The one pin drawn for each name in each cell. Key mixes the cell with the name hash.
        private static readonly Dictionary<long, Minimap.PinData> Survivors =
            new Dictionary<long, Minimap.PinData>();

        /// Pins this pass decided not to draw - too far from the player, or a repeat of one drawn
        /// beside it. Held by reference; PinData is a plain class with no equality of its own, so
        /// this is identity, which is what is wanted.
        private static readonly HashSet<Minimap.PinData> NotDrawn = new HashSet<Minimap.PinData>();

        /// Which patch of ground a pin stands on. World metres, so dragging the map cannot change
        /// the answer and neither can scrolling a pin off the edge.
        private static long CellFor(Minimap.PinData pin)
        {
            int x = Mathf.RoundToInt(pin.m_pos.x / _cellMetres);
            int z = Mathf.RoundToInt(pin.m_pos.z / _cellMetres);
            return ((long)x << 32) ^ (uint)z;
        }

        private static long MergeKey(long cell, string name) => cell * 31 + name.GetHashCode();

        /// Which of two pins standing in the same place is kept, when nothing else separates them.
        ///
        /// Decided on world position rather than on whichever came first in the list. Order is
        /// only stable while the same pins are in play, and they are not: markers come and go as
        /// the view moves, so "first one seen" handed the job to a different pin each time and the
        /// survivor visibly swapped for no reason. Position is a property of the pin.
        private static bool Precedes(Minimap.PinData a, Minimap.PinData b)
        {
            if (!Mathf.Approximately(a.m_pos.x, b.m_pos.x))
                return a.m_pos.x < b.m_pos.x;
            return a.m_pos.z < b.m_pos.z;
        }

        /// Counts in steps rather than one for one.
        ///
        /// A pin near a cell boundary flips its neighbour's count between two and three, and a
        /// size that answers every such flip is the shrinking and growing you can see on the map.
        /// Steps mean the common wobble changes nothing: it takes a real change in how crowded a
        /// spot is to move a pin to the next size down.
        private static float ScaleForCount(int count)
        {
            if (count <= 1)
                return 1f;
            if (count == 2)
                return 0.9f;   // a pair is the common case: separate them without shrinking much
            if (count <= 4)
                return 0.8f;
            if (count <= 6)
                return 0.65f;
            return Floor;
        }

        /// How far out the map is zoomed, as a percentage: 0 is fully zoomed in, 100 is the whole
        /// world. The raw zoom value is not a usable number to set a threshold in - it runs from
        /// m_minZoom 0.015 to m_maxZoom 1, both read from the live component - so every setting
        /// that takes a zoom takes it in these terms instead.
        public static float ZoomOutPercent() => ZoomOutPercent(Minimap.instance);

        public static float ZoomOutPercent(Minimap map)
        {
            if (map == null)
                return 0f;

            float zoom = map.m_mode == Minimap.MapMode.Large ? map.LargeZoom : map.SmallZoom;
            return Mathf.InverseLerp(map.m_minZoom, map.m_maxZoom, zoom) * 100f;
        }

        /// Full size until the map is zoomed out past the configured point, then down to the floor
        /// at fully out.
        private static float ZoomScale(Minimap map)
        {
            float floor = Mathf.Clamp(Plugin.ZoomedOutPinScale.Value, 0.2f, 1f);
            if (map == null || floor >= 1f)
                return 1f;

            float start = Mathf.Clamp(Plugin.ShrinkPinsFromZoom.Value, 0f, 100f);
            float past = Mathf.InverseLerp(start, 100f, ZoomOutPercent(map));
            return Mathf.Lerp(1f, floor, past);
        }

        /// How much ground one icon covers, in metres.
        ///
        /// The map image shows a slice of the map texture whose height is exactly the zoom
        /// (uvRect.height = LargeZoom), and that texture spans m_textureSize * m_pixelSize metres
        /// - all three read off the live component rather than assumed, since the defaults in the
        /// assembly are not what a running game holds. Divide by the height the image occupies on
        /// screen and one screen pixel has a size in metres; an icon is that many times wider.
        private static float CellMetres(Minimap map)
        {
            RawImage image = map.m_mode == Minimap.MapMode.Large ? map.m_mapImageLarge : map.m_mapImageSmall;
            float zoom = map.m_mode == Minimap.MapMode.Large ? map.LargeZoom : map.SmallZoom;

            float height = image != null ? image.rectTransform.rect.height : 0f;
            float span = map.m_textureSize * map.m_pixelSize;
            if (height < 1f || span <= 0f || zoom <= 0f)
                return _cellMetres;

            return _iconPixels * (zoom * span / height);
        }

        /// Squared metres past which a pin is not drawn, or 0 when the setting is off. Squared
        /// here so the per-pin test needs no square root.
        private static float HideRangeSqr()
        {
            float range = Plugin.HidePinsBeyond.Value;
            return range > 0f ? range * range : 0f;
        }

        public static void Measure(List<Minimap.PinData> pins)
        {
            Counts.Clear();
            NameCounts.Clear();
            Winners.Clear();
            Survivors.Clear();
            NotDrawn.Clear();

            Minimap map = Minimap.instance;
            _zoom = ZoomScale(map);
            if (map == null || pins == null)
                return;

            // The icon size has to come from a live icon, so it is taken from whatever is on
            // screen - but only the size. Nothing else below looks at the screen at all.
            //
            // Smallest wins, not last seen: vanilla draws a pin flagged m_doubleSize at twice the
            // normal width, so one boss pin was setting the grid for everything around it.
            float smallest = float.MaxValue;
            foreach (Minimap.PinData pin in pins)
            {
                if (pin?.m_iconElement == null || !pin.m_iconElement.gameObject.activeInHierarchy)
                    continue;
                float width = pin.m_iconElement.rectTransform.rect.width;
                if (width > 1f && width < smallest)
                    smallest = width;
            }
            if (smallest < float.MaxValue)
                _iconPixels = smallest;

            _cellMetres = CellMetres(map);
            if (_cellMetres <= 0f)
                return;

            float hideSqr = HideRangeSqr();
            Player player = Player.m_localPlayer;
            bool hiding = hideSqr > 0f && player != null;
            Vector3 origin = player != null ? player.transform.position : Vector3.zero;

            bool crowding = Plugin.ShrinkCrowdedPins.Value;
            bool merging = Plugin.MergeRepeatedPins.Value;

            // Pass one: everything that is not drawn at all, and who wins each patch of ground.
            // Every pin, not only the ones with an icon right now - a pin just off the edge is
            // still standing in that patch, and leaving it out was half of why the map kept
            // changing its mind.
            foreach (Minimap.PinData pin in pins)
            {
                if (pin == null)
                    continue;

                if (IsVanillaSpawnDuplicate(pin))
                {
                    NotDrawn.Add(pin);
                    continue;
                }

                if (hiding)
                {
                    float dx = pin.m_pos.x - origin.x;
                    float dz = pin.m_pos.z - origin.z;
                    if (dx * dx + dz * dz > hideSqr)
                    {
                        // Out of range before counting, not after: a pin nobody is going to see
                        // must not shrink the pins around it.
                        NotDrawn.Add(pin);
                        continue;
                    }
                }

                if (!merging)
                    continue;

                long key = MergeKey(CellFor(pin), pin.m_name ?? string.Empty);
                if (!Survivors.TryGetValue(key, out Minimap.PinData standing) || Precedes(pin, standing))
                    Survivors[key] = pin;
            }

            if (!crowding && !merging)
                return;

            // Pass two: the repeats step aside, and what is left is counted.
            foreach (Minimap.PinData pin in pins)
            {
                if (pin == null || NotDrawn.Contains(pin))
                    continue;

                string name = pin.m_name ?? string.Empty;
                long cell = CellFor(pin);

                if (merging)
                {
                    // One icon per name per patch. A tin field is fifteen separate deposits and so
                    // fifteen separate pins, every one reading "Tin", and at map scale they are a
                    // single white blob you can neither count nor click.
                    if (Survivors.TryGetValue(MergeKey(cell, name), out Minimap.PinData standing) &&
                        !ReferenceEquals(standing, pin))
                    {
                        NotDrawn.Add(pin);
                        continue;
                    }
                }

                if (!crowding)
                    continue;

                Counts.TryGetValue(cell, out int count);
                Counts[cell] = count + 1;

                NameCounts.TryGetValue(name, out int seen);
                NameCounts[name] = seen + 1;
            }

            if (!crowding)
                return;

            // Pass three, once the name counts are complete: whoever is rarest in a patch keeps
            // full size. Shrinking every pin in a cluster equally loses the one pin that was worth
            // noticing - CRYPT goes down with the forty CHESTs standing around it.
            foreach (Minimap.PinData pin in pins)
            {
                if (pin == null || NotDrawn.Contains(pin))
                    continue;

                long cell = CellFor(pin);
                if (!Winners.TryGetValue(cell, out Minimap.PinData best))
                {
                    Winners[cell] = pin;
                    continue;
                }

                int mine = Rarity(pin);
                int theirs = Rarity(best);
                if (mine < theirs || (mine == theirs && Precedes(pin, best)))
                    Winners[cell] = pin;
            }
        }

        private static int Rarity(Minimap.PinData pin)
        {
            NameCounts.TryGetValue(pin.m_name ?? string.Empty, out int count);
            return count;
        }

        /// True when this pass decided not to draw this pin at all - out of range, or a repeat.
        public static bool OutOfSight(Minimap.PinData pin) => NotDrawn.Contains(pin);

        /// How much to shrink this pin: zoom always, crowding on top of it, never past half.
        public static float ScaleFor(Minimap.PinData pin)
        {
            if (pin == null)
                return _zoom;

            long cell = CellFor(pin);
            if (!Counts.TryGetValue(cell, out int count) || count <= 1)
                return _zoom;

            // The rarest pin in the patch is the reason you are looking at the patch at all.
            if (Winners.TryGetValue(cell, out Minimap.PinData best) && ReferenceEquals(best, pin))
                return _zoom;

            return _zoom * ScaleForCount(count);
        }

        /// Every number that decides a pin size, in one line.
        ///
        /// Here because the map twice failed to look like the settings said it should, and one
        /// measurement ends an argument that another edit to a guess will not. Read by the
        /// carturpins_zoom command; costs nothing until somebody types it.
        public static string Describe()
        {
            Minimap map = Minimap.instance;
            if (map == null)
                return "no map";

            float zoom = map.m_mode == Minimap.MapMode.Large ? map.LargeZoom : map.SmallZoom;
            return $"mode={map.m_mode} zoom={zoom:F4} ({ZoomOutPercent(map):F0}% out) "
                 + $"min={map.m_minZoom:F4} max={map.m_maxZoom:F4} "
                 + $"shrinkFrom={Plugin.ShrinkPinsFromZoom.Value:F0}% floor={Plugin.ZoomedOutPinScale.Value:F2} "
                 + $"=> zoomScale={_zoom:F3}, icon={_iconPixels:F0}px, cell={_cellMetres:F0}m, "
                 + $"cells={Counts.Count}, notDrawn={NotDrawn.Count}";
        }

        /// Vanilla's own spawn marker, standing on a bed we have already pinned.
        ///
        /// UpdateProfilePins keeps one unsaved PinType.Bed pin on the current spawn point, and our
        /// Home pin sits on the same spawn point by design - PinHome is given GetSpawnPoint(), the
        /// same position vanilla uses - so the two land exactly on top of each other.
        ///
        /// Only ever vanilla's: m_save is false on the profile pin and true on every pin of ours,
        /// so the test cannot take out the pin it is protecting. One unsaved Bed pin exists at a
        /// time, so the record lookup runs at most once a pass.
        private static bool IsVanillaSpawnDuplicate(Minimap.PinData pin)
        {
            if (pin.m_save || pin.m_type != Minimap.PinType.Bed)
                return false;

            Plugin.CategorySettings home = Plugin.SettingsFor(PinCategory.Home);
            if (home == null || !home.Enabled.Value)
                return false;

            return PinRecord.HasCategoryNear(PinCategory.Home, pin.m_pos, home.DedupeRadius.Value);
        }
    }
}
