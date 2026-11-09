using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// What a badge stands for, listed beside the cursor while it is hovered: "12 pins", then one
    /// row per name with how many and how far the nearest is, up to eight, then "+ N more kinds".
    /// Same panel family as the rest (dark, bronze rail, warm text). Read-only, with raycasts off,
    /// so it cannot swallow a click.
    ///
    /// Rows, not lines of one text, with every other row shaded and the columns kept in line
    /// (totya, 2.4.0: "a compact popup rather than clearly separated rows"). The panel has a canvas
    /// of its own, sorted above the map: the names of nearby pins live in a root drawn after this
    /// one and were printed across the list, "unreadable" in his screenshot.
    internal static class ClusterPopup
    {
        private const int Rows = 8;
        private const float NameW = 190f, CountW = 44f, DistW = 64f, RowH = 22f;
        private static GameObject _panel;
        private static TextMeshProUGUI _head, _more;
        private static readonly Row[] Lines = new Row[Rows];

        private sealed class Row
        {
            public GameObject Go;
            public TextMeshProUGUI Name, Count, Dist;
        }

        public static bool Show(Minimap.PinData leader, Transform root)
        {
            IReadOnlyList<Minimap.PinData> group = Clusters.GroupOf(leader);
            if (ClusterInspector.IsOpen || group == null || group.Count == 0)
                return false;
            if (_panel == null || _panel.transform.parent != root)
                Build(root);
            if (_panel == null)
                return false;

            Vector3 me = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : leader.m_pos;
            var byName = new Dictionary<string, KeyValuePair<int, float>>();
            int total = 0;
            foreach (Minimap.PinData pin in group)
            {
                string name = Labels.Localize(string.IsNullOrEmpty(pin.m_name) ? "Pin" : pin.m_name);
                int weight = Crowding.MergedCount(pin);
                float distance = Utils.DistanceXZ(me, pin.m_pos);
                if (byName.TryGetValue(name, out KeyValuePair<int, float> have))
                    byName[name] = new KeyValuePair<int, float>(have.Key + weight, Mathf.Min(have.Value, distance));
                else
                    byName[name] = new KeyValuePair<int, float>(weight, distance);
                total += weight;
            }

            _head.text = "<color=#C79452>" + total + " pins</color>";
            var shown = byName.OrderByDescending(r => r.Value.Key).ThenBy(r => r.Key).Take(Rows).ToList();
            for (int i = 0; i < Rows; i++)
            {
                bool used = i < shown.Count;
                Lines[i].Go.SetActive(used);
                if (!used)
                    continue;
                Lines[i].Name.text = shown[i].Key;
                Lines[i].Count.text = "x" + shown[i].Value.Key;
                Lines[i].Dist.text = Mathf.RoundToInt(shown[i].Value.Value) + " m";
            }
            _more.gameObject.SetActive(byName.Count > Rows);
            if (byName.Count > Rows)
                _more.text = "+ " + (byName.Count - Rows) + " more kinds";
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            return true;
        }

        public static void Place(Vector3 localPosition)
        {
            if (_panel != null && _panel.activeSelf)
                _panel.transform.localPosition = localPosition;
        }

        public static void Hide()
        {
            if (_panel != null && _panel.activeSelf)
                _panel.SetActive(false);
        }

        private static void Build(Transform root)
        {
            if (_panel != null)
                Object.Destroy(_panel);
            _panel = new GameObject("CarturPins_ClusterPopup", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(root, false);
            var rt = (RectTransform)_panel.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            Image back = _panel.GetComponent<Image>();
            back.color = new Color(0.04f, 0.035f, 0.03f, 0.97f);
            back.raycastTarget = false;

            // Above the pin names, which sit in a root drawn after this one. No raycaster: nothing here
            // takes input.
            var canvas = _panel.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            Canvas top = root.GetComponentInParent<Canvas>()?.rootCanvas;
            canvas.sortingLayerID = top != null ? top.sortingLayerID : 0;
            canvas.sortingOrder = (top != null ? top.sortingOrder : 0) + 10;

            var fit = _panel.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = _panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 2f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            _head = Label(_panel.transform, "Head", 17f, TextAlignmentOptions.Left);

            for (int i = 0; i < Rows; i++)
            {
                var row = new Row();
                row.Go = new GameObject("Row" + i, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.Go.transform.SetParent(_panel.transform, false);
                Image shade = row.Go.GetComponent<Image>();
                shade.color = i % 2 == 0 ? new Color(1f, 1f, 1f, 0.07f) : new Color(1f, 1f, 1f, 0f);
                shade.raycastTarget = false;
                row.Go.GetComponent<LayoutElement>().minHeight = RowH;
                var h = row.Go.GetComponent<HorizontalLayoutGroup>();
                h.padding = new RectOffset(4, 4, 0, 0);
                h.spacing = 6f;
                h.childAlignment = TextAnchor.MiddleLeft;
                h.childControlWidth = h.childControlHeight = true;
                h.childForceExpandWidth = h.childForceExpandHeight = false;
                row.Name = Cell(row.Go.transform, "Name", NameW, TextAlignmentOptions.MidlineLeft, new Color(0.95f, 0.92f, 0.82f, 1f));
                row.Count = Cell(row.Go.transform, "Count", CountW, TextAlignmentOptions.MidlineRight, new Color(0.78f, 0.58f, 0.32f, 1f));
                row.Dist = Cell(row.Go.transform, "Dist", DistW, TextAlignmentOptions.MidlineRight, new Color(0.95f, 0.92f, 0.82f, 0.6f));
                Lines[i] = row;
            }

            _more = Label(_panel.transform, "More", 14f, TextAlignmentOptions.Left);
            _more.color = new Color(0.95f, 0.92f, 0.82f, 0.6f);

            var rail = new GameObject("Rail", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rail.transform.SetParent(_panel.transform, false);
            rail.GetComponent<LayoutElement>().ignoreLayout = true;
            var rrt = (RectTransform)rail.transform;
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(0f, 2f);
            rrt.anchoredPosition = Vector2.zero;
            rail.GetComponent<Image>().color = IconGrid.Bronze;
            rail.GetComponent<Image>().raycastTarget = false;
            _panel.SetActive(false);
        }

        private static TextMeshProUGUI Label(Transform parent, string name, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI text = Fonts.AddLabel(go);
            text.fontSize = size;
            text.color = new Color(0.95f, 0.92f, 0.82f, 1f);
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        /// One column of a row: a fixed width, so the columns line up from row to row.
        private static TextMeshProUGUI Cell(Transform parent, string name, float width, TextAlignmentOptions align, Color colour)
        {
            TextMeshProUGUI text = Label(parent, name, 16f, align);
            text.color = colour;
            text.overflowMode = TextOverflowModes.Ellipsis;
            var element = text.gameObject.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = width;
            return text;
        }
    }
}
