using UnityEngine;

namespace ASH.Wildlife
{
    /// <summary>
    /// Receiver for vendor (Malbers) animation events on a wildlife Animator that sits on a child object, so clips such as
    /// the bear's 'Be_Stand Roar' don't log "AnimationEvent 'PlaySound' has no receiver". Added at runtime by ASHWildlifeAnim.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHWildlifeAnimEvents : MonoBehaviour
    {
        AudioSource _audio;

        public void PlaySound(AnimationEvent e)
        {
            if (e == null || !(e.objectReferenceParameter is AudioClip c)) return;
            if (!_audio) _audio = GetComponentInParent<AudioSource>();
            if (_audio) _audio.PlayOneShot(c, 0.9f);
        }
    }
}
