using System.Collections.Generic;
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
        /// Sized by dragging it about in game rather than by arithmetic, which is why these are
        /// two plain numbers: 263 x 273 is what looked right beside vanilla's pin column. The grid
        /// fills the width it is given, so the column count follows from this rather than being a
        /// second number that has to agree with it.
        private const float PanelWidth = 263f;
        private const float PanelHeight = 273f;

        /// Only still here to seed the grid's default: the sections lay out flexibly.
        private const int Columns = 4;

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
        public static bool PointerOverPanel() => IconGrid.PointerOver(_panel, _panelRect, _canvasCamera);

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
            // Bottom right, under vanilla's own column of pin-type buttons. Everything that
            // answers "what will this pin look like" then sits in one place instead of the eye
            // crossing the whole map between two pickers - and it is off the Bounties and
            // Treasure Maps corner, which the old bottom-left position overlapped.
            //
            // The offsets are insets from that corner, so a wider panel grows leftwards and stays
            // clear of the edge rather than running off it.
            panelRt.anchorMin = new Vector2(1f, 0f);
            panelRt.anchorMax = new Vector2(1f, 0f);
            panelRt.pivot = new Vector2(1f, 0f);
            panelRt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panelRt.anchoredPosition = new Vector2(-Plugin.MapPickerRight.Value, Plugin.MapPickerBottom.Value);

            Image bg = _panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);

            _canvasCamera = IconGrid.CameraFor(_panel);

            AddCaption(_panel);
            BuildSearchBar(map);

            _highlights.Clear();
            _highlights.AddRange(IconGrid.Build(_panel, IconGrid.FindTemplateButton(map), Columns,
                                                index => Select(map, index), top: CaptionHeight));

            // Mark whatever is already selected, so the grid opens saying which icon a new pin
            // will get rather than looking like nothing is chosen.
            if (_selectedType.GetValue(map) is Minimap.PinType active)
            {
                int index = CustomIcons.PickerIndexFor(active);
                for (int i = 0; i < _highlights.Count; i++)
                    IconGrid.SetSelected(_highlights[i], i == index);
            }

            Plugin.Log.LogInfo($"Map icon picker built with {CustomIcons.Count} icons plus {CustomIcons.LegacyCount} from the old sheet.");
        }

        private const float CaptionHeight = 20f;

        /// A line above the grid saying how to change a pin you have already placed.
        ///
        /// Shift-clicking a pin is the only way to reach the editor and nothing on screen says so,
        /// which made it a feature only people who read the page knew about. The grid is where
        /// somebody is already looking when they are thinking about icons, so the sentence belongs
        /// here rather than in a toast they will miss.
        private static void AddCaption(GameObject panel)
        {
            var go = new GameObject("Caption");
            go.transform.SetParent(panel.transform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(8f, -CaptionHeight);
            rt.offsetMax = new Vector2(-8f, -2f);

            TextMeshProUGUI text = Fonts.AddLabel(go);
            text.text = "SHIFT CLICK ICON TO CHANGE";
            text.fontSize = 14f;
            text.color = new Color(0.95f, 0.92f, 0.82f, 0.85f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
        }

        private static GameObject _searchBar;

        /// The search box, across the top of the map rather than inside the picker.
        ///
        /// It searches pins, not icons, so it does not belong to the icon grid - and the top
        /// centre is where a search box is looked for. Sized in fractions of the map's width so it
        /// stays centred and proportionate at any resolution.
        ///
        /// Cloned from the map's own pin-name field, the way the grid clones its buttons: it is
        /// already wired for this game's input handling, which a field built from nothing is not -
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
            bar.anchorMin = new Vector2(0.5f, 1f);
            bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(340f, 30f);
            bar.anchoredPosition = new Vector2(0f, -16f);

            Image backing = _searchBar.AddComponent<Image>();
            backing.color = new Color(0f, 0f, 0f, 0.6f);

            TMP_InputField input = UnityEngine.Object.Instantiate(source, _searchBar.transform);
            input.gameObject.name = "CarturPinFilter";
            input.gameObject.SetActive(true);

            RectTransform rt = input.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6f, 3f);
            rt.offsetMax = new Vector2(-6f, -3f);

            input.text = string.Empty;
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

            // So the game knows to stop reading the keyboard while this has focus - and so Enter
            // here no longer reaches Minimap.OnPinTextEntered, which the clone inherited.
            TextFocus.Register(input);
            TextFocus.DropVanillaSubmit(input);
        }

        /// The flag the map raises when its pins need drawing again. Searching has to raise it
        /// too, or a search reads as broken until something else happens to the map.
        private static readonly FieldInfo PinUpdateRequired =
            AccessTools.Field(typeof(Minimap), "m_pinUpdateRequired");

        private static readonly FieldInfo NameInputField =
            AccessTools.Field(typeof(Minimap), "m_nameInput");

        private static void Select(Minimap map, int index)
        {
            Minimap.PinType type = CustomIcons.TypeForPicker(index);

            // Vanilla's SelectIcon also clears the seven vanilla highlights and flags a pin
            // refresh, so going through it keeps everything consistent.
            _selectIcon.Invoke(map, new object[] { type });

            for (int i = 0; i < _highlights.Count; i++)
                IconGrid.SetSelected(_highlights[i], i == index);
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
        /// Vanilla's own markers - your spawn point, your last death - are not saved pins: the map
        /// rebuilds them from your profile on a timer, so a name or an icon set on one is gone by
        /// the next refresh. The cursor lands on the spawn marker rather than on the Home pin
        /// underneath it, which is why a house you had just marked looked nameless and refused to
        /// be edited.
        ///
        /// So an unsaved pin hands over to a saved one on the same spot, and where there is none,
        /// nothing opens at all - better than an editor whose changes evaporate.
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
    /// Returning false skips the original deliberately: shift-click means "edit this pin", and
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
            // Both panels scroll their own grid, so the wheel has to be withheld from the map
            // over either of them.
            if (!MapIconPicker.PointerOverPanel() && !PinEditor.PointerOverPanel())
                return true;
            __result = 0f;
            return false;
        }
    }
}
