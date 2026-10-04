using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// The scrollable grid of icon buttons, shared by the map picker and the pin editor. They sit
    /// on the same screen, so the clipping viewport, layout, scrolling and buttons must match.
    internal static class IconGrid
    {
        public const float CellSize = 46f;
        public const float Spacing = 4f;

        /// Height of a panel showing `rows` rows, including its padding.
        public static float PanelHeight(float rows) => rows * (CellSize + Spacing) + 24f;

        /// Width of a panel showing `columns` columns, including its padding.
        public static float PanelWidth(int columns) => columns * (CellSize + Spacing) + 24f;

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

        /// Fills `panel` with a clipped, scrollable grid of one button per icon.
        ///
        /// `onClick` receives the icon index. Returns one entry per icon - the button's selection
        /// highlight, or null where the template had none - so the caller can show which is
        /// currently chosen.
        public static List<Image> Build(GameObject panel, GameObject template, Action<int> onClick, float top = 0f, float bottom = 0f)
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

            // A column of sections rather than one grid, so a heading can separate the current
            // icons from the previous set. Without it the old art reads as inconsistent drawing.
            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);
            // A RectTransform added in code starts at sizeDelta (100, 100), and with stretched X
            // anchors that 100 is added to the viewport's width: content was 247 + 100 = 347 wide,
            // which is exactly the 7 columns reported in a 5-column window (read from code).
            contentRt.sizeDelta = new Vector2(0f, contentRt.sizeDelta.y);

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
            // Six rows per wheel tick: 236 icons in five columns (current sheet plus 1.2.2's) is
            // 48 rows, so a tick has to move about a tenth of the way down to be fast enough.
            scroll.scrollSensitivity = (CellSize + Spacing) * 6f;

            var highlights = new List<Image>(CustomIcons.PickerCount);

            // As many columns as the panel's own width holds (its sizeDelta, set by the caller
            // before this runs; both panels are fixed width).
            int fit = Mathf.Max(1, Mathf.FloorToInt(
                (((RectTransform)panel.transform).sizeDelta.x - 16f + Spacing) / (CellSize + Spacing)));
            Transform current = AddSection(content.transform, fit);
            for (int i = 0; i < CustomIcons.Count; i++)
                highlights.Add(CreateButton(current, template, i, onClick));

            if (CustomIcons.LegacyCount > 0)
            {
                AddHeading(content.transform, "PREVIOUS ICON SET");
                Transform legacy = AddSection(content.transform, fit);
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
            // A fixed count, not Flexible. Flexible takes the count from this section's laid-out
            // width, which inherited the content's extra 100 (see Build): 7 columns in a
            // 5-column window, 44 of 158 icons unreachable (Dukaine, Penitence).
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

            TextMeshProUGUI label = Fonts.AddLabel(go);
            label.text = text;
            label.fontSize = 12f;
            label.color = new Color(0.95f, 0.92f, 0.82f, 0.6f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        /// White for a cell at rest, gold for the chosen one.
        public static readonly Color Resting = new Color(0.95f, 0.94f, 0.90f, 0.7f);
        public static readonly Color Selected = new Color(1f, 0.78f, 0.25f, 1f);
        public static readonly Color Hovered = new Color(1f, 1f, 1f, 1f);

        /// A cell whose pin type is currently filtered off the map. Faded rather than hidden:
        /// the cell still has to be there to right-click a second time and bring the pins back.
        public static readonly Color Filtered = new Color(1f, 1f, 1f, 0.22f);

        /// Marks a cell's border as chosen or not. Colour rather than visibility, because the
        /// border is there at rest too - it is what gives the grid its shape.
        public static void SetSelected(Image border, bool selected)
        {
            if (border != null)
                border.color = selected ? Selected : Resting;
        }

        private static Sprite _border;

        /// The same frame the cells use, so a chosen swatch and a chosen icon say it the same way.
        public static Sprite Border => BorderSprite();

        /// A one-pixel frame, built once at runtime and nine-sliced so it draws crisply at any
        /// cell size. Drawn rather than shipped: it is four lines, and a PNG for it would be one
        /// more thing to keep in step with the sheet.
        private static Sprite BorderSprite()
        {
            if (_border != null)
                return _border;

            const int size = 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    tex.SetPixel(x, y, edge ? Color.white : clear);
                }
            }
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();

            // A two-pixel border on every side keeps the corners square when the middle stretches.
            _border = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                                    100f, 0u, SpriteMeshType.FullRect, new Vector4(2f, 2f, 2f, 2f));
            return _border;
        }

        private static Sprite _disc;

        /// A filled circle, drawn once, for slider handles. Its edge is softened by alpha rather
        /// than by a texture filter, so it stays round at any size instead of turning into a
        /// blurred square.
        public static Sprite Disc()
        {
            if (_disc != null)
                return _disc;

            const int size = 32;
            const float radius = size / 2f - 1f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var centre = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), centre);
                    float alpha = Mathf.Clamp01(radius - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();

            _disc = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return _disc;
        }

        /// A cell: a dark slab, the icon on top of it, and a border this mod controls.
        ///
        /// Vanilla's pin button is cloned for its size and its Button wiring, then stripped of its
        /// artwork - it is a gold-framed slab, and two frames on one cell means the selected one
        /// cannot be told from the rest.
        private static Image CreateButton(Transform parent, GameObject template, int index, Action<int> onClick)
        {
            GameObject cell;

            if (template != null)
            {
                cell = UnityEngine.Object.Instantiate(template, parent);
                cell.name = $"CarturIcon_{index}";

                // Every decoration the template brought with it goes off. Vanilla's pin button is
                // a gold-framed slab, so the clone would draw our border inside its frame and a
                // gold selection on a gold rest state. It is kept for its size and Button wiring.
                foreach (Image img in cell.GetComponentsInChildren<Image>(true))
                {
                    if (img.gameObject != cell)
                        img.enabled = false;
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

            // The root becomes a plain dark slab and the icon moves to a child, so the drawing
            // order is slab, then icon, then border. Keeping the icon on the root instead would
            // put it under anything added afterwards.
            Image backing = cell.GetComponent<Image>();
            if (backing != null)
            {
                backing.sprite = null;
                backing.color = new Color(0.07f, 0.08f, 0.08f, 0.85f);
                backing.enabled = true;
            }

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(cell.transform, false);
            RectTransform irt = iconGo.AddComponent<RectTransform>();
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            // Inset, so an icon does not run into the frame around it.
            irt.offsetMin = new Vector2(5f, 5f);
            irt.offsetMax = new Vector2(-5f, -5f);

            Image icon = iconGo.AddComponent<Image>();
            icon.sprite = CustomIcons.SpriteForPicker(index);
            icon.color = CustomIcons.IsVisible(Minimap.instance, CustomIcons.TypeForPicker(index))
                ? Color.white
                : Filtered;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var frame = new GameObject("Border");
            frame.transform.SetParent(cell.transform, false);
            RectTransform frt = frame.AddComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            Image border = frame.AddComponent<Image>();
            border.sprite = BorderSprite();
            border.type = Image.Type.Sliced;
            border.color = Resting;
            border.raycastTarget = false;

            cell.AddComponent<CellHover>().Bind(border);

            Button button = cell.GetComponent<Button>() ?? cell.AddComponent<Button>();

            // A whole fresh event object, NOT onClick.RemoveAllListeners(): that only drops
            // listeners added at runtime and leaves the prefab's serialized (persistent) calls
            // intact, so a cloned vanilla button would still fire vanilla's handler alongside
            // ours and select a vanilla pin type as well.
            button.onClick = new Button.ButtonClickedEvent();
            int captured = index;
            button.onClick.AddListener(() => onClick(captured));

            // Same problem as onClick, for the other mouse buttons. Read off the shipped prefab
            // (SoftRef bundle 17245031): vanilla's pin button "Icon0" carries a Button AND a
            // MouseClick on the SAME object, and MouseClick.m_rightClick holds a serialized call
            // to Minimap.OnAltPressedIcon0, which is ToggleIconFilter(Icon0) with the type
            // hardcoded. The template is always one fixed vanilla button (FindTemplateButton takes
            // the first non-null of m_selectedIcon0/1/2/Boss), so right-clicking any cell hid that
            // one vanilla pin type instead of the icon under the cursor ("it just hides the
            // vanilla campfire pins"). Fresh UnityEvents, for the reason given above.
            MouseClick mouse = cell.GetComponent<MouseClick>();
            if (mouse != null)
            {
                mouse.m_leftClick = new UnityEvent();
                mouse.m_middleClick = new UnityEvent();
                mouse.m_rightClick = new UnityEvent();
                mouse.m_rightClick.AddListener(() =>
                {
                    Minimap map = Minimap.instance;
                    Minimap.PinType type = CustomIcons.TypeForPicker(captured);
                    CustomIcons.ToggleFilter(map, type);
                    icon.color = CustomIcons.IsVisible(map, type) ? Color.white : Filtered;
                });
            }

            return border;
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
