using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace CarturMapPins
{
    /// Draws an icon setting as a dropdown that shows the icons themselves.
    ///
    /// ConfigurationManager's own enum drawer is a text list - IMGUI has no picture control - so
    /// showing the actual pins needs a custom drawer. The value stays a PinIcon either way, so the
    /// config file still reads "Icon = Pickaxe" rather than "Icon = 24".
    ///
    /// Expands inline rather than floating over the page: the drawer runs inside the manager's
    /// scroll view, and an overlay drawn there lands in the wrong place as soon as it scrolls.
    internal static class IconDrawer
    {
        private const int Columns = 10;
        private const int Cell = 34;

        private static readonly HashSet<string> Expanded = new HashSet<string>();

        public static void Draw(ConfigEntryBase entry)
        {
            string key = entry.Definition.Section + "/" + entry.Definition.Key;
            PinIcon current = (PinIcon)entry.BoxedValue;
            Sprite[] sprites = CustomIcons.Sprites;

            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            Rect preview = GUILayoutUtility.GetRect(Cell, Cell, GUILayout.Width(Cell), GUILayout.Height(Cell));
            DrawIcon(preview, sprites, (int)current);
            bool open = Expanded.Contains(key);
            if (GUILayout.Button(current + (open ? "  \u25b2" : "  \u25bc"), GUILayout.MinWidth(150)))
            {
                if (open) Expanded.Remove(key); else Expanded.Add(key);
            }
            GUILayout.EndHorizontal();

            if (!Expanded.Contains(key))
            {
                GUILayout.EndVertical();
                return;
            }

            if (sprites == null || sprites.Length == 0)
            {
                GUILayout.Label("Icon sheet not loaded yet - close and reopen this screen.");
                GUILayout.EndVertical();
                return;
            }

            if (GUILayout.Button("Default", GUILayout.Width(80)))
            {
                entry.BoxedValue = PinIcon.Default;
                Expanded.Remove(key);
            }

            for (int i = 0; i < sprites.Length; i++)
            {
                if (i % Columns == 0)
                    GUILayout.BeginHorizontal();

                Rect slot = GUILayoutUtility.GetRect(Cell, Cell, GUILayout.Width(Cell), GUILayout.Height(Cell));
                if (GUI.Button(slot, GUIContent.none))
                {
                    entry.BoxedValue = (PinIcon)i;
                    Expanded.Remove(key);
                }
                DrawIcon(slot, sprites, i, dim: i != (int)current);

                if (i % Columns == Columns - 1 || i == sprites.Length - 1)
                    GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        /// Sprites are slices of one sheet, so they are drawn by UV rect rather than as whole
        /// textures - GUI.DrawTexture would draw the entire sheet into every cell.
        private static void DrawIcon(Rect slot, Sprite[] sprites, int index, bool dim = false)
        {
            if (sprites == null || index < 0 || index >= sprites.Length)
                return;

            Sprite sprite = sprites[index];
            if (sprite == null || sprite.texture == null)
                return;

            Color previous = GUI.color;
            GUI.color = dim ? new Color(1f, 1f, 1f, 0.5f) : Color.white;

            Rect r = sprite.textureRect;
            GUI.DrawTextureWithTexCoords(slot, sprite.texture,
                new Rect(r.x / sprite.texture.width, r.y / sprite.texture.height,
                         r.width / sprite.texture.width, r.height / sprite.texture.height));

            GUI.color = previous;
        }
    }
}
