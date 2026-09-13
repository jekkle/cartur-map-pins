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
    /// Opened by shift-clicking a pin on the large map. Works on any pin, ours or hand-placed,
    /// which is the only repair available for a pin whose artwork moved when the icon sheet was
    /// replaced - nothing records which icon was originally meant, but the player can point at
    /// the pin and choose again.
    ///
    /// Parented to Minimap.m_largeRoot so it disappears with the map rather than needing its own
    /// open/close bookkeeping.
    internal static class PinEditor
    {
        private const int Columns = 5;
        private const float VisibleRows = 5f;
        private const float HeaderHeight = 34f;

        private static GameObject _panel;
        private static TMP_InputField _nameInput;
        private static TMP_Text _title;
        private static List<Image> _highlights;
        private static Minimap.PinData _target;
        private static Minimap _map;

        /// Minimap.m_nameInput is a GUIFramework.GuiInputField, which lives in gui_framework.dll.
        /// Read by reflection rather than referencing that assembly: the clone only needs to be a
        /// TMP_InputField, which GuiInputField derives from and which is already referenced.
        private static readonly FieldInfo NameInputField = AccessTools.Field(typeof(Minimap), "m_nameInput");

        public static bool IsOpen => _panel != null && _panel.activeSelf;

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
            if (_title != null)
                _title.text = string.IsNullOrEmpty(pin.m_name) ? "Unnamed pin" : pin.m_name;
            if (_nameInput != null)
                _nameInput.text = pin.m_name ?? string.Empty;

            RefreshHighlights();
            _panel.SetActive(true);
        }

        public static void Close()
        {
            _target = null;
            if (_panel != null)
                _panel.SetActive(false);
        }

        private static void Build(Minimap map)
        {
            if (map.m_largeRoot == null || !CustomIcons.Ready)
                return;

            _panel = new GameObject("CarturPinEditor");
            _panel.transform.SetParent(map.m_largeRoot.transform, false);
            RectTransform rt = _panel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(IconGrid.PanelWidth(Columns),
                                       IconGrid.PanelHeight(VisibleRows) + HeaderHeight);
            rt.anchoredPosition = new Vector2(-Plugin.MapPickerX.Value, Plugin.MapPickerY.Value);

            Image bg = _panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.78f);

            _title = AddLabel(_panel.transform, "Edit pin", -6f);
            _nameInput = AddNameInput(map, _panel.transform);

            _highlights = IconGrid.Build(_panel, IconGrid.FindTemplateButton(map), Columns, Apply, HeaderHeight);
            _panel.SetActive(false);

            Plugin.Log.LogInfo("Pin editor built.");
        }

        private static TMP_Text AddLabel(Transform parent, string text, float y)
        {
            var go = new GameObject("Title");
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(10f, 0f);
            rt.offsetMax = new Vector2(-10f, y);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, 16f);

            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 14f;
            label.color = new Color(0.9f, 0.86f, 0.76f);
            label.alignment = TextAlignmentOptions.Left;
            label.raycastTarget = false;
            // No font is assigned on purpose: TMP falls back to its default, and naming a font
            // asset that may not exist yields invisible text rather than an obvious error.
            return label;
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
                rt.anchoredPosition = new Vector2(0f, -16f);
                rt.sizeDelta = new Vector2(-20f, 22f);
            }

            var input = clone.GetComponent<TMP_InputField>();
            if (input == null)
                return null;

            // Fresh event objects rather than RemoveAllListeners, which leaves the prefab's
            // serialized calls intact - vanilla's handler would otherwise also fire and try to
            // name a pin that isn't the one being edited.
            input.onSubmit = new TMP_InputField.SubmitEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.onSubmit.AddListener(Rename);
            input.onEndEdit.AddListener(Rename);
            return input;
        }

        private static void Rename(string text)
        {
            if (_target == null || _map == null)
                return;
            if (_target.m_name == text)
                return;

            _target.m_name = text ?? string.Empty;
            if (_title != null)
                _title.text = string.IsNullOrEmpty(_target.m_name) ? "Unnamed pin" : _target.m_name;
            _map.SaveMapData();
        }

        private static void Apply(int index)
        {
            if (_target == null || _map == null)
                return;

            Minimap.PinType type = CustomIcons.TypeForIndex(index);
            if (PinRecord.Repoint(_map, _target, type))
                _map.SaveMapData();
            RefreshHighlights();
        }

        private static void RefreshHighlights()
        {
            if (_highlights == null)
                return;
            int current = _target != null && CustomIcons.IsCustom(_target.m_type)
                ? (int)_target.m_type - CustomIcons.FirstCustomType
                : -1;
            for (int i = 0; i < _highlights.Count; i++)
            {
                if (_highlights[i] != null)
                    _highlights[i].enabled = i == current;
            }
        }
    }
}
