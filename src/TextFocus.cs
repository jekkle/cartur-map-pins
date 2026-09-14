using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;

namespace CarturMapPins
{
    /// Tells the game that one of this mod's text boxes has the keyboard.
    ///
    /// Minimap.InTextInput is what PlayerController.TakeInput, Chat.Update and Minimap.Update all
    /// consult before acting on a key - it is how vanilla's own pin-name field stops M from closing
    /// the map while you are typing a name. Fields this mod builds are not that field, so the
    /// answer was no and every letter went to the game: h opened another mod's menu, m shut the
    /// map.
    ///
    /// Answering yes for our fields too fixes vanilla and every mod polite enough to ask. A mod
    /// that reads the keyboard raw without asking anyone cannot be fixed from here.
    internal static class TextFocus
    {
        private static readonly List<TMP_InputField> Fields = new List<TMP_InputField>();

        public static void Register(TMP_InputField field)
        {
            if (field != null && !Fields.Contains(field))
                Fields.Add(field);
        }

        /// True while any of them has focus. Destroyed fields are dropped as they are found -
        /// the map is rebuilt on every world load, and its fields go with it.
        public static bool Active
        {
            get
            {
                for (int i = Fields.Count - 1; i >= 0; i--)
                {
                    if (Fields[i] == null)
                    {
                        Fields.RemoveAt(i);
                        continue;
                    }
                    if (Fields[i].isFocused)
                        return true;
                }
                return false;
            }
        }

        /// Cuts a cloned text box loose from vanilla's pin-naming handler.
        ///
        /// Minimap.m_nameInput is a GUIFramework.GuiInputField, and that class's own Start()
        /// wires onSubmit to its OnInputSubmit event - a serialized field, so Instantiate copies
        /// it with the prefab's listener intact, and that listener is how the map names a pin.
        /// Giving the clone a fresh onSubmit does not help: Start runs a frame later and hooks
        /// the fresh one just the same. So Enter in a box of ours reached Minimap.OnPinTextEntered,
        /// which closes vanilla's own name box and leaves whatever pin it was naming unnamed.
        ///
        /// Only the field itself stops it. Its handler null-checks it before invoking, so null is
        /// the whole fix - no listener to remove and no replacement event to build.
        private static FieldInfo _vanillaSubmit;

        public static void DropVanillaSubmit(TMP_InputField field)
        {
            if (field == null)
                return;
            if (_vanillaSubmit == null)
                _vanillaSubmit = AccessTools.Field(field.GetType(), "OnInputSubmit");
            _vanillaSubmit?.SetValue(field, null);
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.InTextInput))]
    internal static class Patch_Minimap_InTextInput
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && TextFocus.Active)
                __result = true;
        }
    }
}
