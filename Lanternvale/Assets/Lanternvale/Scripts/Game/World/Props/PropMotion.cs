// Tiny self-animation for the few moving prop parts: windmill sails turning, campfire flames flickering.
// Lives on the moving child only (static props carry no scripts). Allocation-free.
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class PropMotion : MonoBehaviour
    {
        /// <summary>Spin about the local axis (degrees per second; 0 = none).</summary>
        public Vector3 SpinAxis = Vector3.forward;
        public float SpinSpeed;

        /// <summary>Flicker: scale wobble amount (0 = none) around the base scale, plus a slight sway.</summary>
        public float Flicker;
        public float FlickerSpeed = 7f;

        Vector3 baseScale = Vector3.one;
        Quaternion baseRotation = Quaternion.identity;
        float phase, angle;

        // Start (not Awake): runs after MapView has placed the prop, so neighbouring fires flicker out of step.
        void Start()
        {
            baseScale = transform.localScale;
            baseRotation = transform.localRotation;
            phase = (transform.position.x * 1.37f + transform.position.y * 0.71f) % 10f;
        }

        void Update()
        {
            float t = Time.time * FlickerSpeed + phase;
            if (SpinSpeed != 0f)
            {
                angle = Mathf.Repeat(angle + SpinSpeed * Time.deltaTime, 360f);
                transform.localRotation = baseRotation * Quaternion.AngleAxis(angle, SpinAxis);
            }
            if (Flicker > 0f)
            {
                float n = Mathf.Sin(t) * 0.55f + Mathf.Sin(t * 2.31f + 1.3f) * 0.3f + Mathf.Sin(t * 5.17f + 0.4f) * 0.15f;
                float s = 1f + n * Flicker;
                transform.localScale = new Vector3(baseScale.x * (1f - n * Flicker * 0.35f), baseScale.y * s, baseScale.z * (1f - n * Flicker * 0.35f));
                if (SpinSpeed == 0f)
                    transform.localRotation = baseRotation * Quaternion.Euler(Mathf.Sin(t * 0.83f) * 4f * Flicker * 6f, 0f, Mathf.Sin(t * 1.1f + 2f) * 4f * Flicker * 6f);
            }
        }
    }
}
