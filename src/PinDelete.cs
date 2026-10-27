using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    /// General/ConfirmDelete.
    public enum DeleteConfirm { Off, AutoPinsOnly, All }

    /// Right-click deletes the pin you are pointing at, Ctrl+Z puts it back, and deleting can be
    /// made to ask twice.
    ///
    /// Vanilla's right-click (Minimap.RemovePinUnderPointer, ILSpy) calls RemovePin(pos,
    /// PinInteractRadius), which removes whichever pin's map position is nearest the cursor. The
    /// pin that grows under the cursor is PinHover.Current, picked by its icon. With pins close
    /// together those are often two different pins, so the one that lit up was not the one that
    /// went (asked on Nexus, 2026-10-23: "easy to accidentally delete the wrong pin"). Getting an
    /// auto pin back meant carturpins_forget_missing and a rescan, which also brings back every
    /// other pin deleted on purpose.
    internal static class PinDelete
    {
        private struct Removed
        {
            public Vector3 Pos;
            public Minimap.PinType Type;
            public string Name;
            public bool Checked;
            public long Owner;
            public object Author;   // PlatformUserID, from Splatform, which this mod does not reference
        }

        private const int UndoDepth = 10;
        private const float ConfirmWindow = 2f;

        private static readonly List<Removed> Undo = new List<Removed>();
        private static Minimap.PinData _armed;
        private static float _armedUntil;

        private static readonly MethodInfo ScreenToWorld = AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint");
        private static readonly MethodInfo HideInput = AccessTools.Method(typeof(Minimap), "HidePinTextInput");
        private static readonly PropertyInfo InteractRadius = AccessTools.Property(typeof(Minimap), "PinInteractRadius");
        private static readonly MethodInfo ClosestPin = AccessTools.Method(typeof(Minimap), "GetClosestPin");
        private static readonly FieldInfo NamePin = AccessTools.Field(typeof(Minimap), "m_namePin");
        private static readonly FieldInfo AuthorField = AccessTools.Field(typeof(Minimap.PinData), "m_author");

        /// Replaces vanilla's right-click delete (returns false) because vanilla removes the
        /// nearest position, not the highlighted pin; see the class comment. Everything else it
        /// does - hide the name box, clear m_namePin - is done here too. Falls through to vanilla
        /// if a member it needs has gone.
        [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
        internal static class Patch_RemovePinUnderPointer
        {
            private static bool Prefix(Minimap __instance)
            {
                if (ScreenToWorld == null || InteractRadius == null || ClosestPin == null)
                    return true;

                HideInput?.Invoke(__instance, new object[] { false });
                NamePin?.SetValue(__instance, null);

                Minimap.PinData target = PinHover.Current;
                if (target == null || !target.m_save)
                {
                    Vector3 pos = (Vector3)ScreenToWorld.Invoke(__instance, new object[] { (Vector3)ZInput.pointerPosition });
                    float radius = (float)InteractRadius.GetValue(__instance);
                    target = ClosestPin.Invoke(__instance, new object[] { pos, radius, true }) as Minimap.PinData;
                }
                if (target == null)
                    return false;

                bool ours = PinRecord.IsOurs(target);
                DeleteConfirm mode = Plugin.ConfirmDelete.Value;
                if ((mode == DeleteConfirm.All || (mode == DeleteConfirm.AutoPinsOnly && ours)) &&
                    !(ReferenceEquals(target, _armed) && Time.unscaledTime < _armedUntil))
                {
                    _armed = target;
                    _armedUntil = Time.unscaledTime + ConfirmWindow;
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                        $"Right-click again to delete {Labels.Localize(target.m_name)} ({(ours ? "auto pin" : "your pin")})");
                    return false;
                }
                _armed = null;

                Undo.Add(new Removed
                {
                    Pos = target.m_pos, Type = target.m_type, Name = target.m_name,
                    Checked = target.m_checked, Owner = target.m_ownerID, Author = AuthorField?.GetValue(target),
                });
                if (Undo.Count > UndoDepth)
                    Undo.RemoveAt(0);
                __instance.RemovePin(target);
                return false;
            }
        }

        /// Ctrl+Z on the large map. The pin goes back where it was with its name, icon, tick and
        /// owner; the pin record and PinStyles are keyed by position, so an auto pin is still the
        /// mod's and keeps its colour.
        [HarmonyPatch(typeof(Minimap), "Update")]
        internal static class Patch_Minimap_Update_Undo
        {
            private static void Postfix(Minimap __instance)
            {
                if (Undo.Count == 0 || __instance.m_mode != Minimap.MapMode.Large || Minimap.InTextInput())
                    return;
                if (!ZInput.GetKeyDown(KeyCode.Z) || !(ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl)))
                    return;

                UndoLast(__instance);
            }
        }

        /// Puts the last deleted pin back. False when there is nothing to undo.
        internal static bool UndoLast(Minimap map)
        {
            if (Undo.Count == 0)
                return false;
            Removed r = Undo[Undo.Count - 1];
            Undo.RemoveAt(Undo.Count - 1);
            Minimap.PinData pin = map.AddPin(r.Pos, r.Type, r.Name, true, r.Checked, r.Owner);
            if (pin != null && r.Author != null)
                AuthorField?.SetValue(pin, r.Author);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Restored {Labels.Localize(r.Name)}");
            return true;
        }

        /// A deleted pin belongs to the world it was on.
        internal static void Reset()
        {
            Undo.Clear();
            _armed = null;
        }
    }
}
