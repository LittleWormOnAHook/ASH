using System;
using System.Collections.Generic;
using ASH.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.Inventory
{
    /// <summary>
    /// Grandpa's Field Manual state on the player: no crafting at all until Grandpa's journal is picked up and its "Survival Guide"
    /// cover read; then the manual tab appears in the journal. Recipes unlock from ASHFieldManualData (basics on read; first pickup
    /// of an item, crafting an item N times, finding torn pages), with a toast and a "new" badge. Gates ASHCrafting and injects
    /// the manual's recipes into it. Added automatically next to ASHCrafting when Resources/ASH_FieldManual exists. Saved.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHFieldManual : MonoBehaviour, IASHSaveable
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHFieldManual()
        {
            Instance = default;
            ShowCover = null;
            _noConds.Clear();
        }

        public static ASHFieldManual Instance { get; private set; }
        public ASHFieldManualData Data { get; private set; }
        public bool IsRead { get; private set; }
        public int PagesFound => _pages.Count;
        public bool HasPage(int n) => _pages.Contains(n);
        /// <summary>Found torn pages in order (the page reader flips through these).</summary>
        public List<int> FoundPages { get { var l = new List<int>(_pages); l.Sort(); return l; } }
        public bool IsPageUnread(int n) => _pages.Contains(n) && !_pagesRead.Contains(n);
        public int UnreadPages { get { int c = 0; foreach (var p in _pages) if (!_pagesRead.Contains(p)) c++; return c; } }
        public void MarkPageRead(int n) { if (_pages.Contains(n) && _pagesRead.Add(n)) Changed?.Invoke(); }
        /// <summary>Only recipes in the Survival Guide tree can unlock; anything else stays hidden and uncraftable.</summary>
        public bool IsUnlocked(ASHRecipe r) => r && IsRead && _unlocked.Contains(Key(r));
        public bool IsNew(ASHRecipe r) => r && _unlocked.Contains(Key(r)) && !_seen.Contains(Key(r));
        public int NewCount { get { int n = 0; foreach (var k in _unlocked) if (!_seen.Contains(k)) n++; return n; } }
        public int CraftedCount(ASHItemDefinition it) => it && _crafted.TryGetValue(it.itemId, out var n) ? n : 0;
        public bool PickedUpEver(ASHItemDefinition it) => it && _firstPickups.Contains(it.itemId);
        public bool PickedUpEver(string itemId) => !string.IsNullOrEmpty(itemId) && _firstPickups.Contains(itemId);
        /// <summary>Grandpa's journal was found (picked up or read): its HUD icon shows. Old saves with it in the pack count.</summary>
        public bool JournalFound => IsRead || (Data && Data.journalItem && (PickedUpEver(Data.journalItem) || (_inv && _inv.Count(Data.journalItem) > 0)));

        /// <summary>(recipe) newly unlocked.</summary>
        public event Action<ASHRecipe> Unlocked;
        public event Action<int> PageFound;
        public event Action ManualRead;
        public event Action Changed;
        /// <summary>Opens the cover view (set by the UI); the manual calls it when the journal is picked up.</summary>
        public static Action<ASHFieldManual> ShowCover;

        ASHCrafting _craft; ASHInventory _inv;
        readonly HashSet<string> _firstPickups = new HashSet<string>(), _unlocked = new HashSet<string>(), _seen = new HashSet<string>();
        readonly HashSet<int> _pages = new HashSet<int>();
        readonly HashSet<int> _pagesRead = new HashSet<int>();
        readonly Dictionary<string, int> _crafted = new Dictionary<string, int>();

        static string Key(ASHRecipe r) => string.IsNullOrEmpty(r.recipeId) ? r.name : r.recipeId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        /// <summary>Also attach after the first scene loads: with Enter Play Mode Options (no scene reload) sceneLoaded never fires on Play.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachAfterLoad() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (!ASHFieldManualData.I) return;
            var c = FindAnyObjectByType<ASHCrafting>(); if (!c || c.GetComponent<ASHFieldManual>()) return;
            c.gameObject.AddComponent<ASHFieldManual>();
        }

        void Awake()
        {
            Instance = this; Data = ASHFieldManualData.I;
            _craft = GetComponent<ASHCrafting>(); _inv = _craft ? _craft.Inventory : GetComponent<ASHInventory>();
            if (!_inv) _inv = FindAnyObjectByType<ASHInventory>();
            if (_craft && Data) foreach (var e in Data.entries) if (e != null && e.recipe) _craft.AddRecipe(e.recipe);
            // anything already in the pack at start counts as picked up (starting kit)
            if (_inv) foreach (var sl in _inv.Slots) if (!sl.IsEmpty) _firstPickups.Add(sl.item.itemId);
        }

        void OnEnable()
        {
            ASHSaveSystem.Register(this);
            ASHCrafting.RecipeGate = Gate;
            UI.ASHHud.CraftingAvailable = () => IsRead;
            UI.ASHHud.CraftingLockedMessage = Data ? Data.lockedMessage : null;
            UI.ASHHud.CraftTabName = Data ? Data.tabName : null;
            if (_inv) { _inv.ItemAdded += OnItemAdded; _inv.Changed += StowJournal; }
            if (_craft) _craft.Crafted += OnCrafted;
            ASHUpgrades.Changed += OnUpgradeChanged;
        }

        /// <summary>Once read, the journal is a permanent tool (J / its HUD icon), not a pack item: any copy in the pack is removed.
        /// Torn pages still go into it. Also cleans old saves that kept the read journal in the pack.</summary>
        void StowJournal()
        {
            if (!IsRead || !_inv || !Data || !Data.journalItem || _inv.Count(Data.journalItem) <= 0) return;
            _firstPickups.Add(Data.journalItem.itemId);
            _inv.RemoveAll(Data.journalItem);
        }

        void OnDisable()
        {
            ASHSaveSystem.Unregister(this);
            if (ASHCrafting.RecipeGate == (Func<ASHRecipe, string>)Gate) ASHCrafting.RecipeGate = null;
            UI.ASHHud.CraftingAvailable = null; UI.ASHHud.CraftingLockedMessage = null; UI.ASHHud.CraftTabName = null;
            if (_inv) { _inv.ItemAdded -= OnItemAdded; _inv.Changed -= StowJournal; }
            if (_craft) _craft.Crafted -= OnCrafted;
            ASHUpgrades.Changed -= OnUpgradeChanged;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        string Gate(ASHRecipe r)
        {
            if (!IsRead) return "No guide yet";
            if (!IsUnlocked(r)) return "Not in the manual yet";
            return null;
        }

        void OnItemAdded(ASHItemDefinition item, int n)
        {
            if (!item) return;
            bool first = _firstPickups.Add(item.itemId);
            if (Data && item == Data.journalItem && !IsRead) ShowCover?.Invoke(this);
            if (first) Evaluate();
        }

        void OnUpgradeChanged() { Evaluate(); Changed?.Invoke(); }

        void OnCrafted(ASHRecipe r)
        {
            if (r && r.outputItem)
            {
                _crafted.TryGetValue(r.outputItem.itemId, out var n); _crafted[r.outputItem.itemId] = n + r.outputAmount;
            }
            Evaluate();
        }

        /// <summary>The cover was read: the manual opens, basics unlock.</summary>
        public void Read()
        {
            if (IsRead) return;
            IsRead = true;
            Evaluate(true);
            StowJournal();
            var hud = FindAnyObjectByType<UI.ASHHud>();
            if (hud) hud.Toast($"{(Data ? Data.tabName : "Field manual")} added to your journal  [{"Q"}]");
            ASH.Map.ASHJournalLog.Add("Grandpa's Field Manual", Data ? Data.coverNote : "Grandpa's survival notes.", "fm_cover");
            ManualRead?.Invoke(); Changed?.Invoke();
        }

        /// <summary>A torn page was found (page pickups). Counts even before the manual was read.</summary>
        public bool FindPage(int n)
        {
            if (n < 1 || !_pages.Add(n)) return false;
            var hud = FindAnyObjectByType<UI.ASHHud>();
            int total = Data ? Data.totalPages : 12;
            var pg = Data ? Data.Page(n) : null;
            if (hud) hud.Toast($"Found a torn page: Page {n} of {total}" + (pg != null && !string.IsNullOrEmpty(pg.title) ? $" - {pg.title}" : "") + (IsRead ? "  (added to the Survival Guide)" : "  (tucked into the journal)"));
            if (pg != null && !string.IsNullOrEmpty(pg.text)) ASH.Map.ASHJournalLog.Add($"Manual page {n}: {pg.title}", pg.text, "fm_page_" + n);
            PageFound?.Invoke(n);
            Evaluate(); Changed?.Invoke();
            return true;
        }

        public void MarkSeen(ASHRecipe r) { if (r && _seen.Add(Key(r))) Changed?.Invoke(); }

        bool Met(ASHUnlockCondition c)
        {
            switch (c.kind)
            {
                case ASHUnlockKind.FirstPickup: return c.item && _firstPickups.Contains(c.item.itemId);
                case ASHUnlockKind.Crafted: return c.item && CraftedCount(c.item) >= Mathf.Max(1, c.quantity);
                case ASHUnlockKind.ManualPage: return _pages.Contains(c.page);
                case ASHUnlockKind.UpgradeTier: return !string.IsNullOrEmpty(c.upgradeId) && ASHUpgrades.Tier(c.upgradeId) >= Mathf.Max(1, c.upgradeTier);
                case ASHUnlockKind.JournalPage: return ASH.Prologue.ASHGrandpaJournal.Has(c.collection, c.pageId);
            }
            return false;
        }

        public bool ConditionMet(ASHUnlockCondition c) => c != null && Met(c);

        /// <summary>Re-check unlocks now (story journal pages call this).</summary>
        public void Reevaluate() { Evaluate(); Changed?.Invoke(); }
        /// <summary>Basic = unlocked when the manual is read. ASH_PrologueSettings (overrideBasics) replaces the manual's own flags without editing the manual asset.</summary>
        public static bool IsBasic(ASHFieldManualEntry e) { if (e == null) return false; var s = ASH.Prologue.ASHPrologueSettings.I; return s && s.overrideBasics ? s.basicRecipes.Contains(e.recipe) : e.basic; }
        static readonly List<ASHUnlockCondition> _noConds = new List<ASHUnlockCondition>();
        public static List<ASHUnlockCondition> Conditions(ASHFieldManualEntry e) { if (e == null) return _noConds; var s = ASH.Prologue.ASHPrologueSettings.I; var o = s && s.overrideBasics ? s.Override(e.recipe) : null; return o != null ? o.conditions : e.conditions; }
        public static bool RequireAll(ASHFieldManualEntry e) { if (e == null) return true; var s = ASH.Prologue.ASHPrologueSettings.I; var o = s && s.overrideBasics ? s.Override(e.recipe) : null; return o != null ? o.requireAll : e.requireAll; }

        public string ConditionText(ASHUnlockCondition c)
        {
            if (c == null) return "";
            switch (c.kind)
            {
                case ASHUnlockKind.FirstPickup: return $"Find {(c.item ? c.item.displayName : "?")}";
                case ASHUnlockKind.Crafted: return c.quantity > 1 ? $"Craft {c.quantity} {(c.item ? c.item.displayName : "?")}  ({Mathf.Min(CraftedCount(c.item), c.quantity)}/{c.quantity})" : $"Craft {(c.item ? c.item.displayName : "?")}";
                case ASHUnlockKind.ManualPage: return $"Find manual page {c.page}";
                case ASHUnlockKind.JournalPage: { var col = ASH.Prologue.ASHJournalCollection.Get(c.collection); var pg = col ? col.Page(c.pageId) : null; return $"Find {(col ? col.title : "journal")} page {(pg != null ? pg.number + " (" + pg.title + ")" : c.pageId)}"; }
                case ASHUnlockKind.UpgradeTier: return $"Craft Ammo Pouch tier {Mathf.Max(1, c.upgradeTier)}" + (string.IsNullOrEmpty(c.upgradeId) ? "" : $"  ({ASHUpgrades.Tier(c.upgradeId)}/{Mathf.Max(1, c.upgradeTier)})");
            }
            return "";
        }

        public string Hint(ASHFieldManualEntry e)
        {
            if (e == null) return "";
            if (!string.IsNullOrEmpty(e.hint)) return e.hint;
            if (IsBasic(e)) return "Read the manual";
            var parts = new List<string>(); foreach (var c in Conditions(e)) parts.Add(ConditionText(c));
            return string.Join(RequireAll(e) ? " + " : " or ", parts);
        }

        void Evaluate(bool silent = false)
        {
            if (!IsRead || !Data) return;
            foreach (var e in Data.entries)
            {
                if (e == null || !e.recipe || _unlocked.Contains(Key(e.recipe))) continue;
                bool ok;
                var conds = Conditions(e); if (IsBasic(e)) ok = true;
                else if (conds.Count == 0) ok = false;
                else if (RequireAll(e)) { ok = true; foreach (var c in conds) if (!Met(c)) { ok = false; break; } }
                else { ok = false; foreach (var c in conds) if (Met(c)) { ok = true; break; } }
                if (!ok) continue;
                _unlocked.Add(Key(e.recipe));
                if (IsBasic(e) && silent) continue; // the basics arrive with the manual (badged "new", no toast spam)
                var hud = FindAnyObjectByType<UI.ASHHud>();
                if (hud) hud.Toast($"New in Grandpa's manual: {e.recipe.displayName}");
                Unlocked?.Invoke(e.recipe);
            }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ save
        [Serializable] sealed class Count { public string id; public int n; }
        [Serializable] sealed class State { public bool read; public List<string> pickups = new List<string>(), unlocked = new List<string>(), seen = new List<string>(); public List<int> pages = new List<int>(), pagesRead = new List<int>(); public List<Count> crafted = new List<Count>(); }
        public string SaveKey => "fieldManual";
        public int RestoreOrder => 20;
        public string CaptureJson()
        {
            var s = new State { read = IsRead, pickups = new List<string>(_firstPickups), unlocked = new List<string>(_unlocked), seen = new List<string>(_seen), pages = new List<int>(_pages), pagesRead = new List<int>(_pagesRead) };
            foreach (var kv in _crafted) s.crafted.Add(new Count { id = kv.Key, n = kv.Value });
            return JsonUtility.ToJson(s);
        }
        public void RestoreJson(string json)
        {
            var s = JsonUtility.FromJson<State>(json); if (s == null) return;
            IsRead = s.read;
            _firstPickups.Clear(); foreach (var x in s.pickups) _firstPickups.Add(x);
            _unlocked.Clear(); foreach (var x in s.unlocked) _unlocked.Add(x);
            _seen.Clear(); foreach (var x in s.seen) _seen.Add(x);
            _pages.Clear(); foreach (var x in s.pages) _pages.Add(x);
            _pagesRead.Clear(); if (s.pagesRead != null) foreach (var x in s.pagesRead) _pagesRead.Add(x);
            _crafted.Clear(); foreach (var c in s.crafted) _crafted[c.id] = c.n;
            // found pages are gone from the world
            foreach (var p in FindObjectsByType<ASH.Interaction.ASHManualPagePickup>()) if (_pages.Contains(p.pageNumber)) Destroy(p.gameObject);
            Evaluate(true);
            StowJournal();
            Changed?.Invoke();
        }
    }
}
