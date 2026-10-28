using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// A colour wheel (hue round, saturation out), a brightness bar, the last five colours used and
    /// an Original button. One control for the pin editor and the Hugin setup menu (owner,
    /// 2026-10-27: "a color wheel and then last 5 colors used").
    ///
    /// A colour is passed around as the int PinStyles stores: 0 for "leave it as it is", otherwise
    /// PinStyles.CustomFlag | 0xRRGGBB. The wheel only reports; whoever owns it decides when to write.
    internal sealed class ColourWheel : MonoBehaviour
    {
        public const float Width = 258f;
        public const float Height = 140f;

        private const int WheelPixels = 128;
        private const float WheelSize = 120f;
        private const float RightX = 134f;
        private const float RightWidth = Width - RightX;

        private static readonly Color Warm = new Color(0.95f, 0.92f, 0.82f, 1f);

        private Action<int> _onPick;
        private float _h, _s = 1f, _v = 1f;
        private bool _has;

        private Image _wheel, _wheelMark, _barMark, _preview;
        private TextMeshProUGUI _previewText;
        private Texture2D _barTexture;
        private readonly Image[] _recent = new Image[Recents];

        /// Builds the control inside `parent`, top-left at (0, y). `onPick` fires while dragging.
        public static ColourWheel Build(Transform parent, float y, Action<int> onPick)
        {
            var go = new GameObject("ColourWheel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(Width, Height);
            ColourWheel wheel = go.AddComponent<ColourWheel>();
            wheel._onPick = onPick;
            wheel.BuildUi(rt);
            return wheel;
        }

        // ---- showing a colour ------------------------------------------------------------------

        /// Shows `code` without reporting it back.
        public void Show(int code)
        {
            _has = code != 0;
            if (_has)
                Color.RGBToHSV(PinStyles.FromCode(code), out _h, out _s, out _v);
            Refresh();
        }

        private void Pick()
        {
            _has = true;
            Refresh();
            _onPick?.Invoke(PinStyles.ToCode(Current));
        }

        private Color Current => Color.HSVToRGB(_h, _s, _v);

        private void Refresh()
        {
            float angle = _h * Mathf.PI * 2f, r = _s * (WheelSize * 0.5f);
            _wheelMark.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
            _wheelMark.enabled = _has;
            _wheel.color = new Color(_v, _v, _v, 1f);

            Color top = Color.HSVToRGB(_h, _s, 1f);
            for (int x = 0; x < _barTexture.width; x++)
                _barTexture.SetPixel(x, 0, Color.Lerp(Color.black, top, x / (float)(_barTexture.width - 1)));
            _barTexture.Apply();
            _barMark.rectTransform.anchoredPosition = new Vector2(_v * RightWidth, 0f);
            _barMark.enabled = _has;

            _preview.color = _has ? Current : new Color(1f, 1f, 1f, 0.12f);
            _previewText.text = _has ? "#" + ColorUtility.ToHtmlStringRGB(Current) : "ORIGINAL";
            _previewText.color = _has && Current.grayscale > 0.55f ? Color.black : Warm;

            IReadOnlyList<Color> recent = Recent;
            for (int i = 0; i < _recent.Length; i++)
            {
                _recent[i].enabled = i < recent.Count;
                if (i < recent.Count)
                    _recent[i].color = recent[i];
            }
        }

        // ---- recent colours -------------------------------------------------------------------

        public const int Recents = 5;
        private static readonly List<Color> RecentList = new List<Color>();
        private static bool _recentLoaded;
        private static string RecentPath => Path.Combine(BepInEx.Paths.ConfigPath, "carturpins_recent_colours.txt");

        /// The last five colours used, newest first. A small file in the config folder, so they
        /// are the same in every world and survive a restart.
        public static IReadOnlyList<Color> Recent
        {
            get
            {
                if (!_recentLoaded)
                {
                    _recentLoaded = true;
                    try
                    {
                        if (File.Exists(RecentPath))
                            foreach (string line in File.ReadAllLines(RecentPath))
                                if (ColorUtility.TryParseHtmlString("#" + line.Trim(), out Color c) && RecentList.Count < Recents)
                                    RecentList.Add(c);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Could not read the recent colours: {e.Message}");
                    }
                }
                return RecentList;
            }
        }

        /// Records a colour as used. Called when a choice is confirmed, not while dragging, so the
        /// row holds colours somebody kept rather than every one passed on the way.
        public static void Remember(int code)
        {
            if (code == 0)
                return;
            Color c = PinStyles.FromCode(code);
            c.a = 1f;
            IReadOnlyList<Color> _ = Recent;
            RecentList.RemoveAll(x => ColorUtility.ToHtmlStringRGB(x) == ColorUtility.ToHtmlStringRGB(c));
            RecentList.Insert(0, c);
            while (RecentList.Count > Recents)
                RecentList.RemoveAt(RecentList.Count - 1);
            try
            {
                var lines = new List<string>();
                foreach (Color x in RecentList)
                    lines.Add(ColorUtility.ToHtmlStringRGB(x));
                File.WriteAllLines(RecentPath, lines.ToArray());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not write the recent colours: {e.Message}");
            }
        }

        // ---- building --------------------------------------------------------------------------

        private void BuildUi(RectTransform root)
        {
            // The wheel: hue by angle, saturation by distance, drawn once into a texture and
            // dimmed by the brightness bar through the Image colour.
            var tex = new Texture2D(WheelPixels, WheelPixels, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float half = WheelPixels * 0.5f;
            for (int py = 0; py < WheelPixels; py++)
            {
                for (int px = 0; px < WheelPixels; px++)
                {
                    float dx = (px + 0.5f - half) / half, dy = (py + 0.5f - half) / half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float hue = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 1f) % 1f;
                    Color c = Color.HSVToRGB(hue, Mathf.Min(dist, 1f), 1f);
                    c.a = Mathf.Clamp01((1f - dist) * half * 0.5f);   // soft edge
                    tex.SetPixel(px, py, c);
                }
            }
            tex.Apply();
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, WheelPixels, WheelPixels), new Vector2(0.5f, 0.5f), 100f);

            var wheelGo = new GameObject("Wheel", typeof(RectTransform), typeof(Image), typeof(WheelInput));
            wheelGo.transform.SetParent(root, false);
            var wrt = (RectTransform)wheelGo.transform;
            wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0f, 1f);
            wrt.anchoredPosition = new Vector2(2f, -2f);
            wrt.sizeDelta = new Vector2(WheelSize, WheelSize);
            _wheel = wheelGo.GetComponent<Image>();
            _wheel.sprite = sprite;
            wheelGo.GetComponent<WheelInput>().Owner = this;

            _wheelMark = Ring(wheelGo.transform, 12f);

            // Right column, top down: preview, brightness, recents, original.
            float y = -2f;
            _preview = Box(root, "Preview", new Vector2(RightX, y), new Vector2(RightWidth, 24f), new Color(1f, 1f, 1f, 0.12f));
            _previewText = LabelOn(_preview.transform, 13f, TextAlignmentOptions.Center);
            y -= 32f;

            LabelAt(root, "BRIGHTNESS", new Vector2(RightX, y), 9f);
            y -= 12f;
            var barGo = new GameObject("Bar", typeof(RectTransform), typeof(RawImage), typeof(BarInput));
            barGo.transform.SetParent(root, false);
            var brt = (RectTransform)barGo.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(RightX, y);
            brt.sizeDelta = new Vector2(RightWidth, 16f);
            _barTexture = new Texture2D(32, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            barGo.GetComponent<RawImage>().texture = _barTexture;
            barGo.GetComponent<BarInput>().Owner = this;
            _barMark = Box(brt, "Mark", Vector2.zero, new Vector2(4f, 22f), Warm);
            _barMark.rectTransform.anchorMin = _barMark.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _barMark.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _barMark.raycastTarget = false;
            y -= 26f;

            LabelAt(root, "LAST USED", new Vector2(RightX, y), 9f);
            y -= 12f;
            for (int i = 0; i < Recents; i++)
            {
                int index = i;
                Image dot = Box(root, "Recent" + i, new Vector2(RightX + i * 25f, y), new Vector2(21f, 21f), Color.white);
                var frame = Box(dot.transform, "Frame", Vector2.zero, Vector2.zero, IconGrid.Resting);
                frame.sprite = IconGrid.Border;
                frame.type = Image.Type.Sliced;
                frame.raycastTarget = false;
                var frt = frame.rectTransform;
                frt.anchorMin = Vector2.zero;
                frt.anchorMax = Vector2.one;
                frt.offsetMin = frt.offsetMax = Vector2.zero;
                var button = dot.gameObject.AddComponent<Button>();
                button.targetGraphic = dot;
                button.onClick.AddListener(() =>
                {
                    IReadOnlyList<Color> r = Recent;
                    if (index >= r.Count)
                        return;
                    Color.RGBToHSV(r[index], out _h, out _s, out _v);
                    Pick();
                });
                dot.gameObject.AddComponent<CellHover>().Bind(null);
                _recent[i] = dot;
            }
            y -= 28f;

            Image original = Box(root, "Original", new Vector2(RightX, y), new Vector2(RightWidth, 22f), new Color(0.18f, 0.14f, 0.1f, 0.95f));
            LabelOn(original.transform, 12f, TextAlignmentOptions.Center).text = "ORIGINAL";
            var obtn = original.gameObject.AddComponent<Button>();
            obtn.targetGraphic = original;
            obtn.onClick.AddListener(() =>
            {
                _has = false;
                Refresh();
                _onPick?.Invoke(0);
            });
            original.gameObject.AddComponent<CellHover>().Bind(null);

            Refresh();
        }

        private static Image Ring(Transform parent, float size)
        {
            Image ring = Box(parent, "Mark", Vector2.zero, new Vector2(size, size), Color.white);
            ring.sprite = IconGrid.Border;
            ring.type = Image.Type.Sliced;
            ring.raycastTarget = false;
            ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            ring.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return ring;
        }

        private static Image Box(Transform parent, string name, Vector2 at, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = at;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI LabelOn(Transform parent, float size, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            TextMeshProUGUI t = Fonts.AddLabel(go);
            t.fontSize = size;
            t.alignment = align;
            t.color = Warm;
            t.raycastTarget = false;
            return t;
        }

        private static void LabelAt(Transform parent, string text, Vector2 at, float size)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = at;
            rt.sizeDelta = new Vector2(RightWidth, 12f);
            TextMeshProUGUI t = Fonts.AddLabel(go);
            t.text = text;
            t.fontSize = size;
            t.characterSpacing = 4f;
            t.color = new Color(0.95f, 0.92f, 0.82f, 0.55f);
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.raycastTarget = false;
        }

        // ---- input -----------------------------------------------------------------------------

        private sealed class WheelInput : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public ColourWheel Owner;
            public void OnPointerDown(PointerEventData e) => Set(e);
            public void OnDrag(PointerEventData e) => Set(e);

            private void Set(PointerEventData e)
            {
                var rt = (RectTransform)transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out Vector2 local))
                    return;
                float r = WheelSize * 0.5f;
                // the pivot is the corner, so measure from the rect's centre
                Vector2 p = new Vector2(local.x - rt.rect.center.x, local.y - rt.rect.center.y) / r;
                float dist = Mathf.Min(p.magnitude, 1f);
                Owner._h = (Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f) + 1f) % 1f;
                Owner._s = dist;
                Owner.Pick();
            }
        }

        private sealed class BarInput : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public ColourWheel Owner;
            public void OnPointerDown(PointerEventData e) => Set(e);
            public void OnDrag(PointerEventData e) => Set(e);

            private void Set(PointerEventData e)
            {
                var rt = (RectTransform)transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out Vector2 local))
                    return;
                Owner._v = Mathf.Clamp01((local.x - rt.rect.xMin) / rt.rect.width);
                Owner.Pick();
            }
        }
    }
}
