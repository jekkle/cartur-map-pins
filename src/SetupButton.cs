using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    /// The "Open Hugin's setup" button at the top of the settings menu.
    ///
    /// A CustomDrawer like IconDrawer and HideListDrawer, except that it never writes the value. It
    /// calls SetupMenu.Open, which is also what P and carturpins_setup call, and then closes
    /// ConfigurationManager: its window is IMGUI, which draws over uGUI, so left open it would
    /// cover the menu that has just opened (ConfigurationManager.DisplayingWindow, read from the
    /// installed DLL).
    internal static class SetupButton
    {
        private static readonly System.Type Manager = AccessTools.TypeByName("ConfigurationManager.ConfigurationManager");
        private static readonly System.Reflection.PropertyInfo Displaying =
            Manager != null ? AccessTools.Property(Manager, "DisplayingWindow") : null;

        public static void Draw(ConfigEntryBase entry)
        {
            if (Player.m_localPlayer == null)
            {
                GUILayout.Label("Load a world first");
                return;
            }
            if (!GUILayout.Button("Open Hugin's setup", GUILayout.MinWidth(180)))
                return;
            SetupMenu.Open();
            if (Displaying == null)
                return;
            foreach (PluginInfo info in Chainloader.PluginInfos.Values)
                if (info.Instance != null && info.Instance.GetType() == Manager)
                    Displaying.SetValue(info.Instance, false, null);
        }
    }
}
