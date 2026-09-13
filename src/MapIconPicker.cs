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
        private const float PanelHeight = 4f * (CellSize + Spacing) + 24f;

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
            if (_selectIcon == null || _selectedType == null)
            {
                Plugin.Log.LogWarning("Minimap.SelectIcon/m_selectedType not found - map icon picker disabled.");
                return;
            }

            GameObject template = FindTemplateButton(map);

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

            // Viewport clips the scrolling content.
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(_panel.transform, false);
            RectTransform viewRt = viewport.AddComponent<RectTransform>();
            viewRt.anchorMin = Vector2.zero;
            viewRt.anchorMax = Vector2.one;
            viewRt.offsetMin = new Vector2(8f, 8f);
            viewRt.offsetMax = new Vector2(-8f, -8f);
            viewport.AddComponent<RectMask2D>();

            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);

            GridLayoutGroup grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CellSize, CellSize);
            grid.spacing = new Vector2(Spacing, Spacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = _panel.AddComponent<ScrollRect>();
            scroll.content = contentRt;
            scroll.viewport = viewRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            // A Screen Space - Overlay canvas must be hit-tested with a null camera; anything else
            // needs its own. Passing the wrong one puts the rect in the wrong coordinate space and
            // the test silently never matches.
            Canvas canvas = _panel.GetComponentInParent<Canvas>();
            _canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            _highlights.Clear();
            for (int i = 0; i < CustomIcons.Count; i++)
                CreateButton(map, content.transform, template, i);

            Plugin.Log.LogInfo($"Map icon picker built with {CustomIcons.Count} icons.");
        }

        /// Vanilla's button root carries the icon Image; its child Image is the highlight. Using
        /// one as a template keeps borders/hover styling consistent with the rest of the map UI.
        private static GameObject FindTemplateButton(Minimap map)
        {
            Image[] candidates = { map.m_selectedIcon0, map.m_selectedIcon1, map.m_selectedIcon2, map.m_selectedIconBoss };
            foreach (Image candidate in candidates)
            {
                if (candidate != null && candidate.transform.parent != null)
                    return candidate.transform.parent.gameObject;
            }
            return null;
        }

        private static void CreateButton(Minimap map, Transform parent, GameObject template, int index)
        {
            GameObject cell;
            Image highlight = null;

            if (template != null)
            {
                cell = Object.Instantiate(template, parent);
                cell.name = $"CarturIcon_{index}";

                // The cloned child Image is vanilla's selection highlight; reuse it as ours.
                foreach (Image img in cell.GetComponentsInChildren<Image>(true))
                {
                    if (img.gameObject != cell)
                    {
                        highlight = img;
                        break;
                    }
                }
            }
            else
            {
                // No template available - plain button rather than giving up on the feature.
                cell = new GameObject($"CarturIcon_{index}");
                cell.transform.SetParent(parent, false);
                cell.AddComponent<RectTransform>();
                cell.AddComponent<Image>();
            }

            Image icon = cell.GetComponent<Image>();
            if (icon != null)
            {
                icon.sprite = CustomIcons.SpriteAt(index);
                icon.color = Color.white;
                icon.enabled = true;
            }

            if (highlight != null)
            {
                highlight.enabled = false;
                _highlights.Add(highlight);
            }
            else
            {
                _highlights.Add(null);
            }

            Button button = cell.GetComponent<Button>() ?? cell.AddComponent<Button>();

            // A whole fresh event object, NOT onClick.RemoveAllListeners(): that only drops
            // listeners added at runtime and leaves the prefab's serialized (persistent) calls
            // intact, so a cloned vanilla button would still fire vanilla's handler alongside
            // ours and select a vanilla pin type as well.
            button.onClick = new Button.ButtonClickedEvent();
            int captured = index;
            button.onClick.AddListener(() => Select(map, captured));
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
