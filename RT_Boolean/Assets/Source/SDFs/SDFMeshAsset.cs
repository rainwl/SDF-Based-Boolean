using UnityEngine;

namespace Source.SDFs
{
    /// <summary>
    /// This class contains data representing a signed distance field of a mesh.
    /// </summary>
    public class SDFMeshAsset : MonoBehaviour
    {
        public static void Create(string path, string name, float[] samples, float[] packedUVs, int tessellationLevel,
            int size, float padding, Mesh sourceMesh, Vector3 minBounds, Vector3 maxBounds)
        {
        }
    }
}