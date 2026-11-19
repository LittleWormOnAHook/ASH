using System;
using System.Collections.Generic;
using System.Text;
using ASH.Inventory;
using ASH.Survival;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Cooking and boiling ON a campfire (added to every ASHCampfire at runtime). Campfire recipes with cookGameMinutes &gt; 0
    /// (raw meat / fish, creek water, chokecherries) move their ingredients onto the nearest lit fire from the Survival Guide's
    /// Craft button; the fire cooks them on the GAME clock while it is lit (no fuel = fire out = cooking pauses), slower while
    /// low on fuel. Done food goes straight into the pack when the player is close, otherwise "Take ..." at the fire.
    /// Tuning: Resources/ASH_CookingSettings (ASH/Systems/Fire Cooking). Saved with the fire by ASHPlacedSave.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHFireCooking : MonoBehaviour
    {
        [Serializable] public sealed class Job { public string recipe; public string output; public int amount = 1; public string label; public float done; public float total; public bool ready; public bool boil; }
        [Serializable] sealed class State { public List<Job> jobs = new List<Job>(); }

        readonly List<Job> _jobs = new List<Job>();
        public IReadOnlyList<Job> Jobs => _jobs;
        ASHCampfire _fire; AudioSource _loop;
        static ASHCrafting _crafter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            ASHCrafting.FireCookBlock = (cr, r) => { var f = FireFor(cr); return f ? f.Block() : "Needs a lit campfire nearby"; };
            ASHCrafting.FireCookStart = (cr, r) => { var f = FireFor(cr); return f && f.Begin(cr, r); };
        }

        static ASHFireCooking FireFor(ASHCrafting cr)
        {
            if (!cr) return null;
            var c = ASHCampfire.NearestLit(cr.transform.position, cr.StationRange);
            if (!c) return null;
            return c.Cooking ? c.Cooking : c.gameObject.AddComponent<ASHFireCooking>();
        }

        void Awake() { _fire = GetComponent<ASHCampfire>(); }

        public int ReadyCount { get { int n = 0; foreach (var j in _jobs) if (j.ready) n++; return n; } }
        public int CookingCount => _jobs.Count - ReadyCount;

        public string ReadyLabel
        {
            get
            {
                var counts = new Dictionary<string, int>(); var order = new List<string>();
                foreach (var j in _jobs) if (j.ready) { if (!counts.ContainsKey(j.label)) { counts[j.label] = 0; order.Add(j.label); } counts[j.label] += Mathf.Max(1, j.amount); }
                var sb = new StringBuilder();
                foreach (var l in order) { if (sb.Length > 0) sb.Append(", "); sb.Append(l); if (counts[l] > 1) sb.Append(" x").Append(counts[l]); }
                return sb.ToString();
            }
        }

        /// <summary>" · cooking 2: Cooked Meat 40%" for the fire prompt (empty when nothing is on the fire).</summary>
        public string StatusSuffix
        {
            get
            {
                foreach (var j in _jobs)
                    if (!j.ready) return $" · {(j.boil ? "boiling" : "cooking")} {CookingCount}: {j.label} {Mathf.FloorToInt(100f * Mathf.Clamp01(j.total > 0f ? j.done / j.total : 1f))}%{(_fire && !_fire.Lit ? " (fire out)" : "")}";
                return "";
            }
        }

        /// <summary>Why nothing more can go on this fire right now (null = ok).</summary>
        public string Block()
        {
            var s = ASHCookingSettings.I;
            if (!_fire || !_fire.Lit) return "Needs a lit campfire nearby";
            if (_jobs.Count >= s.slotsPerFire) return ReadyCount > 0 ? "Fire is full: take the cooked food" : $"Fire is full ({s.slotsPerFire} cooking)";
            if (_fire.FuelHours < s.minFuelHoursToStart) return "Fire needs more fuel";
            return null;
        }

        static bool IsBoil(ASHRecipe r) => (r.outputItem && r.outputItem.itemId.Contains("water")) || r.recipeId.Contains("water");

        public bool Begin(ASHCrafting cr, ASHRecipe r)
        {
            if (!cr || !r || !r.outputItem || Block() != null || !cr.HasIngredients(r) || !ASHCrafting.HasWaterFor(r)) return false;
            var s = ASHCookingSettings.I;
            if (r.waterPercent > 0f) ASHCrafting.ConsumeWater?.Invoke(r.waterPercent);
            foreach (var ing in r.ingredients) cr.Inventory.Remove(ing.item, ing.amount);
            var j = new Job
            {
                recipe = r.recipeId, output = r.outputItem.itemId, amount = r.outputAmount, label = r.outputItem.displayName,
                total = Mathf.Max(0.01f, r.cookGameMinutes / 60f * s.cookTimeMultiplier), boil = IsBoil(r)
            };
            _jobs.Add(j); _crafter = cr;
            Toast($"{(j.boil ? "Boiling" : "Cooking")} on the fire: {j.label} in {ASHGameTime.Format(j.total)}");
            ASHSfx.Play("cook_start", s.startClips, transform.position, s.sfxVolume);
            return true;
        }

        void Update()
        {
            if (_jobs.Count == 0) { Loop(null); return; }
            var s = ASHCookingSettings.I;
            bool lit = _fire && _fire.Lit;
            float dh = lit ? ASHGameTime.DeltaHours : 0f;
            if (dh <= 0f) { if (!lit || Time.timeScale <= 0f) Loop(null); return; }
            float heat = s.scaleWithHeat ? Mathf.Max(0.25f, _fire.Heat01) : 1f;
            int active = 0; bool food = false;
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                if (i >= _jobs.Count) continue;
                var j = _jobs[i];
                if (!j.ready)
                {
                    j.done += dh * heat; active++; if (!j.boil) food = true;
                    if (j.done >= j.total) { j.done = j.total; j.ready = true; Finish(j); }
                }
                else if (s.burnAfterGameMinutes > 0f && !j.boil)
                {
                    j.done += dh;
                    if (j.done >= j.total + s.burnAfterGameMinutes / 60f) { _jobs.Remove(j); Toast($"{j.label} burned on the fire"); }
                }
            }
            if (active > 0 && s.fuelDrawPerCookHour > 0f && _fire) _fire.ConsumeFuel(dh * s.fuelDrawPerCookHour * active);
            Loop(active == 0 ? null : food ? s.cookLoop : s.boilLoop);
        }

        void Finish(Job j)
        {
            var s = ASHCookingSettings.I;
            ASHSfx.Play("cook_done", s.doneClips, transform.position, s.sfxVolume);
            var cr = Crafter();
            if (cr && s.autoCollectRange > 0f && (cr.transform.position - transform.position).sqrMagnitude <= s.autoCollectRange * s.autoCollectRange && Deliver(j, cr.Inventory, cr)) return;
            Toast($"{j.label} is ready at the fire");
        }

        bool Deliver(Job j, ASHInventory inv, ASHCrafting cr)
        {
            var reg = ASHItemRegistry.I; var item = reg ? reg.Find(j.output) : null;
            if (!item || !inv || !inv.HasSpaceFor(item, j.amount)) return false;
            inv.AddItem(item, j.amount);
            _jobs.Remove(j);
            ASHRecipe recipe = null;
            if (cr) foreach (var r in cr.Recipes) if (r && r.recipeId == j.recipe) { recipe = r; break; }
            if (recipe) cr.NotifyCrafted(recipe); else Toast($"{j.label} added to pack");
            return true;
        }

        /// <summary>Takes every finished item that fits into the pack. Returns how many were taken.</summary>
        public int Collect(ASHInventory inv, Transform player)
        {
            var cr = player ? player.GetComponentInParent<ASHCrafting>() : null; if (!cr) cr = Crafter();
            if (!inv && cr) inv = cr.Inventory;
            int n = 0;
            foreach (var j in _jobs.ToArray()) if (j.ready && Deliver(j, inv, cr)) n++;
            if (n == 0 && ReadyCount > 0) Toast("No space in pack");
            return n;
        }

        static ASHCrafting Crafter() { if (!_crafter) _crafter = FindAnyObjectByType<ASHCrafting>(); return _crafter; }

        void Loop(AudioClip clip)
        {
            if (!clip) { if (_loop && _loop.isPlaying) _loop.Stop(); return; }
            if (!_loop)
            {
                _loop = gameObject.AddComponent<AudioSource>();
                _loop.loop = true; _loop.playOnAwake = false; _loop.spatialBlend = 1f;
                _loop.rolloffMode = AudioRolloffMode.Linear; _loop.minDistance = 1f; _loop.maxDistance = 18f;
            }
            _loop.volume = ASHCookingSettings.I.loopVolume;
            if (_loop.clip != clip) { _loop.clip = clip; _loop.Play(); }
            else if (!_loop.isPlaying) _loop.Play();
        }

        static void Toast(string m) { var h = FindAnyObjectByType<ASH.UI.ASHHud>(); if (h) h.Toast(m); }

        // ---- save (stored inside the fire's record by ASHPlacedSave) ----
        public static string Capture(ASHCampfire c)
        {
            var k = c ? c.Cooking : null;
            if (!k || k._jobs.Count == 0) return null;
            var st = new State(); st.jobs.AddRange(k._jobs);
            return JsonUtility.ToJson(st);
        }

        public static void Restore(ASHCampfire c, string json)
        {
            if (!c || string.IsNullOrEmpty(json)) return;
            var st = JsonUtility.FromJson<State>(json); if (st == null || st.jobs == null) return;
            var k = c.Cooking ? c.Cooking : c.gameObject.AddComponent<ASHFireCooking>();
            k._jobs.Clear();
            foreach (var j in st.jobs) if (j != null && !string.IsNullOrEmpty(j.output)) k._jobs.Add(j);
        }
    }
}
