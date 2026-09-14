using UnityEngine;
using UnityEngine.EventSystems;

namespace CarturMapPins
{
    /// Grows a slider's handle while it is held.
    ///
    /// A slider is the one control here you operate by feel rather than by reading: the handle has
    /// to say "you have hold of me" or a drag that misses reads as the slider being broken.
    ///
    /// Same shape as CellHover - a target scale, an Update that returns at rest, and no coroutine.
    internal class SliderHandle : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private const float HeldScale = 1.35f;
        private const float Speed = 16f;

        private float _target = 1f;
        private bool _animating;

        public void OnPointerDown(PointerEventData eventData)
        {
            _target = HeldScale;
            _animating = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _target = 1f;
            _animating = true;
        }

        private void Update()
        {
            if (!_animating)
                return;

            float scale = Mathf.Lerp(transform.localScale.x, _target, Time.unscaledDeltaTime * Speed);
            if (Mathf.Abs(scale - _target) < 0.005f)
            {
                scale = _target;
                _animating = false;
            }

            transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
