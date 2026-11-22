using UnityEngine;

namespace ASH.UI
{
    /// <summary>Rook (character) journal tab look: silhouette art/tint and slot/grid sizes. Edits apply live, Play edits persist.</summary>
    [CreateAssetMenu(menuName = "ASH/Settings/Character Tab", fileName = "ASH_CharacterTabSettings")]
    public sealed class ASHCharacterTabSettings : ScriptableObject
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHCharacterTabSettings()
        {
            _i = default;
        }

        static ASHCharacterTabSettings _i;
        public static ASHCharacterTabSettings I { get { if (!_i) { _i = Resources.Load<ASHCharacterTabSettings>("ASH_CharacterTabSettings"); if (!_i) _i = CreateInstance<ASHCharacterTabSettings>(); } return _i; } }

        [Header("Silhouette")]
        public Sprite silhouette;
        public Color silhouetteTint = new Color(0.32f, 0.29f, 0.25f, 0.55f);
        [Range(200, 600)] public float silhouetteHeight = 470f;
        [Range(-150, 150)] public float silhouetteOffsetY = 6f;

        [Header("Equipment slots")]
        [Range(40, 120)] public float slotSize = 72f;
        [Range(100, 340)] public float slotColumnX = 165f;
        [Range(60, 140)] public float slotRowStep = 92f;
        [Range(-150, 150)] public float slotTopY = 205f;

        [Header("Inventory grid (left)")]
        [Range(32, 96)] public float cellSize = 60f;
        [Range(0, 16)] public float cellSpacing = 6f;
        [Range(2, 10)] public int columns = 6;
    }
}
