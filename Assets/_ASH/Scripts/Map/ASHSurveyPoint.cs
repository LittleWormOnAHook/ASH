using ASH.Interaction;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Map
{
    /// <summary>World interactable at a survey point (topo panel, survey marker, trail register, radio relay).
    /// Hold Interact with the GPS in the pack to log the viewpoint.</summary>
    public sealed class ASHSurveyPoint : MonoBehaviour, IASHUsable
    {
        [SerializeField] private ASHSurveyPointDef definition;
        [Range(0.5f, 8f)] [SerializeField] private float holdSeconds = 2.5f;

        public ASHSurveyPointDef Definition => definition;
        public Transform UseTransform => transform;
        public void Configure(ASHSurveyPointDef def) { definition = def; }

        static string Noun(ASHSurveyKind k)
        {
            switch (k)
            {
                case ASHSurveyKind.SurveyMarker: return "survey marker";
                case ASHSurveyKind.TrailRegister: return "trail register";
                case ASHSurveyKind.RadioRelay: return "radio relay";
                default: return "topo panel";
            }
        }

        public float GetUsePriority(in ASHUseContext c) => definition == null || ASHGpsMap.Instance == null ? -1f : 70f;
        public float GetHoldDuration(in ASHUseContext c) => Logged ? 0f : holdSeconds;
        bool Logged => ASHGpsMap.Instance != null && ASHGpsMap.Instance.IsLogged(definition);

        public string GetPrompt(in ASHUseContext c)
        {
            if (definition == null) return null;
            if (Logged) return $"{definition.displayName}: viewpoint logged";
            if (c.Inventory == null || !c.Inventory.HasTool(ASHToolType.GPS)) return $"Old {Noun(definition.kind)} (need the GPS to log it)";
            return $"Log viewpoint with GPS ({Noun(definition.kind)})";
        }

        public bool TryUse(in ASHUseContext c)
        {
            if (definition == null || Logged || c.Inventory == null || !c.Inventory.HasTool(ASHToolType.GPS)) return false;
            return ASHGpsMap.Instance.LogSurvey(definition);
        }
    }
}
