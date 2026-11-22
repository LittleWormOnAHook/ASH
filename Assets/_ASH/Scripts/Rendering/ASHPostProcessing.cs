using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ASH.PostFX
{
    /// <summary>
    /// Runtime ASH post stack: creates a global Volume (priority above the scene's) with ASH_PostProfile (PC) or
    /// ASH_PostProfile_Mobile, and turns post-processing on for the main (player) camera. Nothing is saved into the scene.
    /// Profiles: Assets/_ASH/Settings/. Toggle at runtime with SetActive (graphics menu / tests).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHPostProcessing : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHPostProcessing()
        {
            Instance = default;
        }

        public VolumeProfile profile;
        public VolumeProfile mobileProfile;
        [Range(0f, 100f)] public float priority = 10f;
        public bool stackEnabled = true;

        public static ASHPostProcessing Instance { get; private set; }
        Volume _vol; Camera _cam;

        void OnEnable()
        {
            Instance = this;
            var p = Application.isMobilePlatform && mobileProfile ? mobileProfile : profile;
            if (!p) { Debug.LogWarning("[ASH] PostProcessing: no profile assigned"); return; }
            var go = new GameObject("ASH_PostVolume"); go.hideFlags = HideFlags.DontSave;
            _vol = go.AddComponent<Volume>(); _vol.isGlobal = true; _vol.priority = priority; _vol.sharedProfile = p;
            Apply();
        }

        void OnDisable() { if (_vol) Destroy(_vol.gameObject); if (Instance == this) Instance = null; }

        void LateUpdate() { var c = Camera.main; if (c != _cam) { _cam = c; Apply(); } }

        /// <summary>ASH volume on/off (the scene's own volume and camera post stay as they are).</summary>
        public void SetActive(bool on) { stackEnabled = on; Apply(); }
        /// <summary>Camera post-processing on/off (all volumes).</summary>
        public static void SetCameraPost(bool on) { var c = Camera.main; if (c) { var d = c.GetUniversalAdditionalCameraData(); if (d) d.renderPostProcessing = on; } }

        void Apply()
        {
            if (_vol) _vol.weight = stackEnabled ? 1f : 0f;
            if (stackEnabled) SetCameraPost(true);
        }
    }
}
