using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    /// Keeps the map readable where labels pile up, and gives every hidden one back under the
    /// cursor.
    ///
    /// At the zoom you get from pressing M, a well-explored map is a wall of overlapping words
    /// (COPPER over CRYPT, TROLL CAVE over CHEST). Two names on top of each other carry less than
    /// either alone, so the collision is resolved rather than drawn.
    ///
    /// The survivor is the rarer name on your map: CHEST appears forty times and says little,
    /// CRYPT appears twice and says a lot. The icons are untouched, so the chest is still there.
    ///
    /// A pin under the cursor always shows its name, so sweeping the mouse across a cluster
    /// reads it out.
    internal static class LabelCrowding
    {
        /// How close the cursor has to be to a pin before its name is forced back on.
        internal const float RevealRadius = 60f;

        private static readonly List<Minimap.PinNameData> Shown = new List<Minimap.PinNameData>();

        /// Where each shown name's words were when it was added, and a grid of them by screen cell, so
        /// "does this overlap a shown name" looks at the few names near it instead of measuring every
        /// one. Measured 2026-11-03 (Release): the pass cost 5 ms at 1,000 pins, 62 ms at 5,000, 360 ms
        /// at 20,000 - the other stages grew in step with the pin count and this one grew faster, and
        /// RectOf on every shown name for every name asked was the part that did.
        private static readonly List<Rect> ShownRects = new List<Rect>();
        private static readonly Dictionary<long, List<int>> Grid = new Dictionary<long, List<int>>();
        private static readonly Stack<List<int>> Spare = new Stack<List<int>>();
        private const float GridCell = 128f;
        private static readonly List<Ranked> Ordered = new List<Ranked>();
        private static readonly Dictionary<string, int> NameCounts = new Dictionary<string, int>();

        /// A label with its rarity read once, so the comparator does no dictionary lookups.
        private struct Ranked
        {
            public Minimap.PinData Pin;
            /// 1 a pin of a kind worth crossing the map for (bosses, dungeons, camps, traders, home,
            /// portals), 2 the rest. (A search hides every non-match before this, so matches need no class.) Claims space before rarity is looked at, so
            /// forty chests cannot take a portal's words.
            public int Class;
            public int Rarity;
        }

        /// Cached, because sorting with a lambda allocates a comparer every pass otherwise.
        ///
        /// List.Sort is unstable and the old comparer returned 0 for every tie, so equally rare
        /// names (most of them) claimed space in a different order from one pass to the next and
        /// the surviving label flickered. Ties fall back to world position, a property of the pin,
        /// the same rule Crowding.Precedes uses.
        private static readonly Comparison<Ranked> ByRarity = (a, b) =>
        {
            int byClass = a.Class.CompareTo(b.Class);
            if (byClass != 0)
                return byClass;
            int byRarity = a.Rarity.CompareTo(b.Rarity);
            if (byRarity != 0)
                return byRarity;
            int byX = a.Pin.m_pos.x.CompareTo(b.Pin.m_pos.x);
            return byX != 0 ? byX : a.Pin.m_pos.z.CompareTo(b.Pin.m_pos.z);
        };

        public static void Apply(List<Minimap.PinData> pins)
        {
            // Nothing is put back here, deliberately. UpdatePins writes every label visibility
            // every pass (LargeZoom below m_showNamesZoom for the large map, off for everything
            // else), so a label wanted shown is already shown by the time we run. Re-showing from
            // this Postfix would override that decision and light collided labels at every zoom,
            // and on the small minimap. Hiding is ours to do; showing is the game's.
            ClearShown();
            Ordered.Clear();
            NameCounts.Clear();
#if DIAGNOSTICS
            GroupedHidden.Clear();
#endif
            ScaleLabels(pins);

            // ShowPinLabels is this mod's switch for this mod's names. Through 1.8.9 it hid every
            // pin's name, so a player who turned it off also lost the names of their own pins and
            // the game's (GitHub/Discord report, bkpenther). Only pins this mod placed are hidden;
            // the zoom and collision rules below still cover the rest as before.
            if (!Plugin.ShowPinLabels.Value)
                HideAll(pins, oursOnly: true);

            // Vanilla's rule is dead on the live component: it hides names while
            // "LargeZoom < m_showNamesZoom", and m_showNamesZoom is 2.0 where the highest zoom the
            // map allows is 1.0, so the test is true at every zoom and no name is ever hidden by
            // it. Measured at runtime; the 0.5 in the assembly is only the field initializer.
            // A band round the limit (10% of it, at least a quarter point) so a zoom resting on the
            // line does not switch every name on and off. One point was six wheel steps each way at
            // a limit of 3 (totya, 2.1.2): a wheel step is about a third of a point.
            float limit = Plugin.HideLabelsFromZoom.Value;
            float band = Mathf.Max(0.25f, limit * 0.1f);
            float percent = Crowding.ZoomOutPercent();
            _zoomHidden = limit < 100f && (_zoomHidden ? percent > limit - band : percent > limit + band);
            if (_zoomHidden || PinHiding.LabelsHidden)
            {
                // Only the pin under the pointer, as with Shift+H. Within 60 px of the pointer, as
                // this was before 2.1.2, was every name of a crowded patch at once when zoomed out
                // past the limit (measured 2026-11-02: 83 names stacked with the pointer on the
                // cluster, 0 with it parked).
                HideAll(pins, oursOnly: false, onlyPointed: true);
                return;
            }

            // Before the early return below: a pin that is not drawn must not leave its name
            // floating over the map, whatever the collision setting says.
            HideOutOfSight(pins);

            bool hideColliding = Plugin.HideCollidingLabels.Value;
            if (!hideColliding && !PinFilter.Active)
                return;

            int direction = ZoomDirection();

            // Only a pin that stands for several loses its name, and only from half way out to the
            // zoom where every name goes: close in, the overlap rule below decides alone. totya
            // (2.1.1): "Chest" stayed hidden beside "Log Cabin" with room for its text. Measured
            // 2026-11-02 on 247 names hidden as grouped: 40% would not have overlapped a shown
            // name, a share that fell from about half at 0-5% zoomed out to a quarter at 15%.
            bool groupRule = Plugin.HideGroupedLabels.Value &&
                             Crowding.ZoomOutPercent() > Plugin.HideLabelsFromZoom.Value * 0.5f;
            bool smart = Plugin.SmartLabelPlacement.Value;
            foreach (Minimap.PinData pin in pins)
            {
                Minimap.PinNameData name = pin?.m_NamePinData;
                if (name?.PinNameRectTransform == null || name.PinNameGameObject == null)
                    continue;
                if (!name.PinNameGameObject.activeInHierarchy || string.IsNullOrEmpty(pin.m_name))
                    continue;

                // A name HideOutOfSight has just asked to fade out is still active for a few
                // frames; ranking it here would Show it again, and the names of hidden pins
                // (the hide key, merged repeats) never went away (owner, 2026-10-27).
                if (Crowding.OutOfSight(pin))
                    continue;

                // A pin that stands for a group (a cluster badge, a merged repeat) or sits in a
                // crowded patch (the ones the mod already shrinks) draws no name; pointing at it
                // gives the name back. A cluster's badge is the exception to the exception: its
                // hover popup already lists every name in it, so its own label stays off.
                //
                // Grouped is decided per grid level, and a pin can be alone at one level and
                // crowded at the next (the rarest-first winner of a patch changes as repeats
                // merge, measured: one pin grouped at zoom 0.065, free at 0.134, grouped again
                // at 0.096 on the way back), so its name went off and on as the zoom passed
                // levels. Zooming in only ever brings names back, so a name that is up stays up
                // until the zoom rests, then this decides it; the collision rule below does the
                // same for names that overlap.
                bool grouped = groupRule && Crowding.StandsForSeveral(pin) &&
                    !(ReferenceEquals(pin, PinHover.Current) && Clusters.CountOf(pin) <= 1);
                if (grouped && direction < 0 && Fades.TryGetValue(name.PinNameGameObject, out Fade up) && up.Want)
                    grouped = false;
                if (grouped)
                {
                    Hide(name);
#if DIAGNOSTICS
                    GroupedHidden.Add(pin);
#endif
                    continue;
                }

                // A name that does not match the search is noise while searching, and the pin it
                // belongs to is still on the map, dimmed.
                if (!PinFilter.Matches(pin.m_name))
                {
                    Hide(name);
                    continue;
                }

                NameCounts.TryGetValue(pin.m_name, out int count);
                NameCounts[pin.m_name] = count + 1;
                string key = PinRecord.KeyOfIndexed(pin);
                Ordered.Add(new Ranked { Pin = pin, Class = key != null && PriorityZoom.TierOfKey(key) == 0 ? 1 : 2 });
            }

            if (Ordered.Count == 0)
            {
#if DIAGNOSTICS
                ReportGroupedHidden();
#endif
                return;
            }

            for (int i = 0; i < Ordered.Count; i++)
            {
                Ranked ranked = Ordered[i];
                ranked.Rarity = NameCounts[ranked.Pin.m_name];
                Ordered[i] = ranked;
            }

            // Rarest first, so the name that says the most claims its space before the ones that
            // repeat all over the map get a chance to sit on it.
            Ordered.Sort(ByRarity);

            Vector2 cursor = CursorIn(Ordered[0].Pin.m_iconElement);

            // Which way the zoom is going decides which way a name may change. Rarest-first
            // overlap resolution cascades: B hides behind a rarer C, which frees A's spot, A comes
            // back, then collides with C and goes again (pilot mapsweep timeline, Troll Cave:
            // off/on/off/on/off in one zoom-out). Zooming out squeezes names together, so they
            // may only go; zooming in spreads or keeps them, so a name already up stays and a hidden
            // one comes back only where nothing that is up overlaps it.
            //
            // Standing still (direction 0) used to be the zooming-in rule, so a name that had been
            // forced up - by the cursor, or by a box too small to see the overlap - stayed up
            // forever, on top of its neighbour. At rest nothing is moving, so there is no cascade
            // to guard against: every name is decided again, in rarity order, with the same margin
            // hysteresis, and the answer is the same on every pass until something changes.
            if (hideColliding && direction < 0)
            {
                foreach (Ranked ranked in Ordered)
                {
                    Minimap.PinNameData name = ranked.Pin.m_NamePinData;
                    if (Fades.TryGetValue(name.PinNameGameObject, out Fade f) && f.Want)
                        AddShown(name);
                }
            }

            // The ONE pin nearest the cursor, within RevealRadius. Every pin within the radius used
            // to be forced on, so pointing at a pair of close pins lit both names on top of each
            // other (the reveal is not a collision-checked label).
            Minimap.PinData pointedAt = null;
            float nearestPx = RevealRadius;
            foreach (Ranked ranked in Ordered)
            {
                UnityEngine.UI.Image icon = ranked.Pin.m_iconElement;
                if (icon == null)
                    continue;
                float px = Vector2.Distance((Vector2)icon.rectTransform.localPosition, cursor);
                if (px < nearestPx)
                {
                    nearestPx = px;
                    pointedAt = ranked.Pin;
                }
            }

            // The pin the cursor is ON (its icon, not merely near it) takes its words' space before
            // anyone else's: every other name that overlaps them is hidden while the cursor stays.
            // Through 2.1.0 the revealed name was only forced on, so it was drawn over whichever
            // earlier-ranked or zoom-held name already sat there (totya: two icons on one row,
            // worst when pointing at one; measured 2.1.1: Sunken Crypt over Portal by 38x16 px).
            //
            // The nearest pin within the reveal radius counts, not only one the cursor is ON
            // (PinHover.Current, null between icons). With the cursor between two icons the
            // revealed name was forced on with no overlap check and drew over its neighbour
            // (totya, 2.1.2: Kis faház over Láda at an intermediate zoom). Read from the code, not
            // yet measured in game.
            bool hasFocus = pointedAt != null;
            Rect focusRect = hasFocus ? RectOf(pointedAt.m_NamePinData) : default;

            foreach (Ranked ranked in Ordered)
            {
                Minimap.PinData pin = ranked.Pin;
                Minimap.PinNameData name = pin.m_NamePinData;
                Rect mine = RectOf(name);

                // Under the cursor wins outright, including over an earlier label: pointing at a
                // pin is asking what it is.
                bool revealed = ReferenceEquals(pin, pointedAt);

                // A name moved off its default place goes back once the default is free with the
                // margin to spare; until then it stays where it is (no flipping between sides).
                if (smart && hideColliding && !revealed && direction == 0 && Displaced(name, out Vector2 moved))
                {
                    Rect home = new Rect(mine.x - moved.x, mine.y - moved.y, mine.width, mine.height);
                    if (!Collides(Grown(home, Margin)))
                    {
                        name.PinNameText.rectTransform.anchoredPosition = Vector2.zero;
                        mine = home;
                    }
                }

                if (hasFocus && !revealed && mine.Overlaps(focusRect))
                {
                    Hide(name);
                    continue;
                }

                // Collides only when collision hiding is on: an active search alone must dim
                // non-matches, not also hide labels that overlap.
                // Hysteresis: a name already up keeps its place unless it overlaps by more than
                // a margin, and a hidden one needs that margin clear before it comes back, so two
                // names grazing each other while the zoom moves do not take turns.
                bool known = Fades.TryGetValue(name.PinNameGameObject, out Fade had);
                bool wasShown = !known || had.Want;
                if (hideColliding && known && !revealed)
                {
                    if (direction < 0 && wasShown)
                    {
                        Show(name);   // already in Shown
                        continue;
                    }
                    if (direction > 0 && !wasShown)
                    {
                        Hide(name);
                        continue;
                    }
                }
                Rect test = Grown(mine, wasShown ? -Margin : Margin);
                if (revealed || !hideColliding || !Collides(test))
                {
                    AddShown(name);
                    Show(name);
                    continue;
                }

                // Before giving up on a name, try the other sides of its icon.
                if (smart && Relocate(pin, name, mine))
                {
                    AddShown(name);
                    Show(name);
                    continue;
                }

                Hide(name);
            }
#if DIAGNOSTICS
            ReportGroupedHidden();
#endif
        }

#if DIAGNOSTICS
        private static readonly List<Minimap.PinData> GroupedHidden = new List<Minimap.PinData>();
        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// totya (2.1.1): "Chest" stayed hidden beside "Log Cabin" with no visible overlap, and
        /// HideGroupedLabels is the suspect. For each name hidden as grouped, how far its text
        /// would overlap the names that are showing; 0x0 means it would have fitted. Once per
        /// pin and zoom step, so the log stays short.
        private static void ReportGroupedHidden()
        {
            foreach (Minimap.PinData pin in GroupedHidden)
            {
                Minimap.PinNameData name = pin.m_NamePinData;
                Rect mine = RectOf(name);
                float ow = 0f, oh = 0f;
                string other = "-";
                foreach (Minimap.PinNameData shown in Shown)
                {
                    Rect r = RectOf(shown);
                    float w = Mathf.Min(mine.xMax, r.xMax) - Mathf.Max(mine.xMin, r.xMin);
                    float h = Mathf.Min(mine.yMax, r.yMax) - Mathf.Max(mine.yMin, r.yMin);
                    if (w > 0f && h > 0f && w * h > ow * oh)
                    {
                        ow = w;
                        oh = h;
                        other = shown.PinNameText.text;
                    }
                }
                string key = $"{pin.m_name}|{pin.m_pos.x:0}|{pin.m_pos.z:0}|{Mathf.RoundToInt(Crowding.ZoomOutPercent() / 5f)}";
                if (Reported.Add(key))
                    Plugin.Log.LogInfo($"[labelwhy] '{pin.m_name}' hidden as grouped (merged {Crowding.MergedCount(pin)}, cluster {Clusters.CountOf(pin)}); its text {mine.width:0}x{mine.height:0} px would overlap a shown name by {ow:0}x{oh:0} px ({other}); {Shown.Count} names shown, zoom-out {Crowding.ZoomOutPercent():0}%");
            }
        }
#endif

        private static float _prefabMax, _prefabMin;
        private static readonly System.Reflection.FieldInfo NamePrefab = AccessTools.Field(typeof(Minimap), "m_pinNamePrefab");

        /// Name size is the game's own prefab size times LabelScale. The prefab's label is TMP
        /// auto-sized between 16 and 18 (measured: autosize on, min 16, max 18) and sets
        /// fontSize itself on every layout, so a written fontSize does not stick: the min and max
        /// are scaled instead. Only written when they differ, so a settled map costs a compare per
        /// label. The crowding rects read the text's preferred size (RectOf), so they follow it.
        private static void ScaleLabels(List<Minimap.PinData> pins)
        {
            if (_prefabMax <= 0f)
            {
                TMPro.TMP_Text prefab = (NamePrefab?.GetValue(Minimap.instance) as GameObject)?.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (prefab == null)
                    return;
                _prefabMax = prefab.fontSizeMax;
                _prefabMin = prefab.fontSizeMin;
            }
            float max = _prefabMax * Plugin.LabelScale.Value, min = _prefabMin * Plugin.LabelScale.Value;
            foreach (Minimap.PinData pin in pins)
            {
                TMPro.TMP_Text text = pin?.m_NamePinData?.PinNameText;
                if (text == null || (Mathf.Approximately(text.fontSizeMax, max) && Mathf.Approximately(text.fontSizeMin, min)))
                    continue;
                text.fontSizeMax = max;
                text.fontSizeMin = min;
            }
        }

        private static void HideOutOfSight(List<Minimap.PinData> pins)
        {
            foreach (Minimap.PinData pin in pins)
            {
                if (!Crowding.OutOfSight(pin))
                    continue;

                Minimap.PinNameData name = pin?.m_NamePinData;
                if (name?.PinNameGameObject == null || !name.PinNameGameObject.activeInHierarchy)
                    continue;

                Hide(name);
            }
        }

        /// Every label off, bar the one under the cursor: a map of unlabelled icons cannot be read
        /// without some way to ask what one is, and this is the same reveal the collision hiding
        /// uses.
        ///
        /// onlyPointed is the names switch (Shift+H): that one is asked for, so only the pin the
        /// cursor is on gets its name back, not every pin within RevealRadius, which on a crowded
        /// patch is a dozen names on top of each other (measured: 12 labels, 41 overlapping pairs).
        private static void HideAll(List<Minimap.PinData> pins, bool oursOnly, bool onlyPointed = false)
        {
            Vector2 cursor = CursorNear(pins);
            if (oursOnly)
                PinRecord.IndexByPosition();

            foreach (Minimap.PinData pin in pins)
            {
                Minimap.PinNameData name = pin?.m_NamePinData;
                if (name?.PinNameGameObject == null || !name.PinNameGameObject.activeInHierarchy)
                    continue;
                if (oursOnly && !PinRecord.IsIndexedOurs(pin))
                    continue;

                if (!Crowding.OutOfSight(pin) && pin.m_iconElement != null &&
                    (onlyPointed ? ReferenceEquals(pin, PinHover.Current)
                                 : Vector2.Distance((Vector2)pin.m_iconElement.rectTransform.localPosition, cursor) < RevealRadius))
                {
                    Show(name);
                    continue;
                }

                Hide(name);
            }
        }

        /// The cursor needs any one pin's icon to convert against, since they all share a parent.
        /// With no drawn icons the map shows nothing, and the off-screen answer reveals nothing.
        private static Vector2 CursorNear(List<Minimap.PinData> pins)
        {
            foreach (Minimap.PinData pin in pins)
            {
                if (pin?.m_iconElement != null)
                    return CursorIn(pin.m_iconElement);
            }
            return new Vector2(float.MaxValue, float.MaxValue);
        }

        // ---- fades -------------------------------------------------------------------------
        //
        // Names fade rather than pop. Vanilla switches every name on in each UpdatePins pass and
        // this class switches the unwanted ones off again in the same frame, so the switch itself
        // is never seen; what was seen is a name present one pass and gone the next. A name now
        // keeps its marker active while it fades on a CanvasGroup (vanilla never looks for one on
        // a name), and is switched off only once it has faded out. One that was already faded out
        // is switched off at once, so a hidden name never flashes when vanilla relights it.

        private sealed class Fade
        {
            public CanvasGroup Group;
            public bool Want;
            /// How far through the fade it is, 0 to 1, moved at a steady rate by Step. The alpha
            /// drawn is its smoothstep, so a name eases in and out instead of lunging.
            public float Level;
        }

        private static readonly ConditionalWeakTable<GameObject, Fade> Fades = new ConditionalWeakTable<GameObject, Fade>();
        private static bool _zoomHidden;

        /// 12% of the label's size, inside the 8-15% band from research/pin-clustering/ux_reference.md.
        private const float Margin = 0.12f;

        /// A name takes this long to go from nothing to full and back. It used to close 14/s of
        /// the remaining gap, which is most of the way in the first 70 ms (a per-frame alpha step
        /// of 0.16-0.22 at 80 fps, measured), so it read as a pop. A steady rate on the progress,
        /// with the alpha drawn as its smoothstep, starts and ends gently and takes the same time
        /// at any frame rate.
        private const float FadeSeconds = 0.2f;
        private const float Invisible = 0.02f;

        private static Fade FadeFor(GameObject go)
        {
            if (Fades.TryGetValue(go, out Fade f) && f.Group != null)
                return f;
            Fades.Remove(go);
            CanvasGroup group = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            f = new Fade { Group = group, Want = true };
            Fades.Add(go, f);
            return f;
        }

        private static Rect Grown(Rect r, float g) =>
            new Rect(r.x - r.width * g, r.y - r.height * g, r.width * (1f + 2f * g), r.height * (1f + 2f * g));

        /// How far the name's text has been moved off the place the game puts it. The game's own
        /// offset is zero (measured: the text child sits at (0, 0) under the label root) and is not
        /// rewritten by later passes, zoom or pan (probe, 2026-11-03), so what is there is ours.
        private static bool Displaced(Minimap.PinNameData name, out Vector2 offset)
        {
            offset = name.PinNameText != null ? name.PinNameText.rectTransform.anchoredPosition : Vector2.zero;
            return offset != Vector2.zero;
        }

        /// The sides of the icon tried in turn when the name will not fit below it: above, right, left.
        /// A side is taken only if its words overlap no shown name even with the margin added, and
        /// stay inside the map when the labels' space is known to start at the parent's corner.
        private static bool Relocate(Minimap.PinData pin, Minimap.PinNameData name, Rect mine)
        {
            UnityEngine.UI.Image icon = pin.m_iconElement;
            if (icon == null || name.PinNameText == null)
                return false;
            RectTransform t = name.PinNameText.rectTransform, root = name.PinNameRectTransform;
            Displaced(name, out Vector2 moved);
            Rect home = new Rect(mine.x - moved.x, mine.y - moved.y, mine.width, mine.height);
            float size = icon.rectTransform.rect.height * icon.rectTransform.localScale.y, gap = 4f;
            Vector2[] shifts =
            {
                new Vector2(0f, size + home.height + gap),                                          // above the icon
                new Vector2(size * 0.5f + gap + home.width * 0.5f, (size + home.height) * 0.5f),    // right of it, level with it
                new Vector2(-(size * 0.5f + gap + home.width * 0.5f), (size + home.height) * 0.5f), // left of it
            };
            var parent = root.parent as RectTransform;
            bool bounded = parent != null && root.anchorMin == Vector2.zero && root.anchorMax == Vector2.zero;
            foreach (Vector2 shift in shifts)
            {
                Rect at = new Rect(home.x + shift.x, home.y + shift.y, home.width, home.height);
                if (bounded && (at.xMin < 0f || at.yMin < 0f || at.xMax > parent.rect.width || at.yMax > parent.rect.height))
                    continue;
                if (Collides(Grown(at, Margin)))
                    continue;
                t.anchoredPosition = shift;
                return true;
            }
            return false;
        }

        private static void Show(Minimap.PinNameData name) => FadeFor(name.PinNameGameObject).Want = true;

        private static void Hide(Minimap.PinNameData name)
        {
            // A hidden name goes back to its place, so it comes back there.
            if (name.PinNameText != null && name.PinNameText.rectTransform.anchoredPosition != Vector2.zero)
                name.PinNameText.rectTransform.anchoredPosition = Vector2.zero;
            Fade f = FadeFor(name.PinNameGameObject);
            f.Want = false;
            if (f.Group.alpha <= Invisible)
                name.PinNameGameObject.SetActive(false);
        }

        /// Called every frame from the Minimap.Update postfix, after vanilla's pass.
        public static void Step(List<Minimap.PinData> pins, float dt)
        {
            if (pins == null || dt <= 0f)
                return;
            // The zoom has just come to rest: ask vanilla for one more pass, so names held back
            // while it moved (ZoomDirection) can come back where there is room. UpdatePins only
            // runs when the map moves or m_pinUpdateRequired is set (Minimap.Update, decompiled).
            if (_direction != 0 && Time.unscaledTime - _zoomMovedAt > HoldSeconds && Minimap.instance != null)
            {
                _direction = 0;
                PinUpdateRequired?.SetValue(Minimap.instance, true);
            }

            // A long frame counts as at most a 30 fps one, as in PinFade, so a hitch does not skip a fade.
            float step = Mathf.Min(dt, PinFade.MaxStep) / FadeSeconds;
            foreach (Minimap.PinData pin in pins)
            {
                GameObject go = pin?.m_NamePinData?.PinNameGameObject;
                if (go == null || !go.activeSelf || !Fades.TryGetValue(go, out Fade f) || f.Group == null)
                    continue;
                float want = f.Want ? 1f : 0f;
                f.Level = Mathf.MoveTowards(f.Level, want, step);
                float alpha = f.Level * f.Level * (3f - 2f * f.Level);
                if (!Mathf.Approximately(f.Group.alpha, alpha))
                    f.Group.alpha = alpha;
                if (!f.Want && alpha <= Invisible)
                    go.SetActive(false);
            }
        }

        private static float _lastZoom = -1f, _zoomMovedAt = -10f;
        private static int _direction;

        /// +1 while the large map is zooming out, -1 zooming in, 0 once it has held still for
        /// HoldSeconds. Names that were held back while it moved are decided again only then, and
        /// the fade carries the change.
        private static int ZoomDirection()
        {
            Minimap map = Minimap.instance;
            float zoom = map != null ? map.LargeZoom : 0f;
            if (_lastZoom >= 0f && !Mathf.Approximately(zoom, _lastZoom))
            {
                _direction = zoom > _lastZoom ? 1 : -1;
                _zoomMovedAt = Time.unscaledTime;
            }
            _lastZoom = zoom;
            if (Time.unscaledTime - _zoomMovedAt > HoldSeconds)
                _direction = 0;
            return _direction;
        }

        private const float HoldSeconds = 0.3f;

        private static readonly System.Reflection.FieldInfo PinUpdateRequired =
            AccessTools.Field(typeof(Minimap), "m_pinUpdateRequired");

        private static void ClearShown()
        {
            Shown.Clear();
            ShownRects.Clear();
            foreach (List<int> cell in Grid.Values)
            {
                cell.Clear();
                Spare.Push(cell);
            }
            Grid.Clear();
        }

        private static void AddShown(Minimap.PinNameData name)
        {
            Rect r = name.PinNameRectTransform != null ? RectOf(name) : default;
            int index = Shown.Count;
            Shown.Add(name);
            ShownRects.Add(r);
            if (name.PinNameRectTransform == null)
                return;
            int x0 = Mathf.FloorToInt(r.xMin / GridCell), x1 = Mathf.FloorToInt(r.xMax / GridCell);
            int y0 = Mathf.FloorToInt(r.yMin / GridCell), y1 = Mathf.FloorToInt(r.yMax / GridCell);
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    long key = ((long)x << 32) ^ (uint)y;
                    if (!Grid.TryGetValue(key, out List<int> cell))
                        Grid[key] = cell = Spare.Count > 0 ? Spare.Pop() : new List<int>();
                    cell.Add(index);
                }
            }
        }

        private static bool Collides(Rect mine)
        {
            int x0 = Mathf.FloorToInt(mine.xMin / GridCell), x1 = Mathf.FloorToInt(mine.xMax / GridCell);
            int y0 = Mathf.FloorToInt(mine.yMin / GridCell), y1 = Mathf.FloorToInt(mine.yMax / GridCell);
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    if (!Grid.TryGetValue(((long)x << 32) ^ (uint)y, out List<int> cell))
                        continue;
                    for (int i = 0; i < cell.Count; i++)
                    {
                        if (ShownRects[cell[i]].Overlaps(mine))
                            return true;
                    }
                }
            }
            return false;
        }

        /// The pointer in the icons' parent space, relative to the parent's pivot - so compare it
        /// with an icon's localPosition, never its anchoredPosition, which is relative to the
        /// icon's anchor. Measured on the large map: cursor (-168, -47) over an icon whose
        /// anchoredPosition was (636, 387). Through 1.8.1 the reveal compared against
        /// anchoredPosition and never fired. Off-screen when there is no canvas to convert
        /// against, which simply means nothing is revealed.
        internal static Vector2 CursorIn(UnityEngine.UI.Image icon)
        {
            var parent = icon != null ? icon.rectTransform.parent as RectTransform : null;
            if (parent == null)
                return new Vector2(float.MaxValue, float.MaxValue);

            Canvas canvas = parent.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                       parent, Pointer, camera, out Vector2 local)
                ? local
                : new Vector2(float.MaxValue, float.MaxValue);
        }

        /// Where the pointer is. On a gamepad the mouse position is whatever it was when the mouse
        /// was last used, so vanilla uses the screen centre instead (Minimap.UpdateBiome and the
        /// JoyButtonA handler in Minimap.Update, read from the decompile).
        internal static Vector3 Pointer =>
            ZInput.IsMouseActive() ? ZInput.pointerPosition : new Vector3(Screen.width / 2, Screen.height / 2);

        /// Where the words are, in the same space as the pin's anchoredPosition.
        ///
        /// The label object's own rect is a 32 x 32 box ON the pin; the text is a child that hangs
        /// BELOW it (pivot top-centre, top edge 16 below the pin, 150 wide, text centred in it).
        /// Colliding the 32 x 32 box, as this did through 2.0.1, said nothing about two names
        /// 33 to 75 px apart on the same row: their words overlapped and the boxes did not
        /// (probe, Home Portal beside Sunken Crypt, 2026-10-30: text 101 px wide on a 43 px box).
        /// TMP caches preferredWidth/Height until the text changes, so this costs no layout.
        private static Rect RectOf(Minimap.PinNameData name)
        {
            RectTransform root = name.PinNameRectTransform;
            Vector2 pos = root.anchoredPosition;
            TMPro.TMP_Text text = name.PinNameText;
            RectTransform t = text.rectTransform;
            float w = Mathf.Min(text.preferredWidth, t.rect.width), h = text.preferredHeight;
            float top = pos.y + t.localPosition.y + (1f - t.pivot.y) * t.rect.height;
            float centre = pos.x + t.localPosition.x + (0.5f - t.pivot.x) * t.rect.width;
            return new Rect(centre - w * 0.5f, top - h, w, h);
        }


    }

    /// Hides a new label in the same frame vanilla shows it.
    ///
    /// CreateMapNamePin starts DelayActivation(go, 1f): the label is made inactive and SetActive(true)d
    /// one second later, from a coroutine, with no UpdatePins pass after it (read from the Minimap
    /// decompile). That undoes whatever Apply did to it in the meantime. Through 1.8.9 a single
    /// shared timestamp asked for one more pass 1.1 s after the newest label was made - and
    /// scrolling the large map out brings pins into view every frame, each one rebuilding its
    /// label, so the timestamp kept moving, every earlier label stayed lit until the scrolling
    /// stopped, and the names appeared in waves. This runs Apply the moment each coroutine has
    /// activated its label, so none of them is ever drawn.
    [HarmonyPatch(typeof(Minimap), "DelayActivation")]
    internal static class Patch_Minimap_DelayActivation
    {
        private static void Postfix(Minimap __instance, ref IEnumerator __result) =>
            __result = ThenApply(__result, __instance);

        private static IEnumerator ThenApply(IEnumerator vanilla, Minimap map)
        {
            while (vanilla.MoveNext())
                yield return vanilla.Current;

            if (map != null)
            {
                List<Minimap.PinData> pins = MinimapAccess.GetPins(map);
                if (pins != null)
                    LabelCrowding.Apply(pins);
            }
        }
    }
}
