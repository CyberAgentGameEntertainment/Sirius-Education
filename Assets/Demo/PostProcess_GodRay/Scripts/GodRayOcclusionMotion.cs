// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

using UnityEngine;

namespace Sirius.Demo
{
    /// <summary>
    ///     開始位置を中心に軸方向へ往復させ、光芒の遮蔽が動的に変化することを示す
    /// </summary>
    public class GodRayOcclusionMotion : MonoBehaviour
    {
        [SerializeField] private Vector3 axis = Vector3.right;
        [SerializeField] private float amplitude = 1.5f;
        [SerializeField] private float frequency = 0.25f;

        private Vector3 _origin;

        private void Start()
        {
            _origin = transform.position;
        }

        private void Update()
        {
            var offset = Mathf.Sin(Time.time * frequency * Mathf.PI * 2f) * amplitude;
            transform.position = _origin + axis * offset;
        }
    }
}
