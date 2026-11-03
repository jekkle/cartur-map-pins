using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// What a click on a cluster's badge opens: every pin it stands for, nearest first, with a
    /// search box. Picking a row glides the map to that pin at a zoom where it stands alone.
    ///
    /// It only reads. Nothing here ticks, renames, moves or deletes a pin, and the pins keep their
    /// places in the game's list. The rows are a fixed pool laid over a scrolled strip, so a
    /// cluster of five hundred costs what a cluster of ten does.
    ///
    /// Escape is vanilla's: it closes the map, and the map closing closes this. A click outside
    /// the panel or on the close button closes it without the map.
    internal static class ClusterInspector
    {
        private const float Width = 340f, RowH = 26f, HeaderH = 30f, SearchH = 30f, Visible = 12f, Pad = 8f;
        private const int Pool = 14;

        private sealed class Entry
        {
            public Minimap.PinData Pin;
            public string Name, Category;
            /// How many pins this one stands for (same-name repeats merged into it).
            public int Weight;
        }

        private static GameObject _panel;
        private static RectTransform _rect, _content;
        private static ScrollRect _scroll;
        private static TMP_InputField _search;
        private static TextMeshProUGUI _title;
        private static Camera _camera;
        private static Minimap _map;
        private static readonly List<Entry> All = new List<Entry>();
        private static readonly List<Entry> Shown = new List<Entry>();
        private static readonly Row[] Rows = new Row[Pool];
        private static int _firstRow = -1;
        private static float _refreshAt;

        private sealed class Row
        {
            public GameObject Go;
            public RectTransform Rect;
            public Image Icon, Back;
            public TextMeshProUGUI Name, Meta;
            public int Index = -1;
        }

#if DIAGNOSTICS
        /// The pilot's click, for when the game window has no OS focus.
        internal static bool TestClick;
#endif

        public static bool IsOpen => _panel != null && _panel.activeSelf;

        public static bool PointerOver() => IsOpen && IconGrid.PointerOver(_panel, _rect, _camera);

        public static void Open(Minimap map, Minimap.PinData leader)
        {
            IReadOnlyList<Minimap.PinData> group = Clusters.GroupOf(leader);
            if (group == null || group.Count == 0)
                return;
            _map = map;
            if (_panel == null)
                Build(map);
            if (_panel == null)
                return;

            Vector3 me = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : leader.m_pos;
            All.Clear();
            foreach (Minimap.PinData pin in group)
            {
                string key = PinRecord.KeyOfIndexed(pin);
                int colon = key != null ? key.IndexOf(':') : -1;
                All.Add(new Entry
                {
                    Pin = pin,
                    Name = Labels.Localize(string.IsNullOrEmpty(pin.m_name) ? "Pin" : pin.m_name),
                    Category = colon > 0 ? key.Substring(0, colon) : "Pin",
                    Weight = Crowding.MergedCount(pin),
                });
            }
            All.Sort((a, b) => Utils.DistanceXZ(me, a.Pin.m_pos).CompareTo(Utils.DistanceXZ(me, b.Pin.m_pos)));
            _title.text = $"<color=#C79452>{Clusters.CountOf(leader)} pins</color>{Breakdown(leader)}";
            _search.text = string.Empty;
            Refilter();

            _panel.SetActive(true);
            _rect.position = LabelCrowding.Pointer + new Vector3(24f, 0f, 0f);
            Clamp();
            _panel.transform.SetAsLastSibling();
            ClusterPopup.Hide();
        }

        /// For a mixed badge, how many of each kind: "Ore 8, Chest 3".
        private static string Breakdown(Minimap.PinData leader)
        {
            if (!Clusters.IsMixed(leader))
                return string.Empty;
            var counts = new Dictionary<string, int>();
            foreach (Entry e in All)
            {
                counts.TryGetValue(e.Category, out int n);
                counts[e.Category] = n + 1;
            }
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> c in counts)
                parts.Add($"{c.Key} {c.Value}");
            return "  <size=12><alpha=#99>" + string.Join(", ", parts);
        }

        public static void Close()
        {
            if (_panel != null && _panel.activeSelf)
                _panel.SetActive(false);
            All.Clear();
            Shown.Clear();
        }

        /// Every frame from the Minimap.Update postfix.
        public static void Tick()
        {
            if (!IsOpen)
                return;
            // A click anywhere but on the panel puts it away. Opening a different cluster reopens it
            // the same frame, since that click is the map's own.
#if DIAGNOSTICS
            bool clicked = ZInput.GetMouseButtonDown(0) || TestClick;
            TestClick = false;
#else
            bool clicked = ZInput.GetMouseButtonDown(0);
#endif
            if (clicked && !PointerOver())
            {
                Close();
                return;
            }
            // Distances follow the player, twice a second; the order stays as it was opened.
            if (Time.unscaledTime >= _refreshAt)
            {
                _refreshAt = Time.unscaledTime + 0.5f;
                Layout(true);
            }
        }

        private static void Refilter()
        {
            Shown.Clear();
            string text = _search != null ? _search.text : string.Empty;
            foreach (Entry e in All)
            {
                if (text.Length == 0 || e.Name.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    Shown.Add(e);
            }
            _content.sizeDelta = new Vector2(0f, Shown.Count * RowH);
            // As tall as what is in it, up to the full twelve rows.
            _rect.sizeDelta = new Vector2(Width, HeaderH + SearchH + Mathf.Max(1, Mathf.Min(Shown.Count, (int)Visible)) * RowH + Pad);
            _content.anchoredPosition = Vector2.zero;
            Layout(true);
        }

        /// Puts the pooled rows over whichever entries the strip is showing.
        private static void Layout(bool force)
        {
            int first = Mathf.Max(0, Mathf.FloorToInt(_content.anchoredPosition.y / RowH));
            if (!force && first == _firstRow)
                return;
            _firstRow = first;
            Vector3 me = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            for (int r = 0; r < Pool; r++)
            {
                Row row = Rows[r];
                int i = first + r;
                if (i >= Shown.Count)
                {
                    if (row.Go.activeSelf)
                        row.Go.SetActive(false);
                    row.Index = -1;
                    continue;
                }
                Entry e = Shown[i];
                row.Index = i;
                row.Rect.anchoredPosition = new Vector2(0f, -i * RowH);
                Sprite sprite = e.Pin.m_iconElement != null ? e.Pin.m_iconElement.sprite : e.Pin.m_icon;
                row.Icon.sprite = sprite;
                row.Icon.enabled = sprite != null;
                row.Name.text = e.Weight > 1 ? $"{e.Name}  <color=#C79452>x{e.Weight}</color>" : e.Name;
                row.Meta.text = $"<alpha=#99>{e.Category}  <alpha=#FF><color=#C79452>{Mathf.RoundToInt(Utils.DistanceXZ(me, e.Pin.m_pos))} m";
                if (!row.Go.activeSelf)
                    row.Go.SetActive(true);
            }
        }

        private static void Pick(Row row)
        {
            if (row.Index < 0 || row.Index >= Shown.Count || _map == null)
                return;
            Minimap.PinData pin = Shown[row.Index].Pin;
            List<Minimap.PinData> group = AllPins();
            Close();
            FocusSeparated(_map, pin, group);
        }

        private static List<Minimap.PinData> AllPins()
        {
            var pins = new List<Minimap.PinData>();
            foreach (Entry e in All)
                pins.Add(e.Pin);
            return pins;
        }

        /// Glides the map to a pin of a cluster at a zoom where it stands alone.
        public static void FocusSeparated(Minimap map, Minimap.PinData pin, IReadOnlyList<Minimap.PinData> group)
        {
            // Zoom until the icon is narrower than the gap to its nearest neighbour in the cluster, so
            // it stands alone (cluster radius is 1.1 icons, snapped to a power of two that can round
            // up by about 1.6x, with the hysteresis band on top). Never zooms out.
            float nearest = float.MaxValue;
            foreach (Minimap.PinData other in group)
            {
                if (ReferenceEquals(other, pin))
                    continue;
                nearest = Mathf.Min(nearest, Utils.DistanceXZ(pin.m_pos, other.m_pos));
            }
            float metres = Crowding.IconMetres(map);
            float zoom = map.LargeZoom;
            if (nearest < float.MaxValue && metres > 0f)
                zoom = Mathf.Min(zoom, zoom * (nearest / 2.6f) / metres);
            SmoothZoom.FocusOn(map, pin.m_pos, zoom);
        }

        private static void Clamp()
        {
            var corners = new Vector3[4];
            _rect.GetWorldCorners(corners);
            float dx = 0f, dy = 0f;
            if (corners[2].x > Screen.width) dx -= corners[2].x - Screen.width;
            if (corners[0].x + dx < 0f) dx = -corners[0].x;
            if (corners[0].y < 0f) dy -= corners[0].y;
            if (corners[2].y + dy > Screen.height) dy -= corners[2].y + dy - Screen.height;
            _rect.position += new Vector3(dx, dy, 0f);
        }

        // ---- building --------------------------------------------------------------------------

        private static readonly Color Text = new Color(0.95f, 0.92f, 0.82f, 1f);

        private static void Build(Minimap map)
        {
            if (map.m_largeRoot == null)
                return;
            float height = HeaderH + SearchH + Visible * RowH + Pad;

            _panel = new GameObject("CarturPins_ClusterInspector", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(map.m_largeRoot.transform, false);
            _rect = (RectTransform)_panel.transform;
            _rect.anchorMin = _rect.anchorMax = Vector2.zero;
            _rect.pivot = new Vector2(0f, 1f);
            _rect.sizeDelta = new Vector2(Width, height);
            _panel.GetComponent<Image>().color = new Color(0.04f, 0.035f, 0.03f, 0.96f);

            var rail = new GameObject("Rail", typeof(RectTransform), typeof(Image));
            rail.transform.SetParent(_panel.transform, false);
            var rr = (RectTransform)rail.transform;
            rr.anchorMin = new Vector2(0f, 1f);
            rr.anchorMax = new Vector2(1f, 1f);
            rr.pivot = new Vector2(0.5f, 1f);
            rr.sizeDelta = new Vector2(0f, 2f);
            rail.GetComponent<Image>().color = IconGrid.Bronze;
            rail.GetComponent<Image>().raycastTarget = false;

            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(_panel.transform, false);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = new Vector2(0f, 1f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0f, 1f);
            tr.offsetMin = new Vector2(Pad + 2f, -HeaderH);
            tr.offsetMax = new Vector2(-HeaderH, -2f);
            _title = Fonts.AddLabel(titleGo);
            _title.fontSize = 16f;
            _title.color = Text;
            _title.alignment = TextAlignmentOptions.MidlineLeft;
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            _title.raycastTarget = false;

            AddButton(_panel.transform, "Close", "X", new Vector2(1f, 1f), new Vector2(-HeaderH * 0.5f - 2f, -HeaderH * 0.5f - 2f),
                      new Vector2(HeaderH - 6f, HeaderH - 6f), Close);

            _search = PinEditor.AddNameInput(map, _panel.transform);
            if (_search != null)
            {
                var sr = (RectTransform)_search.transform;
                sr.anchorMin = new Vector2(0f, 1f);
                sr.anchorMax = new Vector2(1f, 1f);
                sr.pivot = new Vector2(0.5f, 1f);
                sr.offsetMin = new Vector2(Pad, 0f);
                sr.offsetMax = new Vector2(-Pad, 0f);
                sr.anchoredPosition = new Vector2(0f, -HeaderH);
                sr.sizeDelta = new Vector2(sr.sizeDelta.x, SearchH - 4f);
                TextFocus.Register(_search);
                _search.onValueChanged.AddListener(_ => { if (IsOpen) Refilter(); });
            }

            var view = new GameObject("View", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            view.transform.SetParent(_panel.transform, false);
            var vr = (RectTransform)view.transform;
            vr.anchorMin = Vector2.zero;
            vr.anchorMax = Vector2.one;
            vr.offsetMin = new Vector2(Pad, Pad * 0.5f);
            vr.offsetMax = new Vector2(-Pad, -(HeaderH + SearchH));
            view.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(view.transform, false);
            _content = (RectTransform)content.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);

            _scroll = view.GetComponent<ScrollRect>();
            _scroll.viewport = vr;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 0f;
            view.AddComponent<IconGrid.SmoothScroll>().Bind(_scroll, RowH);
            _scroll.onValueChanged.AddListener(_ => Layout(false));

            for (int i = 0; i < Pool; i++)
                Rows[i] = BuildRow(_content);

            _camera = IconGrid.CameraFor(_panel);
            _panel.SetActive(false);
        }

        private static Row BuildRow(Transform parent)
        {
            var row = new Row();
            row.Go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button));
            row.Go.transform.SetParent(parent, false);
            row.Rect = (RectTransform)row.Go.transform;
            row.Rect.anchorMin = new Vector2(0f, 1f);
            row.Rect.anchorMax = new Vector2(1f, 1f);
            row.Rect.pivot = new Vector2(0f, 1f);
            row.Rect.sizeDelta = new Vector2(0f, RowH - 2f);
            row.Back = row.Go.GetComponent<Image>();
            row.Back.color = new Color(1f, 1f, 1f, 0.04f);
            Button button = row.Go.GetComponent<Button>();
            button.targetGraphic = row.Back;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.85f, 0.55f, 1f);
            colors.pressedColor = new Color(0.8f, 0.6f, 0.3f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => Pick(row));

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(row.Go.transform, false);
            var ir = (RectTransform)icon.transform;
            ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
            ir.pivot = new Vector2(0f, 0.5f);
            ir.anchoredPosition = new Vector2(3f, 0f);
            ir.sizeDelta = new Vector2(RowH - 6f, RowH - 6f);
            row.Icon = icon.GetComponent<Image>();
            row.Icon.raycastTarget = false;
            row.Icon.preserveAspect = true;

            row.Name = AddText(row.Go.transform, "Name", 15f, TextAlignmentOptions.MidlineLeft, RowH, 0f, 0.58f);
            row.Meta = AddText(row.Go.transform, "Meta", 13f, TextAlignmentOptions.MidlineRight, 0f, 0.58f, 1f);
            row.Go.SetActive(false);
            return row;
        }

        private static TextMeshProUGUI AddText(Transform parent, string name, float size, TextAlignmentOptions align, float left, float from, float to)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(from, 0f);
            rt.anchorMax = new Vector2(to, 1f);
            rt.offsetMin = new Vector2(left > 0f ? left : 0f, 0f);
            rt.offsetMax = new Vector2(-4f, 0f);
            TextMeshProUGUI label = Fonts.AddLabel(go);
            label.fontSize = size;
            label.color = Text;
            label.alignment = align;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private static void AddButton(Transform parent, string name, string caption, Vector2 anchor, Vector2 at, Vector2 size, System.Action click)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = at;
            rt.sizeDelta = size;
            Image back = go.GetComponent<Image>();
            back.color = new Color(0.24f, 0.22f, 0.20f, 0.95f);
            go.GetComponent<Button>().targetGraphic = back;
            go.GetComponent<Button>().onClick.AddListener(() => click());
            var label = new GameObject("Caption", typeof(RectTransform));
            label.transform.SetParent(go.transform, false);
            var lr = (RectTransform)label.transform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            TextMeshProUGUI text = Fonts.AddLabel(label);
            text.text = caption;
            text.fontSize = 14f;
            text.color = Text;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
        }
    }

    /// The panel is a child of m_largeRoot, which the game deactivates when the map leaves Large, but
    /// its own active flag would stay set and it would be back, stale, on the next open.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
    internal static class Patch_Minimap_SetMapMode_Inspector
    {
        private static void Postfix(Minimap __instance)
        {
            if (__instance.m_mode != Minimap.MapMode.Large)
                ClusterInspector.Close();
        }
    }
}
