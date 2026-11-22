using System;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Fuel-free world lights (lanterns, lamps, table flashlights): E to toggle, start-off, flicker, warmth when on.
    /// Resources/ASH_LightToggleSettings. Editable in Play; edits are kept.</summary>
    [CreateAssetMenu(menuName = "ASH/Gameplay/Light Toggle Settings", fileName = "ASH_LightToggleSettings")]
    public sealed class ASHLightToggleSettings : ScriptableObject
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _i = default; }

        [Header("Interaction")]
        [Tooltip("How far the player can reach to toggle (metres).")]
        [Range(0.5f, 12f)] public float useRange = 3f;
        [Tooltip("Higher wins when two usables overlap. Campfires are 30.")]
        [Range(0f, 80f)] public float usePriority = 25f;
        [Tooltip("{0} is the light's short name.")]
        public string promptOn = "Turn on {0}";
        public string promptOff = "Turn off {0}";

        [Header("Start state")]
        [Tooltip("New lights start off unless a save restored them on, or the component's Start On is ticked.")]
        public bool defaultStartOn;

        [Header("Look")]
        [Range(0f, 1f)] public float flicker = 0.15f;
        [Range(0.1f, 20f)] public float flickerSpeed = 6f;
        [Tooltip("Dim or boost emission on the mesh when the light is on. 1 = the material's own emission.")]
        [Range(0f, 4f)] public float emissionWhenOn = 1f;

        [Header("Audio")]
        [Range(0f, 1f)] public float sfxVolume = 0.7f;
        [Tooltip("Optional. Empty = ASH sound bank lantern (or flashlight) clicks.")]
        public AudioClip[] onClips;
        public AudioClip[] offClips;

        [Header("Warmth")]
        [Tooltip("Add a lantern heat source if the object has none. Existing heat is left as-is except Require Light is turned on.")]
        public bool addWarmth = true;
        [Tooltip("Skip warmth on names that look like flashlights.")]
        public bool warmthOnFlashlights;

        [Header("Auto attach (runtime, no scene save)")]
        [Tooltip("Scene objects whose names contain these (no spaces, case ignored) get a toggle, unless they are a campfire, a held light, or the player.")]
        public string[] autoAttachNamePatterns = { "hanginglantern", "ash_hanginglantern", "lantern_on" };
        [Tooltip("Also attach to these exact scene names.")]
        public string[] autoAttachExactNames = { "ASH_HangingLantern (swings)" };

        [Header("Cabin stove (new game)")]
        [Tooltip("Parent / object names that count as Grandpa's cabin.")]
        public string[] cabinNamePatterns = { "grandpa", "granpa", "granpas", "cabin" };
        [Tooltip("Stove / furnace names inside the cabin.")]
        public string[] cabinStoveNamePatterns = { "furnace", "stove", "woodburner", "wood stove" };
        [Tooltip("If the furnace isn't under a cabin-named parent, still include cook stations this close to the hanging lantern or cabin (metres).")]
        [Range(0f, 40f)] public float cabinStoveRadius = 14f;
        [Tooltip("New games put the cabin stove out and empty, so it needs wood or kindling. Loaded saves keep their own fuel and lit state.")]
        public bool cabinStoveStartsEmpty = true;

        [Header("Interact collider")]
        [Tooltip("Added only when the light has no collider of its own (the hanging lantern's collider was removed). Not moved.")]
        public Vector3 fallbackColliderSize = new Vector3(0.45f, 0.7f, 0.45f);

        static ASHLightToggleSettings _i;
        public static ASHLightToggleSettings I
        {
            get
            {
                if (!_i) { _i = Resources.Load<ASHLightToggleSettings>("ASH_LightToggleSettings"); if (!_i) _i = CreateInstance<ASHLightToggleSettings>(); }
                return _i;
            }
        }
    }
}
