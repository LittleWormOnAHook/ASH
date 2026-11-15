using System.Collections.Generic;
using ASH.Gameplay;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Opening
{
    /// <summary>
    /// "Secure the cabin and prepare for worsening conditions." Runs as a priority objective on the existing
    /// ASHObjectiveTracker (HUD top-left + toasts) and listens to ASHInventory for water/food (any item that restores
    /// thirst/hunger counts - bottled water, canned food, filled canteens). The journal step completes when the
    /// journal is opened [J] after the broadcast entry was written.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHOpeningObjective : MonoBehaviour
    {
        [Range(1, 10)] public int waterNeeded = 1;
        [Range(1, 10)] public int foodNeeded = 1;
        public BoxCollider interior;

        ASHObjectiveTracker _tracker; ASHInventory _inv; UI.ASHHud _hud; Transform _player;
        int _water, _food; bool _journalRead, _active, _eased;
        public bool Active => _active;
        public bool WaterDone => _water >= waterNeeded;
        public bool FoodDone => _food >= foodNeeded;
        public bool JournalRead => _journalRead;
        public bool AllDone => WaterDone && FoodDone && _journalRead;

        public void Begin(string title, string message)
        {
            _tracker = FindAnyObjectByType<ASHObjectiveTracker>(); _inv = FindAnyObjectByType<ASHInventory>(); _hud = FindAnyObjectByType<UI.ASHHud>();
            var cc = FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>(); _player = cc ? cc.transform : null;
            _water = Have(true); _food = Have(false); _journalRead = false; _eased = false; _active = true;
            if (_inv) { _inv.ItemAdded -= OnAdded; _inv.ItemAdded += OnAdded; }
            if (_tracker) _tracker.SetPriority(title, Lines, message);
        }

        public void MarkEased() { _eased = true; if (_tracker) _tracker.SetPriority(_tracker.PriorityTitle, Lines, "The fallout is easing. Finish preparing."); }

        public void End(string message = null)
        {
            if (!_active) return; _active = false;
            if (_inv) _inv.ItemAdded -= OnAdded;
            if (_tracker) _tracker.ClearPriority(message);
        }

        void OnDisable() { if (_inv) _inv.ItemAdded -= OnAdded; }

        int Have(bool water)
        {
            int n = 0; if (!_inv) return 0;
            foreach (var s in _inv.Slots) if (!s.IsEmpty && (water ? s.item.restoreThirst > 0f : s.item.restoreHunger > 0f)) n += s.amount;
            return n;
        }

        void OnAdded(ASHItemDefinition item, int amount)
        {
            if (!_active || !item) return;
            bool before = AllDone;
            if (item.restoreThirst > 0f) { _water += amount; if (_water - amount < waterNeeded && WaterDone) Toast("Drinking water stored."); }
            if (item.restoreHunger > 0f) { _food += amount; if (_food - amount < foodNeeded && FoodDone) Toast("Food set aside."); }
            if (!before && AllDone) Toast("The cabin is as ready as it can be.");
        }

        void Toast(string m) { if (_hud) _hud.Toast(m); }

        void Update()
        {
            if (!_active) return;
            if (!_journalRead && _hud && _hud.Journal && _hud.Journal.IsOpen) { _journalRead = true; Toast("Journal checked."); }
        }

        public bool PlayerInside => interior && _player && interior.bounds.Contains(_player.position + Vector3.up * 0.5f);

        IEnumerable<(string text, bool done)> Lines()
        {
            yield return (_eased ? "Wait out the fallout" : PlayerInside ? "Stay inside until the fallout eases" : "<color=#ffb070>Get back inside the cabin</color>", _eased);
            yield return ($"Store drinking water  {Mathf.Min(_water, waterNeeded)}/{waterNeeded}", WaterDone);
            yield return ($"Set aside food  {Mathf.Min(_food, foodNeeded)}/{foodNeeded}", FoodDone);
            yield return ("Read the broadcast notes in the journal [J]", _journalRead);
        }
    }
}
