using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// Lifts an icon cell slightly while the cursor is on it.
    ///
    /// In a grid of 236 identical squares, the thing under the cursor needs to say so - clicking
    /// the neighbour of what you meant is easy, and a pin's icon is not a mistake you notice until
    /// you are back on the map.
    ///
    /// Update is switched off whenever the cell has finished moving, so a grid of 236 of these
    /// costs nothing while the cursor is elsewhere - only the one or two mid-animation tick.
    internal class CellHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float HoverScale = 1.12f;
        private const float Speed = 12f;

        private Image _border;
        private float _target = 1f;

        public void Bind(Image border) => _border = border;

        private void Awake() => enabled = false;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _target = HoverScale;
            enabled = true;

            // The selected cell keeps its gold: hover says "this is under your cursor", selection
            // says "this is the one you picked", and the second outranks the first.
            if (_border != null && _border.color != IconGrid.Selected)
                _border.color = IconGrid.Hovered;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _target = 1f;
            enabled = true;

            if (_border != null && _border.color != IconGrid.Selected)
                _border.color = IconGrid.Resting;
        }

        private void Update()
        {
            float scale = Mathf.Lerp(transform.localScale.x, _target, Time.unscaledDeltaTime * Speed);

            // Snapped and stopped once it is close enough, rather than lerping towards the target
            // forever at ever smaller steps.
            if (Mathf.Abs(scale - _target) < 0.005f)
            {
                scale = _target;
                enabled = false;
            }

            transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
