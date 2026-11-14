using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ASH.UI
{
    /// <summary>Drags map content inside a clipped viewport (left mouse). Ported from Genesis Project.UI.MapViewportPanHandler.</summary>
    public sealed class ASHMapPanHandler : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        Canvas _canvas; Action<Vector2> _onPan;
        public void Initialize(Action<Vector2> onPan) { _onPan = onPan; _canvas = GetComponentInParent<Canvas>(); }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) e.Use(); }
        public void OnDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || _onPan == null) return;
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            _onPan(e.delta / (_canvas ? _canvas.scaleFactor : 1f)); e.Use();
        }
    }
}
