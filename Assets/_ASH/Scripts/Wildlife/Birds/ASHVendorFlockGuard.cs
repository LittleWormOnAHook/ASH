using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.Wildlife.Birds
{
    /// <summary>
    /// Console fix: a Bird Flocks bird (FlockChild) placed straight into a scene has no FlockController (_spawner), so the vendor
    /// FlockChild.Update throws a NullReferenceException every frame (46,635 in one session, from the 'Seagull' prefab in ASH_Prologue).
    /// On each scene load, before Start runs, any FlockChild without a spawner gets an inert, inactive FlockController as its spawner and
    /// is switched off; the model stays where it was placed. Reflection only, so ASH compiles without the vendor pack. Birds spawned by a
    /// FlockController (sky flocks, roosts) always have a spawner and are never touched.
    /// </summary>
    static class ASHVendorFlockGuard
    {
        static Type _childType, _ctrlType;
        static FieldInfo _fSpawner, _fAmount;
        static GameObject _holder;
        static Component _inert;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _holder = null; _inert = null; SceneManager.sceneLoaded -= OnLoaded; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            Sweep();
        }

        static void OnLoaded(Scene s, LoadSceneMode m) => Sweep();

        static bool Resolve()
        {
            if (_childType != null) return _fSpawner != null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_childType == null) _childType = asm.GetType("FlockChild", false);
                if (_ctrlType == null) _ctrlType = asm.GetType("FlockController", false);
                if (_childType != null && _ctrlType != null) break;
            }
            if (_childType == null || _ctrlType == null) { _childType = typeof(void); return false; }
            _fSpawner = _childType.GetField("_spawner", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _fAmount = _ctrlType.GetField("_childAmount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return _fSpawner != null;
        }

        static void Sweep()
        {
            if (!Resolve()) return;
            foreach (var b in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                if (!b || b.GetType() != _childType) continue;
                if (_fSpawner.GetValue(b) is UnityEngine.Object sp && sp) continue;
                var inert = Inert();
                if (!inert) return;
                _fSpawner.SetValue(b, inert);
                b.enabled = false; // FlockChild.OnDisable decrements the inert controller's count instead of throwing
                Debug.Log($"[ASH] Birds: vendor bird '{b.name}' is in the scene without a FlockController, so it can't fly; switched off to stop " +
                          "a NullReferenceException every frame. Delete it from the scene (sky flocks and roosts spawn their own birds).", b);
            }
        }

        static Component Inert()
        {
            if (_inert) return _inert;
            // inactive holder: AddComponent on an inactive object runs no Awake/Start, so the vendor controller never spawns birds
            _holder = new GameObject("ASH_InertFlockController") { hideFlags = HideFlags.HideInHierarchy };
            _holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_holder);
            _inert = _holder.AddComponent(_ctrlType);
            if (_inert && _fAmount != null) _fAmount.SetValue(_inert, 0);
            return _inert;
        }
    }
}
