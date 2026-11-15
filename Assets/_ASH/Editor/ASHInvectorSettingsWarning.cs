using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Hides Invector's Scene-view overlay "Missing ProjectSettings / Go to the Menu Invector/Import ProjectSettings".
    /// Invector's check (vCheckForProjectSettings.Validation) wants the "Player" layer at index 8 and its legacy InputManager axes;
    /// ASH keeps Player on layer 13 (8 is a Gaia layer) and uses the Input System, so the check can never pass without breaking
    /// ASH's layers/inputs, and Invector's Import ProjectSettings would overwrite ASH's TagManager/Input/Physics. Do NOT run it.
    /// This removes the overlay's Scene-view callback (vendor code untouched) right away on every domain reload (also entering
    /// Play), and marks the message closed, so it never flashes. Toggle: ASH/Settings/Show Invector ProjectSettings Warning.
    /// </summary>
    [InitializeOnLoad]
    public static class ASHInvectorSettingsWarning
    {
        const string Pref = "ASH_ShowInvectorProjectSettingsWarning";
        const string MenuPath = "ASH/Settings/Show Invector ProjectSettings Warning";
        static Action<SceneView> _cb;

        static ASHInvectorSettingsWarning()
        {
            Apply(); // immediately: no frame with the overlay registered
            EditorApplication.delayCall += Apply;
            EditorApplication.playModeStateChanged += _ => Apply();
        }

        static Type Invector => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Invector.vCharacterController.vCheckForProjectSettings", false)).FirstOrDefault(x => x != null);

        static void Apply()
        {
            var t = Invector;
            if (t == null) return;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle); // make sure Invector registered first
            bool show = EditorPrefs.GetBool(Pref, false);
            var closed = t.GetField("isClosed", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (closed != null) closed.SetValue(null, !show);
            var m = t.GetMethod("OnScene", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (m == null) return;
            _cb ??= (Action<SceneView>)Delegate.CreateDelegate(typeof(Action<SceneView>), m);
            SceneView.duringSceneGui -= _cb;
            if (show) SceneView.duringSceneGui += _cb;
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, priority = 200)]
        static void Toggle() { EditorPrefs.SetBool(Pref, !EditorPrefs.GetBool(Pref, false)); Apply(); }
        [MenuItem(MenuPath, true)]
        static bool ToggleValidate() { Menu.SetChecked(MenuPath, EditorPrefs.GetBool(Pref, false)); return true; }

        /// <summary>For the harness: true when the Invector overlay is not hooked into the Scene view.</summary>
        public static bool Hidden()
        {
            Apply();
            if (_cb == null) return true;
            var f = typeof(SceneView).GetField("duringSceneGui", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var d = f != null ? f.GetValue(null) as Delegate : null;
            return d == null || !d.GetInvocationList().Any(x => x.Method == _cb.Method);
        }
        public static void Report() => Debug.Log($"[ASH] InvectorWarning hidden={Hidden()}");
    }
}
