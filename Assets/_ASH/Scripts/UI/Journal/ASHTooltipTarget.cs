using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ASH.UI
{
    /// <summary>Hover / focus source for ASHItemTooltip on a slot or row (added by ASHItemTooltip.Attach). Leaves clicks and drags to the
    /// slot's own handlers.</summary>
    public sealed class ASHTooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public Func<ASHItemTooltip.Info> provider;

        public ASHItemTooltip.Info Get() { try { return provider != null ? provider() : default; } catch (Exception) { return default; } }

        public void OnPointerEnter(PointerEventData e) => ASHItemTooltipView.Ensure().Hover(this, false);
        public void OnPointerExit(PointerEventData e) { var v = ASHItemTooltipView.Current; if (v) v.Unhover(this); }
        public void OnSelect(BaseEventData e) { if (ASHItemTooltipSettings.I.gamepadFocus) ASHItemTooltipView.Ensure().Hover(this, true); }
        public void OnDeselect(BaseEventData e) { var v = ASHItemTooltipView.Current; if (v) v.Unhover(this); }
        void OnDisable() { var v = ASHItemTooltipView.Current; if (v) v.Unhover(this); }
    }
}
