using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// The scrollable grid of icon buttons, shared by the map picker and the pin editor.
    ///
    /// Extracted only once there were two real callers. Both need the same clipping viewport,
    /// grid layout, scroll behaviour and vanilla-styled buttons, and the two drifting apart would
    /// be visible - they sit on the same screen.
    internal static class IconGrid
    {
        public const float CellSize = 46f;
        public const float Spacing = 4f;

        /// Height of a panel showing `rows` rows, including its padding.
        public static float PanelHeight(float rows) => rows * (CellSize + Spacing) + 24f;

        /// Width of a panel showing `columns` columns, including its padding.
        public static float PanelWidth(int columns) => columns * (CellSize + Spacing) + 24f;

        /// Fills `panel` with a clipped, scrollable grid of one button per icon.
        ///
        /// `onClick` receives the icon index. Returns one entry per icon - the button's selection
        /// highlight, or null where the template had none - so the caller can show which is
        /// currently chosen.
        /// True while the cursor is over `panel`. Both panels need this to keep the scroll wheel
        /// off the map: Unity's ScrollRect goes through the event system, which respects the
        /// pointer, while Minimap.UpdateMap reads the wheel raw and does not.
        ///
        /// `camera` must be null for a Screen Space - Overlay canvas and the canvas's own camera
        /// otherwise; the wrong one puts the rect in the wrong coordinate space and the test
        /// silently never matches.
        public static bool PointerOver(GameObject panel, RectTransform rect, Camera camera)
        {
            if (panel == null || rect == null || !panel.activeInHierarchy)
                return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, ZInput.pointerPosition, camera);
        }

        /// The camera a panel's canvas needs for hit-testing. Null is an answer, not a failure.
        public static Camera CameraFor(GameObject panel)
        {
            Canvas canvas = panel != null ? panel.GetComponentInParent<Canvas>() : null;
            return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
        }

        public static List<Image> Build(GameObject panel, GameObject template, int columns, Action<int> onClick, float top = 0f, float bottom = 0f)
        {
            // Viewport clips the scrolling content.
            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(panel.transform, false);
            RectTransform viewRt = viewport.AddComponent<RectTransform>();
            viewRt.anchorMin = Vector2.zero;
            viewRt.anchorMax = Vector2.one;
            viewRt.offsetMin = new Vector2(8f, 8f + bottom);
            viewRt.offsetMax = new Vector2(-8f, -8f - top);
            viewport.AddComponent<RectMask2D>();

            // A column of sections rather than one grid, so a heading can sit between the current
            // icons and 1.2.2's. Without it the old art simply continues after the new and reads
            // as inconsistent drawing rather than as a second set.
            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);

            VerticalLayoutGroup column = content.AddComponent<VerticalLayoutGroup>();
            column.childForceExpandHeight = false;
            column.childForceExpandWidth = true;
            column.childControlHeight = true;
            column.childControlWidth = true;
            column.spacing = 6f;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = panel.AddComponent<ScrollRect>();
            scroll.content = contentRt;
            scroll.viewport = viewRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            // Six rows per wheel tick. Half a row was fine for 83 icons; the grid now holds 236
            // in five columns - the current sheet and 1.2.2's below it - which is 48 rows, so a
            // tick that moves a tenth of the way down is what "fast enough" has to mean.
            scroll.scrollSensitivity = (CellSize + Spacing) * 6f;

            var highlights = new List<Image>(CustomIcons.PickerCount);

            Transform current = AddSection(content.transform, columns);
            for (int i = 0; i < CustomIcons.Count; i++)
                highlights.Add(CreateButton(current, template, i, onClick));

            if (CustomIcons.LegacyCount > 0)
            {
                AddHeading(content.transform, "PREVIOUS ICON SET");
                Transform legacy = AddSection(content.transform, columns);
                for (int i = CustomIcons.Count; i < CustomIcons.PickerCount; i++)
                    highlights.Add(CreateButton(legacy, template, i, onClick));
            }

            return highlights;
        }

        /// One grid of cells, sized by its contents so the column above can stack sections.
        private static Transform AddSection(Transform parent, int columns)
        {
            var section = new GameObject("Section");
            section.transform.SetParent(parent, false);
            section.AddComponent<RectTransform>();

            GridLayoutGroup grid = section.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CellSize, CellSize);
            grid.spacing = new Vector2(Spacing, Spacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;

            ContentSizeFitter fitter = section.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return section.transform;
        }

        private static void AddHeading(Transform parent, string text)
        {
            var go = new GameObject("Heading");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.preferredHeight = 18f;
            layout.flexibleWidth = 1f;

            // No font assigned, same as every other label here: TMP falls back to its default,
            // where naming a font asset that may not exist yields invisible text instead.
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 12f;
            label.color = new Color(0.95f, 0.92f, 0.82f, 0.6f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        /// Vanilla's button root carries the icon Image; its child Image is the selection
        /// highlight. Cloning one keeps borders and hover styling consistent with the map UI.
        private static Image CreateButton(Transform parent, GameObject template, int index, Action<int> onClick)
        {
            GameObject cell;
            Image highlight = null;

            if (template != null)
            {
                cell = UnityEngine.Object.Instantiate(template, parent);
                cell.name = $"CarturIcon_{index}";

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
                // No template available - a plain button rather than giving up on the feature.
                cell = new GameObject($"CarturIcon_{index}");
                cell.transform.SetParent(parent, false);
                cell.AddComponent<RectTransform>();
                cell.AddComponent<Image>();
            }

            Image icon = cell.GetComponent<Image>();
            if (icon != null)
            {
                icon.sprite = CustomIcons.SpriteForPicker(index);
                icon.color = Color.white;
                icon.enabled = true;
            }

            if (highlight != null)
                highlight.enabled = false;

            Button button = cell.GetComponent<Button>() ?? cell.AddComponent<Button>();

            // A whole fresh event object, NOT onClick.RemoveAllListeners(): that only drops
            // listeners added at runtime and leaves the prefab's serialized (persistent) calls
            // intact, so a cloned vanilla button would still fire vanilla's handler alongside
            // ours and select a vanilla pin type as well.
            button.onClick = new Button.ButtonClickedEvent();
            int captured = index;
            button.onClick.AddListener(() => onClick(captured));

            return highlight;
        }

        /// Vanilla's own pin-type buttons, used as the styling template. Null is tolerated.
        public static GameObject FindTemplateButton(Minimap map)
        {
            Image[] candidates = { map.m_selectedIcon0, map.m_selectedIcon1, map.m_selectedIcon2, map.m_selectedIconBoss };
            foreach (Image candidate in candidates)
            {
                if (candidate != null && candidate.transform.parent != null)
                    return candidate.transform.parent.gameObject;
            }
            return null;
        }
    }
}
