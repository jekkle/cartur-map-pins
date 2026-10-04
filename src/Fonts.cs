using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace CarturMapPins
{
    /// The font every label this mod creates is given.
    ///
    /// A label built with no font does not reliably fall back to TMP's default.
    /// TextMeshProUGUI.Awake, finding no font, tries TMP_Settings.defaultFontAsset and then
    /// Resources.Load("Fonts &amp; Materials/LiberationSans SDF") - and Valheim ships its TMP
    /// fonts in an asset bundle rather than under Resources, so on some installs both come back
    /// empty:
    ///
    ///     The LiberationSans SDF Font Asset was not found. There is no Font Asset assigned to
    ///     Caption.
    ///
    /// A TMP_Text with a null font throws out of MaterialReference..ctor the moment anything
    /// measures it, and these labels sit in a layout group inside a ScrollRect - so the throw comes
    /// back every frame from CanvasUpdateRegistry.PerformUpdate and takes the rest of that UI
    /// rebuild with it. The fallback happens to resolve on the dev machine, so it hid this.
    ///
    /// The font is taken from the map's own pin-name field rather than looked up by name: that is
    /// the face the surrounding UI is already drawn in, and a hardcoded name is exactly what a
    /// game update changes.
    internal static class Fonts
    {
        private static readonly FieldInfo NameInputField = AccessTools.Field(typeof(Minimap), "m_nameInput");

        private static TMP_FontAsset _font;
        private static bool _warned;

        /// Cached only once something is found, so a call made before the map UI exists doesn't
        /// freeze the answer at null for the rest of the session.
        public static TMP_FontAsset Game
        {
            get
            {
                if (_font != null)
                    return _font;

                _font = FromMap() ?? AnyLoaded();
                if (_font == null && !_warned)
                {
                    _warned = true;
                    Plugin.Log.LogWarning("No TMP font asset found - this mod's labels will not draw.");
                }
                return _font;
            }
        }

        /// Adds a label carrying that font. Every label in this mod goes through here.
        ///
        /// The object is deactivated across the AddComponent because TextMeshProUGUI.Awake runs
        /// inside it, and Awake is what emits the missing-font warning. The font cannot be set
        /// first since the component has to exist, so without this every label logs a warning
        /// even though the font is assigned a line later.
        public static TextMeshProUGUI AddLabel(GameObject go)
        {
            TMP_FontAsset font = Game;
            if (font == null)
                return go.AddComponent<TextMeshProUGUI>();

            bool active = go.activeSelf;
            go.SetActive(false);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.font = font;
            go.SetActive(active);
            return label;
        }

        private static TMP_FontAsset FromMap()
        {
            if (Minimap.instance == null || NameInputField == null)
                return null;
            var input = NameInputField.GetValue(Minimap.instance) as TMP_InputField;
            return input?.textComponent?.font;
        }

        /// Last resort: whatever font the game has already loaded. Which one it is matters far
        /// less than having one - a label in the wrong face still reads, where a label with no
        /// font throws every frame.
        private static TMP_FontAsset AnyLoaded()
        {
            foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font != null)
                    return font;
            }
            return null;
        }
    }
}
