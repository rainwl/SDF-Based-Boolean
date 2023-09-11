using Source.Utilities;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Source.SDFs
{
    /// <summary>
    /// This class contains data representing a signed distance field of a mesh.
    /// </summary>
    public class SDFMeshAsset : ScriptableObject
    {
        #region Fields

        public int Size => size;
        private int CellsPerSide => size - 1;
        private int PointsPerSide => size;
        public int TotalSize => size * size * size;
        public bool HasUVs => !packedUVs.IsNullOrEmpty();
        public bool IsTessellated => tessellationLevel > 0;
        public float Padding => padding;
        public Vector3 MinBounds => minBounds;
        public Vector3 MaxBounds => maxBounds;
        public Vector3 Centre => (maxBounds + minBounds) * 0.5f;
        public Bounds Bounds => new Bounds(Centre, MaxBounds - MinBounds);
        public Mesh SourceMesh => sourceMesh;

        [SerializeField] [ReadOnly] private Mesh sourceMesh;
        [SerializeField] [HideInInspector] private float[] samples;
        [SerializeField] [HideInInspector] private float[] packedUVs;
        [SerializeField] [ReadOnly] private int tessellationLevel = 0;
        [SerializeField] [ReadOnly] private int size;
        [SerializeField] [ReadOnly] private Vector3 maxBounds;
        [SerializeField] [ReadOnly] private Vector3 minBounds;
        [SerializeField] [ReadOnly] private float padding;

        #endregion

        public static void Create(string path, string name, float[] samples, float[] packedUVs, int tessellationLevel,
            int size, float padding, Mesh sourceMesh, Vector3 minBounds, Vector3 maxBounds)
        {
            var asset = Utils.CreateAsset<SDFMeshAsset>(path, name + "_" + size);
            asset.sourceMesh = sourceMesh;
            asset.minBounds = minBounds;
            asset.maxBounds = maxBounds;
            asset.size = size;
            asset.tessellationLevel = tessellationLevel;
            asset.padding = padding;
            asset.samples = samples;
            asset.packedUVs = packedUVs;

#if UNITY_EDITOR
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
#endif
        }


        #region Public methods

        // note that the true purpose of this class is as a data container for information to be passed to the gpu.
        // these methods are basically clones of hlsl methods for debugging purposes.

        /// <summary>
        /// Given a point anywhere in space, return the point nearest it within the bounds of the volume.
        /// Optionally can provide another parameter which just adjusts the size of this volume.
        /// </summary>
        private Vector3 ClampToVolume(Vector3 input, float boundsOffset = 0f)
        {
            return new Vector3(
                Mathf.Clamp(input.x, MinBounds.x + boundsOffset, MaxBounds.x - boundsOffset),
                Mathf.Clamp(input.y, MinBounds.y + boundsOffset, MaxBounds.y - boundsOffset),
                Mathf.Clamp(input.z, MinBounds.z + boundsOffset, MaxBounds.z - boundsOffset)
            );
        }

        /// <summary>
        /// Given a point anywhere in space, return the point nearest it within the bounds of the volume,
        /// normalized to the range [0, 1] on all axes.
        /// 
        /// Optionally can provide another parameter which just adjusts the size of this volume.
        /// </summary>
        private Vector3 ClampAndNormalizeToVolume(Vector3 p, float boundsOffset = 0f)
        {
            // clamp so we're inside the volume
            p = ClampToVolume(p, boundsOffset);

            var (x, y, z) = (p.x, p.y, p.z);

            x = Mathf.InverseLerp(MinBounds.x, MaxBounds.x, x);
            y = Mathf.InverseLerp(MinBounds.y, MaxBounds.y, y);
            z = Mathf.InverseLerp(MinBounds.z, MaxBounds.z, z);

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Given a point anywhere in space, return the coordinates of the nearest cell, as well as the fractional component
        /// from that cell to the next cell on all 3 axes.
        /// 
        /// Optionally can provide another parameter which just adjusts the size of this volume.
        /// </summary>
        private (int, int, int) GetNearestCoordinates(Vector3 p, out Vector3 frac, float boundsOffset = 0f)
        {
            p = ClampAndNormalizeToVolume(p, boundsOffset);

            (int x, int y, int z) result = p.PiecewiseOp(f => Mathf.FloorToInt(f * CellsPerSide));
            result = result.PiecewiseOp(i => Mathf.Min(i, CellsPerSide - 1));

            frac = p.PiecewiseOp(f => (f * CellsPerSide) % 1f);

            return result;
        }

        /// <summary>
        /// Given a cell coordinate, return the distance to the nearest triangle.
        /// </summary>
        private float GetSignedDistance(int x, int y, int z)
        {
            var index = CellCoordinateToIndex(x, y, z);
            return GetSignedDistanceAtIndex(index);
        }

        private float GetSignedDistanceAtIndex(int index)
        {
            return samples[index];
        }

        /// <summary>
        /// Given a point anywhere in space, clamp the point to be within the volume and then return the distance to the mesh.
        /// </summary>
        public float Sample(Vector3 p)
        {
            var (x, y, z) = GetNearestCoordinates(p, out Vector3 frac);

            var sampleA = GetSignedDistance(x, y, z);
            var sampleB = GetSignedDistance(x + 1, y, z);
            var sampleC = GetSignedDistance(x, y + 1, z);
            var sampleD = GetSignedDistance(x + 1, y + 1, z);
            var sampleE = GetSignedDistance(x, y, z + 1);
            var sampleF = GetSignedDistance(x + 1, y, z + 1);
            var sampleG = GetSignedDistance(x, y + 1, z + 1);
            var sampleH = GetSignedDistance(x + 1, y + 1, z + 1);

            return Utils.TrilinearInterpolate(frac, sampleA, sampleB, sampleC, sampleD, sampleE, sampleF, sampleG,
                sampleH);
        }

        // publicly exposing an array just feels wrong to me hehe
        public void GetDataArrays(out float[] samples, out float[] packedUVs)
        {
            samples = this.samples;
            packedUVs = this.packedUVs;
        }

        #endregion

        #region General Helper Methods

        /// <summary>
        /// Convert a 1-dimensional cell index into the object space position it corresponds to.
        /// </summary>
        public Vector3 IndexToVertex(int index)
        {
            var (x, y, z) = IndexToCellCoordinate(index);
            return CellCoordinateToVertex(x, y, z);
        }

        /// <summary>
        /// Convert a 3-dimensional cell coordinate into the object space position it corresponds to.
        /// </summary>
        private Vector3 CellCoordinateToVertex(int x, int y, int z)
        {
            float gridSize = CellsPerSide;
            var xPos = Mathf.Lerp(MinBounds.x, MaxBounds.x, x / gridSize);
            var yPos = Mathf.Lerp(MinBounds.y, MaxBounds.y, y / gridSize);
            var zPos = Mathf.Lerp(MinBounds.z, MaxBounds.z, z / gridSize);

            return new Vector3(xPos, yPos, zPos);
        }

        /// <summary>
        /// Convert a 1-dimensional cell index into the 3-dimensional cell coordinate it corresponds to.
        /// </summary>
        private (int x, int y, int z) IndexToCellCoordinate(int index)
        {
            var z = index / (PointsPerSide * PointsPerSide);
            index -= (z * PointsPerSide * PointsPerSide);
            var y = index / PointsPerSide;
            var x = index % PointsPerSide;

            return (x, y, z);
        }

        /// <summary>
        /// Convert a 3-dimensional cell coordinate into the 1-dimensional cell index it corresponds to.
        /// </summary>
        private int CellCoordinateToIndex(int x, int y, int z) =>
            (x + y * PointsPerSide + z * PointsPerSide * PointsPerSide);

        #endregion
    }
}