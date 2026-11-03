using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    /// Mouse-wheel zoom on the large map that eases to a target and zooms about the pointer.
    ///
    /// Root cause of the jumps: Minimap.UpdateMap turns every wheel event, whatever its size, into
    /// a fixed 10% step applied at once (Clamp(wheel, -0.05, 0.05) * LargeZoom * 2). Vanilla also
    /// has a zoom-about-the-pointer branch, but it sits behind `if (false && ...)`, so the zoom
    /// always runs about the middle of the view.
    ///
    /// The wheel is read here, in a prefix on UpdateMap that runs before vanilla's own read, then
    /// withheld from vanilla for the rest of that call (Suppress, honoured by the ZInput patch in
    /// MapIconPicker.cs). LargeZoom and m_mapOffset are written before vanilla's CenterMap at the
    /// end of UpdateMap, so the frame is drawn with them. The M/Z keys, the controller and touch
    /// are left to vanilla; any change they make is noticed and becomes the new target.
    internal static class SmoothZoom
    {
        internal static bool Suppress;

        private static readonly FieldInfo MapOffset = AccessTools.Field(typeof(Minimap), "m_mapOffset");
        private static readonly FieldInfo DragView = AccessTools.Field(typeof(Minimap), "m_dragView");

        private static float _target, _written;
        private static bool _anchored, _focus;
        private static float _fx, _fz;
        private static float _wx, _wz, _nx, _ny;

        /// Hold longer than any real frame; a hitch must not teleport the zoom.
        private const float MaxStep = 0.05f;

        internal static void Tick(Minimap map, Player player, bool takeInput)
        {
            float zoom = map.LargeZoom;
            if (!Plugin.SmoothMapZoom.Value || map.m_mode != Minimap.MapMode.Large || !takeInput || player == null ||
                MapOffset == null || Minimap.InTextInput() || ZInput.IsGamepadActive())
            {
                Rest(zoom);
                return;
            }

            // Something else moved the zoom since we last wrote it (the M and Z keys, a pinch,
            // another mod): that is the target now, and no anchor holds.
            if (_written <= 0f || Mathf.Abs(Mathf.Log(zoom / _written)) > 1e-4f)
                Rest(zoom);

            float wheel = ZInput.GetMouseScrollWheel();
            if (wheel != 0f)
            {
                float percent = (PrecisionHeld() ? Plugin.PrecisionZoomPercent.Value : Plugin.MapZoomStepPercent.Value) / 100f;
                float factor = wheel > 0f ? 1f / (1f + percent) : 1f + percent;
                _target = Mathf.Clamp(_target * factor, map.m_minZoom, map.m_maxZoom);
                _focus = false;
                Anchor(map, player);
            }
            Suppress = true;

            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStep), tau = ZoomMath.Tau(Plugin.MapZoomDurationMs.Value);
            Vector3 at = player.transform.position;
            bool zooming = !Mathf.Approximately(zoom, _target);
            if (zooming)
            {
                map.LargeZoom = ZoomMath.Step(zoom, _target, dt, tau);
                _written = map.LargeZoom;
            }

            RectTransform rect = (RectTransform)map.m_mapImageLarge.transform;
            if (_anchored && zooming && !(DragView?.GetValue(map) is bool dragging && dragging))
            {
                ZoomMath.CentreFor(_wx, _wz, _nx, _ny, rect.rect.width / rect.rect.height, _written, map.m_textureSize * map.m_pixelSize, out float cx, out float cz);
                MapOffset.SetValue(map, new Vector3(cx - at.x, 0f, cz - at.z));
            }
            else if (_focus)
            {
                // The centre glides to the pin along the same curve as the zoom, so they arrive together.
                Vector3 offset = (Vector3)MapOffset.GetValue(map);
                float k = tau <= 0f ? 1f : 1f - Mathf.Exp(-dt / tau);
                float gx = _fx - (at.x + offset.x), gz = _fz - (at.z + offset.z);
                if (gx * gx + gz * gz < 1f && !zooming)
                    k = 1f;
                MapOffset.SetValue(map, new Vector3(offset.x + gx * k, 0f, offset.z + gz * k));
                _focus = k < 1f;
            }
            if (!zooming || Mathf.Approximately(_written, _target))
                _anchored = false;
        }

        /// Glides the view to a world point at a zoom (the cluster list uses it to open a pin up).
        internal static void FocusOn(Minimap map, Vector3 world, float zoom)
        {
            Player player = Player.m_localPlayer;
            if (player == null || MapOffset == null)
                return;
            zoom = Mathf.Clamp(zoom, map.m_minZoom, map.m_maxZoom);
            if (!Plugin.SmoothMapZoom.Value || Plugin.MapZoomDurationMs.Value <= 0)
            {
                map.LargeZoom = zoom;
                MapOffset.SetValue(map, new Vector3(world.x - player.transform.position.x, 0f, world.z - player.transform.position.z));
                Rest(map.LargeZoom);
                return;
            }
            _target = zoom;
            _anchored = false;
            _focus = true;
            _fx = world.x;
            _fz = world.z;
        }

        private static void Rest(float zoom)
        {
            _target = _written = zoom;
            _anchored = _focus = false;
        }

        private static bool PrecisionHeld()
        {
            KeyCode key = Plugin.PrecisionZoomKey.Value.MainKey;
            return key != KeyCode.None && (ZInput.GetKey(key, false) ||
                   (key == KeyCode.LeftShift && ZInput.GetKey(KeyCode.RightShift, false)));
        }

        /// Fixes the world point under the pointer, once per notch, from the view as it is drawn now.
        /// Held for the whole animation rather than read again each frame, so there is nothing to drift.
        private static void Anchor(Minimap map, Player player)
        {
            _anchored = false;
            if (!Plugin.CursorCenteredZoom.Value || !ZInput.IsMouseActive() || DragView == null)
                return;
            RectTransform rect = (RectTransform)map.m_mapImageLarge.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, ZInput.pointerPosition, null, out Vector2 local))
                return;
            Vector2 n = Rect.PointToNormalized(rect.rect, local);
            if (n.x < 0f || n.x > 1f || n.y < 0f || n.y > 1f)
                return;

            Vector3 offset = (Vector3)MapOffset.GetValue(map);
            Vector3 at = player.transform.position;
            ZoomMath.WorldUnder(at.x + offset.x, at.z + offset.z, n.x, n.y, rect.rect.width / rect.rect.height,
                                map.LargeZoom, map.m_textureSize * map.m_pixelSize, out _wx, out _wz);
            _nx = n.x;
            _ny = n.y;
            _anchored = true;
        }
    }

    [HarmonyPatch(typeof(Minimap), "UpdateMap")]
    internal static class Patch_Minimap_UpdateMap
    {
        private static bool _warned;

        private static void Prefix(Minimap __instance, Player player, bool takeInput)
        {
            try
            {
                SmoothZoom.Tick(__instance, player, takeInput);
            }
            catch (Exception e)
            {
                SmoothZoom.Suppress = false;
                if (_warned)
                    return;
                _warned = true;
                Plugin.Log.LogWarning($"Smooth zoom failed, the game's own zoom runs instead: {e}");
            }
        }

        /// A finalizer, so an exception in vanilla cannot leave the wheel withheld.
        private static void Finalizer() => SmoothZoom.Suppress = false;
    }
}
