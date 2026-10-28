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
        private const float HoverScale = 1.14f;
        private const float Speed = 24f;   // about the 0.1 s Cartur's UI gives every button

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

        // Cartur's UI adds its HoverFx to a Button the moment it is first enabled, which for a
        // cell built under a closed map is after IconGrid.YieldHover looked (measured with the
        // pilot's pickpop: HoverFx enabled on our cells and the game's buttons, which held the
        // pop to its 1.05 while Epic Loot's, moved in later, reached 1.14). So for a few frames
        // after this cell is enabled, any HoverFx on it is switched off again.
        private int _yieldFrames;

        private void OnEnable() => _yieldFrames = 12;

        private void Update()
        {
            if (_yieldFrames > 0)
            {
                _yieldFrames--;
                IconGrid.YieldHover(gameObject);
            }

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
}
