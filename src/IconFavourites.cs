using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    /// The icons a player has starred, and the last few they placed or picked, kept in two small
    /// files in the config folder (one picker index per line), the same in every world.
    ///
    /// The picker shows them as rows above everything else (FAVOURITES, RECENT), so the three
    /// icons somebody uses all the time are not 150 cells away (owner plan, Release 4).
    internal static class IconFavourites
    {
        public const int RecentMax = 8;

        private static readonly List<int> FavouriteList = new List<int>();
        private static readonly List<int> RecentList = new List<int>();
        private static bool _loaded;

        /// Raised after either list changes, so the picker can rebuild its top rows.
        public static event Action Changed;

        private static string FavouritePath => Path.Combine(BepInEx.Paths.ConfigPath, "carturpins_favourite_icons.txt");
        private static string RecentPath => Path.Combine(BepInEx.Paths.ConfigPath, "carturpins_recent_icons.txt");

        public static IReadOnlyList<int> Favourites { get { Load(); return FavouriteList; } }
        public static IReadOnlyList<int> Recent { get { Load(); return RecentList; } }
        public static bool IsFavourite(int index) { Load(); return FavouriteList.Contains(index); }

        public static void Toggle(int index)
        {
            Load();
            if (!FavouriteList.Remove(index))
                FavouriteList.Add(index);
            Save(FavouritePath, FavouriteList);
            Changed?.Invoke();
        }

        /// Newest first, no repeats. A starred icon is not repeated in Recent.
        public static void NoteUsed(int index)
        {
            Load();
            if (index < 0 || (RecentList.Count > 0 && RecentList[0] == index))
                return;
            RecentList.Remove(index);
            RecentList.Insert(0, index);
            while (RecentList.Count > RecentMax)
                RecentList.RemoveAt(RecentList.Count - 1);
            Save(RecentPath, RecentList);
            Changed?.Invoke();
        }

        private static void Load()
        {
            if (_loaded)
                return;
            _loaded = true;
            Read(FavouritePath, FavouriteList);
            Read(RecentPath, RecentList);
        }

        private static void Read(string path, List<int> into)
        {
            try
            {
                if (!File.Exists(path))
                    return;
                foreach (string line in File.ReadAllLines(path))
                    if (int.TryParse(line.Trim(), out int index) && index >= 0 && !into.Contains(index))
                        into.Add(index);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not read {Path.GetFileName(path)}: {e.Message}");
            }
        }

        private static void Save(string path, List<int> from)
        {
            try
            {
                var lines = new List<string>(from.Count);
                foreach (int i in from)
                    lines.Add(i.ToString());
                File.WriteAllLines(path, lines.ToArray());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not write {Path.GetFileName(path)}: {e.Message}");
            }
        }
    }

    /// A pin placed by hand takes the icon that was selected at that moment: vanilla's
    /// ShowPinNameInput adds it (read from the decompile: AddPin(pos, m_selectedType, ...)), so
    /// that is the moment to note the icon as recently used.
    [HarmonyPatch(typeof(Minimap), "ShowPinNameInput")]
    internal static class Patch_Minimap_ShowPinNameInput
    {
        private static void Postfix(Minimap __instance)
        {
            var selected = AccessTools.Field(typeof(Minimap), "m_selectedType")?.GetValue(__instance);
            if (selected is Minimap.PinType type)
            {
                int index = CustomIcons.PickerIndexFor(type);
                if (index >= 0)
                    IconFavourites.NoteUsed(index);
            }
        }
    }
}
