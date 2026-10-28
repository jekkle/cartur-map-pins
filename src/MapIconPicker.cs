using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// A scrollable icon picker on the large map, sitting alongside vanilla's own row of seven
    /// pin-type buttons rather than replacing it.
    ///
    /// Vanilla's picker cannot simply be extended: its seven buttons are prefab-authored objects
    /// wired to serialized fields (m_selectedIcon0..4, Boss, Death), where the BUTTON holds the
    /// icon Image and its CHILD Image is the selection highlight, which is why SelectIcon does
    /// `selectedIcon.Value.enabled = (key == type)`. So this builds its own grid, cloning one of
    /// those buttons as a template so the styling matches, and calls the private SelectIcon by
    /// reflection to keep vanilla's bookkeeping (m_selectedType, m_pinUpdateRequired, clearing
    /// the vanilla highlights) intact.
    ///
    /// This only affects pins placed BY HAND. Auto-pins take their icon from the config.
    internal static class MapIconPicker
    {
        /// Sized by dragging it about in game, not by arithmetic: 263 x 273 is what looked right
        /// beside vanilla's pin column. The grid fills the width it is given, so the column count
        /// follows from this instead of being a second number that has to agree with it.
        private const float PanelWidth = 263f;
        private const float PanelHeight = 380f;

        private static GameObject _panel;
        private static RectTransform _panelRect;

        /// Resolved once in Build, not per call: PointerOverPanel is reached from a patch on
        /// ZInput.GetMouseScrollWheel, which GameCamera calls every frame, and walking up the
        /// hierarchy for the Canvas there would be a frame-rate bug. Null is the correct value
        /// for a Screen Space - Overlay canvas, so null here is an answer, not a missing one.
        private static Camera _canvasCamera;
        private static readonly List<Image> _highlights = new List<Image>();
        private static MethodInfo _selectIcon;
        private static FieldInfo _selectedType;

        /// True while the cursor is over the picker, which tells the map to leave the scroll wheel
        /// alone. Unity's ScrollRect handles the wheel through the event system and the map does
        /// not, so otherwise both would act on one wheel tick.
        public static bool PointerOverPanel() => IconGrid.PointerOver(_panel, _panelRect, _canvasCamera);

        /// The picker or the search bar, for the hover check (PinHover): a pin behind either is not
        /// the one being pointed at.
        public static bool PointerOverUi() =>
            PointerOverPanel() || IconGrid.PointerOver(_searchBar, _searchRect, _canvasCamera);

        /// MapPicker switched in game. Build reads the setting once, when the map is first made, so
        /// without this turning it off does nothing until a restart (KARAKUL47 asked how to hide it).
        public static void SetVisible(bool visible)
        {
            if (_panel == null)
            {
                if (visible)
                    Build(Minimap.instance);
                return;
            }
            _panel.SetActive(visible);
            if (visible)
            {
                Absorb(Minimap.instance);
                if (_publicVanilla != null)
                {
                    _publicVanilla.alpha = 0f;
                    _publicVanilla.blocksRaycasts = false;
                }
            }
            else
            {
                RestoreAbsorbed();
                RestoreVanillaPublic();
            }
            if (_searchBar != null)
                _searchBar.SetActive(visible);

            // The search is part of the hidden UI: left set, it would keep dimming pins and hiding
            // labels above a search bar nobody can see or clear. Clearing the box fires its
            // listener, which writes PinFilter.Text and asks for a redraw; PinFilter.Text is also
            // cleared directly in case the listener does not run.
            if (!visible)
            {
                if (_searchInput != null)
                    _searchInput.text = string.Empty;
                PinFilter.Text = string.Empty;
                if (Minimap.instance != null)
                    PinUpdateRequired?.SetValue(Minimap.instance, true);
            }
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

            _panel = new GameObject("CarturIconPicker");
            _panel.transform.SetParent(map.m_largeRoot.transform, false);
            RectTransform panelRt = _panel.AddComponent<RectTransform>();
            _panelRect = panelRt;
            // Bottom right, under vanilla's own column of pin-type buttons, so everything that
            // answers "what will this pin look like" sits in one place. It is also off the
            // Bounties and Treasure Maps corner, which a bottom-left position overlaps.
            //
            // The offsets are insets from that corner, so a wider panel grows leftwards instead
            // of running off the edge.
            panelRt.anchorMin = new Vector2(1f, 0f);
            panelRt.anchorMax = new Vector2(1f, 0f);
            panelRt.pivot = new Vector2(1f, 0f);
            panelRt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panelRt.anchoredPosition = new Vector2(-Plugin.MapPickerRight.Value, Plugin.MapPickerBottom.Value);

            Image bg = _panel.AddComponent<Image>();
            bg.color = IconGrid.PanelBack;
            Rail(_panel.transform, true);
            Rail(_panel.transform, false);

            _canvasCamera = IconGrid.CameraFor(_panel);

            AddHeader(_panel, map);
            BuildSearchBar(map);

            _highlights.Clear();
            _highlights.AddRange(IconGrid.Build(_panel, IconGrid.FindTemplateButton(map),
                                                index => Select(map, index), top: CaptionHeight));

            // Mark whatever is already selected, so the grid shows which icon a new pin will get.
            if (_selectedType.GetValue(map) is Minimap.PinType active)
            {
                int index = CustomIcons.PickerIndexFor(active);
                for (int i = 0; i < _highlights.Count; i++)
                    IconGrid.SetCellSelected(_highlights[i], i == index);
            }

            // Also clamped here, not only on release: the position comes out of the config, and a
            // config written at one resolution can be off screen at another. Without this a panel
            // dragged into the corner of a big monitor is unreachable in a smaller window.
            ClampToScreen();

            Absorb(map);
            Plugin.Log.LogInfo($"Map icon picker built with {CustomIcons.Count} icons plus {CustomIcons.LegacyCount} from the old sheet.");
        }

        // ---- the game's own pin buttons, and other mods', inside the picker -------------------

        /// Where a moved button came from, so turning the picker off puts it back exactly.
        private struct Origin
        {
            public RectTransform Button;
            public Transform Parent;
            public int Sibling;
            public Vector2 AnchorMin, AnchorMax, Pivot, Position, Size;
            public Vector3 Scale;
            public Image Highlight;
            public Sprite HighlightSprite;
            public Image.Type HighlightType;
            public Color HighlightColor;
        }

        private static readonly List<Origin> Moved = new List<Origin>();
        private static readonly List<GameObject> EmptiedColumns = new List<GameObject>();
        private static Transform _gameSection, _modSection;

        private static readonly FieldInfo SelectedIcons = AccessTools.Field(typeof(Minimap), "m_selectedIcons");

        /// Owner, 2026-10-27: "add the vanilla ones to the menu and remove them from the map, same
        /// for the Epic Loot ones". The buttons themselves are moved, not copied: each keeps the
        /// wiring it came with - vanilla's SelectIcon and right-click filter, Epic Loot's
        /// AdventurePinFilter toggles, any other mod's hover - so nothing is reimplemented here.
        ///
        /// Minimap.m_selectedIcons maps every pin type that has a button to that button's
        /// selection highlight, whose parent is the button (read in game with the pilot's mapui:
        /// Icon0-4 under IconPanel; Boss, Death and Epic Loot's AdventurePinFilter800/801 in a
        /// second column). Mods register there too, so one dictionary finds them all. A column
        /// is switched off only once no button is left in it, so anything unknown stays put.
        ///
        /// Run again whenever the large map opens: Epic Loot adds its buttons when its adventure
        /// data arrives, which can be after the picker is built.
        private static void Absorb(Minimap map)
        {
            if (map == null || _panel == null || !(SelectedIcons?.GetValue(map) is Dictionary<Minimap.PinType, Image> icons))
                return;
            Transform content = _panel.transform.Find("Viewport/Content");
            GridLayoutGroup grid = content != null && content.childCount > 0 ? content.GetComponentInChildren<GridLayoutGroup>() : null;
            if (grid == null)
                return;

            var gameButtons = new List<RectTransform>();
            var modButtons = new List<RectTransform>();
            foreach (KeyValuePair<Minimap.PinType, Image> kv in icons)
            {
                var button = kv.Value != null ? kv.Value.transform.parent as RectTransform : null;
                if (button == null || Moved.Exists(m => m.Button == button))
                    continue;
                (System.Enum.IsDefined(typeof(Minimap.PinType), kv.Key) ? gameButtons : modButtons).Add(button);
            }
            if (gameButtons.Count == 0 && modButtons.Count == 0)
                return;

            if (_gameSection == null)
            {
                // Top down: the game's pins, other mods', then this mod's own set.
                Transform ours = IconGrid.AddHeading(content, "CARTUR'S ICONS");
                ours.SetSiblingIndex(0);
                _modSection = IconGrid.AddSection(content, grid.constraintCount);
                _modSection.SetSiblingIndex(0);
                Transform modHeading = IconGrid.AddHeading(content, "FROM OTHER MODS");
                modHeading.SetSiblingIndex(0);
                _modSection.gameObject.name = "ModSection";
                modHeading.gameObject.name = "ModHeading";
                _gameSection = IconGrid.AddSection(content, grid.constraintCount);
                _gameSection.SetSiblingIndex(0);
                IconGrid.AddHeading(content, "MAP PINS").SetSiblingIndex(0);
            }

            gameButtons.Sort((a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));
            foreach (RectTransform b in gameButtons)
                Move(b, _gameSection, Highlight(icons, b));
            foreach (RectTransform b in modButtons)
                Move(b, _modSection, Highlight(icons, b));

            bool anyMod = _modSection.childCount > 0;
            _modSection.gameObject.SetActive(anyMod);
            content.Find("ModHeading")?.gameObject.SetActive(anyMod);
            // Epic Loot's buttons are its own; name the section after it when they are all it holds.
            if (anyMod && content.Find("ModHeading")?.GetComponent<TextMeshProUGUI>() is TextMeshProUGUI label)
                label.text = _modSection.Cast<Transform>().All(t => t.name.StartsWith("AdventurePinFilter")) ? "EPIC LOOT" : "FROM OTHER MODS";

            foreach (Origin m in Moved)
            {
                GameObject column = m.Parent != null ? m.Parent.gameObject : null;
                if (column == null || !column.activeSelf || EmptiedColumns.Contains(column))
                    continue;
                bool buttonLeft = false;
                foreach (Transform child in column.transform)
                    buttonLeft |= child.gameObject.activeSelf && child.GetComponent<Button>() != null;
                if (buttonLeft)
                    continue;
                column.SetActive(false);
                EmptiedColumns.Add(column);
            }
        }

        private static Image Highlight(Dictionary<Minimap.PinType, Image> icons, RectTransform button)
        {
            foreach (Image img in icons.Values)
                if (img != null && img.transform.parent == button)
                    return img;
            return null;
        }

        /// Moves one button in and dresses it like the picker's own cells: the gold frame for its
        /// selection highlight (the child vanilla's SelectIcon switches on), and the inventory's
        /// hover on its icon. How it looked before is kept for RestoreAbsorbed.
        private static void Move(RectTransform button, Transform section, Image highlight)
        {
            Moved.Add(new Origin
            {
                Button = button, Parent = button.parent, Sibling = button.GetSiblingIndex(),
                AnchorMin = button.anchorMin, AnchorMax = button.anchorMax, Pivot = button.pivot,
                Position = button.anchoredPosition, Size = button.sizeDelta, Scale = button.localScale,
                Highlight = highlight, HighlightSprite = highlight != null ? highlight.sprite : null,
                HighlightType = highlight != null ? highlight.type : Image.Type.Simple,
                HighlightColor = highlight != null ? highlight.color : Color.white,
            });
            button.SetParent(section, false);
            button.localScale = Vector3.one;
            if (highlight != null)
            {
                highlight.sprite = IconGrid.SelectedFrame();
                highlight.type = Image.Type.Sliced;
                highlight.color = IconGrid.SelectedGold;
            }
            IconGrid.SlotHover(button.GetComponent<Button>(), button.GetComponent<Image>());
            // The pop is ours, the same on every cell (see IconGrid.YieldHover).
            IconGrid.YieldHover(button.gameObject);
            if (button.GetComponent<CellHover>() == null)
                button.gameObject.AddComponent<CellHover>().Bind(null);
        }

        /// MapPicker switched off: every button goes back where and how it was, and the columns
        /// come back on.
        private static void RestoreAbsorbed()
        {
            for (int i = Moved.Count - 1; i >= 0; i--)
            {
                Origin m = Moved[i];
                if (m.Button == null || m.Parent == null)
                    continue;
                m.Button.SetParent(m.Parent, false);
                m.Button.SetSiblingIndex(m.Sibling);
                m.Button.anchorMin = m.AnchorMin;
                m.Button.anchorMax = m.AnchorMax;
                m.Button.pivot = m.Pivot;
                m.Button.anchoredPosition = m.Position;
                m.Button.sizeDelta = m.Size;
                m.Button.localScale = m.Scale;
                CellHover pop = m.Button.GetComponent<CellHover>();
                if (pop != null)
                    Object.Destroy(pop);
                foreach (MonoBehaviour behaviour in m.Button.GetComponents<MonoBehaviour>())
                {
                    if (behaviour != null && behaviour.GetType().Name == "HoverFx")
                        behaviour.enabled = true;
                }
                if (m.Highlight != null)
                {
                    m.Highlight.sprite = m.HighlightSprite;
                    m.Highlight.type = m.HighlightType;
                    m.Highlight.color = m.HighlightColor;
                }
            }
            Moved.Clear();
            foreach (GameObject column in EmptiedColumns)
                if (column != null)
                    column.SetActive(true);
            EmptiedColumns.Clear();
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
        private static class Patch_AbsorbOnOpen
        {
            private static void Postfix(Minimap __instance, Minimap.MapMode mode)
            {
                if (mode == Minimap.MapMode.Large && _panel != null && _panel.activeSelf)
                    Absorb(__instance);
            }
        }

        private const float CaptionHeight = 30f;

        /// A thin bronze line across the top or bottom edge, as on the setup menu.
        private static void Rail(Transform panel, bool top)
        {
            var go = new GameObject("Rail", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(panel, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.sizeDelta = new Vector2(0f, 2f);
            go.GetComponent<Image>().color = IconGrid.Bronze;
            go.GetComponent<Image>().raycastTarget = false;
        }

        private const float HandleSize = 12f;

        private static Toggle _public;
        private static CanvasGroup _publicVanilla;

        /// The picker's top bar: grip, title, and the "Visible to other players" checkbox, and the
        /// WHOLE bar drags the panel (owner, 2026-10-27), not a handle in the corner. The checkbox
        /// is a child with its own raycast target, so a click on it never starts a drag.
        ///
        /// The checkbox drives vanilla's own Minimap.m_publicPosition toggle (its handler,
        /// SetPublicReferencePosition, is what the game runs), and the toggle's panel at the
        /// bottom of the map is made invisible rather than switched off, so the game's own
        /// bookkeeping on it carries on. Where the panel ends up is written to MapPickerRight /
        /// MapPickerBottom, so it survives a restart and can still be typed in by hand.
        private static void AddHeader(GameObject panel, Minimap map)
        {
            var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(panel.transform, false);
            var rt = (RectTransform)header.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -CaptionHeight);
            rt.offsetMax = new Vector2(0f, -2f);
            Image back = header.GetComponent<Image>();
            back.color = new Color(0f, 0f, 0f, 0f);
            back.raycastTarget = true;
            header.AddComponent<PanelDrag>().Init(_panelRect, CommitPosition);

            // Six dots: the usual "drag me" grip, decoration only.
            var grip = new GameObject("Grip", typeof(RectTransform));
            grip.transform.SetParent(header.transform, false);
            var grt = (RectTransform)grip.transform;
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0f, 0.5f);
            grt.sizeDelta = new Vector2(HandleSize, HandleSize + 4f);
            grt.anchoredPosition = new Vector2(8f, 0f);
            for (int i = 0; i < 6; i++)
            {
                var dot = new GameObject("Dot", typeof(RectTransform), typeof(Image));
                dot.transform.SetParent(grip.transform, false);
                var d = (RectTransform)dot.transform;
                d.anchorMin = d.anchorMax = d.pivot = new Vector2(0f, 1f);
                d.sizeDelta = new Vector2(3f, 3f);
                d.anchoredPosition = new Vector2(2f + (i % 2) * 5f, -2f - (i / 2) * 5.5f);
                dot.GetComponent<Image>().color = IconGrid.Bronze;
                dot.GetComponent<Image>().raycastTarget = false;
            }

            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(header.transform, false);
            var trt = (RectTransform)titleGo.transform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 0.5f);
            trt.anchoredPosition = new Vector2(26f, 0f);
            trt.sizeDelta = new Vector2(60f, 0f);
            TextMeshProUGUI title = Fonts.AddLabel(titleGo);
            title.text = "PINS";
            title.fontSize = 17f;
            title.color = new Color(0.95f, 0.92f, 0.82f, 1f);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.raycastTarget = false;

            AddVisibleCheckbox(header.transform, map);
        }

        private static void AddVisibleCheckbox(Transform header, Minimap map)
        {
            _public = map.m_publicPosition;
            if (_public == null)
                return;

            // The vanilla panel stays alive (the game sets the toggle's state through it), it is only not drawn or clickable.
            Transform vanilla = _public.transform.parent;
            if (vanilla != null)
            {
                _publicVanilla = vanilla.GetComponent<CanvasGroup>() ?? vanilla.gameObject.AddComponent<CanvasGroup>();
                _publicVanilla.alpha = 0f;
                _publicVanilla.blocksRaycasts = false;
            }

            var row = new GameObject("Visible", typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(header, false);
            var rt = (RectTransform)row.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-8f, 0f);
            rt.sizeDelta = new Vector2(158f, 22f);
            row.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(row.transform, false);
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = new Vector2(-20f, 0f);
            TextMeshProUGUI label = Fonts.AddLabel(labelGo);
            label.text = Localization.instance != null ? Localization.instance.Localize("$menu_visibletootherplayers") : "Visible to other players";
            if (label.text.StartsWith("$") || label.text.StartsWith("["))
                label.text = "Visible to other players";
            label.fontSize = 11.5f;
            label.alignment = TextAlignmentOptions.MidlineRight;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;

            // Our checkbox: the setup menu's bronze box, filled when on.
            var box = new GameObject("Box", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(row.transform, false);
            var brt = (RectTransform)box.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 0.5f);
            brt.anchoredPosition = new Vector2(-1f, 0f);
            brt.sizeDelta = new Vector2(15f, 15f);
            Image frame = box.GetComponent<Image>();
            frame.sprite = IconGrid.Border;
            frame.type = Image.Type.Sliced;
            frame.color = IconGrid.Bronze;
            frame.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(box.transform, false);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(3.5f, 3.5f);
            frt.offsetMax = new Vector2(-3.5f, -3.5f);
            _publicTick = fill.GetComponent<Image>();
            _publicTick.color = IconGrid.Bronze;
            _publicTick.raycastTarget = false;

            // Tint like the inventory's buttons: dimmer at rest, full under the mouse.
            Button button = row.GetComponent<Button>();
            button.targetGraphic = label;
            IconGrid.SlotHover(button, label);
            IconGrid.YieldHover(row);
            button.onClick.AddListener(() =>
            {
                if (_public != null && _public.interactable)
                    _public.isOn = !_public.isOn;
            });
            row.AddComponent<PublicSync>();
        }

        private static Image _publicTick;

        /// Keeps our box in step with the vanilla toggle, whoever changed it.
        private sealed class PublicSync : MonoBehaviour
        {
            private void Update()
            {
                if (_public != null && _publicTick != null && _publicTick.enabled != _public.isOn)
                    _publicTick.enabled = _public.isOn;
            }
        }

        private static void RestoreVanillaPublic()
        {
            if (_publicVanilla == null)
                return;
            _publicVanilla.alpha = 1f;
            _publicVanilla.blocksRaycasts = true;
        }

        /// Turns where the panel now sits back into the two insets the config stores.
        ///
        /// The panel is anchored to the bottom-right corner with a bottom-right pivot, so its
        /// anchoredPosition is already "how far in from that corner", negative going left and
        /// positive going up. The settings are plain distances, hence one is negated and the
        /// other is not.
        private static void CommitPosition()
        {
            if (_panelRect == null)
                return;

            ClampToScreen();

            Vector2 pos = _panelRect.anchoredPosition;
            Plugin.MapPickerRight.Value = -pos.x;
            Plugin.MapPickerBottom.Value = pos.y;
        }

        /// Pulls the panel back until the caption row is on screen again.
        ///
        /// Dragged fully past an edge it would be unreachable, and the handle is the only way to
        /// move it, so the strip the handle sits in must never leave the screen. Everything below
        /// it may hang off the bottom.
        private static void ClampToScreen()
        {
            var corners = new Vector3[4];
            _panelRect.GetWorldCorners(corners);
            float minX = corners[0].x, maxX = corners[2].x, maxY = corners[2].y;

            float dx = 0f, dy = 0f;
            if (maxX > Screen.width) dx -= maxX - Screen.width;
            if (minX + dx < 0f) dx += -(minX + dx);
            if (maxY > Screen.height) dy -= maxY - Screen.height;
            if (maxY + dy < CaptionHeight) dy += CaptionHeight - (maxY + dy);

            if (dx != 0f || dy != 0f)
                _panelRect.position += new Vector3(dx, dy, 0f);
        }

        private static GameObject _searchBar;
        private static RectTransform _searchRect;
        private static TMP_InputField _searchInput;

        /// The search box, across the top of the map rather than inside the picker: it searches
        /// pins, not icons, and the top centre is where a search box is looked for.
        ///
        /// Cloned from the map's own pin-name field, the way the grid clones its buttons: it is
        /// already wired for this game's input handling, which a field built from nothing is not,
        /// and a search box that eats movement keys or swallows Escape is worse than no search.
        private static void BuildSearchBar(Minimap map)
        {
            if (_searchBar != null || map.m_largeRoot == null)
                return;

            var source = NameInputField?.GetValue(map) as TMP_InputField;
            if (source == null)
            {
                Plugin.Log.LogWarning("Minimap.m_nameInput not found - the pin search box is disabled.");
                return;
            }

            _searchBar = new GameObject("CarturPinSearch");
            _searchBar.transform.SetParent(map.m_largeRoot.transform, false);
            RectTransform bar = _searchBar.AddComponent<RectTransform>();
            _searchRect = bar;
            bar.anchorMin = new Vector2(0.5f, 1f);
            bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(340f, 30f);
            bar.anchoredPosition = new Vector2(0f, -16f);

            Image backing = _searchBar.AddComponent<Image>();
            backing.color = new Color(0f, 0f, 0f, 0.6f);

            TMP_InputField input = UnityEngine.Object.Instantiate(source, _searchBar.transform);
            _searchInput = input;
            input.gameObject.name = "CarturPinFilter";
            input.gameObject.SetActive(true);

            RectTransform rt = input.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6f, 3f);
            rt.offsetMax = new Vector2(-6f, -3f);

            input.text = string.Empty;
            // The box and the filter are cleared together. PinFilter.Text is static and survives
            // leaving a world, while this input is a fresh clone per Minimap, and only the
            // listener attached further down ever writes PinFilter.Text. Clearing the box alone
            // would leave the last world's search filtering: the new world would open with every
            // non-matching pin dimmed above a search box that looked empty.
            PinFilter.Text = string.Empty;
            if (input.placeholder is TMP_Text placeholder)
                placeholder.text = "Search pins...";

            // Fresh events, not RemoveAllListeners: the clone carries the map's own serialized
            // handlers, which would rename a pin as you typed.
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.onValueChanged.AddListener(text =>
            {
                PinFilter.Text = text;
                PinUpdateRequired?.SetValue(map, true);
            });

            // So the game stops reading the keyboard while this has focus, and Enter here no
            // longer reaches Minimap.OnPinTextEntered, which the clone inherited.
            TextFocus.Register(input);
            TextFocus.DropVanillaSubmit(input);
        }

        /// The flag the map raises when its pins need drawing again. Searching has to raise it
        /// too, or a search looks broken until something else happens to the map.
        private static readonly FieldInfo PinUpdateRequired =
            AccessTools.Field(typeof(Minimap), "m_pinUpdateRequired");

        private static readonly FieldInfo NameInputField =
            AccessTools.Field(typeof(Minimap), "m_nameInput");

        private static void Select(Minimap map, int index)
        {
            Minimap.PinType type = CustomIcons.TypeForPicker(index);

            // Vanilla's SelectIcon also clears the seven vanilla highlights and flags a pin
            // refresh, so go through it.
            _selectIcon.Invoke(map, new object[] { type });

            for (int i = 0; i < _highlights.Count; i++)
                IconGrid.SetCellSelected(_highlights[i], i == index);
        }

        /// Private, and a thin forwarder onto GetClosestPin(pos, PinInteractRadius, true), which
        /// is fine to call since it applies vanilla's own cursor-to-pin radius. Resolved here, not
        /// in Build: Build returns early when MapPicker is off, and shift-click editing does not
        /// depend on the picker.
        private static readonly MethodInfo _closestPinToCursor =
            AccessTools.Method(typeof(Minimap), "GetClosestPinToCursor");

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

            var pin = Editable(map, _closestPinToCursor.Invoke(map, null) as Minimap.PinData);
            if (pin == null)
            {
                PinEditor.Close();
                return false;
            }

            PinEditor.Open(map, pin);
            return true;
        }

        /// The pin worth editing at this spot.
        ///
        /// Vanilla's own markers (your spawn point, your last death) are not saved pins: the map
        /// rebuilds them from your profile on a timer, so a name or icon set on one is gone by
        /// the next refresh. The cursor lands on the spawn marker rather than the Home pin
        /// underneath it, so a house you had just marked would look nameless and refuse edits.
        ///
        /// So an unsaved pin hands over to a saved one on the same spot, and where there is none
        /// nothing opens, which beats an editor whose changes evaporate.
        private static Minimap.PinData Editable(Minimap map, Minimap.PinData pin)
        {
            if (pin == null || pin.m_save)
                return pin;

            List<Minimap.PinData> pins = MinimapAccess.GetPins(map);
            if (pins == null)
                return null;

            // Generous, because the two are not placed by the same code: ours goes on the bed's
            // spawn point and vanilla's on the profile's copy of it.
            const float SameSpot = 8f;
            Minimap.PinData best = null;
            float bestSqr = SameSpot * SameSpot;

            foreach (Minimap.PinData other in pins)
            {
                if (other == null || !other.m_save)
                    continue;
                Vector3 d = other.m_pos - pin.m_pos;
                float sqr = d.x * d.x + d.z * d.z;
                if (sqr > bestSqr)
                    continue;
                bestSqr = sqr;
                best = other;
            }

            return best;
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
    /// shift opens the pin editor on it instead, so a pin can be renamed or re-iconed without
    /// deleting and replacing it.
    ///
    /// Returning false skips the original deliberately: letting vanilla also toggle the tick
    /// would make one shift-click do two unrelated things.
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
    /// the event system, which respects the pointer) and zooms the map.
    ///
    /// This forwards to a private Internal_GetMouseScrollWheel that holds the real logic, which
    /// normally makes the forwarder the wrong thing to patch, but every caller in the game goes
    /// through this public static one (Minimap.UpdateMap, GameCamera.UpdateCamera,
    /// GameCamera.UpdateFreeFly, Player.UpdatePlacement), so nothing bypasses it. The other three
    /// are unreachable while the cursor sits on a panel that only exists on the open large map.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class Patch_ZInput_GetMouseScrollWheel
    {
        private static bool Prefix(ref float __result)
        {
            // Both panels scroll their own grid, so the wheel has to be withheld from the map
            // over either of them.
            if (!MapIconPicker.PointerOverPanel() && !PinEditor.PointerOverPanel())
                return true;
            __result = 0f;
            return false;
        }
    }
}
