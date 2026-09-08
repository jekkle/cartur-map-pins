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
        private const int Columns = 6;
        private const float CellSize = 46f;
        private const float Spacing = 4f;
        private const float PanelWidth = Columns * (CellSize + Spacing) + 24f;
        private const float PanelHeight = 4f * (CellSize + Spacing) + 24f;

        private static GameObject _panel;
        private static readonly List<Image> _highlights = new List<Image>();
        private static MethodInfo _selectIcon;
        private static FieldInfo _selectedType;

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
            // The clone inherits the prefab's own click wiring, which would select a vanilla type.
            button.onClick.RemoveAllListeners();
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
}
