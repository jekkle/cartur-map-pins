using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    /// Registers the icon sheet as extra map pin types, alongside the vanilla ones.
    ///
    /// Nothing vanilla is replaced: custom types are appended starting at FirstCustomType, so
    /// Icon0-Icon4, Boss, Bed and Death keep working and stay selectable.
    ///
    /// Three things have to line up for this to work, and only the first is obvious:
    ///  1. Minimap.m_icons is a public List&lt;SpriteData&gt; and GetSprite does m_icons.Find(...),
    ///     a predicate search - so a PinType cast from an arbitrary int resolves fine.
    ///  2. Minimap.AddPin CLAMPS any type >= m_visibleIconTypes.Length to Icon3, and
    ///  3. the pin render loop does a raw m_visibleIconTypes[(int)pin.m_type], which throws for
    ///     an out-of-range type.
    /// So the private bool[] m_visibleIconTypes must be grown before any custom pin exists.
    /// Without that, custom pins would be silently downgraded to Icon3 *and written that way to
    /// the save file*, which is why this whole feature is opt-in.
    internal static class CustomIcons
    {
        /// Well clear of the 17 vanilla PinType values, leaving room for the game to add more
        /// without colliding with saved pins.
        public const int FirstCustomType = 100;

        private const int SheetColumns = 10;

        /// Derived from the enum rather than written down, because the enum and the sheet are
        /// generated together by tools/build_sheet.py. A hardcoded count that fell behind the
        /// sheet would slice off the last row; one that ran ahead would create null sprites.
        public static readonly int IconCount = Enum.GetValues(typeof(PinIcon)).Length - 1;   // -1: Default

        private static Sprite[] _sprites;

        /// The sliced icon sheet, for anything that needs to draw the icons itself - the map
        /// picker and the settings-screen drawer. Null until the sheet has loaded.
        public static Sprite[] Sprites => _sprites;

        /// Which Minimap instance we've registered against.
        ///
        /// This must NOT be a plain "done" flag: returning to the menu and loading another world
        /// builds a fresh Minimap with a fresh m_icons list and a fresh m_visibleIconTypes array
        /// (Start recreates it). A static bool would skip re-registration, leaving custom types
        /// unknown to that map - so AddPin would clamp them to Icon3 and write that into the new
        /// world's saved pins. Keying on the instance re-registers whenever the map is rebuilt.
        private static Minimap _registeredFor;

        public static bool Ready => _sprites != null;

        public static int Count => _sprites?.Length ?? 0;

        public static Sprite SpriteAt(int index) =>
            _sprites != null && index >= 0 && index < _sprites.Length ? _sprites[index] : null;

        /// Maps an icon index (0-82) to the PinType that carries it.
        public static Minimap.PinType TypeForIndex(int index) =>
            (Minimap.PinType)(FirstCustomType + index);

        public static bool IsCustom(Minimap.PinType type) => (int)type >= FirstCustomType;

        /// Resolves a category's configured icon: a custom index when set, otherwise its vanilla
        /// PinType. Falls back to the vanilla type if custom icons failed to load, so a bad sheet
        /// degrades to "normal pins" rather than invisible ones.
        public static Minimap.PinType Resolve(int iconIndex, Minimap.PinType fallback)
        {
            if (!Plugin.CustomIconsEnabled.Value || !Ready)
                return fallback;
            if (iconIndex < 0 || iconIndex >= Count)
                return fallback;
            return TypeForIndex(iconIndex);
        }

        public static void Register(Minimap map)
        {
            if (map == null || _registeredFor == map)
                return;
            if (!Plugin.CustomIconsEnabled.Value)
                return;

            try
            {
                // The sheet and its sprites survive a world reload; only this Minimap's icon
                // list and filter array need redoing.
                if (_sprites == null)
                {
                    Texture2D sheet = LoadSheet();
                    if (sheet == null)
                        return;
                    _sprites = Slice(sheet);
                }

                // Grow the visibility filter FIRST - AddPin and the render loop both index it.
                GrowVisibleIconTypes(map, FirstCustomType + _sprites.Length + 1);

                int added = 0;
                for (int i = 0; i < _sprites.Length; i++)
                {
                    if (_sprites[i] == null)
                        continue;
                    if (AlreadyRegistered(map, TypeForIndex(i)))
                        continue;
                    map.m_icons.Add(new Minimap.SpriteData
                    {
                        m_name = TypeForIndex(i),
                        m_icon = _sprites[i]
                    });
                    added++;
                }

                ReplaceBedSprite(map);

                _registeredFor = map;
                Plugin.Log.LogInfo($"Registered {added} custom pin icons as types {FirstCustomType}-{FirstCustomType + _sprites.Length - 1}.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Custom icons failed to register, falling back to vanilla pin types: {e}");
                _sprites = null;
                _registeredFor = null;
            }
        }

        /// Repoints vanilla's own spawn-point marker at the house icon.
        ///
        /// Minimap keeps one m_spawnPointPin and moves it when you claim a different bed, so it
        /// marks where you respawn rather than where you have lived. The mod's own Home pins mark
        /// the beds and stay put - and since the two sit on the same spot for the bed you are
        /// currently using, vanilla's bed glyph on top of our house would just be a double.
        ///
        /// Swapping the sprite in m_icons rather than touching the pin: the pin is re-created and
        /// re-positioned by UpdateProfilePins on its own schedule, and anything done to the pin
        /// itself would have to be redone every time it does that.
        private static void ReplaceBedSprite(Minimap map)
        {
            if (!Plugin.ReplaceBedMarker.Value)
                return;

            Plugin.CategorySettings home = Plugin.SettingsFor(PinCategory.Home);
            if (home == null)
                return;

            Sprite sprite = SpriteFor((int)home.IconIndex.Value);
            if (sprite == null)
                return;

            // By index and written back: SpriteData is a struct, so the loop variable is a copy
            // and assigning to it changes nothing that survives the iteration.
            for (int i = 0; i < map.m_icons.Count; i++)
            {
                if (map.m_icons[i].m_name != Minimap.PinType.Bed)
                    continue;
                Minimap.SpriteData data = map.m_icons[i];
                data.m_icon = sprite;
                map.m_icons[i] = data;
                return;
            }
        }

        private static Sprite SpriteFor(int iconIndex)
        {
            if (_sprites == null || iconIndex < 0 || iconIndex >= _sprites.Length)
                return null;
            return _sprites[iconIndex];
        }

        private static bool AlreadyRegistered(Minimap map, Minimap.PinType type)
        {
            foreach (Minimap.SpriteData data in map.m_icons)
            {
                if (data.m_name == type)
                    return true;
            }
            return false;
        }

        /// An override file next to the config wins, so the sheet can be swapped without a
        /// rebuild; otherwise the embedded copy is used.
        private static Texture2D LoadSheet()
        {
            string overridePath = Path.Combine(BepInEx.Paths.ConfigPath, "carturmappins_icons.png");
            byte[] png;

            if (File.Exists(overridePath))
            {
                png = File.ReadAllBytes(overridePath);
                Plugin.Log.LogInfo($"Loading icon sheet override from {overridePath}");
            }
            else
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("CarturMapPins.pin_icons.png"))
                {
                    if (stream == null)
                    {
                        Plugin.Log.LogError("Embedded icon sheet not found.");
                        return null;
                    }
                    png = new byte[stream.Length];
                    stream.Read(png, 0, png.Length);
                }
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!LoadImageViaReflection(tex, png))
                return null;

            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        /// Texture2D.LoadImage is called by reflection on purpose.
        ///
        /// Referencing that overload set at compile time drags in System.ReadOnlySpan&lt;byte&gt;,
        /// which doesn't resolve against net472 plus this game's netstandard.dll (CS0518) - the
        /// same wall hit in the compass mod, which worked around it by shipping raw RGBA bytes
        /// instead of a PNG. Reflection avoids the compile-time reference entirely, so the sheet
        /// can stay a ~570KB PNG rather than a 16MB raw dump.
        private static bool LoadImageViaReflection(Texture2D tex, byte[] data)
        {
            Type imageConversion = AccessTools.TypeByName("UnityEngine.ImageConversion");
            if (imageConversion == null)
            {
                Plugin.Log.LogError("UnityEngine.ImageConversion not found; cannot decode the icon sheet.");
                return false;
            }

            MethodInfo loadImage = null;
            foreach (MethodInfo m in imageConversion.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "LoadImage")
                    continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 3 && ps[1].ParameterType == typeof(byte[]))
                {
                    loadImage = m;
                    break;
                }
            }

            if (loadImage == null)
            {
                Plugin.Log.LogError("No LoadImage(Texture2D, byte[], bool) overload found.");
                return false;
            }

            object result = loadImage.Invoke(null, new object[] { tex, data, false });
            return result is bool ok && ok;
        }

        /// The sheet is a uniform grid; cell pitch is the texture width divided by the column
        /// count, and rows are counted from the top while Unity's texture origin is bottom-left.
        private static Sprite[] Slice(Texture2D sheet)
        {
            float pitch = sheet.width / (float)SheetColumns;
            var sprites = new Sprite[IconCount];

            for (int i = 0; i < IconCount; i++)
            {
                int col = i % SheetColumns;
                int row = i / SheetColumns;

                float x = col * pitch;
                float yTop = row * pitch;
                float y = sheet.height - yTop - pitch;   // flip to bottom-left origin

                var rect = new Rect(x, y, pitch, pitch);
                sprites[i] = Sprite.Create(sheet, rect, new Vector2(0.5f, 0.5f));
                sprites[i].name = $"CarturPin_{i}";
            }

            return sprites;
        }

        private static void GrowVisibleIconTypes(Minimap map, int size)
        {
            FieldInfo field = AccessTools.Field(typeof(Minimap), "m_visibleIconTypes");
            if (field == null)
                throw new InvalidOperationException("Minimap.m_visibleIconTypes not found - refusing to register custom icons, since AddPin would silently rewrite them to Icon3 in the save.");

            var existing = field.GetValue(map) as bool[];
            int from = existing?.Length ?? 0;
            if (from >= size)
                return;

            var grown = new bool[size];
            for (int i = 0; i < size; i++)
                grown[i] = i < from ? existing[i] : true;   // new types default to visible

            field.SetValue(map, grown);
            Plugin.Log.LogInfo($"Grew Minimap.m_visibleIconTypes from {from} to {size}.");
        }
    }

    /// Minimap.Start is the right moment: it creates m_visibleIconTypes, and saved pins are
    /// re-added later from Update -> LoadMapData, so sprites are registered before any pin needs
    /// one resolved.
    [HarmonyPatch(typeof(Minimap), "Start")]
    internal static class Patch_Minimap_Start
    {
        private static void Postfix(Minimap __instance)
        {
            CustomIcons.Register(__instance);
            MapIconPicker.Build(__instance);
        }
    }
}
