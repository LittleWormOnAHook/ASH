using System;
using UnityEngine;

namespace ASH.World.Accumulation
{
    /// <summary>
    /// Environmental inputs fed INTO the accumulation system (e.g. by a future ASHWeatherDirector).
    /// The accumulation controller alone decides how these affect surfaces (rate, melt, wetness, wind, persistence).
    /// </summary>
    [Serializable]
    public struct ASHWeatherInputs
    {
        [Range(0f, 1f)] public float ashfallIntensity;
        [Range(0f, 1f)] public float snowfallIntensity;
        [Tooltip("Direction the wind blows TOWARDS (world XZ). Default westerly: +X.")] public Vector2 windDirection;
        [Range(0f, 1f)] public float windStrength;
        [Range(-40f, 40f)] public float temperatureC;
        [Range(0f, 1f)] public float rainIntensity;

        public static ASHWeatherInputs Calm => new ASHWeatherInputs { ashfallIntensity = 0.3f, snowfallIntensity = 0f, windDirection = new Vector2(1f, 0f), windStrength = 0.4f, temperatureC = -3f, rainIntensity = 0f };
    }

    /// <summary>Optional pull-style source: assign any component implementing this to ASHAccumulationController.weatherSource.</summary>
    public interface IASHWeatherInputs
    {
        ASHWeatherInputs GetWeatherInputs();
    }
}
