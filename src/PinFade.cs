using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    /// Moves each pin towards the size and opacity it is supposed to have, instead of putting it
    /// there in one step.
    ///
    /// Every decision this mod makes about a pin (crowded, repeated, out of range, zoomed out) is
    /// recomputed whenever the map moves. A pin drifting over a cell boundary changes its
    /// neighbour's count, so without easing the whole cluster resizes in one frame and panning
    /// reads as flicker.
    ///
    /// No table of pins is kept: it would have to be pruned every pass and would leak if pruning
    /// missed something.
    ///
    ///  - Scale lives on the transform. Vanilla sets a pin's pixel size once, when the marker is
    ///    built (SetSizeWithCurrentAnchors), and never touches localScale, so the current value is
    ///    ours alone.
    ///  - Opacity lives on a CanvasGroup we add to the icon, not in the Image colour: UpdatePins
    ///    rewrites that every pass (white, or grey for a shared-map pin) and would reset a fade
    ///    written there. Vanilla has no CanvasGroup on a pin and never looks for one.
    ///
    /// Both die with the marker, so a new marker fades in: the CanvasGroup is created at zero.
    internal static class PinFade
    {
        /// How fast a pin closes the gap to its target, per second, as an exponential rate:
        /// framerate-independent, and it eases in without a curve. Roughly a twelfth of a second to
        /// cover most of the distance, fast enough not to feel laggy when you scrub the zoom, slow
        /// enough that a cluster resizing reads as movement.
        private const float Rate = 12f;

        /// Below this a pin is not worth drawing, and the Image is switched off so it costs
        /// nothing. Not zero: an exponential approach never quite arrives.
        private const float Invisible = 0.02f;

        public static void Step(List<Minimap.PinData> pins, float dt)
        {
            if (pins == null || dt <= 0f)
                return;

            // Fraction of the remaining distance to cover this frame, the same for every pin.
            float t = 1f - Mathf.Exp(-Rate * dt);

            foreach (Minimap.PinData pin in pins)
            {
                if (pin?.m_iconElement == null)
                    continue;

                bool fresh;
                CanvasGroup group = GroupFor(pin.m_iconElement, out fresh);
                if (group == null)
                    continue;

                float wantedAlpha = Crowding.OutOfSight(pin) ? 0f : 1f;

                // A marker built this frame for a pin that is not supposed to be seen starts and
                // stays hidden, rather than fading in and straight back out.
                if (fresh && wantedAlpha <= 0f)
                    group.alpha = 0f;

                group.alpha = Mathf.Lerp(group.alpha, wantedAlpha, t);
                if (group.alpha > 1f - Invisible)
                    group.alpha = 1f;

                bool visible = group.alpha > Invisible;
                if (pin.m_iconElement.enabled != visible)
                    pin.m_iconElement.enabled = visible;

                Transform icon = pin.m_iconElement.transform;
                float wantedScale = ScaleFor(pin);
                float scale = Mathf.Lerp(icon.localScale.x, wantedScale, t);
                if (!Mathf.Approximately(icon.localScale.x, scale))
                    icon.localScale = new Vector3(scale, scale, 1f);
            }
        }

        /// The size this pin should end up at: whatever size it was given by hand, shrunk by how
        /// crowded and how zoomed out it is.
        ///
        /// Recomputed per frame rather than remembered: crowding reads icon positions, which only
        /// move when UpdatePins runs, so between passes it is the same answer for a dictionary
        /// lookup.
        private static float ScaleFor(Minimap.PinData pin)
        {
            PinStyles.Style style = PinStyles.For(pin.m_pos);
            float chosen = style.IsDefault ? 1f : Mathf.Clamp(style.Size, 0.4f, 3f);
            return chosen * Crowding.ScaleFor(pin);
        }

        /// The icon's CanvasGroup, adding one at zero the first time. `fresh` says it was just
        /// added, which is the only way to tell a marker built this frame from one already faded
        /// in - the pin itself carries no such flag.
        private static CanvasGroup GroupFor(UnityEngine.UI.Image icon, out bool fresh)
        {
            CanvasGroup group = icon.GetComponent<CanvasGroup>();
            fresh = group == null;
            if (!fresh)
                return group;

            group = icon.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            // The map finds pins by position, not through the event system, so the group must not
            // start blocking raycasts.
            group.blocksRaycasts = false;
            group.interactable = false;
            return group;
        }
    }

    /// Minimap.Update runs every frame and calls UpdatePins from inside itself, so a Postfix here
    /// runs after any pass that changed what the targets are.
    ///
    /// UpdatePins alone would not do: it runs only when the map centre or the zoom actually moves,
    /// so one notch of the scroll wheel would take a single step towards the new size and stop
    /// there until the map moved again.
    [HarmonyPatch(typeof(Minimap), "Update")]
    internal static class Patch_Minimap_Update
    {
        private static void Postfix(Minimap __instance)
        {
            if (__instance.m_mode == Minimap.MapMode.None)
                return;

            PinFade.Step(MinimapAccess.GetPins(__instance), Time.deltaTime);
        }
    }

    /// Vanilla's GetClosestPin skips a pin whose marker is inactive, but this mod never
    /// deactivates a marker: hidden, merged and out-of-range pins are only faded (Image disabled,
    /// alpha 0), so the marker stays active and the pin still took the click, the hover and the
    /// delete. Those clicks landed on something the player could not see. When vanilla's answer is
    /// a pin this mod is not drawing, pick the nearest one it is drawing instead. The loop is
    /// vanilla's own, read from the installed assembly, with that one extra test.
    [HarmonyPatch(typeof(Minimap), "GetClosestPin")]
    internal static class Patch_Minimap_GetClosestPin
    {
        private static void Postfix(Minimap __instance, Vector3 pos, float radius, bool mustBeVisible,
                                    ref Minimap.PinData __result)
        {
            if (!mustBeVisible || __result == null || !Crowding.OutOfSight(__result))
                return;

            List<Minimap.PinData> pins = MinimapAccess.GetPins(__instance);
            Minimap.PinData best = null;
            float bestDistance = float.MaxValue;
            if (pins != null)
            {
                foreach (Minimap.PinData pin in pins)
                {
                    if (pin == null || !pin.m_save || Crowding.OutOfSight(pin))
                        continue;
                    if (!pin.m_uiElement || !pin.m_uiElement.gameObject.activeInHierarchy)
                        continue;

                    float distance = Utils.DistanceXZ(pos, pin.m_pos);
                    if (distance < radius && distance < bestDistance)
                    {
                        best = pin;
                        bestDistance = distance;
                    }
                }
            }

            __result = best;
        }
    }
}
