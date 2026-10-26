using UnityEngine;
namespace Blocks.Gameplay.Shooter
{
    // Detached local visual: survives its owner's coroutine, but never its fuse.
    public sealed class DvaAbandonedMechLifetime : MonoBehaviour
    {
        public float seconds = 3.1f;
        float elapsed;
        void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            if (elapsed < seconds) return;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
        void OnDestroy()
        {
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))
                foreach(var material in renderer.sharedMaterials)
                    if(material != null) Destroy(material);
        }
    }
}
