using UnityEngine;
using UnityEngine.EventSystems;

namespace CarturMapPins
{
    /// Drags the icon picker around the map by its grab handle.
    ///
    /// Unity's own drag events rather than mouse tracking: the canvas already has a
    /// GraphicRaycaster working out what the cursor is over, and doing it by hand would mean
    /// re-deciding every frame whether the pointer is still on the handle.
    ///
    /// Only the handle carries this, not the panel, so dragging never competes with scrolling the
    /// grid or clicking an icon - the two live on different objects and Unity routes to whichever
    /// the cursor is actually on.
    internal class PanelDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform _panel;
        private Canvas _canvas;
        private System.Action _commit;

        public void Init(RectTransform panel, System.Action commit)
        {
            _panel = panel;
            _commit = commit;
            _canvas = panel != null ? panel.GetComponentInParent<Canvas>() : null;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_panel == null)
                return;

            // delta is in screen pixels and the panel is positioned in the canvas's own units.
            // scaleFactor is exactly that conversion, so the panel keeps up with the cursor at
            // any UI scale rather than lagging behind or outrunning it.
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            if (scale <= 0f)
                scale = 1f;

            _panel.anchoredPosition += eventData.delta / scale;
        }

        /// Written on release rather than on every frame of the drag: the config file is saved
        /// when a value changes, and doing that a few hundred times across one drag would write
        /// the file a few hundred times.
        public void OnEndDrag(PointerEventData eventData)
        {
            _commit?.Invoke();
        }
    }
}
