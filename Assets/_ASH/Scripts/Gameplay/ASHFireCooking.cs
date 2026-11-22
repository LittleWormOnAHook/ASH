using System;
using System.Collections.Generic;
using System.Text;
using ASH.Inventory;
using ASH.Survival;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Cooking and boiling ON a campfire or stove (added to every ASHCampfire at runtime). Batches go on the fire from the
    /// cook menu (ASHCookMenu: single-item recipes and two-item combinations) or the Survival Guide's Craft button.
    /// Each item takes REAL seconds (settings: food 5, meat 10, water 15; recipe / combo cookSeconds override), cut by the
    /// Cooking skill, slower while the fire is low on fuel, paused while it is out. Finished items go straight into the
    /// pack when the player is close, otherwise they wait at the fire. Every item cooked gives Cooking XP.
    /// Tuning: Resources/ASH_CookingSettings (ASH/Systems/Fire Cooking). Saved with the fire by ASHPlacedSave.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHFireCooking : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHFireCooking()
        {
            _crafter = default;
        }

        /// <summary>One batch. v &lt; 2 = old game-clock job from an older save (converted on restore).</summary>
        [Serializable] public sealed class Job
        {
            public string recipe; public string output; public int amount = 1; public string label;
            public float done; public float total; public bool ready; public bool boil;   // total/ready: old saves only
            public int v; public string combo; public int count; public int cooked; public float per; public float idle; public int startCount;
            /// <summary>Water container batches: % of the container used per item (refunded on cancel) and the ingredient it stood in for.</summary>
            public float bottlePct; public string bottleFor;
            public bool Cooking => count > 0;
        }
        [Serializable] sealed class State { public List<Job> jobs = new List<Job>(); }

        readonly List<Job> _jobs = new List<Job>();
        public IReadOnlyList<Job> Jobs => _jobs;
        ASHCampfire _fire; AudioSource _loop;
        static ASHCrafting _crafter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            ASHCrafting.FireCookBlock = (cr, r) => { var f = FireFor(cr); return f ? f.Block() : "Needs a lit fire nearby"; };
            ASHCrafting.FireCookStart = (cr, r) => { var f = FireFor(cr); return f && f.Begin(cr, r, 1); };
            ASHSkills.Define(ASHSkills.Cooking, () => { var s = ASHCookingSettings.I; return new ASHSkills.Curve { name = "Cooking", baseXp = s.xpBase, exponent = s.xpExponent, maxLevel = s.maxLevel }; });
            ASHSkills.LevelUp -= OnLevelUp; ASHSkills.LevelUp += OnLevelUp;
        }

        static void OnLevelUp(string id, int level)
        {
            if (id != ASHSkills.Cooking) return;
            var sb = new StringBuilder($"Cooking skill {level}: cooking {Mathf.RoundToInt((1f - ASHCookingSettings.I.SkillTimeFactor(level)) * 100f)}% faster");
            foreach (var c in ASHCookCombo.All) if (c.unlockLevel == level && !ASHSkills.Knows(c.LearnKey)) sb.Append($". Learned {c.displayName}");
            Toast(sb.ToString());
        }

        static ASHFireCooking FireFor(ASHCrafting cr)
        {
            if (!cr) return null;
            var c = ASHCampfire.NearestLit(cr.transform.position, cr.StationRange);
            if (!c) return null;
            return c.Cooking ? c.Cooking : c.gameObject.AddComponent<ASHFireCooking>();
        }

        void Awake() { _fire = GetComponent<ASHCampfire>(); }

        // ------------------------------------------------------------------ queries
        public int ReadyCount { get { int n = 0; foreach (var j in _jobs) n += j.cooked; return n; } }
        public int CookingCount { get { int n = 0; foreach (var j in _jobs) n += j.count; return n; } }
        public bool Busy => _jobs.Count > 0;

        public string ReadyLabel
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var j in _jobs) if (j.cooked > 0) { if (sb.Length > 0) sb.Append(", "); sb.Append(j.label); if (j.cooked > 1) sb.Append(" x").Append(j.cooked); }
                return sb.ToString();
            }
        }

        /// <summary>" · cooking 3: Cooked Meat 40%" for the fire prompt (empty when nothing is cooking).</summary>
        public string StatusSuffix
        {
            get
            {
                foreach (var j in _jobs)
                    if (j.Cooking) return $" · {(j.boil ? "boiling" : "cooking")} {CookingCount}: {j.label} {Mathf.FloorToInt(100f * Progress01(j))}%{(_fire && !_fire.Lit ? " (fire out)" : "")}";
                int r = ReadyCount; return r > 0 ? $" · {r} ready" : "";
            }
        }

        public static float Progress01(Job j) => j.per > 0f ? Mathf.Clamp01(j.done / j.per) : 1f;
        /// <summary>True while this batch is actually cooking (first slotsPerFire unfinished batches).</summary>
        public bool IsActive(Job j)
        {
            int slots = ASHCookingSettings.I.slotsPerFire, n = 0;
            foreach (var x in _jobs) { if (!x.Cooking) continue; if (x == j) return n < slots; n++; }
            return false;
        }

        /// <summary>Why nothing more can go on this fire right now (null = ok).</summary>
        public string Block()
        {
            var s = ASHCookingSettings.I;
            if (!_fire || !_fire.Lit) return $"Light the {(_fire ? _fire.StationName.ToLowerInvariant() : "fire")} first";
            if (_jobs.Count >= s.maxBatchesPerFire) return ReadyCount > 0 ? "Full: take the cooked food" : $"Full ({s.maxBatchesPerFire} batches)";
            if (_fire.FuelHours < s.minFuelHoursToStart) return "Needs more fuel";
            return null;
        }

        // ------------------------------------------------------------------ timing
        static bool IsWaterId(string id) => !string.IsNullOrEmpty(id) && id.Contains("water");
        static bool IsMeatId(string id) => !string.IsNullOrEmpty(id) && (id.Contains("meat") || id.Contains("fish") || id.Contains("trout") || id.Contains("rabbit") || id.Contains("venison") || id.Contains("stew") || id.Contains("chili"));

        /// <summary>Base REAL seconds per item for these items (water &gt; meat &gt; other; slowest wins), before skill and multiplier.</summary>
        public static float BaseSecondsFor(params ASHItemDefinition[] items)
        {
            var s = ASHCookingSettings.I; float t = s.secondsDefault;
            foreach (var it in items)
            {
                if (!it) continue;
                if (IsWaterId(it.itemId)) t = Mathf.Max(t, s.secondsWater);
                else if (IsMeatId(it.itemId)) t = Mathf.Max(t, s.secondsMeat);
            }
            return t;
        }

        public static float BaseSeconds(ASHRecipe r)
        {
            if (!r) return ASHCookingSettings.I.secondsDefault;
            if (r.cookSeconds > 0f) return r.cookSeconds;
            var list = new List<ASHItemDefinition>(); foreach (var g in r.ingredients) if (g != null && g.item) list.Add(g.item);
            if (r.waterPercent > 0f || IsWaterId(r.recipeId)) return Mathf.Max(BaseSecondsFor(list.ToArray()), ASHCookingSettings.I.secondsWater);
            return BaseSecondsFor(list.ToArray());
        }

        public static float BaseSeconds(ASHCookCombo c) => !c ? ASHCookingSettings.I.secondsDefault : c.cookSeconds > 0f ? c.cookSeconds : BaseSecondsFor(c.ingredientA, c.ingredientB);

        /// <summary>Final real seconds per item: base x multiplier x Cooking skill.</summary>
        public static float FinalSeconds(float baseSeconds)
        {
            var s = ASHCookingSettings.I;
            return Mathf.Max(0.1f, baseSeconds * s.cookTimeMultiplier * s.SkillTimeFactor(ASHSkills.Level(ASHSkills.Cooking)));
        }

        static bool IsBoil(ASHRecipe r) => (r.outputItem && IsWaterId(r.outputItem.itemId)) || IsWaterId(r.recipeId);

        // ------------------------------------------------------------------ water containers
        // The refillable water bottle (fill = slot condition) stands in for the water ingredient: boiling (creek water ->
        // boiled water) and combos with boiled water (stew, broth, tea). Each item cooked uses containerWaterPercentPerItem
        // of the bottle; the bottle stays in the pack. Untreated (dirty) bottle water only counts as raw water, so it has
        // to be boiled (cook slot) before it can go into a combo that needs boiled water.
        public static bool IsWaterContainer(ASHItemDefinition it) => it && it.itemId == ASHFaceProtection.BottleId;
        public static float ContainerPercent => ASHCrafting.WaterPercentAvailable == null ? 0f : ASHCrafting.WaterPercentAvailable();
        public static bool ContainerDirty => ASHFaceProtection.Instance && ASHFaceProtection.Instance.BottleDirty;
        public static float PercentPerItem => Mathf.Max(0.1f, ASHCookingSettings.I.containerWaterPercentPerItem);
        /// <summary>Items the fullest container can supply water for.</summary>
        public static int ContainerUses => Mathf.FloorToInt((ContainerPercent + 0.01f) / PercentPerItem);
        static bool IsCleanWater(ASHItemDefinition it) => it && (it.itemId.Contains("boiled") || it.itemId.Contains("bottled"));
        /// <summary>A water ingredient a container could stand in for (ignores dirty).</summary>
        public static bool IsWaterIngredient(ASHItemDefinition it) => it && !IsWaterContainer(it) && IsWaterId(it.itemId);
        /// <summary>True when the container's water may replace this ingredient now (dirty water only replaces raw water).</summary>
        public static bool ContainerCanBe(ASHItemDefinition ing) => IsWaterIngredient(ing) && (!IsCleanWater(ing) || !ContainerDirty);
        static bool UseWater(float pct) => ASHCrafting.ConsumeWater != null && ASHCrafting.ConsumeWater(pct);
        static void RefundWater(float pct) { if (pct > 0f && ASHFaceProtection.Instance) ASHFaceProtection.Instance.AddWater(pct); }

        /// <summary>Campfire recipe the container can feed (the single water ingredient of a boil recipe).</summary>
        public static ASHRecipe ContainerRecipe(ASHCrafting cr)
        {
            if (!cr) return null;
            foreach (var r in cr.Recipes)
            {
                if (!r || !r.outputItem || r.station != ASHCraftStation.Campfire || !r.CooksOnFire || !IsBoil(r)) continue;
                foreach (var g in r.ingredients) if (g != null && IsWaterIngredient(g.item) && !IsCleanWater(g.item)) return r;
            }
            return null;
        }

        /// <summary>Combo for this pair when one side is a water container (the container replaces the combo's water ingredient).</summary>
        public static ASHCookCombo FindCombo(ASHItemDefinition a, ASHItemDefinition b, out ASHItemDefinition waterFor)
        {
            waterFor = null;
            var c = ASHCookCombo.Find(a, b); if (c) return c;
            bool ca = IsWaterContainer(a), cb = IsWaterContainer(b);
            if (ca == cb || !a || !b) return null;
            var other = ca ? b : a;
            foreach (var x in ASHCookCombo.All)
            {
                if (Same(x.ingredientA, other) && IsWaterIngredient(x.ingredientB)) { waterFor = x.ingredientB; return x; }
                if (Same(x.ingredientB, other) && IsWaterIngredient(x.ingredientA)) { waterFor = x.ingredientA; return x; }
            }
            return null;
        }
        static bool Same(ASHItemDefinition x, ASHItemDefinition y) => x && y && (x == y || x.itemId == y.itemId);

        // ------------------------------------------------------------------ start
        /// <summary>Most of this recipe the pack can cook in one batch.</summary>
        public static int MaxFor(ASHCrafting cr, ASHRecipe r) => MaxFor(cr, r, false);

        /// <summary>As MaxFor; useContainer = the water ingredient comes from the water container.</summary>
        public static int MaxFor(ASHCrafting cr, ASHRecipe r, bool useContainer)
        {
            if (!cr || !cr.Inventory || !r) return 0;
            int n = ASHCookingSettings.I.maxPerBatch;
            foreach (var g in r.ingredients)
            {
                if (g == null || !g.item) return 0;
                if (useContainer && ContainerCanBe(g.item)) { n = Mathf.Min(n, ContainerUses / Mathf.Max(1, g.amount)); continue; }
                n = Mathf.Min(n, cr.Inventory.Count(g.item) / Mathf.Max(1, g.amount));
            }
            if (r.waterPercent > 0f) n = ASHCrafting.WaterPercentAvailable == null ? 0 : Mathf.Min(n, Mathf.FloorToInt((ASHCrafting.WaterPercentAvailable() + 0.01f) / r.waterPercent));
            return Mathf.Max(0, n);
        }

        public static int MaxFor(ASHInventory inv, ASHItemDefinition a, ASHItemDefinition b)
        {
            if (!inv || !a || !b) return 0;
            int n;
            if (IsWaterContainer(a) != IsWaterContainer(b)) n = Mathf.Min(ContainerUses, inv.Count(IsWaterContainer(a) ? b : a));
            else n = a == b ? inv.Count(a) / 2 : Mathf.Min(inv.Count(a), inv.Count(b));
            return Mathf.Clamp(n, 0, ASHCookingSettings.I.maxPerBatch);
        }

        public bool Begin(ASHCrafting cr, ASHRecipe r, int count) => Begin(cr, r, count, false);

        /// <summary>useContainer: the water ingredient is drawn from the water container (PercentPerItem per item).</summary>
        public bool Begin(ASHCrafting cr, ASHRecipe r, int count, bool useContainer)
        {
            if (!cr || !r || !r.outputItem || Block() != null) return false;
            count = Mathf.Min(count, MaxFor(cr, r, useContainer)); if (count <= 0) return false;
            float bottlePct = 0f; string bottleFor = null;
            for (int i = 0; i < count; i++)
            {
                if (r.waterPercent > 0f) ASHCrafting.ConsumeWater?.Invoke(r.waterPercent);
                foreach (var ing in r.ingredients)
                {
                    if (useContainer && ContainerCanBe(ing.item)) { float p = PercentPerItem * Mathf.Max(1, ing.amount); if (UseWater(p)) { bottlePct = p; bottleFor = ing.item.itemId; } continue; }
                    cr.Inventory.Remove(ing.item, ing.amount);
                }
            }
            var j = new Job { bottlePct = bottlePct, bottleFor = bottleFor, v = 2, recipe = r.recipeId, output = r.outputItem.itemId, amount = Mathf.Max(1, r.outputAmount), label = r.outputItem.displayName, boil = IsBoil(r), count = count, startCount = count, per = FinalSeconds(BaseSeconds(r)) };
            Add(j, cr);
            return true;
        }

        public bool BeginCombo(ASHCrafting cr, ASHCookCombo c, int count) => BeginCombo(cr, c, count, null);

        /// <summary>waterFor: the combo ingredient the water container replaces (null = both ingredients from the pack).</summary>
        public bool BeginCombo(ASHCrafting cr, ASHCookCombo c, int count, ASHItemDefinition waterFor)
        {
            if (!cr || !cr.Inventory || !c || !c.Valid || Block() != null) return false;
            float bottlePct = 0f;
            if (waterFor)
            {
                if (!ContainerCanBe(waterFor)) return false;
                var other = Same(c.ingredientA, waterFor) ? c.ingredientB : c.ingredientA;
                count = Mathf.Min(count, Mathf.Min(ContainerUses, cr.Inventory.Count(other)), ASHCookingSettings.I.maxPerBatch); if (count <= 0) return false;
                int used = 0; for (int i = 0; i < count; i++) if (UseWater(PercentPerItem)) used++; else break;
                count = used; if (count <= 0) return false;
                cr.Inventory.Remove(other, count); bottlePct = PercentPerItem;
            }
            else
            {
                count = Mathf.Min(count, MaxFor(cr.Inventory, c.ingredientA, c.ingredientB)); if (count <= 0) return false;
                cr.Inventory.Remove(c.ingredientA, count); cr.Inventory.Remove(c.ingredientB, count);
            }
            var j = new Job { bottlePct = bottlePct, bottleFor = waterFor ? waterFor.itemId : null, v = 2, combo = c.comboId, output = c.output.itemId, amount = c.outputAmount, label = c.output.displayName, boil = false, count = count, startCount = count, per = FinalSeconds(BaseSeconds(c)) };
            Add(j, cr);
            return true;
        }

        void Add(Job j, ASHCrafting cr)
        {
            _jobs.Add(j); _crafter = cr;
            var s = ASHCookingSettings.I;
            Toast($"{(j.boil ? "Boiling" : "Cooking")} {j.label} x{j.count}: {j.per:0.#}s each");
            ASHSfx.Play("cook_start", s.startClips, transform.position, s.sfxVolume);
        }

        // ------------------------------------------------------------------ tick
        void Update()
        {
            if (_jobs.Count == 0) { Loop(null); return; }
            var s = ASHCookingSettings.I;
            bool lit = _fire && _fire.Lit;
            float dt = lit ? Time.deltaTime : 0f;
            if (dt <= 0f) { Loop(null); return; }
            float heat = s.scaleWithHeat ? Mathf.Max(0.25f, _fire.Heat01) : 1f;
            int active = 0; bool food = false;
            for (int i = 0; i < _jobs.Count; i++)
            {
                var j = _jobs[i];
                if (j.Cooking && active < s.slotsPerFire)
                {
                    active++; if (!j.boil) food = true;
                    j.done += dt * heat;
                    while (j.count > 0 && j.done >= j.per) { j.done -= j.per; j.count--; ItemDone(j); }
                    if (j.count == 0) j.done = 0f;
                }
                if (j.cooked > 0 && !j.boil && s.burnAfterSeconds > 0f)
                {
                    j.idle += dt;
                    if (j.idle >= s.burnAfterSeconds) { Toast($"{j.label} x{j.cooked} burned on the {_fire.StationName.ToLowerInvariant()}"); j.cooked = 0; }
                }
            }
            _jobs.RemoveAll(x => x.count <= 0 && x.cooked <= 0);
            float dh = ASHGameTime.DeltaHours;
            if (active > 0 && s.fuelDrawPerCookHour > 0f && dh > 0f) _fire.ConsumeFuel(dh * s.fuelDrawPerCookHour * active);
            Loop(active == 0 ? null : food ? s.cookLoop : s.boilLoop);
        }

        void ItemDone(Job j)
        {
            var s = ASHCookingSettings.I;
            int lvl = ASHSkills.Level(ASHSkills.Cooking);
            int units = Mathf.Max(1, j.amount) + (UnityEngine.Random.value < s.SkillBonusChance(lvl) ? 1 : 0);
            j.cooked += units; j.idle = 0f;
            ASHSkills.AddXp(ASHSkills.Cooking, string.IsNullOrEmpty(j.combo) ? s.xpPerItem : s.xpPerItem + s.xpPerComboItem);
            ASHSfx.Play("cook_done", s.doneClips, transform.position, s.sfxVolume);
            var cr = Crafter();
            if (cr && s.autoCollectRange > 0f && (cr.transform.position - transform.position).sqrMagnitude <= s.autoCollectRange * s.autoCollectRange && Deliver(j, cr.Inventory, cr) > 0) return;
            if (j.count == 0) Toast($"{j.label} x{j.cooked} ready at the {(_fire ? _fire.StationName.ToLowerInvariant() : "fire")}");
        }

        /// <summary>Moves as many finished items of this batch as fit into the pack. Returns how many.</summary>
        int Deliver(Job j, ASHInventory inv, ASHCrafting cr)
        {
            var reg = ASHItemRegistry.I; var item = reg ? reg.Find(j.output) : null;
            if (!item || !inv || j.cooked <= 0) return 0;
            int n = 0;
            while (j.cooked > 0 && inv.HasSpaceFor(item, 1) && inv.AddItem(item, 1) > 0) { j.cooked--; n++; }
            if (n == 0) return 0;
            ASHRecipe recipe = null;
            if (cr && !string.IsNullOrEmpty(j.recipe)) foreach (var r in cr.Recipes) if (r && r.recipeId == j.recipe) { recipe = r; break; }
            if (recipe) { int crafts = Mathf.Max(1, n / Mathf.Max(1, recipe.outputAmount)); for (int i = 0; i < crafts; i++) cr.NotifyCrafted(recipe); }
            else Toast($"{j.label} x{n} added to pack");
            return n;
        }

        /// <summary>Takes every finished item that fits into the pack. Returns how many were taken.</summary>
        public int Collect(ASHInventory inv, Transform player)
        {
            var cr = player ? player.GetComponentInParent<ASHCrafting>() : null; if (!cr) cr = Crafter();
            if (!inv && cr) inv = cr.Inventory;
            int n = 0;
            foreach (var j in _jobs.ToArray()) n += Deliver(j, inv, cr);
            _jobs.RemoveAll(x => x.count <= 0 && x.cooked <= 0);
            if (n == 0 && ReadyCount > 0) Toast("No space in pack");
            return n;
        }

        /// <summary>Takes a batch's uncooked items back into the pack (combos give back both ingredients).</summary>
        public bool Cancel(Job j, ASHCrafting cr)
        {
            if (j == null || !_jobs.Contains(j) || !cr || !cr.Inventory) return false;
            var inv = cr.Inventory; int back = j.count;
            if (back > 0)
            {
                bool fromBottle = !string.IsNullOrEmpty(j.bottleFor);
                if (fromBottle) RefundWater(j.bottlePct * back);
                if (!string.IsNullOrEmpty(j.combo))
                {
                    var c = ASHCookCombo.ById(j.combo);
                    if (c)
                    {
                        if (!(fromBottle && c.ingredientA && c.ingredientA.itemId == j.bottleFor)) inv.AddItem(c.ingredientA, back);
                        if (!(fromBottle && c.ingredientB && c.ingredientB.itemId == j.bottleFor)) inv.AddItem(c.ingredientB, back);
                    }
                }
                else
                {
                    ASHRecipe recipe = null; foreach (var r in cr.Recipes) if (r && r.recipeId == j.recipe) { recipe = r; break; }
                    if (recipe) foreach (var g in recipe.ingredients) if (g != null && g.item && !(fromBottle && g.item.itemId == j.bottleFor)) inv.AddItem(g.item, g.amount * back); // recipe waterPercent is not refunded
                }
                j.count = 0; j.done = 0f;
            }
            Deliver(j, inv, cr);
            _jobs.RemoveAll(x => x.count <= 0 && x.cooked <= 0);
            return true;
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
            foreach (var j in st.jobs)
            {
                if (j == null || string.IsNullOrEmpty(j.output)) continue;
                if (j.v < 2) // old game-clock job: one item, keep its progress ratio
                {
                    float ratio = j.total > 0f ? Mathf.Clamp01(j.done / j.total) : 1f;
                    j.v = 2; j.amount = Mathf.Max(1, j.amount);
                    j.per = FinalSeconds(IsWaterId(j.output) ? ASHCookingSettings.I.secondsWater : IsMeatId(j.output) ? ASHCookingSettings.I.secondsMeat : ASHCookingSettings.I.secondsDefault);
                    if (j.ready) { j.count = 0; j.cooked = j.amount; j.done = 0f; } else { j.count = 1; j.cooked = 0; j.done = ratio * j.per; }
                    j.startCount = 1;
                }
                if (j.count <= 0 && j.cooked <= 0) continue;
                if (j.per <= 0f) j.per = FinalSeconds(ASHCookingSettings.I.secondsDefault);
                k._jobs.Add(j);
            }
        }
    }
}
