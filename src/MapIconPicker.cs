using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// A scrollable icon picker on the large map, sitting alongside vanilla's own row of seven
    /// pin-type buttons rather than replacing it.
    ///
    /// Vanilla's picker can't simply be extended: its seven buttons are prefab-authored objects
    /// wired to serialized fields (m_selectedIcon0..4, Boss, Death), where the BUTTON holds the
    /// icon Image and its CHILD Image is the selection highlight - which is why SelectIcon does
    /// `selectedIcon.Value.enabled = (key == type)`. So this builds its own grid, cloning one of
    /// those buttons as a template so the styling matches, and calls the private SelectIcon by
    /// reflection to keep vanilla's own bookkeeping (m_selectedType, m_pinUpdateRequired, and
    /// clearing the vanilla highlights) intact.
    ///
    /// This only affects pins placed BY HAND. Auto-pins take their icon from the config.
    internal static class MapIconPicker
    {
        private const int Columns = 5;
        private const float CellSize = 46f;
        private const float Spacing = 4f;
        private const float PanelWidth = Columns * (CellSize + Spacing) + 24f;
        private const float VisibleRows = 6f;
        private const float PanelHeight = VisibleRows * (CellSize + Spacing) + 24f;

        private static GameObject _panel;
        private static RectTransform _panelRect;

        /// Resolved once in Build, not per call: PointerOverPanel is reached from a patch on
        /// ZInput.GetMouseScrollWheel, which GameCamera calls every frame, and walking up the
        /// hierarchy for the Canvas there would be a frame-rate bug. Null is the correct value
        /// for a Screen Space - Overlay canvas, so "null" here is an answer, not a missing one.
        private static Camera _canvasCamera;
        private static readonly List<Image> _highlights = new List<Image>();
        private static MethodInfo _selectIcon;
        private static FieldInfo _selectedType;

        /// True while the cursor is over the picker, which is what tells the map to leave the
        /// scroll wheel alone. Unity's own ScrollRect already handles the wheel through the event
        /// system; the map does not go through the event system at all, so the two would
        /// otherwise both act on one wheel tick.
        public static bool PointerOverPanel()
        {
            if (_panel == null || _panelRect == null || !_panel.activeInHierarchy)
                return false;

            // ZInput.pointerPosition rather than Input.mousePosition: it is what the game itself
            // treats as the cursor, so this still works when a gamepad is driving it.
            return RectTransformUtility.RectangleContainsScreenPoint(_panelRect, ZInput.pointerPosition, _canvasCamera);
        }

        public static void Build(Minimap map)
        {
            if (_panel != null || map == null)
                return;
            if (!Plugin.MapPickerEnabled.Value || !CustomIcons.Ready)
                return;
            if (map.m_largeRoot == null)
                return;

            _selectIcon = AccessTools.Method(typeof(Minimap), "SelectIcon", new[] { typeof(Minimap.PinType) });
            _selectedType = AccessTools.Field(typeof(Minimap), "m_selectedType");
            // Private, and a thin forwarder onto GetClosestPin(pos, PinInteractRadius, true) -
            // which is fine to call, since it applies vanilla's own cursor-to-pin radius.
            _closestPinToCursor = AccessTools.Method(typeof(Minimap), "GetClosestPinToCursor");
            if (_selectIcon == null || _selectedType == null)
            {
                Plugin.Log.LogWarning("Minimap.SelectIcon/m_selectedType not found - map icon picker disabled.");
                return;
            }


            _panel = new GameObject("CarturIconPicker");
            _panel.transform.SetParent(map.m_largeRoot.transform, false);
            RectTransform panelRt = _panel.AddComponent<RectTransform>();
            _panelRect = panelRt;
            panelRt.anchorMin = new Vector2(0f, 0f);
            panelRt.anchorMax = new Vector2(0f, 0f);
            panelRt.pivot = new Vector2(0f, 0f);
            panelRt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panelRt.anchoredPosition = new Vector2(Plugin.MapPickerX.Value, Plugin.MapPickerY.Value);

            Image bg = _panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);

            // A Screen Space - Overlay canvas must be hit-tested with a null camera; anything else
            // needs its own. Passing the wrong one puts the rect in the wrong coordinate space and
            // the test silently never matches.
            Canvas canvas = _panel.GetComponentInParent<Canvas>();
            _canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            _highlights.Clear();
            _highlights.AddRange(IconGrid.Build(_panel, IconGrid.FindTemplateButton(map), Columns,
                                                index => Select(map, index)));

            Plugin.Log.LogInfo($"Map icon picker built with {CustomIcons.Count} icons.");
        }

        private static void Select(Minimap map, int index)
        {
            Minimap.PinType type = CustomIcons.TypeForIndex(index);

            // Vanilla's SelectIcon also clears the seven vanilla highlights and flags a pin
            // refresh, so going through it keeps everything consistent.
            _selectIcon.Invoke(map, new object[] { type });

            for (int i = 0; i < _highlights.Count; i++)
            {
                if (_highlights[i] != null)
                    _highlights[i].enabled = i == index;
            }
        }

        private static MethodInfo _closestPinToCursor;

        /// Shift-click an existing pin to open the editor on it, instead of vanilla's plain
        /// left-click which ticks the pin off.
        ///
        /// Returns true when it handled the click, which suppresses the tick-off.
        public static bool TryEditPinUnderCursor(Minimap map)
        {
            if (map == null || _closestPinToCursor == null)
                return false;
            if (!ZInput.GetKey(KeyCode.LeftShift, false) && !ZInput.GetKey(KeyCode.RightShift, false))
                return false;

            var pin = _closestPinToCursor.Invoke(map, null) as Minimap.PinData;
            if (pin == null)
            {
                PinEditor.Close();
                return false;
            }

            PinEditor.Open(map, pin);
            return true;
        }

        /// Vanilla selecting one of its own icons must clear our highlights, otherwise two
        /// icons look selected at once.
        public static void ClearHighlightsIfVanilla(Minimap.PinType type)
        {
            if (CustomIcons.IsCustom(type))
                return;
            foreach (Image highlight in _highlights)
            {
                if (highlight != null)
                    highlight.enabled = false;
            }
        }
    }

    /// Vanilla's left-click on a pin ticks it off (or clears a shared pin's owner first). Holding
    /// shift repoints it to the selected icon instead, so an existing pin can be corrected without
    /// deleting and replacing it.
    ///
    /// Returning false skips the original deliberately: shift-click means "change this icon", and
    /// letting vanilla also toggle the tick would make one click do two unrelated things.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
    internal static class Patch_Minimap_OnMapLeftClick
    {
        private static bool Prefix(Minimap __instance) =>
            !MapIconPicker.TryEditPinUnderCursor(__instance);
    }

    [HarmonyPatch(typeof(Minimap), "SelectIcon")]
    internal static class Patch_Minimap_SelectIcon
    {
        private static void Postfix(Minimap.PinType type)
        {
            MapIconPicker.ClearHighlightsIfVanilla(type);
        }
    }

    /// Swallows the scroll wheel while the cursor is over the picker, so it scrolls the icon
    /// grid instead of zooming the map underneath it.
    ///
    /// Minimap.UpdateMap reads ZInput.GetMouseScrollWheel() directly every frame and never asks
    /// whether the pointer is over UI, so without this one wheel tick both scrolls the grid (via
    /// the event system, which does respect the pointer) and zooms the map.
    ///
    /// This forwards to a private Internal_GetMouseScrollWheel that holds the real logic, which
    /// normally means the forwarder is the wrong thing to patch - but every caller in the game
    /// goes through this public static one (Minimap.UpdateMap, GameCamera.UpdateCamera,
    /// GameCamera.UpdateFreeFly, Player.UpdatePlacement), so nothing bypasses it. The other three
    /// are unreachable while the cursor sits on a panel that only exists on the open large map.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class Patch_ZInput_GetMouseScrollWheel
    {
        private static bool Prefix(ref float __result)
        {
            if (!MapIconPicker.PointerOverPanel())
                return true;
            __result = 0f;
            return false;
        }
    }
}
