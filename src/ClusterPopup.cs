using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// What a badge stands for, listed beside the cursor while it is hovered: "12 pins", then one
    /// row per name with how many and how far the nearest is, up to eight, then "+ N more kinds".
    /// Same panel family as the rest (dark, bronze rail, warm text). Read-only, with raycasts off,
    /// so it cannot swallow a click.
    internal static class ClusterPopup
    {
        private const int Rows = 8;
        private static GameObject _panel;
        private static TextMeshProUGUI _text;

        public static bool Show(Minimap.PinData leader, Transform root)
        {
            IReadOnlyList<Minimap.PinData> group = Clusters.GroupOf(leader);
            if (group == null || group.Count == 0)
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

            var sb = new StringBuilder();
            sb.Append("<color=#C79452>").Append(total).Append(" pins</color>");
            foreach (KeyValuePair<string, KeyValuePair<int, float>> row in byName.OrderByDescending(r => r.Value.Key).ThenBy(r => r.Key).Take(Rows))
                sb.Append('\n').Append(row.Key).Append("  <color=#C79452>x").Append(row.Value.Key).Append("</color>  <alpha=#99>")
                  .Append(Mathf.RoundToInt(row.Value.Value)).Append(" m");
            if (byName.Count > Rows)
                sb.Append("\n<alpha=#99>+ ").Append(byName.Count - Rows).Append(" more kinds");
            _text.text = sb.ToString();
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
            back.color = new Color(0.04f, 0.035f, 0.03f, 0.94f);
            back.raycastTarget = false;
            var fit = _panel.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = _panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(_panel.transform, false);
            _text = Fonts.AddLabel(textGo);
            _text.fontSize = 16f;
            _text.color = new Color(0.95f, 0.92f, 0.82f, 1f);
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.raycastTarget = false;

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
    }
}
