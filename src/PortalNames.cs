using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    /// A portal's pin carries the portal's name, whenever that name is set or changed (owner,
    /// 2026-10-27: "when a player builds a portal and names it we can auto spawn a portal pin
    /// with that name").
    ///
    /// The scanner pins a new portal as soon as it is built, when its tag is still empty, so the
    /// pin starts as "Portal". The tag lives in the portal's ZDO (TeleportWorld.GetText, read from
    /// the decompile) and changes through an RPC the owner applies, so this does not hook a call:
    /// every second and a half it reads the tag of each loaded portal that has no name yet and, the
    /// first time it has one, puts it on the pin. After that the portal is not looked at again
    /// (owner: "it will check just the first time"), so a later rename does not change the pin,
    /// and a loaded world with fifty named portals costs nothing. A pin whose name was changed by
    /// hand is left alone (the same test as every other re-label: it must still read what this
    /// mod wrote).
    internal static class PortalNames
    {
        private static readonly HashSet<TeleportWorld> Live = new HashSet<TeleportWorld>();
        private static readonly HashSet<TeleportWorld> Named = new HashSet<TeleportWorld>();
        private static float _timer;

        [HarmonyPatch(typeof(TeleportWorld), "Awake")]
        private static class Patch_Awake
        {
            private static void Postfix(TeleportWorld __instance) => Live.Add(__instance);
        }

        public static void Tick(float dt)
        {
            _timer -= dt;
            if (_timer > 0f)
                return;
            _timer = 1.5f;

            Plugin.CategorySettings settings = Plugin.SettingsFor(PinCategory.Portal);
            if (settings == null || !settings.Enabled.Value || Minimap.instance == null || Player.m_localPlayer == null)
                return;

            Live.RemoveWhere(p => p == null);
            Named.RemoveWhere(p => p == null);
            foreach (TeleportWorld portal in Live)
            {
                if (Named.Contains(portal))
                    continue;
                string tag = portal.GetText();
                if (string.IsNullOrWhiteSpace(tag))
                    continue;   // still unnamed: look again next time
                string label = tag.Trim();
                PinRecord.Rename("Portal", portal.transform.position, 3f, label, Labels.ForPin(label));
                Named.Add(portal);   // named once, done; a pin that is not there yet is placed with this name anyway
            }
        }
    }
}
