using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Shared yield rolls for world pickups and (later) hunting. A 0-1 skill factor shifts the roll toward the
    /// top of the range. World deer-skin pickups pass 0 (a plain 2-5). Wildlife kills should pass
    /// <see cref="Skill01"/> for the weapon or hunting skill used.
    /// </summary>
    public static class ASHHarvest
    {
        /// <summary>Maps a skill level to 0-1 using that skill's max level (level 1 = 0, max = 1).</summary>
        public static float Skill01(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return 0f;
            var c = ASHSkills.CurveOf(skillId);
            int max = Mathf.Max(2, c.maxLevel);
            return Mathf.Clamp01((ASHSkills.Level(skillId) - 1) / (float)(max - 1));
        }

        public static float HuntingSkill01() => Skill01(ASHSkills.Hunting);

        /// <summary>Inclusive random yield between min and max, shifted up by skill01 (0 = full range, 1 = almost always max).</summary>
        public static int Yield(int min, int max, float skill01)
        {
            if (max < min) { int swap = min; min = max; max = swap; }
            min = Mathf.Max(1, min);
            max = Mathf.Max(min, max);
            float u = Mathf.Clamp01(skill01);
            int lo = Mathf.RoundToInt(Mathf.Lerp(min, max, u * 0.8f));
            if (lo > max) lo = max;
            return Random.Range(lo, max + 1);
        }
    }
}
