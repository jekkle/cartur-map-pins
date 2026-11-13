using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// A small cluster, opened in place: its pins fan out on a ring round the badge, each on a thin
    /// line back to where it really is. Clicking one glides the map to it.
    ///
    /// The fanned icons are copies drawn on top, in the same space as the pins' own icons. The pins
    /// themselves are not touched, and they are not in the game's list, so right-click delete cannot
    /// reach them. Anything that moves the view puts them away: a zoom, a drag, a click elsewhere,
    /// or the map closing (Escape).
    internal static class ClusterSpiderfy
    {
        private static GameObject _root;
        private static readonly List<GameObject> Spread = new List<GameObject>();
        private static readonly List<GameObject> Legs = new List<GameObject>();
        private static readonly List<RectTransform> Hit = new List<RectTransform>();
        private static readonly List<Minimap.PinData> HitPin = new List<Minimap.PinData>();
        private static IReadOnlyList<Minimap.PinData> _group;
        private static Minimap.PinData _leader;
        private static Vector2 _centre;
        private static float _zoom;

        public static bool IsOpen => _root != null && _root.activeSelf;

        /// True when a click on a badge should fan it out rather than list it.
        public static bool Wants(int members) =>
            Plugin.ClusterClickAction.Value == ClusterClick.Spiderfy && Plugin.SpiderfyMaxPins.Value >= members;

        public static void Open(Minimap map, Minimap.PinData leader)
        {
            IReadOnlyList<Minimap.PinData> group = Clusters.GroupOf(leader);
            Image anchor = leader?.m_iconElement;
            if (group == null || anchor == null)
                return;
            Close();

            Transform parent = anchor.transform.parent;
            if (_root == null || _root.transform.parent != parent)
            {
                if (_root != null)
                    Object.Destroy(_root);
                _root = new GameObject("CarturPins_Spiderfy", typeof(RectTransform));
                _root.transform.SetParent(parent, false);
                Spread.Clear();
                Legs.Clear();
            }
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            _leader = leader;
            _zoom = map.LargeZoom;
            _centre = anchor.rectTransform.localPosition;
            Hit.Clear();
            HitPin.Clear();
            _group = group;

            float icon = anchor.rectTransform.rect.width * anchor.rectTransform.localScale.x;
            float ring = Mathf.Max(icon * 2.4f, group.Count * icon * 1.6f / (2f * Mathf.PI));
            for (int i = 0; i < group.Count; i++)
            {
                Minimap.PinData pin = group[i];
                float angle = Mathf.PI * 2f * i / group.Count - Mathf.PI * 0.5f;
                Vector2 at = _centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ring;
                Vector2 real = pin.m_iconElement != null ? (Vector2)pin.m_iconElement.rectTransform.localPosition - Clusters.ShiftOf(pin) : _centre;

                RectTransform leg = Get(Legs, "Leg", i, typeof(Image));
                Vector2 line = real - at;
                leg.anchorMin = leg.anchorMax = new Vector2(0.5f, 0.5f);
                leg.pivot = new Vector2(0f, 0.5f);
                leg.localPosition = at;
                leg.sizeDelta = new Vector2(line.magnitude, 2f);
                leg.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(line.y, line.x) * Mathf.Rad2Deg);
                Image legImage = leg.GetComponent<Image>();
                legImage.color = new Color(0.78f, 0.58f, 0.32f, 0.8f);
                legImage.raycastTarget = false;

                RectTransform node = Get(Spread, "Pin", i, typeof(Image), typeof(Button));
                node.anchorMin = node.anchorMax = new Vector2(0.5f, 0.5f);
                node.pivot = new Vector2(0.5f, 0.5f);
                node.localPosition = at;
                node.localScale = Vector3.one;
                node.sizeDelta = new Vector2(icon * 1.4f, icon * 1.4f);
                // A dark disc with the pin's icon on it, so it reads against a busy map.
                Image disc = node.GetComponent<Image>();
                disc.sprite = IconGrid.Disc();
                disc.color = new Color(0.07f, 0.06f, 0.05f, 0.92f);
                disc.raycastTarget = true;
                Transform face = node.Find("Icon");
                if (face == null)
                {
                    var f = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                    f.transform.SetParent(node, false);
                    var fr = (RectTransform)f.transform;
                    fr.anchorMin = fr.anchorMax = new Vector2(0.5f, 0.5f);
                    fr.sizeDelta = new Vector2(icon, icon);
                    f.GetComponent<Image>().raycastTarget = false;
                    f.GetComponent<Image>().preserveAspect = true;
                    face = f.transform;
                }
                Image image = face.GetComponent<Image>();
                image.sprite = pin.m_iconElement != null ? pin.m_iconElement.sprite : pin.m_icon;
                image.color = pin.m_iconElement != null ? pin.m_iconElement.color : Color.white;
                Button button = node.GetComponent<Button>();
                button.targetGraphic = disc;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => { Close(); ClusterInspector.FocusSeparated(map, pin, group); });
                Hit.Add(node);
                HitPin.Add(pin);
            }
            for (int i = group.Count; i < Spread.Count; i++)
            {
                Spread[i].SetActive(false);
                Legs[i].SetActive(false);
            }
            // Lines under the icons.
            for (int i = 0; i < group.Count; i++)
                Legs[i].transform.SetAsLastSibling();
            for (int i = 0; i < group.Count; i++)
                Spread[i].transform.SetAsLastSibling();
            ClusterPopup.Hide();
        }

        public static void Close()
        {
            if (_root != null && _root.activeSelf)
                _root.SetActive(false);
            _leader = null;
        }

        /// Every frame from the Minimap.Update postfix.
        public static void Tick(Minimap map)
        {
            if (!IsOpen)
                return;
            Image anchor = _leader?.m_iconElement;
            bool moved = anchor == null || ((Vector2)anchor.rectTransform.localPosition - _centre).sqrMagnitude > 4f ||
                         Mathf.Abs(map.LargeZoom / _zoom - 1f) > 0.005f;
            if (moved)
            {
                Close();
                return;
            }
            if (ZInput.GetMouseButtonDown(0) && !Over())
                Close();
        }

        /// A click on a fanned icon. The icons are buttons, but a raycast at one reaches only the map (measured:
        /// raycast order at an icon was map, OutsideMapClick, large; the button was never in it), so the click
        /// fell through to Minimap.OnMapLeftClick, which ticked the nearest pin - the X - and never navigated
        /// (totya, 2.5.0). The click is answered here instead, from where the icons are drawn, and the map's own
        /// handler is skipped for it by Patch_Cluster_LeftClick.
        public static bool TryClick(Minimap map)
        {
            if (!IsOpen)
                return false;
            for (int i = 0; i < Hit.Count; i++)
            {
                if (Hit[i] == null || !RectTransformUtility.RectangleContainsScreenPoint(Hit[i], ZInput.pointerPosition, null))
                    continue;
                Minimap.PinData pin = HitPin[i];
                IReadOnlyList<Minimap.PinData> group = _group;
                Close();
                ClusterInspector.FocusSeparated(map, pin, group);
                return true;
            }
            return false;
        }

        private static bool Over()
        {
            foreach (RectTransform r in Hit)
            {
                if (r != null && RectTransformUtility.RectangleContainsScreenPoint(r, ZInput.pointerPosition, null))
                    return true;
            }
            return false;
        }

        private static RectTransform Get(List<GameObject> pool, string name, int i, params System.Type[] parts)
        {
            while (pool.Count <= i)
            {
                var go = new GameObject(name, typeof(RectTransform));
                foreach (System.Type part in parts)
                    go.AddComponent(part);
                go.transform.SetParent(_root.transform, false);
                pool.Add(go);
            }
            pool[i].SetActive(true);
            return (RectTransform)pool[i].transform;
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
    internal static class Patch_Minimap_SetMapMode_Spiderfy
    {
        private static void Postfix(Minimap __instance)
        {
            if (__instance.m_mode != Minimap.MapMode.Large)
                ClusterSpiderfy.Close();
        }
    }
}
