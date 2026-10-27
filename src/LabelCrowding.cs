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
        private static readonly List<Ranked> Ordered = new List<Ranked>();
        private static readonly Dictionary<string, int> NameCounts = new Dictionary<string, int>();

        /// A label with its rarity read once, so the comparator does no dictionary lookups.
        private struct Ranked
        {
            public Minimap.PinData Pin;
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
            Shown.Clear();
            Ordered.Clear();
            NameCounts.Clear();

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
            // A band round the limit (10% of it, at least one point) so a zoom resting on the line
            // does not switch every name on and off.
            float limit = Plugin.HideLabelsFromZoom.Value;
            float band = Mathf.Max(1f, limit * 0.1f);
            float percent = Crowding.ZoomOutPercent();
            _zoomHidden = limit < 100f && (_zoomHidden ? percent > limit - band : percent > limit + band);
            if (_zoomHidden)
            {
                HideAll(pins, oursOnly: false);
                return;
            }

            // Before the early return below: a pin that is not drawn must not leave its name
            // floating over the map, whatever the collision setting says.
            HideOutOfSight(pins);

            bool hideColliding = Plugin.HideCollidingLabels.Value;
            if (!hideColliding && !PinFilter.Active)
                return;

            foreach (Minimap.PinData pin in pins)
            {
                Minimap.PinNameData name = pin?.m_NamePinData;
                if (name?.PinNameRectTransform == null || name.PinNameGameObject == null)
                    continue;
                if (!name.PinNameGameObject.activeInHierarchy || string.IsNullOrEmpty(pin.m_name))
                    continue;

                // A name that does not match the search is noise while searching, and the pin it
                // belongs to is still on the map, dimmed.
                if (!PinFilter.Matches(pin.m_name))
                {
                    Hide(name);
                    continue;
                }

                NameCounts.TryGetValue(pin.m_name, out int count);
                NameCounts[pin.m_name] = count + 1;
                Ordered.Add(new Ranked { Pin = pin });
            }

            if (Ordered.Count == 0)
                return;

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
            // may only go; zooming in or standing still spreads or keeps them, so a name already
            // up stays and a hidden one comes back only where nothing that is up overlaps it.
            int direction = ZoomDirection();
            if (hideColliding && direction <= 0)
            {
                foreach (Ranked ranked in Ordered)
                {
                    Minimap.PinNameData name = ranked.Pin.m_NamePinData;
                    if (Fades.TryGetValue(name.PinNameGameObject, out Fade f) && f.Want)
                        Shown.Add(name);
                }
            }

            foreach (Ranked ranked in Ordered)
            {
                Minimap.PinData pin = ranked.Pin;
                Minimap.PinNameData name = pin.m_NamePinData;
                Rect mine = RectOf(name.PinNameRectTransform);

                // Under the cursor wins outright, including over an earlier label: pointing at a
                // pin is asking what it is.
                bool revealed = pin.m_iconElement != null &&
                                Vector2.Distance((Vector2)pin.m_iconElement.rectTransform.localPosition, cursor) < RevealRadius;

                // Collides only when collision hiding is on: an active search alone must dim
                // non-matches, not also hide labels that overlap.
                // Hysteresis: a name already up keeps its place unless it overlaps by more than
                // a margin, and a hidden one needs that margin clear before it comes back, so two
                // names grazing each other while the zoom moves do not take turns.
                bool known = Fades.TryGetValue(name.PinNameGameObject, out Fade had);
                bool wasShown = !known || had.Want;
                if (hideColliding && known && !revealed)
                {
                    if (direction <= 0 && wasShown)
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
                float grow = wasShown ? -Margin : Margin;
                Rect test = new Rect(mine.x - mine.width * grow, mine.y - mine.height * grow,
                                     mine.width * (1f + 2f * grow), mine.height * (1f + 2f * grow));
                if (revealed || !hideColliding || !Collides(test))
                {
                    Shown.Add(name);
                    Show(name);
                    continue;
                }

                Hide(name);
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
        private static void HideAll(List<Minimap.PinData> pins, bool oursOnly)
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
                    Vector2.Distance((Vector2)pin.m_iconElement.rectTransform.localPosition, cursor) < RevealRadius)
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
        }

        private static readonly ConditionalWeakTable<GameObject, Fade> Fades = new ConditionalWeakTable<GameObject, Fade>();
        private static bool _zoomHidden;

        /// 12% of the label's size, inside the 8-15% band from research/pin-clustering/ux_reference.md.
        private const float Margin = 0.12f;
        private const float FadeRate = 14f;
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

        private static void Show(Minimap.PinNameData name) => FadeFor(name.PinNameGameObject).Want = true;

        private static void Hide(Minimap.PinNameData name)
        {
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

            float t = 1f - Mathf.Exp(-FadeRate * Mathf.Min(dt, PinFade.MaxStep));
            foreach (Minimap.PinData pin in pins)
            {
                GameObject go = pin?.m_NamePinData?.PinNameGameObject;
                if (go == null || !go.activeSelf || !Fades.TryGetValue(go, out Fade f) || f.Group == null)
                    continue;
                float want = f.Want ? 1f : 0f;
                float alpha = Mathf.Lerp(f.Group.alpha, want, t);
                if (Mathf.Abs(alpha - want) < Invisible)
                    alpha = want;
                if (!Mathf.Approximately(f.Group.alpha, alpha))
                    f.Group.alpha = alpha;
                if (!f.Want && alpha <= Invisible)
                    go.SetActive(false);
            }
        }

        private static float _lastZoom = -1f, _zoomMovedAt = -10f;
        private static int _direction;

        /// +1 while the large map is zooming out, -1 zooming in, 0 once it has held still for
        /// HoldSeconds (research: a ~0.15 s hold before re-deciding, ux_reference.md).
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

        private const float HoldSeconds = 0.15f;

        private static readonly System.Reflection.FieldInfo PinUpdateRequired =
            AccessTools.Field(typeof(Minimap), "m_pinUpdateRequired");

        private static bool Collides(Rect mine)
        {
            foreach (Minimap.PinNameData other in Shown)
            {
                if (other.PinNameRectTransform != null && RectOf(other.PinNameRectTransform).Overlaps(mine))
                    return true;
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

        private static Rect RectOf(RectTransform rt)
        {
            Vector2 pos = rt.anchoredPosition;
            Vector2 size = rt.rect.size;
            return new Rect(pos.x - size.x * 0.5f, pos.y - size.y * 0.5f, size.x, size.y);
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
