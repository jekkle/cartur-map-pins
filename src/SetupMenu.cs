using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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

        private const string Topic = "Hugin";
        private const string Text =
            "The Allfather's ravens forget nothing, and now your map need not either. " +
            "Here is your chance to choose what I mark for you as you wander.";

        /// The welcome page, in Hugin's voice.
        private const string Welcome =
            "Wanderer, the Allfather bid me watch over your map. As you roam, I will mark what you " +
            "discover: ore veins, burial chambers, camps, and the altars of the Forsaken.\n\n" +
            "Tell me what is worth remembering, and I will leave the rest to the mist.";

        private const string MoreSettings =
            "Every switch, icon and size can be changed later in the mod settings " +
            "(BepInEx/config, or F1 with Configuration Manager).";

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
            private static void Postfix(Player __instance, string name)
            {
                if (name == Key)
                    __instance.StartCoroutine(OpenAfter(2.5f));
            }
        }

        /// Hugin's line goes up in his speech bubble first; the menu follows once it can be read.
        private static System.Collections.IEnumerator OpenAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Open();
        }

        // ---- the pages ------------------------------------------------------------------------

        private sealed class Item
        {
            public string Name;
            public int Icon;                    // picker index chosen here, -1 for the fallback
            public int DefaultIcon;             // what a fresh install draws
            public int FallbackIcon;            // the category's icon, drawn when Icon is -1
            public ConfigEntry<PinIcon> IconEntry;
            public string Description;
            public string Biomes;               // read from the game's location table, or null
            public ConfigEntry<bool> Entry;     // the kind's own switch, or the category's Enabled
            public PinCategory Category;        // whose Enabled follows the page (kinds only)
            public bool IsKind;
            public bool On;
            public string Heading;              // starts a new group on the page
        }

        private sealed class Page
        {
            public string Title;
            public bool IsWelcome;
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
            _sharing = Plugin.PinSharing.Value;
            var pages = new List<Page>
            {
                new Page { Title = "Hugin's map", IsWelcome = true },
                KindPage("Ores", PinCategory.Ore, Subtypes.Ores),
                KindPage("Dungeons", PinCategory.Dungeon, Subtypes.Dungeons),
                KindPage("Camps and villages", PinCategory.Camp, Subtypes.Camps),
            };

            Page bosses = KindPage("Bosses", PinCategory.BossAltar, Subtypes.Bosses, "Boss altars");
            AddKinds(bosses, PinCategory.Miniboss, Subtypes.Minibosses, "Minibosses");
            pages.Add(bosses);

            Page picks = new Page { Title = "Berries and things to pick" };
            AddGroup(picks, PickableGroup.HighValue, "Valuable finds");
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

            return pages.Where(p => p.IsWelcome || p.Items.Count > 0).ToList();
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
                ConfigEntry<PinIcon> iconEntry = Plugin.KindIcon(e.Name);
                page.Items.Add(new Item
                {
                    Name = Names.TryGetValue(e.Name, out string n) ? n : e.Name,
                    IconEntry = iconEntry,
                    Icon = iconEntry != null ? (int)iconEntry.Value : -1,
                    DefaultIcon = iconEntry != null ? (int)(PinIcon)iconEntry.DefaultValue : -1,
                    FallbackIcon = (int)settings.IconIndex.Value,
                    Description = SetupText.For(e.Name, category),
                    Biomes = BiomesOf(table, e.Name),
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
            page.Items.Add(new Item
            {
                Name = name, IconEntry = settings.IconIndex, Icon = (int)settings.IconIndex.Value,
                DefaultIcon = (int)(PinIcon)settings.IconIndex.DefaultValue, FallbackIcon = -1,
                Description = SetupText.ForCategory(category, settings.Enabled.Description.Description),
                Entry = settings.Enabled, Category = category, On = settings.Enabled.Value,
            });
        }

        private static void AddGroup(Page page, PickableGroup group, string name)
        {
            ConfigEntry<bool> entry = Plugin.PickableSwitch(group);
            Plugin.CategorySettings pick = Plugin.SettingsFor(PinCategory.Pickable);
            if (entry == null || pick == null)
                return;
            page.Items.Add(new Item
            {
                Name = name, Entry = entry,
                IconEntry = Plugin.PickableIcon(group),
                Icon = Plugin.PickableIcon(group) != null ? (int)Plugin.PickableIcon(group).Value : -1,
                DefaultIcon = Plugin.PickableIcon(group) != null ? (int)(PinIcon)Plugin.PickableIcon(group).DefaultValue : -1,
                FallbackIcon = (int)pick.IconIndex.Value,
                Description = SetupText.ForGroup(group),
                Category = PinCategory.Pickable, IsKind = true, On = pick.Enabled.Value && entry.Value,
            });
        }

        /// Which biomes a kind's locations are generated in, read from ZoneSystem.m_locations with
        /// the same matcher the scanner uses, so it is the game's answer, not a written one.
        private static string BiomesOf(Subtypes.Entry[] table, string kind)
        {
            if (ZoneSystem.instance == null)
                return null;
            Heightmap.Biome biomes = 0;
            foreach (ZoneSystem.ZoneLocation loc in ZoneSystem.instance.m_locations)
            {
                if (loc != null && loc.m_enable && !string.IsNullOrEmpty(loc.m_prefabName) &&
                    Subtypes.Match(table, loc.m_prefabName) == kind)
                    biomes |= loc.m_biome;
            }
            if (biomes == 0)
                return null;
            var names = new List<string>();
            foreach (Heightmap.Biome b in System.Enum.GetValues(typeof(Heightmap.Biome)))
            {
                if (b != 0 && b != Heightmap.Biome.All && (biomes & b) == b && Mathf.IsPowerOfTwo((int)b))
                    names.Add(Localization.instance.Localize("$biome_" + b.ToString().ToLowerInvariant()));
            }
            return string.Join(", ", names);
        }

        /// Writes every page. A category whose kinds are listed is on when any of its kinds is,
        /// so switching every Dungeon kind off also stops unlisted (modded) dungeons, as asked.
        private static void Save()
        {
            Plugin.PinSharing.Value = _sharing;
            var anyOn = new Dictionary<PinCategory, bool>();
            foreach (Item item in _pages.SelectMany(p => p.Items))
            {
                item.Entry.Value = item.On;
                if (item.IconEntry != null && (int)item.IconEntry.Value != item.Icon)
                    item.IconEntry.Value = (PinIcon)item.Icon;
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
        private static readonly Color ButtonHover = new Color(0.36f, 0.26f, 0.15f, 1f);
        private static readonly Color RowRest = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color RowHover = new Color(1f, 0.85f, 0.6f, 0.14f);

        private static GameObject _root;
        private static RectTransform _grid;
        private static TextMeshProUGUI _title, _pageNo, _next, _footer;
        private static List<Page> _pages;
        private static int _page;
        private static readonly List<(RectTransform rect, Item item)> RowRects = new List<(RectTransform, Item)>();
        private static RectTransform _panel, _tip;
        private static TextMeshProUGUI _tipText;
        private static GameObject _picker;
        private static Plugin.PinSharingMode _sharing;
        private static readonly List<GameObject> PageButtons = new List<GameObject>();
        private static readonly List<(Plugin.PinSharingMode mode, Image box, TextMeshProUGUI label, RectTransform rect)> ShareRows =
            new List<(Plugin.PinSharingMode, Image, TextMeshProUGUI, RectTransform)>();
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
            RowRects.Clear();
            _picker = null;
            _tip = null;
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

            RectTransform panel = _panel = Box(_root.transform, "Panel", Panel, new Vector2(860f, 640f));
            Edge(panel, true);
            Edge(panel, false);

            _title = Label(panel, "Title", 34f, Warm, TextAlignmentOptions.TopLeft, new Vector2(36f, -26f), new Vector2(520f, 44f));
            _pageNo = Label(panel, "PageNo", 17f, new Color(Warm.r, Warm.g, Warm.b, 0.6f), TextAlignmentOptions.TopLeft, new Vector2(38f, -72f), new Vector2(400f, 24f));

            PageButtons.Clear();
            PageButtons.Add(Button(panel, "Select all", new Vector2(-322f, -30f), 128f, true, () => SetAll(true)).transform.parent.gameObject);
            PageButtons.Add(Button(panel, "Deselect all", new Vector2(-180f, -30f), 128f, true, () => SetAll(false)).transform.parent.gameObject);
            PageButtons.Add(Button(panel, "Undo", new Vector2(-38f, -30f), 128f, true, Undo).transform.parent.gameObject);

            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(panel, false);
            _grid = (RectTransform)gridGo.transform;
            _grid.anchorMin = _grid.anchorMax = new Vector2(0f, 1f);
            _grid.pivot = new Vector2(0f, 1f);
            _grid.anchoredPosition = new Vector2(36f, -112f);
            _grid.sizeDelta = new Vector2(788f, 440f);

            Button(panel, "Not now", new Vector2(36f, 28f), 140f, false, () => Close(false), anchorLeft: true);
            Button(panel, "Back", new Vector2(-188f, 28f), 140f, false, () => ShowPage(_page - 1));
            _footer = Label(panel, "Footer", 14f, new Color(Warm.r, Warm.g, Warm.b, 0.6f), TextAlignmentOptions.Left,
                            new Vector2(196f, -584f), new Vector2(330f, 52f));
            _footer.textWrappingMode = TextWrappingModes.Normal;
            BuildTip();
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
            // Taken the first time the page is shown, so leaving and coming back does not move
            // what Undo returns to (pilot: Select all, Next, Back, Undo kept everything on).
            if (page.Snapshot == null)
                page.Snapshot = page.Items.Select(i => i.On).ToArray();
            ShareRows.Clear();
            _title.text = page.Title;
            _pageNo.text = page.IsWelcome ? $"Page 1 of {_pages.Count}"
                : $"Page {index + 1} of {_pages.Count}  -  choose what gets a pin";
            foreach (GameObject b in PageButtons)
                b.SetActive(!page.IsWelcome);
            _footer.text = index == _pages.Count - 1 ? MoreSettings : "";
            _next.text = index == _pages.Count - 1 ? "Done" : "Next";

            foreach (Transform child in _grid)
                Object.Destroy(child.gameObject);
            Rows.Clear();
            RowRects.Clear();
            ClosePicker();

            // Three columns, filled top to bottom; a heading takes a whole row.
            const float colWidth = 262f, rowHeight = 34f;
            if (page.IsWelcome)
            {
                BuildWelcome();
                return;
            }

            // On a page with headings, row 0 of every column is heading space, so the columns'
            // first items line up (Bosses: The Queen sat level with the "Boss altars" heading).
            int start = page.Items.Any(i => i.Heading != null) ? 1 : 0;
            int col = 0, row = 0, perColumn = start + Mathf.Max(1, Mathf.CeilToInt(page.Items.Count / 3f));
            foreach (Item item in page.Items)
            {
                if (item.Heading != null)
                {
                    if (row > 0) { col++; row = 0; }
                    Label(_grid, "Heading", 15f, Bronze, TextAlignmentOptions.BottomLeft,
                          new Vector2(col * colWidth, -row * rowHeight), new Vector2(colWidth, rowHeight)).text = item.Heading.ToUpperInvariant();
                    row++;
                }
                if (row >= perColumn && col < 2) { col++; row = start; }
                AddRow(item, new Vector2(col * colWidth, -row * rowHeight), colWidth - 10f, rowHeight - 4f);
                row++;
            }
            Refresh();
        }

        private static readonly (Plugin.PinSharingMode mode, string name, string tip)[] ShareChoices =
        {
            (Plugin.PinSharingMode.Everything, "Share all my pins",
             "Friends who read your cartography table get every pin on your map, the ones I place for you included."),
            (Plugin.PinSharingMode.HandPlacedOnly, "Share only pins I place myself",
             "Friends get the pins you placed by hand. The ore, berries and the rest that I mark for you stay on your map alone, so their maps are not buried."),
            (Plugin.PinSharingMode.Nothing, "Share no pins",
             "The table still shares the ground you have explored, but none of your pins. Pins your friends share still reach you."),
        };

        private static void BuildWelcome()
        {
            TextMeshProUGUI words = Label(_grid, "Hugin", 20f, Warm, TextAlignmentOptions.TopLeft, Vector2.zero, new Vector2(788f, 150f));
            words.textWrappingMode = TextWrappingModes.Normal;
            words.fontStyle = FontStyles.Italic;
            words.text = Welcome;

            Label(_grid, "Heading", 15f, Bronze, TextAlignmentOptions.BottomLeft, new Vector2(0f, -170f), new Vector2(788f, 30f))
                .text = "SHARING AT THE CARTOGRAPHY TABLE";
            for (int i = 0; i < ShareChoices.Length; i++)
            {
                var (mode, name, _) = ShareChoices[i];
                var go = new GameObject("Share", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_grid, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, -204f - i * 36f);
                rt.sizeDelta = new Vector2(420f, 32f);
                go.GetComponent<Image>().color = RowRest;
                go.GetComponent<Button>().transition = Selectable.Transition.None;
                go.GetComponent<Button>().onClick.AddListener(() => { _sharing = mode; Refresh(); });
                go.AddComponent<HoverFx>().Bind(go.GetComponent<Image>(), RowRest, RowHover, 1.02f);

                Image box = Box(rt, "Check", Bronze, new Vector2(18f, 18f)).GetComponent<Image>();
                var boxRt = (RectTransform)box.transform;
                boxRt.anchorMin = boxRt.anchorMax = new Vector2(0f, 0.5f);
                boxRt.anchoredPosition = new Vector2(16f, 0f);

                TextMeshProUGUI label = Label(rt, "Name", 17f, Warm, TextAlignmentOptions.Left, Vector2.zero, new Vector2(370f, 32f));
                var labelRt = (RectTransform)label.transform;
                labelRt.anchorMin = labelRt.anchorMax = labelRt.pivot = new Vector2(0f, 0.5f);
                labelRt.anchoredPosition = new Vector2(40f, 0f);
                label.text = name;
                ShareRows.Add((mode, box, label, rt));
            }

            TextMeshProUGUI more = Label(_grid, "More", 15f, new Color(Warm.r, Warm.g, Warm.b, 0.6f), TextAlignmentOptions.TopLeft,
                                         new Vector2(0f, -330f), new Vector2(788f, 60f));
            more.textWrappingMode = TextWrappingModes.Normal;
            more.text = MoreSettings + " Press Next to choose what gets a pin.";
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
            back.color = RowRest;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => { item.On = !item.On; Refresh(); });
            go.AddComponent<HoverFx>().Bind(back, RowRest, RowHover, 1.02f);

            Image box = Box(rt, "Check", Bronze, new Vector2(18f, 18f)).GetComponent<Image>();
            var boxRt = (RectTransform)box.transform;
            boxRt.anchorMin = boxRt.anchorMax = new Vector2(0f, 0.5f);
            boxRt.anchoredPosition = new Vector2(16f, 0f);

            Image icon = Box(rt, "Icon", Color.white, new Vector2(26f, 26f)).GetComponent<Image>();
            var iconRt = (RectTransform)icon.transform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(46f, 0f);
            icon.preserveAspect = true;
            // The icon is its own button: clicking it picks the icon, clicking the rest of the
            // row switches the kind on or off.
            if (item.IconEntry != null)
            {
                icon.gameObject.AddComponent<Button>().onClick.AddListener(() => OpenPicker(item));
                // The map picker's own cell frame round it, so it reads as something to click,
                // lit up under the mouse the way the picker's cells are (CellHover).
                Image frame = Box(iconRt, "Frame", IconGrid.Resting, new Vector2(32f, 32f)).GetComponent<Image>();
                frame.sprite = IconGrid.Border;
                frame.type = Image.Type.Sliced;
                frame.raycastTarget = false;
                icon.gameObject.AddComponent<CellHover>().Bind(frame);
            }

            TextMeshProUGUI label = Label(rt, "Name", 17f, Warm, TextAlignmentOptions.Left, new Vector2(66f, 0f), new Vector2(width - 70f, height));
            var labelRt = (RectTransform)label.transform;
            labelRt.anchorMin = labelRt.anchorMax = labelRt.pivot = new Vector2(0f, 0.5f);
            labelRt.anchoredPosition = new Vector2(66f, 0f);
            label.text = item.Name;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;

            Rows.Add((item, box, icon, label));
            RowRects.Add((rt, item));
        }

        private static void Refresh()
        {
            foreach (var (mode, box, label, _) in ShareRows)
            {
                box.color = mode == _sharing ? Bronze : new Color(Bronze.r, Bronze.g, Bronze.b, 0.25f);
                label.color = new Color(Warm.r, Warm.g, Warm.b, mode == _sharing ? 1f : 0.45f);
            }
            foreach (var (item, box, icon, label) in Rows)
            {
                box.color = item.On ? Bronze : new Color(Bronze.r, Bronze.g, Bronze.b, 0.25f);
                icon.sprite = CustomIcons.SpriteForPicker(item.Icon >= 0 ? item.Icon : item.FallbackIcon);
                icon.enabled = icon.sprite != null;
                icon.color = new Color(1f, 1f, 1f, item.On ? 1f : 0.35f);
                label.color = new Color(Warm.r, Warm.g, Warm.b, item.On ? 1f : 0.45f);
            }
        }

        private static void SetAll(bool on)
        {
            if (_pages[_page].IsWelcome)
                return;
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

        // ---- icon picker --------------------------------------------------------------------

        /// The map's own icon grid (IconGrid, shared with the map picker and the pin editor) over
        /// the right of the panel. The default icon wears a DEFAULT tag, the chosen one the gold
        /// border; the choice is written with everything else on Done.
        private static void OpenPicker(Item item)
        {
            ClosePicker();
            RectTransform box = Box(_panel, "IconPicker", new Color(0.03f, 0.03f, 0.025f, 0.98f),
                                    new Vector2(IconGrid.PanelWidth(7), 470f));
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(1f, 1f);
            box.anchoredPosition = new Vector2(-24f, -100f);
            _picker = box.gameObject;
            Edge(box, true);

            TextMeshProUGUI title = Label(box, "Title", 18f, Warm, TextAlignmentOptions.TopLeft, new Vector2(14f, -12f), new Vector2(220f, 26f));
            title.text = "Icon for " + item.Name;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            Button(box, "Use default", new Vector2(-10f, -8f), 120f, true, () =>
            {
                item.Icon = item.DefaultIcon;
                ClosePicker();
                Refresh();
            }).fontSize = 15f;

            List<Image> borders = IconGrid.Build(box.gameObject, IconGrid.FindTemplateButton(Minimap.instance), index =>
            {
                item.Icon = index;
                ClosePicker();
                Refresh();
            }, top: 46f);

            int chosen = item.Icon >= 0 ? item.Icon : item.FallbackIcon;
            for (int i = 0; i < borders.Count; i++)
            {
                if (borders[i] == null)
                    continue;
                IconGrid.SetSelected(borders[i], i == chosen);
                if (i != item.DefaultIcon)
                    continue;
                TextMeshProUGUI tag = Label(borders[i].transform.parent, "Default", 9f, Bronze, TextAlignmentOptions.Bottom,
                                            Vector2.zero, new Vector2(IconGrid.CellSize, IconGrid.CellSize));
                tag.text = "DEFAULT";
                tag.fontStyle = FontStyles.Bold;
            }
        }

        private static void ClosePicker()
        {
            if (_picker != null)
                Object.Destroy(_picker);
            _picker = null;
        }

        // ---- hover description --------------------------------------------------------------

        private const float TipWidth = 340f;

        private static void BuildTip()
        {
            _tip = Box(_root.transform, "Tip", new Color(0.02f, 0.02f, 0.015f, 0.96f), new Vector2(TipWidth, 10f));
            _tip.anchorMin = _tip.anchorMax = new Vector2(0.5f, 0.5f);
            _tip.pivot = new Vector2(0f, 1f);
            Edge(_tip, true);
            _tipText = Label(_tip, "Text", 16f, Warm, TextAlignmentOptions.TopLeft, new Vector2(14f, -12f), new Vector2(TipWidth - 28f, 10f));
            _tipText.textWrappingMode = TextWrappingModes.Normal;
            _tip.gameObject.SetActive(false);
        }

        private static string Gold(string text) => $"<color=#{ColorUtility.ToHtmlStringRGB(Bronze)}>{text}</color>";

        /// The row under the mouse gets its full description in a box just right of the pointer,
        /// kept on screen. Nothing under the mouse, or the icon picker open, hides it.
        private static void UpdateTip()
        {
            if (_tip == null)
                return;
            Vector2 pointer = ZInput.pointerPosition;
            Item hovered = null;
            if (_picker == null)
            {
                foreach ((RectTransform rect, Item item) in RowRects)
                {
                    if (rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, pointer, null))
                    {
                        hovered = item;
                        break;
                    }
                }
            }
            string text = null;
            if (hovered == null && _picker == null)
            {
                foreach (var (mode, _, _, rect) in ShareRows)
                {
                    if (rect == null || !RectTransformUtility.RectangleContainsScreenPoint(rect, pointer, null))
                        continue;
                    var choice = ShareChoices.First(c => c.mode == mode);
                    text = $"<size=20>{Gold(choice.name)}</size>\n{choice.tip}" +
                           (mode == Plugin.PinSharingMode.Everything ? $"\n\n{Gold("Default:")} this one, as in the base game" : "");
                }
            }
            if (hovered == null && text == null)
            {
                _tip.gameObject.SetActive(false);
                return;
            }
            if (hovered != null)
                text = TipFor(hovered);
            ShowTip(text, pointer);
        }

        private static string TipFor(Item hovered)
        {
            bool defaultOn = hovered.Entry.DefaultValue is bool on && on;
            return $"<size=20>{Gold(hovered.Name)}</size>\n" + hovered.Description +
                            (hovered.Biomes != null ? $"\n\n{Gold("Found in:")} {hovered.Biomes}" : "\n") +
                            $"\n{Gold("On by default:")} {(defaultOn ? "yes" : "no")}" +
                            (hovered.IconEntry != null ? "\n\n<size=14><alpha=#99>Click the icon to change it.</size>" : "");
        }

        private static void ShowTip(string text, Vector2 pointer)
        {
            _tipText.text = text;
            float height = _tipText.GetPreferredValues(_tipText.text, TipWidth - 28f, 0f).y + 26f;
            ((RectTransform)_tipText.transform).sizeDelta = new Vector2(TipWidth - 28f, height);
            _tip.sizeDelta = new Vector2(TipWidth, height);

            var root = (RectTransform)_root.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, pointer, null, out Vector2 local);
            Vector2 half = root.rect.size * 0.5f;
            _tip.anchoredPosition = new Vector2(Mathf.Min(local.x + 24f, half.x - TipWidth - 8f),
                                                Mathf.Max(local.y - 8f, -half.y + height + 8f));
            _tip.SetAsLastSibling();
            _tip.gameObject.SetActive(true);
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
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(click);
            rt.gameObject.AddComponent<HoverFx>().Bind(rt.GetComponent<Image>(), ButtonBack, ButtonHover, 1.05f);
            RectTransform line = Box(rt, "Line", Bronze, new Vector2(0f, 2f));
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 0f);
            line.anchoredPosition = Vector2.zero;
            TextMeshProUGUI label = Label(rt, "Text", 18f, Warm, TextAlignmentOptions.Center, Vector2.zero, new Vector2(width, 38f));
            label.text = text;
            return label;
        }

        /// Every row and button in the menu answers the mouse: the backing warms, the whole thing
        /// grows a touch under the pointer and dips when pressed, eased both ways. Stays enabled
        /// and idles at rest, like CellHover, because a disabled component gets no pointer events.
        private sealed class HoverFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                                                     IPointerDownHandler, IPointerUpHandler
        {
            private const float Speed = 14f;
            private Graphic _back;
            private Color _rest, _hover;
            private float _grow, _t, _target, _press = 1f, _pressTarget = 1f;

            public void Bind(Graphic back, Color rest, Color hover, float grow)
            {
                _back = back;
                _rest = rest;
                _hover = hover;
                _grow = grow;
            }

            public void OnPointerEnter(PointerEventData e) => _target = 1f;
            public void OnPointerExit(PointerEventData e) { _target = 0f; _pressTarget = 1f; }
            public void OnPointerDown(PointerEventData e) => _pressTarget = 0.97f;
            public void OnPointerUp(PointerEventData e) => _pressTarget = 1f;

            private void Update()
            {
                if (Mathf.Approximately(_t, _target) && Mathf.Approximately(_press, _pressTarget))
                    return;
                float k = 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime);
                _t = Mathf.Abs(_t - _target) < 0.01f ? _target : Mathf.Lerp(_t, _target, k);
                _press = Mathf.Abs(_press - _pressTarget) < 0.002f ? _pressTarget : Mathf.Lerp(_press, _pressTarget, k);
                if (_back != null)
                    _back.color = Color.Lerp(_rest, _hover, _t);
                float s = Mathf.Lerp(1f, _grow, _t) * _press;
                transform.localScale = new Vector3(s, s, 1f);
            }
        }

        /// Keys, the hover description, and no map underneath.
        private sealed class Keys : MonoBehaviour
        {
            private void Update()
            {
                // Escape closes the icon picker first, then the menu ("Not now").
                if (ZInput.GetKeyDown(KeyCode.Escape))
                {
                    if (_picker != null)
                        ClosePicker();
                    else
                        Close(false);
                    return;
                }
                UpdateTip();
                if (Minimap.instance != null && Minimap.IsOpen())
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
