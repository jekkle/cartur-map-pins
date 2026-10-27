using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// First-install setup: Hugin lands once and offers a menu, one page per kind of place, to
    /// choose what Map Pins marks. It only writes the switches the config already has, so
    /// everything stays editable there afterwards; "Not now" changes nothing.
    ///
    /// Hugin is spawned the way Cartur's UI welcome (cartur-ui-hud Welcome.cs) and the game's own
    /// Tutorial.SpawnRaven do it: instantiate Tutorial.m_ravenPrefab only when no raven is up, then
    /// Raven.AddTempText. The game keeps one raven and a queue of texts, so with Cartur's UI also
    /// installed there is still one raven, carrying the UI's welcome first (it moves itself to the
    /// front) and this after it. Temp texts are tutorial texts (RavenText.m_isTutorial defaults to
    /// true), and Raven skips those while Raven.m_tutorialsEnabled is false (decompiled), so the
    /// game's "tutorials off" keeps Hugin away; carturpins_setup opens the menu by hand.
    ///
    /// Talking to him calls Player.SetSeenTutorial(key) (Raven.Talk, decompiled), which is what
    /// opens the menu. That is saved per character, so a flag file makes it once per install.
    internal static class SetupMenu
    {
        private const string Key = "carturpins_setup";
        private static readonly string Flag = Path.Combine(BepInEx.Paths.ConfigPath, "carturpins.setup.seen");

        private const string Topic = "Cartur's Map Pins";
        private const string Text =
            "I will mark what you find on your map: ore, dungeons, camps and much more.\n\n" +
            "Let us choose together what is worth a pin. You can change it later in the config.";

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Patch_Land
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || File.Exists(Flag) || Tutorial.instance == null ||
                    !Raven.m_tutorialsEnabled)
                    return;
                if (!Raven.IsInstantiated())
                    Object.Instantiate(Tutorial.instance.m_ravenPrefab, Vector3.zero, Quaternion.identity);
                Raven.AddTempText(Key, Topic, Text, "", false);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetSeenTutorial))]
        private static class Patch_Talked
        {
            private static void Postfix(string name)
            {
                if (name == Key)
                    Open();
            }
        }

        // ---- the pages ------------------------------------------------------------------------

        private sealed class Item
        {
            public string Name;
            public int Icon;                    // picker index, -1 for none
            public ConfigEntry<bool> Entry;     // the kind's own switch, or the category's Enabled
            public PinCategory Category;        // whose Enabled follows the page (kinds only)
            public bool IsKind;
            public bool On;
            public string Heading;              // starts a new group on the page
        }

        private sealed class Page
        {
            public string Title;
            public List<Item> Items = new List<Item>();
            public bool[] Snapshot;
        }

        /// Player-facing names where the config key is not one.
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { PinCatalog.GiantArmor, "Giant swords and helmets" },
        };

        private static List<Page> BuildPages()
        {
            var pages = new List<Page>
            {
                KindPage("Ores", PinCategory.Ore, Subtypes.Ores),
                KindPage("Dungeons", PinCategory.Dungeon, Subtypes.Dungeons),
                KindPage("Camps and villages", PinCategory.Camp, Subtypes.Camps),
            };

            Page bosses = KindPage("Bosses", PinCategory.BossAltar, Subtypes.Bosses, "Boss altars");
            AddKinds(bosses, PinCategory.Miniboss, Subtypes.Minibosses, "Minibosses");
            pages.Add(bosses);

            Page picks = new Page { Title = "Berries and things to pick" };
            AddGroup(picks, PickableGroup.HighValue, "Valuable finds (surtling cores, eggs, barley, flax)");
            AddGroup(picks, PickableGroup.Berries, "Berries and mushrooms");
            AddGroup(picks, PickableGroup.Crops, "Crops and herbs");
            AddGroup(picks, PickableGroup.Junk, "Branches, stones and flint");
            AddGroup(picks, PickableGroup.Other, "Anything else you can pick");
            pages.Add(picks);

            pages.Add(KindPage("Landmarks", PinCategory.Landmark, Subtypes.Landmarks));
            pages.Add(KindPage("Creature spawners", PinCategory.Spawner, Subtypes.Spawners));

            Page other = new Page { Title = "Everything else" };
            AddCategory(other, PinCategory.Chest, "Chests");
            AddCategory(other, PinCategory.Beehive, "Wild beehives");
            AddCategory(other, PinCategory.Runestone, "Runestones");
            AddCategory(other, PinCategory.LoreStone, "Lore stones");
            AddCategory(other, PinCategory.Leviathan, "Leviathans");
            AddCategory(other, PinCategory.Wisp, "Wisp fountains");
            AddCategory(other, PinCategory.Prop, "Maypole");
            AddCategory(other, PinCategory.Home, "Your bed (home)");
            AddKinds(other, PinCategory.Trader, Subtypes.Traders, "Traders");
            pages.Add(other);

            return pages.Where(p => p.Items.Count > 0).ToList();
        }

        private static Page KindPage(string title, PinCategory category, Subtypes.Entry[] table, string heading = null)
        {
            var page = new Page { Title = title };
            AddKinds(page, category, table, heading);
            return page;
        }

        private static void AddKinds(Page page, PinCategory category, Subtypes.Entry[] table, string heading)
        {
            Plugin.CategorySettings settings = Plugin.SettingsFor(category);
            bool first = true;
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(table))
            {
                ConfigEntry<bool> entry = Plugin.KindSwitch(category, e.Name);
                if (entry == null || settings == null)
                    continue;
                int icon = Plugin.KindIconIndex(e.Name);
                page.Items.Add(new Item
                {
                    Name = Names.TryGetValue(e.Name, out string n) ? n : e.Name,
                    Icon = icon >= 0 ? icon : (int)settings.IconIndex.Value,
                    Entry = entry,
                    Category = category,
                    IsKind = true,
                    On = settings.Enabled.Value && entry.Value,
                    Heading = first ? heading : null,
                });
                first = false;
            }
        }

        private static void AddCategory(Page page, PinCategory category, string name)
        {
            Plugin.CategorySettings settings = Plugin.SettingsFor(category);
            if (settings == null)
                return;
            page.Items.Add(new Item { Name = name, Icon = (int)settings.IconIndex.Value, Entry = settings.Enabled, Category = category, On = settings.Enabled.Value });
        }

        private static void AddGroup(Page page, PickableGroup group, string name)
        {
            ConfigEntry<bool> entry = Plugin.PickableSwitch(group);
            Plugin.CategorySettings pick = Plugin.SettingsFor(PinCategory.Pickable);
            if (entry == null || pick == null)
                return;
            page.Items.Add(new Item
            {
                Name = name, Icon = Plugin.PickableIconIndex(group), Entry = entry,
                Category = PinCategory.Pickable, IsKind = true, On = pick.Enabled.Value && entry.Value,
            });
        }

        /// Writes every page. A category whose kinds are listed is on when any of its kinds is,
        /// so switching every Dungeon kind off also stops unlisted (modded) dungeons, as asked.
        private static void Save()
        {
            var anyOn = new Dictionary<PinCategory, bool>();
            foreach (Item item in _pages.SelectMany(p => p.Items))
            {
                item.Entry.Value = item.On;
                if (item.IsKind)
                    anyOn[item.Category] = (anyOn.TryGetValue(item.Category, out bool on) && on) || item.On;
            }
            foreach (KeyValuePair<PinCategory, bool> kv in anyOn)
            {
                Plugin.CategorySettings settings = Plugin.SettingsFor(kv.Key);
                if (settings != null)
                    settings.Enabled.Value = kv.Value;
            }
        }

        // ---- the window -----------------------------------------------------------------------

        private static readonly Color Panel = new Color(0.04f, 0.035f, 0.03f, 0.92f);
        private static readonly Color Warm = new Color(0.95f, 0.92f, 0.82f, 1f);
        private static readonly Color Bronze = new Color(0.78f, 0.58f, 0.32f, 1f);
        private static readonly Color ButtonBack = new Color(0.18f, 0.14f, 0.1f, 0.95f);

        private static GameObject _root;
        private static RectTransform _grid;
        private static TextMeshProUGUI _title, _pageNo, _next;
        private static List<Page> _pages;
        private static int _page;
        private static readonly List<(Item item, Image box, Image icon, TextMeshProUGUI label)> Rows =
            new List<(Item, Image, Image, TextMeshProUGUI)>();

        public static bool IsOpen => _root != null;

        public static void Open()
        {
            if (_root != null || Player.m_localPlayer == null)
                return;
            // Once asked, never landed again - even if the game is closed with the menu up.
            if (!File.Exists(Flag))
                File.WriteAllText(Flag, "");
            if (Minimap.instance != null && Minimap.IsOpen())
                Minimap.instance.SetMapMode(Minimap.MapMode.Small);

            _pages = BuildPages();
            _page = 0;
            Build();
            ShowPage(0);
        }

        private static void Close(bool save)
        {
            if (save)
                Save();
            Object.Destroy(_root);
            _root = null;
            Rows.Clear();
        }

        private static void Build()
        {
            _root = new GameObject("CarturPinsSetup");
            Object.DontDestroyOnLoad(_root);
            Canvas canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();
            _root.AddComponent<Keys>();

            RectTransform panel = Box(_root.transform, "Panel", Panel, new Vector2(860f, 640f));
            Edge(panel, true);
            Edge(panel, false);

            _title = Label(panel, "Title", 34f, Warm, TextAlignmentOptions.TopLeft, new Vector2(36f, -26f), new Vector2(520f, 44f));
            _pageNo = Label(panel, "PageNo", 17f, new Color(Warm.r, Warm.g, Warm.b, 0.6f), TextAlignmentOptions.TopLeft, new Vector2(38f, -72f), new Vector2(400f, 24f));

            Button(panel, "Select all", new Vector2(-322f, -30f), 128f, true, () => SetAll(true));
            Button(panel, "Deselect all", new Vector2(-180f, -30f), 128f, true, () => SetAll(false));
            Button(panel, "Undo", new Vector2(-38f, -30f), 128f, true, Undo);

            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(panel, false);
            _grid = (RectTransform)gridGo.transform;
            _grid.anchorMin = _grid.anchorMax = new Vector2(0f, 1f);
            _grid.pivot = new Vector2(0f, 1f);
            _grid.anchoredPosition = new Vector2(36f, -112f);
            _grid.sizeDelta = new Vector2(788f, 440f);

            Button(panel, "Not now", new Vector2(36f, 28f), 140f, false, () => Close(false), anchorLeft: true);
            Button(panel, "Back", new Vector2(-188f, 28f), 140f, false, () => ShowPage(_page - 1));
            _next = Button(panel, "Next", new Vector2(-36f, 28f), 140f, false, () =>
            {
                if (_page == _pages.Count - 1)
                    Close(true);
                else
                    ShowPage(_page + 1);
            });
        }

        private static void ShowPage(int index)
        {
            if (index < 0 || index >= _pages.Count)
                return;
            _page = index;
            Page page = _pages[index];
            page.Snapshot = page.Items.Select(i => i.On).ToArray();
            _title.text = page.Title;
            _pageNo.text = $"Page {index + 1} of {_pages.Count}  -  choose what gets a pin";
            _next.text = index == _pages.Count - 1 ? "Done" : "Next";

            foreach (Transform child in _grid)
                Object.Destroy(child.gameObject);
            Rows.Clear();

            // Three columns, filled top to bottom; a heading takes a whole row.
            const float colWidth = 262f, rowHeight = 34f;
            int col = 0, row = 0, perColumn = Mathf.CeilToInt((page.Items.Count + page.Items.Count(i => i.Heading != null)) / 3f);
            perColumn = Mathf.Max(perColumn, 1);
            foreach (Item item in page.Items)
            {
                if (item.Heading != null)
                {
                    if (row > 0) { col++; row = 0; }
                    Label(_grid, "Heading", 15f, Bronze, TextAlignmentOptions.BottomLeft,
                          new Vector2(col * colWidth, -row * rowHeight), new Vector2(colWidth, rowHeight)).text = item.Heading.ToUpperInvariant();
                    row++;
                }
                if (row >= perColumn && col < 2) { col++; row = 0; }
                AddRow(item, new Vector2(col * colWidth, -row * rowHeight), colWidth - 10f, rowHeight - 4f);
                row++;
            }
            Refresh();
        }

        private static void AddRow(Item item, Vector2 at, float width, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_grid, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = at;
            rt.sizeDelta = new Vector2(width, height);
            var back = go.GetComponent<Image>();
            back.color = new Color(1f, 1f, 1f, 0.03f);
            var button = go.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.9f, 0.7f, 4f);   // tints the 0.03 backing to ~0.12 under the mouse
            button.colors = colors;
            button.onClick.AddListener(() => { item.On = !item.On; Refresh(); });

            Image box = Box(rt, "Check", Bronze, new Vector2(18f, 18f)).GetComponent<Image>();
            var boxRt = (RectTransform)box.transform;
            boxRt.anchorMin = boxRt.anchorMax = new Vector2(0f, 0.5f);
            boxRt.anchoredPosition = new Vector2(16f, 0f);

            Image icon = Box(rt, "Icon", Color.white, new Vector2(26f, 26f)).GetComponent<Image>();
            var iconRt = (RectTransform)icon.transform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(46f, 0f);
            icon.sprite = item.Icon >= 0 ? CustomIcons.SpriteForPicker(item.Icon) : null;
            icon.preserveAspect = true;
            icon.enabled = icon.sprite != null;

            TextMeshProUGUI label = Label(rt, "Name", 17f, Warm, TextAlignmentOptions.Left, new Vector2(66f, 0f), new Vector2(width - 70f, height));
            var labelRt = (RectTransform)label.transform;
            labelRt.anchorMin = labelRt.anchorMax = labelRt.pivot = new Vector2(0f, 0.5f);
            labelRt.anchoredPosition = new Vector2(66f, 0f);
            label.text = item.Name;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;

            Rows.Add((item, box, icon, label));
        }

        private static void Refresh()
        {
            foreach (var (item, box, icon, label) in Rows)
            {
                box.color = item.On ? Bronze : new Color(Bronze.r, Bronze.g, Bronze.b, 0.25f);
                icon.color = new Color(1f, 1f, 1f, item.On ? 1f : 0.35f);
                label.color = new Color(Warm.r, Warm.g, Warm.b, item.On ? 1f : 0.45f);
            }
        }

        private static void SetAll(bool on)
        {
            foreach (Item item in _pages[_page].Items)
                item.On = on;
            Refresh();
        }

        /// Back to how this page was when it was opened.
        private static void Undo()
        {
            Page page = _pages[_page];
            for (int i = 0; i < page.Items.Count; i++)
                page.Items[i].On = page.Snapshot[i];
            Refresh();
        }

        // ---- building blocks ------------------------------------------------------------------

        private static RectTransform Box(Transform parent, string name, Color color, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            return rt;
        }

        /// A thin bronze rail along the top or bottom of the panel.
        private static void Edge(RectTransform panel, bool top)
        {
            RectTransform rail = Box(panel, "Rail", Bronze, new Vector2(0f, 3f));
            rail.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rail.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rail.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rail.anchoredPosition = Vector2.zero;
        }

        private static TextMeshProUGUI Label(Transform parent, string name, float size, Color color,
                                             TextAlignmentOptions align, Vector2 at, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = at;
            rt.sizeDelta = box;
            TextMeshProUGUI text = Fonts.AddLabel(go);
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            return text;
        }

        private static TextMeshProUGUI Button(RectTransform panel, string text, Vector2 at, float width, bool top,
                                              UnityEngine.Events.UnityAction click, bool anchorLeft = false)
        {
            RectTransform rt = Box(panel, text, ButtonBack, new Vector2(width, 38f));
            float x = anchorLeft ? 0f : 1f, y = top ? 1f : 0f;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(x, y);
            rt.anchoredPosition = at;
            var button = rt.gameObject.AddComponent<Button>();
            button.onClick.AddListener(click);
            RectTransform line = Box(rt, "Line", Bronze, new Vector2(0f, 2f));
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 0f);
            line.anchoredPosition = Vector2.zero;
            TextMeshProUGUI label = Label(rt, "Text", 18f, Warm, TextAlignmentOptions.Center, Vector2.zero, new Vector2(width, 38f));
            label.text = text;
            return label;
        }

        /// Escape is "Not now"; the map cannot be opened underneath.
        private sealed class Keys : MonoBehaviour
        {
            private void Update()
            {
                if (ZInput.GetKeyDown(KeyCode.Escape))
                    Close(false);
                else if (Minimap.instance != null && Minimap.IsOpen())
                    Minimap.instance.SetMapMode(Minimap.MapMode.Small);
            }
        }

        // ---- while open, the menu has the mouse and the player stands still -------------------

        /// PlayerController.TakeInput is the gate every movement and attack goes through
        /// (decompiled); the game's own menus close it the same way, by being visible.
        [HarmonyPatch(typeof(PlayerController), "TakeInput")]
        private static class Patch_TakeInput
        {
            private static void Postfix(ref bool __result)
            {
                if (IsOpen)
                    __result = false;
            }
        }

        /// GameCamera.UpdateMouseCapture locks and hides the cursor unless one of the game's own
        /// windows is up (decompiled); it cannot know about this one, so it is undone after it.
        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
        private static class Patch_Cursor
        {
            private static void Postfix()
            {
                if (!IsOpen)
                    return;
                ZCursor.LockState = CursorLockMode.None;
                ZCursor.Show();
            }
        }
    }
}
