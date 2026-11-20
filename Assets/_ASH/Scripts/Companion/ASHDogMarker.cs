using ASH.Interaction;
using UnityEngine;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.Companion
{
    /// <summary>Small floating marker the dog leaves over something it found (bobbing dot + label, faces the camera, fades out).
    /// Gone early when the pickup is taken or the crate opened.</summary>
    public sealed class ASHDogMarker : MonoBehaviour
    {
        Transform _target; float _until, _start, _height; CanvasGroup _cg; ASHSupplyCrate _crate;

        public static ASHDogMarker Show(Transform target, Color color, string label, float seconds)
        {
            if (!target) return null;
            var go = new GameObject("ASH_DogMarker", typeof(RectTransform));
            var m = go.AddComponent<ASHDogMarker>(); m._target = target; m._start = Time.time; m._until = Time.time + seconds; m._crate = target.GetComponent<ASHSupplyCrate>();
            float top = 0.5f; foreach (var r in target.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y - target.position.y);
            m._height = Mathf.Clamp(top, 0.3f, 3f) + 0.45f;
            var cv = go.AddComponent<Canvas>(); cv.renderMode = RenderMode.WorldSpace; cv.sortingOrder = 5;
            var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(240, 120); rt.localScale = Vector3.one * 0.006f;
            m._cg = go.AddComponent<CanvasGroup>();
            var dot = K.At("Dot", rt, new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(46, 46)); var di = K.Image(dot, color); di.sprite = K.Dot;
            var ring = K.At("Ring", rt, new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(70, 70)); var ri = K.Image(ring, color); ri.sprite = K.Ring;
            var t = K.Text(K.At("Label", rt, new Vector2(0.5f, 1f), new Vector2(0, -20), new Vector2(240, 40)), label ?? "", 26, color, TextAnchor.MiddleCenter, FontStyle.Bold); K.Shadow(t, 2f);
            m.Place();
            return m;
        }

        void LateUpdate()
        {
            bool gone = !_target || !_target.gameObject.activeInHierarchy || (_crate && _crate.Opened) || (!_crate && !_target.GetComponent<ASHItemPickup>());
            if (gone || Time.time >= _until) { Destroy(gameObject); return; }
            Place();
            float age = Time.time - _start, left = _until - Time.time;
            _cg.alpha = Mathf.Clamp01(age * 3f) * Mathf.Clamp01(left / 1.5f);
        }

        void Place()
        {
            var cam = Camera.main;
            transform.position = _target.position + Vector3.up * (_height + Mathf.Sin(Time.time * 3f) * 0.06f);
            if (cam) { transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up); float d = Vector3.Distance(cam.transform.position, transform.position); transform.localScale = Vector3.one * 0.006f * Mathf.Clamp(d / 6f, 0.6f, 4f); }
        }
    }
}