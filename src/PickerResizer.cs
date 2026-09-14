using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// TEMPORARY, and goes with PickerDragger. A grip in the panel's top-left corner that resizes
    /// it, so the shape can be found by eye instead of by guessing a column count.
    ///
    /// The panel is anchored to the bottom right, so it grows up and to the left: dragging the
    /// grip left widens it and dragging it up makes it taller. The grid reflows on its own because
    /// its sections lay out flexibly - how many icons fit per row follows the width rather than
    /// being told.
    ///
    /// On release it logs the size, which is what turns into the shipped default. Delete both
    /// files and the two AddComponent calls once the shape is settled.
    internal class PickerResizer : MonoBehaviour, IDragHandler, IEndDragHandler
    {
        private const float MinWidth = 120f;
        private const float MinHeight = 110f;

        private RectTransform _panel;
        private Canvas _canvas;

        public static void Attach(GameObject panel)
        {
            var go = new GameObject("ResizeGrip");
            go.transform.SetParent(panel.transform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(16f, 16f);
            rt.anchoredPosition = Vector2.zero;

            Image image = go.AddComponent<Image>();
            image.color = new Color(0.95f, 0.92f, 0.82f, 0.5f);

            var resizer = go.AddComponent<PickerResizer>();
            resizer._panel = panel.GetComponent<RectTransform>();
            resizer._canvas = panel.GetComponentInParent<Canvas>();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_panel == null)
                return;

            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            Vector2 delta = eventData.delta / scale;

            // Left and up are negative in screen space but both mean "bigger" here, hence the
            // flipped x: the panel's fixed corner is the bottom right one.
            Vector2 size = _panel.sizeDelta + new Vector2(-delta.x, delta.y);
            _panel.sizeDelta = new Vector2(Mathf.Max(MinWidth, size.x), Mathf.Max(MinHeight, size.y));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_panel == null)
                return;

            Plugin.Log.LogInfo($"Icon picker resized: {_panel.sizeDelta.x:F0} x {_panel.sizeDelta.y:F0} " +
                               $"(about {Mathf.FloorToInt((_panel.sizeDelta.x - 24f) / (IconGrid.CellSize + IconGrid.Spacing))} columns, " +
                               $"{Mathf.FloorToInt((_panel.sizeDelta.y - 44f) / (IconGrid.CellSize + IconGrid.Spacing))} rows)");
        }
    }
}
