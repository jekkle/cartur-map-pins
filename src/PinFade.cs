using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    /// Moves each pin towards the size and opacity it is supposed to have, instead of putting it
    /// there in one step.
    ///
    /// Every decision this mod makes about a pin - crowded, repeated, out of range, zoomed out -
    /// is recomputed whenever the map moves, and a pin crossing the line between two answers used
    /// to snap. Panning a map full of ore read as flickering, because a pin drifting over a cell
    /// boundary changes its neighbour's count, and the whole cluster resized on the frame it
    /// happened.
    ///
    /// Two things make this work without keeping a table of pins, which would have to be pruned
    /// every pass and would leak the day pruning missed something:
    ///
    ///  - Scale lives on the transform. Vanilla sets a pin's pixel size once, when the marker is
    ///    built (SetSizeWithCurrentAnchors), and never touches localScale - so the current value
    ///    is readable off the object and is ours alone.
    ///  - Opacity lives on a CanvasGroup we add to the icon. It cannot live in the Image colour:
    ///    UpdatePins rewrites that on every pass - white, or grey for a shared-map pin - so a fade
    ///    written there would be reset under us. Vanilla has no CanvasGroup on a pin and never
    ///    looks for one.
    ///
    /// Both die with the marker, which is what makes a new marker fade in rather than appear: the
    /// CanvasGroup is created at zero.
    internal static class PinFade
    {
        /// How fast a pin closes the gap to its target, per second, as an exponential rate rather
        /// than a step: framerate-independent, and it eases in without needing a curve. Roughly a
        /// twelfth of a second to cover most of the distance - fast enough not to feel laggy when
        /// you scrub the zoom, slow enough that a cluster resizing reads as movement.
        private const float Rate = 12f;

        /// Below this a pin is not worth drawing, and the Image is switched off so it costs
        /// nothing. Not zero: an exponential approach never quite arrives.
        private const float Invisible = 0.02f;

        public static void Step(List<Minimap.PinData> pins, float dt)
        {
            if (pins == null || dt <= 0f)
                return;

            // The fraction of the remaining distance to cover this frame. Same for every pin, so
            // it is worked out once rather than per pin.
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
        /// Recomputed per frame rather than remembered. Crowding reads icon positions, which only
        /// move when UpdatePins runs, so between passes this is the same answer each time - and
        /// working it out costs a dictionary lookup, against a table that would need pruning.
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
            // Nothing on a pin is clicked through the event system - the map finds pins by
            // position - but a CanvasGroup blocking raycasts by default would be a change to
            // behaviour nobody asked for.
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
}
