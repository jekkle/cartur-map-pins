using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// A small panel for editing one existing pin: rename it, or give it a different icon.
    ///
    /// Opened by shift-clicking a pin on the large map, and placed beside that pin rather than in
    /// a fixed corner, so the icon being changed stays visible while you choose its replacement.
    ///
    /// Works on any pin, ours or hand-placed, which is the only repair available for a pin whose
    /// artwork moved when the icon sheet was replaced - nothing records which icon was originally
    /// meant, but the player can point at the pin and choose again.
    ///
    /// Nothing is written until Confirm: picking an icon only marks the choice, so a misclick in
    /// a 153-icon grid costs nothing.
    internal static class PinEditor
    {
        private const int Columns = 5;
        private const float VisibleRows = 5f;
        private const float HeaderHeight = 30f;   // name field
        private const float FooterHeight = 30f;   // confirm button
        private const float GapFromPin = 26f;     // keeps the pin itself uncovered

        private static GameObject _panel;
        private static RectTransform _panelRect;
        private static Camera _canvasCamera;
        private static TMP_InputField _nameInput;
        private static List<Image> _highlights;
        private static Minimap.PinData _target;
        private static Minimap _map;
        private static int _pending = -1;

        /// Minimap.m_nameInput is a GUIFramework.GuiInputField, which lives in gui_framework.dll.
        /// Read by reflection rather than referencing that assembly: the clone only needs to be a
        /// TMP_InputField, which GuiInputField derives from and which is already referenced.
        private static readonly FieldInfo NameInputField = AccessTools.Field(typeof(Minimap), "m_nameInput");

        /// Keeps the scroll wheel off the map while the cursor is over the editor.
        public static bool PointerOverPanel() => IconGrid.PointerOver(_panel, _panelRect, _canvasCamera);

        public static void Open(Minimap map, Minimap.PinData pin)
        {
            if (map == null || pin == null)
                return;

            _map = map;
            if (_panel == null)
                Build(map);
            if (_panel == null)
                return;

            _target = pin;
            _pending = CustomIcons.IsCustom(pin.m_type)
                ? (int)pin.m_type - CustomIcons.FirstCustomType
                : -1;

            if (_nameInput != null)
                _nameInput.text = pin.m_name ?? string.Empty;

            // Opens showing the icon this pin already has, so the gold border says "this is what
            // it is" before it says "this is what you just picked".
            _pending = CustomIcons.PickerIndexFor(pin.m_type);

            RefreshHighlights();
            _panel.SetActive(true);
            PlaceBeside(pin);
        }

        public static void Close()
        {
            _target = null;
            _pending = -1;
            if (_panel != null)
                _panel.SetActive(false);
        }

        /// Positions the panel next to the pin's own UI element, nudged clear of it, then pulled
        /// back inside the screen so a pin near an edge doesn't open the panel off-screen.
        private static void PlaceBeside(Minimap.PinData pin)
        {
            if (_panelRect == null)
                return;

            RectTransform pinRect = pin.m_uiElement;
            if (pinRect == null)
            {
                // The pin has no UI element yet (off-screen, or not drawn this frame). Fall back
                // to the cursor, which is where the click just happened anyway.
                _panelRect.position = ZInput.pointerPosition + new Vector3(GapFromPin, 0f, 0f);
            }
            else
            {
                _panelRect.position = pinRect.position + new Vector3(GapFromPin, 0f, 0f);
            }

            ClampToScreen();
        }

        /// RectTransform.position is screen space for an overlay canvas, so the corners can be
        /// compared against the screen directly and the whole panel shifted back into view.
        private static void ClampToScreen()
        {
            var corners = new Vector3[4];
            _panelRect.GetWorldCorners(corners);
            float minX = corners[0].x, minY = corners[0].y;
            float maxX = corners[2].x, maxY = corners[2].y;

            float dx = 0f, dy = 0f;
            if (maxX > Screen.width) dx -= maxX - Screen.width;
            if (minX + dx < 0f) dx += -(minX + dx);
            if (maxY > Screen.height) dy -= maxY - Screen.height;
            if (minY + dy < 0f) dy += -(minY + dy);

            if (dx != 0f || dy != 0f)
                _panelRect.position += new Vector3(dx, dy, 0f);
        }

        private static void Build(Minimap map)
        {
            if (map.m_largeRoot == null || !CustomIcons.Ready)
                return;

            _panel = new GameObject("CarturPinEditor");
            _panel.transform.SetParent(map.m_largeRoot.transform, false);
            _panelRect = _panel.AddComponent<RectTransform>();
            // Anchored to one corner so that setting .position places it outright, rather than
            // being stretched by a parent whose size we don't control.
            _panelRect.anchorMin = Vector2.zero;
            _panelRect.anchorMax = Vector2.zero;
            _panelRect.pivot = new Vector2(0f, 0.5f);
            _panelRect.sizeDelta = new Vector2(IconGrid.PanelWidth(Columns),
                                               IconGrid.PanelHeight(VisibleRows) + HeaderHeight + FooterHeight);

            Image bg = _panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.85f);

            _nameInput = AddNameInput(map, _panel.transform);
            // Confirm on the right where a dialog usually puts it, cancel on the left.
            AddFooterButton(_panel.transform, "Cancel", 0f, 0.5f, Cancel,
                            new Color(0.24f, 0.22f, 0.20f, 0.95f));
            AddFooterButton(_panel.transform, "Confirm", 0.5f, 1f, Confirm,
                            new Color(0.35f, 0.30f, 0.20f, 0.95f));

            _highlights = IconGrid.Build(_panel, IconGrid.FindTemplateButton(map), Columns,
                                         Choose, HeaderHeight, FooterHeight);
            _canvasCamera = IconGrid.CameraFor(_panel);
            _panel.SetActive(false);

            Plugin.Log.LogInfo("Pin editor built.");
        }

        /// Clones vanilla's pin-name field so the box matches the rest of the map UI. Returns null
        /// when it can't be found, leaving the editor icon-only rather than failing to open.
        private static TMP_InputField AddNameInput(Minimap map, Transform parent)
        {
            var source = NameInputField?.GetValue(map) as Component;
            if (source == null)
            {
                Plugin.Log.LogWarning("Minimap.m_nameInput not found - the pin editor will not offer renaming.");
                return null;
            }

            GameObject clone = Object.Instantiate(source.gameObject, parent);
            clone.name = "PinName";
            clone.SetActive(true);

            RectTransform rt = clone.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(8f, 0f);
                rt.offsetMax = new Vector2(-8f, 0f);
                rt.anchoredPosition = new Vector2(0f, -5f);
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, HeaderHeight - 10f);
            }

            var input = clone.GetComponent<TMP_InputField>();
            if (input == null)
                return null;

            // Fresh event objects rather than RemoveAllListeners, which leaves the prefab's
            // serialized calls intact - vanilla's handler would otherwise also fire and try to
            // name a pin that isn't the one being edited. Nothing is applied here: the name is
            // read when Confirm is pressed.
            input.onSubmit = new TMP_InputField.SubmitEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            return input;
        }

        /// One half of the footer. `from` and `to` are fractions of the panel's width, so the two
        /// buttons split it without either needing to know the panel's size.
        private static void AddFooterButton(Transform parent, string text, float from, float to,
                                            UnityEngine.Events.UnityAction onClick, Color colour)
        {
            var go = new GameObject(text);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(from, 0f);
            rt.anchorMax = new Vector2(to, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(from == 0f ? 8f : 3f, 6f);
            rt.offsetMax = new Vector2(to == 1f ? -8f : -3f, 0f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, FooterHeight - 10f);

            Image bg = go.AddComponent<Image>();
            bg.color = colour;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(onClick);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            RectTransform lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            // No font assigned on purpose: TMP falls back to its default, whereas naming a font
            // asset that may not exist yields invisible text rather than an obvious error.
            TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 15f;
            label.color = new Color(0.95f, 0.92f, 0.82f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        /// Marks a choice without applying it, so a misclick in a 153-icon grid costs nothing.
        private static void Choose(int index)
        {
            _pending = index;
            RefreshHighlights();
        }

        /// Throws away whatever was typed or clicked and shuts the panel.
        ///
        /// Nothing has to be undone: choosing an icon only marks it, and the name lives in the
        /// input box until Confirm copies it across - so closing is the undo. Worth a button of
        /// its own all the same, because "click away and hope" is not an obvious way to back out
        /// of a dialog with a Confirm on it.
        private static void Cancel() => Close();

        private static void Confirm()
        {
            if (_target == null || _map == null)
            {
                Close();
                return;
            }

            bool changed = false;

            if (_nameInput != null && _nameInput.text != _target.m_name)
            {
                _target.m_name = _nameInput.text ?? string.Empty;
                changed = true;
            }

            if (_pending >= 0)
            {
                Minimap.PinType wanted = CustomIcons.TypeForPicker(_pending);
                if (wanted != _target.m_type && PinRecord.Repoint(_map, _target, wanted))
                    changed = true;
            }

            if (changed)
                _map.SaveMapData();

            Close();
        }

        private static void RefreshHighlights()
        {
            if (_highlights == null)
                return;
            for (int i = 0; i < _highlights.Count; i++)
                IconGrid.SetSelected(_highlights[i], i == _pending);
        }
    }
}
