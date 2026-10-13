using UnityEngine;

namespace WorldGenesis.Runtime
{
    // A top-down foot point controls the whole silhouette, independently of its height,
    // atlas or renderer batch. Positive Y is farther from the fixed 2D camera.
    public static class WorldDepth
    {
        public const int Back = 0, Body = 2, Front = 4;
        public static float Z(float footY, int part = Body) { return 10 + footY * .1f - part * .0001f; }
        public static Material Material(Texture texture, bool writeDepth = true)
        {
            Shader shader = Resources.Load<Shader>("WorldDepthSprite");
            if (shader == null) throw new System.InvalidOperationException("WorldDepthSprite shader is missing from Resources.");
            Material material = new Material(shader) { mainTexture = texture };
            material.SetFloat("_WriteDepth", writeDepth ? 1 : 0);
            return material;
        }
    }
}
