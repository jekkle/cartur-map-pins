using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// Lifts an icon cell slightly while the cursor is on it.
    ///
    /// In a grid of identical squares the thing under the cursor needs to say so - clicking the
    /// neighbour of what you meant is easy, and a wrong icon is not noticed until you are back on
    /// the map.
    ///
    /// The component stays enabled and Update returns immediately when the cell is at rest.
    /// Disabling it instead breaks it: the event system does not deliver pointer callbacks to a
    /// disabled MonoBehaviour, so the first hover would never arrive.
    internal class CellHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float HoverScale = 1.22f;
        private const float Speed = 12f;

        private Image _border;
        private float _target = 1f;
        private bool _animating;

        public void Bind(Image border) => _border = border;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _target = HoverScale;
            _animating = true;

            // The selected cell keeps its gold: hover says "this is under your cursor", selection
            // says "this is the one you picked", and the second outranks the first.
            if (_border != null && _border.color != IconGrid.Selected)
                _border.color = IconGrid.Hovered;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _target = 1f;
            _animating = true;

            if (_border != null && _border.color != IconGrid.Selected)
                _border.color = IconGrid.Resting;
        }

        private void Update()
        {
            if (!_animating)
                return;

            float scale = Mathf.Lerp(transform.localScale.x, _target, Time.unscaledDeltaTime * Speed);

            // Snapped and stopped once it is close enough, rather than lerping towards the target
            // forever at ever smaller steps.
            if (Mathf.Abs(scale - _target) < 0.005f)
            {
                scale = _target;
                _animating = false;
            }

            transform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    /// The inventory slot's own picture, faded in behind an icon under the mouse. In the
    /// inventory the hover is the slot's box brightening (Button ColorTint, 0.86 -> 1.0 over
    /// 0.1 s, measured with the pilot's slotdump); a picker cell has no box at rest, so the same
    /// picture comes up only while hovered (owner: "the same hover as the inventory"). Used by
    /// every cell, ours and the buttons moved in from the game and Epic Loot. Stays enabled and
    /// idle at rest, like CellHover.
    internal class GlowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float Speed = 24f;   // the slot's 0.1 s fade
        private static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

        private Image _glow, _fill;
        private float _t, _target;

        /// The slot picture is dark, on a dark panel: brightening it alone is hard to see, so a
        /// pale wash comes up with it, which is what makes the hover read at a glance.
        private const float FillPeak = 0.16f;

        private void Awake()
        {
            var go = new GameObject("Glow", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            go.transform.SetAsFirstSibling();
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-1f, -1f);
            rt.offsetMax = new Vector2(1f, 1f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(transform, false);
            fill.transform.SetAsFirstSibling();
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            _fill = fill.GetComponent<Image>();
            _fill.color = new Color(1f, 0.95f, 0.85f, 0f);
            _fill.raycastTarget = false;
            go.transform.SetAsLastSibling();
            go.transform.SetSiblingIndex(1);
            _glow = go.GetComponent<Image>();
            Sprite slot = IconGrid.SlotSprite();
            _glow.sprite = slot != null ? slot : IconGrid.Disc();
            _glow.type = slot != null ? Image.Type.Sliced : Image.Type.Simple;
            _glow.color = Clear;
            _glow.raycastTarget = false;
        }

        public void OnPointerEnter(PointerEventData eventData) => _target = 1f;
        public void OnPointerExit(PointerEventData eventData) => _target = 0f;

        private void Update()
        {
            if (_glow == null || Mathf.Approximately(_t, _target))
                return;
            _t = Mathf.Abs(_t - _target) < 0.01f ? _target : Mathf.Lerp(_t, _target, 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime));
            _glow.color = new Color(1f, 1f, 1f, _t);
            _fill.color = new Color(1f, 0.95f, 0.85f, FillPeak * _t);
        }
    }
}
