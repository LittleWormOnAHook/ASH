using System.Collections.Generic;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Two-ingredient cooking combination (cook menu Combine slots). Secret until learned: reaching unlockLevel in Cooking
    /// teaches it, or the player guesses the pair (order does not matter) and learns it for good (saved in the skills
    /// section as "combo:&lt;comboId&gt;"). Assets live in Resources/CookCombos (ASH/Systems/Fire Cooking creates a starter set).
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Gameplay/Cook Combo", fileName = "ASH_Combo_New")]
    public sealed class ASHCookCombo : ScriptableObject
    {
        public string comboId = "combo";
        public string displayName = "New Combo";
        [TextArea(2, 3)] public string hint;
        public ASHItemDefinition ingredientA;
        public ASHItemDefinition ingredientB;
        public ASHItemDefinition output;
        [Range(1, 10)] public int outputAmount = 1;
        [Tooltip("Cooking level that teaches it automatically. 0 = only by guessing the pair.")]
        [Range(0, 50)] public int unlockLevel = 0;
        [Tooltip("REAL seconds per item before the skill. 0 = by type (water 15 / meat 10 / other 5, slowest ingredient wins).")]
        [Range(0f, 600f)] public float cookSeconds = 0f;

        public string LearnKey => "combo:" + comboId;
        public bool UnlockedByLevel => unlockLevel > 0 && ASHSkills.Level(ASHSkills.Cooking) >= unlockLevel;
        public bool Known => ASHSkills.Knows(LearnKey) || UnlockedByLevel;
        public bool Valid => ingredientA && ingredientB && output;

        public bool Matches(ASHItemDefinition a, ASHItemDefinition b) =>
            Valid && a && b && ((a == ingredientA && b == ingredientB) || (a == ingredientB && b == ingredientA) ||
                                (a.itemId == ingredientA.itemId && b.itemId == ingredientB.itemId) || (a.itemId == ingredientB.itemId && b.itemId == ingredientA.itemId));

        static List<ASHCookCombo> _all;
        public static IReadOnlyList<ASHCookCombo> All
        {
            get
            {
                if (_all == null)
                {
                    _all = new List<ASHCookCombo>();
                    foreach (var c in Resources.LoadAll<ASHCookCombo>("CookCombos")) if (c && c.Valid) _all.Add(c);
                    _all.Sort((x, y) => x.unlockLevel == y.unlockLevel ? string.CompareOrdinal(x.displayName, y.displayName) : (x.unlockLevel == 0 ? 1 : y.unlockLevel == 0 ? -1 : x.unlockLevel.CompareTo(y.unlockLevel)));
                }
                return _all;
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => _all = null;

        public static ASHCookCombo Find(ASHItemDefinition a, ASHItemDefinition b) { foreach (var c in All) if (c.Matches(a, b)) return c; return null; }
        public static ASHCookCombo ById(string id) { foreach (var c in All) if (c.comboId == id) return c; return null; }
        public static bool IsIngredient(ASHItemDefinition it) { if (!it) return false; foreach (var c in All) if (c.ingredientA == it || c.ingredientB == it) return true; return false; }
    }
}
