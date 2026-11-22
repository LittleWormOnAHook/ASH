using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Interaction;
using ASH.Inventory;
using ASH.UI;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Hand-placed gear box pickup (ASH_Pickup_GearBox): tints its box in the item's rarity colour.</summary>
    [ExecuteAlways]
    public sealed class ASHGearBoxTint : MonoBehaviour
    {
        static MaterialPropertyBlock _mpb;
        void OnEnable() => Apply();
        void OnValidate() => Apply();
        public void Apply()
        {
            var pk = GetComponent<ASHItemPickup>(); var it = pk ? pk.Item : null; if (!it) return;
            var col = ASHClothingSettings.I.RarityColor(it.rarity);
            _mpb ??= new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                r.GetPropertyBlock(_mpb); _mpb.SetColor("_BaseColor", col); _mpb.SetColor("_Color", col); r.SetPropertyBlock(_mpb);
            }
        }
    }
}
