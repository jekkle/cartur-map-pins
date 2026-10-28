using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// Crowded pins of the same icon collapse into one pin with a count badge (Release 6, the
    /// plan's "clusters"), on the large map, behind General / ClusterPins, off by default.
    ///
    /// Built to stay still, after the zoom work showed pins that travel read as wrong: the pin
    /// that stands for a group is one of the group's own pins, in its own place, and the others
    /// simply fade out where they are (they join Crowding's NotDrawn, so labels, hover and the
    /// patched GetClosestPin already treat them as gone). Nothing is drawn that is not a pin,
    /// nothing is saved, nothing is shared.
    ///
    /// Groups are made in world metres, not on the screen, so panning cannot change them; only
    /// zoom can, and the radius snaps to powers of two with a margin (the same rule as the merge
    /// grid, Crowding.Level) so a zoom resting on a boundary does not flip a group back and forth.
    /// Only pins this mod placed are grouped: the player's own pins, other players' pins, area
    /// pins, pings, markers and ticked pins never are.
    internal static class Clusters
    {
        /// Fewer than this many real pins is not worth a badge.
        private const int MinimumPins = 3;
        private const float RadiusInIcons = 1.1f;
        private const float HysteresisSteps = 0.17f;

        private static readonly Dictionary<Minimap.PinData, int> Leaders = new Dictionary<Minimap.PinData, int>();
        private static readonly List<Minimap.PinData> Members = new List<Minimap.PinData>();
        private static readonly Dictionary<Minimap.PinData, List<Minimap.PinData>> Groups = new Dictionary<Minimap.PinData, List<Minimap.PinData>>();

        /// The pins a badge stands for, itself first (the popup lists them).
        public static IReadOnlyList<Minimap.PinData> GroupOf(Minimap.PinData leader) =>
            leader != null && Groups.TryGetValue(leader, out List<Minimap.PinData> group) ? group : null;
        private static int _level = int.MinValue;
        private static string _signature;

        /// The pilot sets this to group its unsaved test pins as well.
        internal static bool IncludeAll;

        public static bool Active => Plugin.ClusterPins != null && Plugin.ClusterPins.Value;

        /// How many real pins this pin stands for: 1 for an ordinary pin.
        public static int CountOf(Minimap.PinData pin) =>
            pin != null && Leaders.TryGetValue(pin, out int count) ? count : 1;

        internal static void Stats(out int leaders, out int members, out int represented)
        {
            leaders = Leaders.Count;
            members = Members.Count;
            represented = 0;
            foreach (int c in Leaders.Values)
                represented += c;
        }

        /// Run after Crowding.Measure, before the labels: members join NotDrawn, leaders keep
        /// their count.
        public static void Measure(List<Minimap.PinData> pins)
        {
            Minimap map = Minimap.instance;
            if (!Active || map == null || map.m_mode != Minimap.MapMode.Large || pins == null)
            {
                if (Leaders.Count > 0 || Members.Count > 0)
                {
                    Leaders.Clear();
                    Members.Clear();
                    Groups.Clear();
                    _signature = null;
                }
                return;
            }

            float raw = Crowding.IconMetres(map);
            if (raw <= 0f)
                return;
            float log = Mathf.Log(raw, 2f);
            if (_level == int.MinValue || Mathf.Abs(log - _level) > 0.5f + HysteresisSteps)
                _level = Mathf.RoundToInt(log);

            // Everything that decides the groups. When none of it changed, the groups are reused:
            // dragging the map asks for a pass every frame and the groups are the same each time.
            string signature = $"{pins.Count}|{_level}|{PinFilter.Text}|{PinHiding.Cycle}|{Plugin.HiddenPins.Value}|{Plugin.HidePinsBeyond.Value}|{Plugin.MergeRepeatedPins.Value}|{IncludeAll}";
            if (signature != _signature)
            {
                _signature = signature;
                Rebuild(pins, Mathf.Pow(2f, _level) * RadiusInIcons);
            }

            foreach (Minimap.PinData member in Members)
                Crowding.MarkNotDrawn(member);
        }

        private static void Rebuild(List<Minimap.PinData> pins, float radius)
        {
            Leaders.Clear();
            Members.Clear();
            Groups.Clear();
            if (!IncludeAll)
                PinRecord.IndexByPosition();

            var byIcon = new Dictionary<Minimap.PinType, List<Minimap.PinData>>();
            foreach (Minimap.PinData pin in pins)
            {
                if (pin == null || Crowding.OutOfSight(pin) || pin.m_worldSize > 0f || pin.m_ownerID != 0L || pin.m_checked)
                    continue;
                if (pin.m_type == Minimap.PinType.Death || pin.m_type == Minimap.PinType.Ping || pin.m_type == Minimap.PinType.Bed)
                    continue;
                if (!IncludeAll && !PinRecord.IsIndexedOurs(pin))
                    continue;
                // A pin the search matches leaves its group: hiding it behind a badge while it is
                // being looked for would defeat the search.
                if (PinFilter.Active && PinFilter.Matches(pin.m_name))
                    continue;
                if (!byIcon.TryGetValue(pin.m_type, out List<Minimap.PinData> list))
                    byIcon[pin.m_type] = list = new List<Minimap.PinData>();
                list.Add(pin);
            }

            foreach (List<Minimap.PinData> list in byIcon.Values)
            {
                if (list.Count < MinimumPins)
                    continue;
                // Position order, not list order: the leader of a group must not change because a
                // pin elsewhere was added.
                list.Sort((a, b) => a.m_pos.x != b.m_pos.x ? a.m_pos.x.CompareTo(b.m_pos.x) : a.m_pos.z.CompareTo(b.m_pos.z));

                var cells = new Dictionary<long, List<int>>();
                for (int i = 0; i < list.Count; i++)
                {
                    long key = CellKey(list[i].m_pos, radius);
                    if (!cells.TryGetValue(key, out List<int> bucket))
                        cells[key] = bucket = new List<int>();
                    bucket.Add(i);
                }

                var taken = new bool[list.Count];
                var near = new List<int>();
                for (int i = 0; i < list.Count; i++)
                {
                    if (taken[i])
                        continue;
                    taken[i] = true;
                    near.Clear();
                    int cx = Mathf.FloorToInt(list[i].m_pos.x / radius), cz = Mathf.FloorToInt(list[i].m_pos.z / radius);
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!cells.TryGetValue(Pack(cx + dx, cz + dz), out List<int> bucket))
                                continue;
                            foreach (int j in bucket)
                            {
                                if (taken[j])
                                    continue;
                                float ex = list[j].m_pos.x - list[i].m_pos.x, ez = list[j].m_pos.z - list[i].m_pos.z;
                                if (ex * ex + ez * ez <= radius * radius)
                                    near.Add(j);
                            }
                        }
                    }

                    int weight = Crowding.MergedCount(list[i]);
                    foreach (int j in near)
                        weight += Crowding.MergedCount(list[j]);
                    if (1 + near.Count < MinimumPins)
                        continue;

                    var group = new List<Minimap.PinData> { list[i] };
                    foreach (int j in near)
                    {
                        taken[j] = true;
                        Members.Add(list[j]);
                        group.Add(list[j]);
                    }
                    Leaders[list[i]] = weight;
                    Groups[list[i]] = group;
                }
            }
        }

        private static long CellKey(Vector3 pos, float cell) =>
            Pack(Mathf.FloorToInt(pos.x / cell), Mathf.FloorToInt(pos.z / cell));

        private static long Pack(int x, int z) => ((long)x << 32) ^ (uint)z;
    }

    /// The count on a group's pin: a small round badge on the icon's lower right, drawn as part
    /// of the pin's own marker, so it moves, fades and scales with it and goes when the marker
    /// does. Plain ring for 2-9, heavier for 10-99, gold for 100 and more (the plan's tiers; the
    /// owner picks the final look from options).
    internal static class ClusterBadge
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, GameObject> Badges =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Image, GameObject>();

        public static void Sync(Minimap.PinData pin)
        {
            if (pin?.m_iconElement == null)
                return;
            bool wanted = Clusters.Active && Clusters.CountOf(pin) > 1;
            if (!wanted)
            {
                if (Badges.TryGetValue(pin.m_iconElement, out GameObject old))
                {
                    if (old != null)
                        Object.Destroy(old);
                    Badges.Remove(pin.m_iconElement);
                }
                return;
            }

            if (!Badges.TryGetValue(pin.m_iconElement, out GameObject badge) || badge == null)
            {
                badge = Create(pin.m_iconElement.transform);
                Badges.Remove(pin.m_iconElement);
                Badges.Add(pin.m_iconElement, badge);
            }
            Apply(badge, Clusters.CountOf(pin));
        }

        private static GameObject Create(Transform marker)
        {
            var go = new GameObject("CarturBadge", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(marker, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-3f, 5f);
            rt.sizeDelta = new Vector2(20f, 20f);
            Image disc = go.GetComponent<Image>();
            disc.sprite = IconGrid.Disc();
            disc.color = new Color(0.07f, 0.06f, 0.05f, 0.95f);
            disc.raycastTarget = false;

            var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ring.transform.SetParent(go.transform, false);
            var rrt = (RectTransform)ring.transform;
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;
            Image rim = ring.GetComponent<Image>();
            rim.sprite = IconGrid.Ring();
            rim.raycastTarget = false;

            var text = new GameObject("Count", typeof(RectTransform));
            text.transform.SetParent(go.transform, false);
            var trt = (RectTransform)text.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(-4f, 0f);
            trt.offsetMax = new Vector2(4f, 0f);
            TextMeshProUGUI label = Fonts.AddLabel(text);
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 11f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            return go;
        }

        private static void Apply(GameObject badge, int count)
        {
            Color rim = count >= 100 ? IconGrid.SelectedGold : count >= 10 ? IconGrid.Bronze : new Color(0.85f, 0.82f, 0.74f, 1f);
            Image ring = badge.transform.Find("Ring").GetComponent<Image>();
            if (ring.color != rim)
                ring.color = rim;
            TextMeshProUGUI label = badge.GetComponentInChildren<TextMeshProUGUI>();
            string text = count > 99 ? "99+" : count.ToString();
            if (label.text != text)
                label.text = text;
            label.color = Color.white;
        }
    }

    /// A click on a group's pin must not do what it would to a single pin (tick it off, delete
    /// it): it stands for several. Left click zooms in on it instead. Priority First, so this runs
    /// before the picker's own click patch.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
    internal static class Patch_Cluster_LeftClick
    {
        private static readonly System.Reflection.FieldInfo MapOffset = AccessTools.Field(typeof(Minimap), "m_mapOffset");

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Minimap __instance)
        {
            Minimap.PinData pin = PinHover.Current;
            if (!Clusters.Active || pin == null || Clusters.CountOf(pin) <= 1 ||
                ZInput.GetKey(KeyCode.LeftShift, false) || ZInput.GetKey(KeyCode.RightShift, false))
                return true;

            // Half the zoom, centred on the group's pin: the group opens up where you clicked.
            __instance.LargeZoom = Mathf.Max(__instance.m_minZoom, __instance.LargeZoom * 0.5f);
            if (Player.m_localPlayer != null && MapOffset != null)
                MapOffset.SetValue(__instance, pin.m_pos - Player.m_localPlayer.transform.position);
            return false;
        }
    }
}
