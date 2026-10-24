using PocketOrbits.Core.Saving;
using UnityEngine;
#if PO_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace PocketOrbits.Rendering
{
    /// <summary>
    /// Restrained bloom and vignette through a global URP Volume. Compiled only when URP is installed
    /// (PO_URP is defined by the assembly's version define); without URP this is a no-op and the additive
    /// halo sprites still provide the glow.
    /// </summary>
    public static class PostProcessing
    {
#if PO_URP
        static Volume _volume;
        static Bloom _bloom;
        static Vignette _vignette;
#endif

        public static void Setup(Camera camera, GameSettings settings)
        {
#if PO_URP
            if (_volume != null) return;
            var data = camera.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
            var go = new GameObject("PostProcessVolume");
            Object.DontDestroyOnLoad(go);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _bloom = profile.Add<Bloom>(true);
            _bloom.threshold.Override(0.85f);
            _bloom.scatter.Override(0.6f);
            _vignette = profile.Add<Vignette>(true);
            _vignette.intensity.Override(0.28f);
            _vignette.color.Override(new Color(0.05f, 0.02f, 0.12f));
            _volume.profile = profile;
            Apply(settings);
#endif
        }

        public static void Apply(GameSettings settings)
        {
#if PO_URP
            if (_bloom == null) return;
            bool on = settings.Quality > 0 && settings.EffectsIntensity > 0.05f;
            _bloom.active = on;
            _bloom.intensity.Override(Mathf.Lerp(0.2f, 0.9f, settings.EffectsIntensity));
            _vignette.active = settings.Quality > 0;
#endif
        }
    }
}
