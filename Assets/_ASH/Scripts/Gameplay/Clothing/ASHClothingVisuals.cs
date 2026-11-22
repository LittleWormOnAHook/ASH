using System.Collections.Generic;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Clothing C4 visual hook: shows worn pieces on the player's humanoid rig. For each worn item with a wornVisualPrefab:
    /// rigid props are parented to the slot's bone (ASH_ClothingSettings.slotAttach, or the item's wornAttach override); skinned meshes
    /// are rebound to the player's skeleton by bone name so they deform with the body. Optional debug placeholders (small rarity boxes
    /// at the attach points, off by default). Rebuilt when equipment changes and when the settings toggles change. No art needed yet.</summary>
    [DefaultExecutionOrder(250)]
    public sealed class ASHClothingVisuals : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHClothingVisuals() { Instance = null; }
        public static ASHClothingVisuals Instance { get; private set; }

        Animator _anim; readonly List<GameObject> _spawned = new List<GameObject>(); bool _dirty = true, _lastShow, _lastPh; float _lastSize;
        Dictionary<string, Transform> _bonesByName;

        public static void Ensure()
        {
            if (Instance) return;
            var inv = FindAnyObjectByType<ASHInventory>(); if (!inv) return;
            if (!inv.GetComponent<ASHClothingVisuals>()) inv.gameObject.AddComponent<ASHClothingVisuals>();
        }

        void Awake() { Instance = this; }
        void OnEnable() { Instance = this; ASHCharacterEquipment.Changed += MarkDirty; _dirty = true; }
        void OnDisable() { ASHCharacterEquipment.Changed -= MarkDirty; Clear(); if (Instance == this) Instance = null; }
        void MarkDirty() => _dirty = true;

        Animator Rig()
        {
            if (_anim && _anim.isHuman) return _anim;
            _anim = null;
            foreach (var a in GetComponentsInChildren<Animator>(true)) if (a.isHuman && a.avatar) { _anim = a; break; }
            if (!_anim) foreach (var a in GetComponentsInParent<Animator>(true)) if (a.isHuman && a.avatar) { _anim = a; break; }
            _bonesByName = null;
            return _anim;
        }

        void LateUpdate()
        {
            var S = ASHClothingSettings.I;
            if (S.showWornVisuals != _lastShow || S.showPlaceholderVisuals != _lastPh || !Mathf.Approximately(S.placeholderSize, _lastSize)) _dirty = true;
            if (!_dirty) return;
            _dirty = false; _lastShow = S.showWornVisuals; _lastPh = S.showPlaceholderVisuals; _lastSize = S.placeholderSize;
            Rebuild();
        }

        void Clear() { foreach (var g in _spawned) if (g) Destroy(g); _spawned.Clear(); }

        void Rebuild()
        {
            Clear();
            var S = ASHClothingSettings.I; var eq = ASHCharacterEquipment.Instance; var rig = Rig();
            if (!eq || !rig || (!S.showWornVisuals && !S.showPlaceholderVisuals)) return;
            foreach (var w in eq.WornPieces())
            {
                var it = w.item; if (!it) continue;
                var at = S.AttachFor(it.wearSlot);
                HumanBodyBones bone = it.wornAttach != null && it.wornAttach.overrideSlot ? it.wornAttach.bone : at.bone;
                var bt = rig.GetBoneTransform(bone) ?? rig.GetBoneTransform(HumanBodyBones.Chest) ?? rig.transform;
                Vector3 pos = it.wornAttach != null && it.wornAttach.overrideSlot ? it.wornAttach.localPosition : at.localPosition;
                Vector3 eul = it.wornAttach != null && it.wornAttach.overrideSlot ? it.wornAttach.localEuler : at.localEuler;
                Vector3 scl = it.wornAttach != null && it.wornAttach.overrideSlot ? it.wornAttach.localScale : Vector3.one;
                if (S.showWornVisuals && it.wornVisualPrefab)
                {
                    var go = Instantiate(it.wornVisualPrefab);
                    go.name = "Worn_" + it.itemId;
                    foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
                    var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    if (skinned.Length > 0)
                    {
                        go.transform.SetParent(rig.transform, false);
                        foreach (var smr in skinned) Rebind(smr, rig);
                    }
                    else
                    {
                        go.transform.SetParent(bt, false);
                        go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(eul); go.transform.localScale = scl;
                    }
                    SetLayer(go, rig.gameObject.layer);
                    _spawned.Add(go);
                }
                else if (S.showPlaceholderVisuals)
                {
                    var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = "WornPlaceholder_" + it.itemId;
                    Destroy(box.GetComponent<Collider>());
                    box.transform.SetParent(bt, false); box.transform.localPosition = pos; box.transform.localRotation = Quaternion.Euler(eul);
                    box.transform.localScale = Vector3.one * S.placeholderSize;
                    var r = box.GetComponent<Renderer>(); var mpb = new MaterialPropertyBlock(); var col = S.RarityColor(it.rarity);
                    mpb.SetColor("_BaseColor", col); mpb.SetColor("_Color", col); r.SetPropertyBlock(mpb);
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    SetLayer(box, rig.gameObject.layer);
                    _spawned.Add(box);
                }
            }
        }

        void Rebind(SkinnedMeshRenderer smr, Animator rig)
        {
            if (_bonesByName == null)
            {
                _bonesByName = new Dictionary<string, Transform>();
                foreach (var t in rig.GetComponentsInChildren<Transform>(true)) if (!_bonesByName.ContainsKey(t.name)) _bonesByName[t.name] = t;
            }
            var src = smr.bones; var dst = new Transform[src.Length]; int miss = 0;
            for (int i = 0; i < src.Length; i++) { if (src[i] && _bonesByName.TryGetValue(src[i].name, out var b)) dst[i] = b; else { dst[i] = src[i]; miss++; } }
            smr.bones = dst;
            if (smr.rootBone && _bonesByName.TryGetValue(smr.rootBone.name, out var rb)) smr.rootBone = rb;
            smr.updateWhenOffscreen = true;
            if (miss > 0) Debug.LogWarning($"[ASH] Worn visual '{smr.name}': {miss} bone(s) not found on the player rig (names must match).");
        }

        static void SetLayer(GameObject go, int layer) { foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }
    }
}
