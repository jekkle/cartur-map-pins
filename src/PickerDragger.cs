using UnityEngine;
using UnityEngine.EventSystems;

namespace CarturMapPins
{
    /// TEMPORARY. Lets the icon picker be dragged around the map screen so a good resting place
    /// can be found by eye rather than by editing two numbers and relaunching.
    ///
    /// On release it writes where it landed into MapPickerRight/MapPickerBottom and logs the
    /// pair, so the chosen position can be read out of the log and become the default.
    ///
    /// Delete this file and the AddComponent call in MapIconPicker.Build once the position is
    /// settled - a panel that can be knocked out of place by a stray click is worse than one
    /// that cannot move, and the config already covers anyone who wants it somewhere else.
    internal class PickerDragger : MonoBehaviour, IDragHandler, IEndDragHandler
    {
        private RectTransform _rect;
        private Canvas _canvas;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_rect == null)
                return;

            // Divided by the canvas scale: pointer deltas are screen pixels and anchoredPosition
            // is in canvas units, which differ on any UI scale but 1.
            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            _rect.anchoredPosition += eventData.delta / scale;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_rect == null)
                return;

            // The panel is anchored to the bottom right corner, so x is negative going left and
            // the settings are insets: flip x, keep y.
            float right = -_rect.anchoredPosition.x;
            float bottom = _rect.anchoredPosition.y;

            Plugin.MapPickerRight.Value = right;
            Plugin.MapPickerBottom.Value = bottom;
            Plugin.Log.LogInfo($"Icon picker moved: MapPickerRight = {right:F0}, MapPickerBottom = {bottom:F0}");
        }
    }
}
