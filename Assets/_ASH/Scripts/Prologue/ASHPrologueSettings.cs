using System;
using System.Collections.Generic;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Prologue
{
    /// <summary>Recipe unlock override used while the prologue owns the basics (does not touch ASH_FieldManual.asset).</summary>
    [Serializable]
    public sealed class ASHRecipeUnlockOverride
    {
        public ASHRecipe recipe;
        [Tooltip("All conditions needed (otherwise any one).")] public bool requireAll = false;
        public List<ASHUnlockCondition> conditions = new List<ASHUnlockCondition>();
    }

    /// <summary>
    /// M1 prologue tuning (Resources/ASH_PrologueSettings, written by ASH/Systems/Prologue/Set Up M1 Opening). The asset is read
    /// directly at runtime (no copies), so edits made in Edit or Play mode are live and stay in the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Prologue/Prologue Settings", fileName = "ASH_PrologueSettings")]
    public sealed class ASHPrologueSettings : ScriptableObject
    {
        [Header("Start")]
        [Tooltip("New games start seated in the arrival car with the dog in the passenger seat (M1). Off = the old cabin opening (dev/debug).")]
        public bool startInCar = true;
        [Tooltip("Debug/test only: run the old slice objective chain (Get your bearings ...) after the opening.")]
        public bool useLegacySliceChain = false;
        [Tooltip("Car start: also run the old opening objective 'Secure the cabin' after the quake (off = the prologue objectives only).")]
        public bool legacySecureCabin = false;
        [Tooltip("Car start, new game only: skip the player's Inspector starting items, so the kit is Grandpa's supplies (sparse ammo). Off = keep them (testing).")]
        public bool prologueKitOnly = true;
        [Header("Weather before the quake (t442)")]
        [Tooltip("Car start: no ash before the first quake. Arrival, drive, parking, Home Again and the warning keep ash fall, ground ash build-up and ash sky tint at 0. Off = debug (whatever the weather does).")]
        public bool noAshBeforeQuake = true;

        [Header("Cabin door + Grandpa's key (t462)")]
        [Tooltip("The cabin door starts locked; trying it adds 'Find Grandpa's key'. The key (world pickup) unlocks and opens it. Auto-unlocks when the quake warning starts (no softlock).")]
        public bool lockCabinUntilKey = true;
        public string keyItemId = "grandpas_key";
        public string doorLockedMessage = "Locked.";
        [Tooltip("Weather snapped to if ash shows up before the quake (non-ash weather only).")]
        public ASH.World.Weather.ASHWeatherKind preQuakeWeather = ASH.World.Weather.ASHWeatherKind.LightSnowMorning;
        [Tooltip("Car-start radio news (subtitles), shown one after another while driving to the cabin.")]
        [TextArea(1, 3)] public List<string> arrivalRadio = new List<string>
        {
            "RADIO: ...continued seismic activity across the region overnight. Officials say there is no cause for alarm.",
            "RADIO: The survey office reports ground deformation near the caldera of several centimetres this week.",
            "RADIO: Residents are reminded to keep an emergency kit: water, food, medicine and a radio.",
            "RADIO: ...caldera unrest remains at advisory level. We'll keep you updated. Now, the weather..."
        };
        [Range(2f, 60f)] public float arrivalRadioInterval = 9f;
        [Range(0f, 30f)] public float arrivalRadioDelay = 3f;

        [Header("P00 Returning home")]
        [Tooltip("The car counts as parked when its centre is this close to the parking zone, metres.")] [Range(1f, 40f)] public float parkingRadius = 7f;
        [Tooltip("Walking into the cabin also finishes P00 (the car gets moved into the zone later, out of sight).")] public bool cabinEntryCompletesArrival = true;

        [Header("P02 Seek shelter")]
        [Tooltip("Seconds from SEEK SHELTER NOW to the quake.")] [Range(10f, 300f)] public float warningSeconds = 75f;
        [Tooltip("Retry from the checkpoint: the warning fires again this long after the load.")] [Range(0.2f, 10f)] public float retryRefireDelay = 1.5f;
        [Tooltip("Quake intensity (fraction of its peak) at which anyone outside a structural shelter dies.")] [Range(0.3f, 1f)] public float killAtIntensity = 0.85f;
        [Tooltip("Seconds the player has to be out of the cabin before the warning fires.")] [Range(0f, 10f)] public float exitConfirmSeconds = 0.6f;
        public string warningBanner = "SEEK SHELTER NOW";
        [TextArea(1, 3)] public string warningSubtitle = "EMERGENCY ALERT: Major seismic event imminent. Take shelter in a solid building immediately.";
        [TextArea(1, 3)] public string deathOutsideToast = "You were caught outside when the ground tore open.";
        [Tooltip("Checkpoint slot written right before the warning.")] public string checkpointSlot = "prologue_checkpoint";
        public string saveBlockedToast = "You can't save right now. Get to shelter.";

        [Header("Falling tree")]
        [Range(0.5f, 8f)] public float treeFallSeconds = 2.2f;
        [Tooltip("Camera cut to the car while the tree falls (seconds). 0 = no cut.")] [Range(0f, 12f)] public float cinematicSeconds = 4.5f;
        [Tooltip("Delay after the kill check before the tree starts falling.")] [Range(0f, 5f)] public float treeDelay = 0.4f;

        [Header("P03 No way back")]
        [TextArea(1, 3)] public string carInspectText = "A pine has crushed the roof. The car isn't going anywhere.";
        [Tooltip("Getting this close to Grandpa's truck (or reading journal page 2) counts as finding it.")] [Range(2f, 40f)] public float truckFoundRadius = 9f;
        public string truckVehicleId = "pickup";
        public string arrivalVehicleId = "car";
        [Tooltip("Aftershocks end once the truck objective starts (the legacy slice area never comes in the prologue).")] public bool endAftershocksOnTruckObjective = true;

        [Header("Grandpa's supplies (exact contents)")]
        public List<ASHRecipeIngredient> supplies = new List<ASHRecipeIngredient>();

        [Header("Crafting basics (overrides the Field Manual's basic flags; the manual asset is left untouched)")]
        public bool overrideBasics = true;
        public List<ASHRecipe> basicRecipes = new List<ASHRecipe>();
        [Tooltip("Former basics (and anything else) with prologue unlock conditions.")]
        public List<ASHRecipeUnlockOverride> unlockOverrides = new List<ASHRecipeUnlockOverride>();

        public ASHRecipeUnlockOverride Override(ASHRecipe r)
        {
            if (!r || unlockOverrides == null) return null;
            foreach (var o in unlockOverrides) if (o != null && o.recipe == r) return o;
            return null;
        }

        static ASHPrologueSettings _i;
        public static ASHPrologueSettings I { get { if (!_i) _i = Resources.Load<ASHPrologueSettings>("ASH_PrologueSettings"); return _i; } }
    }
}
