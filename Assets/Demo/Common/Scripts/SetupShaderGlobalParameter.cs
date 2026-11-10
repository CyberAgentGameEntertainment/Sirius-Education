using UnityEngine;

namespace Demo.Scripts
{
    [ExecuteAlways]
    public class SetupShaderGlobalParameter : MonoBehaviour
    {
        private static readonly int CTTimeScale = Shader.PropertyToID("_CTTimeScale");
        public void Update()
        {
            if (Application.isPlaying)
            {
                Shader.SetGlobalFloat(CTTimeScale, Time.timeScale);
            }
            else
            {
                Shader.SetGlobalFloat(CTTimeScale, 0.0f);
            }
        }
    }
}
