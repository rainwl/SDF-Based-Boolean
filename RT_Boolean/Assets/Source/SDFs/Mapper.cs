using System.Collections.Generic;
using Source.Utilities;
using UnityEngine;

namespace Source.SDFs
{
    public class Mapper
    {
        #region Fields

        private SDFGroup.Settings _settings;
        private IList<SdfGpuData> _sdfData;
        private IList<SDFMaterialGPU> _sdfMaterial;
        private IList<float> _sdfMeshSamples;
        private IList<float> _sdfMeshPackedUVs;
        private const float MinNormalSmoothingCpu = 0.002f;

        #endregion

        #region Methods

        /// <summary>
        /// Get the signed distance to the object represented by the given data object.
        /// </summary>
        private float SDF(Vector3 p, SdfGpuData data)
        {
            if (data.IsMesh)
            {
                var vec = GetDirectionToMesh(p, data, out var distSign, out var transformedP);
                return vec.magnitude * distSign * data.Flip;
            }
            else
            {
                p = data.Transform.MultiplyPoint(p);

                return (SDFPrimitiveType)(data.Type - 1) switch
                {
                    SDFPrimitiveType.Sphere => MapSphere(p, data.Data.x) * data.Flip,
                    SDFPrimitiveType.Torus => MapTorus(p, data.Data) * data.Flip,
                    SDFPrimitiveType.Cuboid => MapRoundedBox(p, data.Data, data.Data.w) * data.Flip,
                    SDFPrimitiveType.Cylinder => MapCylinder(p, data.Data.x, data.Data.y) * data.Flip,
                    _ => MapBoxFrame(p, data.Data, data.Data.w) * data.Flip
                };
            }
        }

        /// <summary>
        /// Returns the signed distance to the field as a whole.
        /// </summary>
        public float Map(Vector3 p)
        {
            var minDist = 10000000f;

            foreach (var data in _sdfData)
            {
                if (data.IsOperation)
                {
                    p = ElongateSpace(p, data.Data, data.Transform);
                }
                else
                {
                    minDist = data.CombineType switch
                    {
                        0 => SmoothUnion(minDist, SDF(p, data), data.Smoothing),
                        1 => SmoothSubtract(SDF(p, data), minDist, data.Smoothing),
                        _ => SmoothIntersect(SDF(p, data), minDist, data.Smoothing)
                    };
                }
            }

            return minDist;
        }


        /// <summary>
        /// Returns a normalized gradient value approximated by tetrahedral central differences. Useful for approximating a surface normal.
        /// </summary>
        public Vector3 MapNormal(Vector3 p, float smoothing = -1f) => MapGradient(p, smoothing).normalized;

        /// <summary>
        /// Returns the gradient of the signed distance field at the given point. Gradient point away from surface and their magnitude is
        /// indicative of the rate of change of the field. This value can be normalized to approximate a surface normal.
        /// 返回给定点处带符号距离字段的梯度。梯度点远离地表，其大小表示磁场的变化率。该值可以归一化以近似于曲面法线
        /// </summary>
        private Vector3 MapGradient(Vector3 p, float smoothing = -1f)
        {
            var normalSmoothing = smoothing < 0f ? _settings.NormalSmoothing : smoothing;
            normalSmoothing = Mathf.Max(normalSmoothing, MinNormalSmoothingCpu);

            var e = new Vector2(normalSmoothing, -normalSmoothing);

            return (
                XYY(e) * Map(p + XYY(e)) +
                YYX(e) * Map(p + YYX(e)) +
                YXY(e) * Map(p + YXY(e)) +
                XXX(e) * Map(p + XXX(e)));
        }

        /// <summary>
        /// Ray march the field. Returns whether the surface was hit, as well as the position and normal of that surface.
        /// </summary>
        public bool RayMarch(Vector3 origin, Vector3 direction, out Vector3 hitPoint, out Vector3 hitNormal,
            float maxDistance = 350f)
        {
            const int maxIterations = 256;
            const float surfaceDistance = 0.001f;

            hitPoint = Vector3.zero;
            hitNormal = Vector3.zero;

            var distanceToSurface = 0f;
            var rayTravelDistance = 0f;

            // March the distance field until a surface is hit.
            for (var i = 0; i < maxIterations; i++)
            {
                var p = origin + direction * rayTravelDistance;

                distanceToSurface = Map(p);
                rayTravelDistance += distanceToSurface;

                if (distanceToSurface < surfaceDistance || rayTravelDistance > maxDistance)
                    break;
            }

            if (distanceToSurface < surfaceDistance)
            {
                hitPoint = origin + direction * rayTravelDistance;
                hitNormal = MapNormal(hitPoint);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Set the global settings of the field.
        /// </summary>
        public void SetSettings(SDFGroup.Settings settings) => _settings = settings;

        /// <summary>
        /// Set the information relating to all sdf objects, such as their positions and rotations.
        /// </summary>
        public void SetData(IList<SdfGpuData> data, IList<SDFMaterialGPU> materials)
        {
            _sdfData = data;
            _sdfMaterial = materials;
        }

        /// <summary>
        /// Set the data relating to meshes specifically, including their sampled distances and UVs.
        /// </summary>
        public void SetMeshData(IList<float> meshSamples, IList<float> meshPackedUVs)
        {
            _sdfMeshSamples = meshSamples;
            _sdfMeshPackedUVs = meshPackedUVs;
        }

        #endregion
        
        #region SDF Functions

        public static Vector3 GetNearestPointOnBox(Vector3 p, Vector3 b, Matrix4x4 worldToLocal) =>
            worldToLocal.inverse.MultiplyPoint(GetNearestPointOnBox(worldToLocal.MultiplyPoint(p), b));

        public static Vector3 GetNearestPointOnBox(Vector3 p, Vector3 b) =>
            p + GetBoxGradient(p, b).normalized * -MapBox(p, b);

        private static Vector3 GetBoxGradient(Vector3 p, Vector3 b, Matrix4x4 worldToLocal) =>
            GetBoxGradient(worldToLocal.MultiplyPoint(p), b);

        private static Vector3 GetBoxGradient(Vector3 p, Vector3 b)
        {
            Vector3 d = Abs(p) - b;
            Vector3 s = Sign(p);
            float g = Mathf.Max(d.x, Mathf.Max(d.y, d.z));

            Vector3 derp;

            if (g > 0f)
            {
                derp = Max(d, 0f).normalized;
            }
            else
            {
                derp = Mul(Step(YZX(d), d), Step(ZXY(d), d));
            }

            return Mul(s, derp);
        }

        public static bool IsInBox(Vector3 p, Vector3 b) =>
            MapBox(p, b) < 0f;

        public static bool IsInBox(Vector3 p, Vector3 b, Matrix4x4 worldToLocal) =>
            MapBox(worldToLocal.MultiplyPoint(p), b) < 0f;

        public static float MapBox(Vector3 p, Vector3 b, Matrix4x4 worldToLocal) =>
            MapBox(worldToLocal.MultiplyPoint(p), b);

        public static float MapBox(Vector3 p, Vector3 b)
        {
            Vector3 q = Abs(p) - b;
            return (Max(q, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
        }

        public static float MapRoundedBox(Vector3 p, Vector3 b, float r) => MapBox(p, b) - r;

        public static float MapBoxFrame(Vector3 p, Vector3 b, float e)
        {
            p = Abs(p) - b;

            Vector3 eVec = Vector3.one * e;
            Vector3 q = Abs(p + eVec) - eVec;

            float one = Max(new Vector3(p.x, q.y, q.z), 0f).magnitude +
                        Mathf.Min(Mathf.Max(p.x, Mathf.Max(q.y, q.z)), 0f);
            float two = Max(new Vector3(q.x, p.y, q.z), 0f).magnitude +
                        Mathf.Min(Mathf.Max(q.x, Mathf.Max(p.y, q.z)), 0f);
            float three = Max(new Vector3(q.x, q.y, p.z), 0f).magnitude +
                          Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, p.z)), 0f);

            return Mathf.Min(one, two, three);
        }

        public static float MapCylinder(Vector3 p, float h, float r)
        {
            Vector2 d = Abs(new Vector2((XZ(p).magnitude), p.y)) - new Vector2(h, r);
            return Mathf.Min(Mathf.Max(d.x, d.y), 0f) + Max(d, 0f).magnitude;
        }

        public static float MapTorus(Vector3 p, Vector2 t)
        {
            Vector2 q = new Vector2(XZ(p).magnitude - t.x, p.y);
            return q.magnitude - t.y;
        }

        public static float MapSphere(Vector3 p, float radius) =>
            p.magnitude - radius;

        // polynomial smooth min (k = 0.1);
        private static float SmoothUnion(float a, float b, float k)
        {
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Min(a, b) - h * h * k * (1f / 4f);
        }

        private static float SmoothSubtract(float d1, float d2, float k)
        {
            var h = Mathf.Clamp(0.5f - 0.5f * (d2 + d1) / k, 0f, 1f);
            return Mathf.Lerp(d2, -d1, h) + k * h * (1f - h);
        }

        private static float SmoothIntersect(float d1, float d2, float k)
        {
            float h = Mathf.Clamp(0.5f - 0.5f * (d2 - d1) / k, 0f, 1f);
            return Mathf.Lerp(d2, d1, h) + k * h * (1f - h);
        }

        private static Vector3 ElongateSpace(Vector3 p, Vector3 h, Matrix4x4 transform)
        {
            Vector3 translation = transform.ExtractTranslation();
            p = transform.MultiplyPoint(p);
            p = p - Clamp(p + translation, -h, h);
            return transform.inverse.MultiplyPoint(p);
        }

        #endregion

        #region Mesh Functions

        // these functions are all specifically to do with SDFMesh objects only
        // given a point, return the coords of the cell it's in, and the fractional component for interpolation
        // 这些函数都是专门与SDFMesh对象做的，只给一个点，返回它所在的单元格的坐标，以及用于插值的分数分量
        private static void GetNearestCoordinates(Vector3 p, SdfGpuData data, out Vector3 coords, out Vector3 fracs,
            float boundsOffset = 0f)
        {
            p = ClampAndNormalizeToVolume(p, data, boundsOffset);
            var cellsPerSide = data.Size - 1;

            // sometimes i'm not good at coming up with names :U
            var floored = Floor(p * cellsPerSide);
            coords = Min(floored, cellsPerSide - 1);

            fracs = Frac(p * cellsPerSide);
        }

        private float SampleAssetInterpolated(Vector3 p, SdfGpuData data, float boundsOffset = 0f)
        {
            GetNearestCoordinates(p, data, out var coords, out var fracs, boundsOffset);

            var x = (int)coords.x;
            var y = (int)coords.y;
            var z = (int)coords.z;

            var sampleA = GetMeshSignedDistance(x, y, z, data);
            var sampleB = GetMeshSignedDistance(x + 1, y, z, data);
            var sampleC = GetMeshSignedDistance(x, y + 1, z, data);
            var sampleD = GetMeshSignedDistance(x + 1, y + 1, z, data);
            var sampleE = GetMeshSignedDistance(x, y, z + 1, data);
            var sampleF = GetMeshSignedDistance(x + 1, y, z + 1, data);
            var sampleG = GetMeshSignedDistance(x, y + 1, z + 1, data);
            var sampleH = GetMeshSignedDistance(x + 1, y + 1, z + 1, data);

            return Utils.TrilinearInterpolate(fracs, sampleA, sampleB, sampleC, sampleD, sampleE, sampleF, sampleG,
                sampleH);
        }

        private Vector3 ComputeMeshGradient(Vector3 p, SdfGpuData data, float epsilon, float boundsOffset = 0f)
        {
            // sample the map 4 times to calculate the gradient at that point, then normalize it
            var e = new Vector2(epsilon, -epsilon);

            return (
                XYY(e) * SampleAssetInterpolated(p + XYY(e), data, boundsOffset) +
                YYX(e) * SampleAssetInterpolated(p + YYX(e), data, boundsOffset) +
                YXY(e) * SampleAssetInterpolated(p + YXY(e), data, boundsOffset) +
                XXX(e) * SampleAssetInterpolated(p + XXX(e), data, boundsOffset)).normalized;
        }

        // returns the vector pointing to the surface of the mesh representation, as well as the sign
        // (negative for inside, positive for outside)
        // this can be used to recreate a signed distance field
        // 返回指向网格表示法表面的矢量，以及符号(内部为负，外部为正)，这可用于重建带符号的距离字段
        private Vector3 GetDirectionToMesh(Vector3 p, SdfGpuData data, out float distSign, out Vector3 transformedP)
        {
            transformedP = data.Transform.MultiplyPoint(p);

            const float epsilon = 0.75f;
            const float pushIntoBounds = 0.04f;

            // get the distance either at p, or at the point on the bounds nearest p
            float sample = SampleAssetInterpolated(transformedP, data);

            Vector3 closestPoint = GetClosestPointToVolume(transformedP, data, pushIntoBounds);

            Vector3 vecInBounds =
                (-(ComputeMeshGradient(closestPoint, data, epsilon, pushIntoBounds)).normalized * sample);
            Vector3 vecToBounds = (closestPoint - transformedP);
            Vector3 finalVec = vecToBounds + vecInBounds;

            distSign = Mathf.Sign(sample);

            return finalVec;
        }

        private float GetMeshSignedDistance(int x, int y, int z, SdfGpuData data)
        {
            var index = CellCoordinateToIndex(x, y, z, data);
            return _sdfMeshSamples[index];
        }

        // clamp the input point to an axis aligned bounding cube of the given bounds
        // optionally can provide an offset which pushes the bounds in or out.
        // this is used to get the position on the bounding cube nearest the given point as 
        // part of the sdf to mesh calculation. the additional push param is used to ensure we have enough
        // samples around our point that we can get a gradient
        // 将输入点夹紧到给定边界的与轴对齐的边界立方体上，可选地提供将边界推入或推出的偏移量。
        // 这用于获取边界立方体上最接近给定点的位置，作为网格计算的SDF的一部分。
        // 额外的push参数用于确保我们在我们的点周围有足够的样本，我们可以得到一个梯度
        private static Vector3 GetClosestPointToVolume(Vector3 p, SdfGpuData data, float boundsOffset = 0f)
        {
            Vector3 minBounds = data.MinBounds;
            Vector3 maxBounds = data.MaxBounds;
            return new Vector3(
                Mathf.Clamp(p.x, minBounds.x + boundsOffset, maxBounds.x - boundsOffset),
                Mathf.Clamp(p.y, minBounds.y + boundsOffset, maxBounds.y - boundsOffset),
                Mathf.Clamp(p.z, minBounds.z + boundsOffset, maxBounds.z - boundsOffset)
            );
        }

        // ensure the given point is inside the volume, and then smush into the the [0, 1] range
        // 确保给定的点在音量内，然后将其挤进[0,1]范围内
        private static Vector3 ClampAndNormalizeToVolume(Vector3 p, SdfGpuData data, float boundsOffset = 0f)
        {
            // clamp so we're inside the volume
            p = GetClosestPointToVolume(p, data, boundsOffset);

            Vector3 minBounds = data.MinBounds;
            Vector3 maxBounds = data.MaxBounds;

            return new Vector3(
                Mathf.InverseLerp(minBounds.x + boundsOffset, maxBounds.x - boundsOffset, p.x),
                Mathf.InverseLerp(minBounds.y + boundsOffset, maxBounds.y - boundsOffset, p.y),
                Mathf.InverseLerp(minBounds.z + boundsOffset, maxBounds.z - boundsOffset, p.z)
            );
        }

        #endregion

        #region Helper Functions

        // these functions mostly just exist to either replicate some hlsl functionality such as swizzling,
        // or make it easier to convert cell coordinates to world space positions or 1d array indices
        // 这些函数的存在主要是为了复制一些HLSL功能，如swizzling，或者使单元格坐标更容易转换为世界空间位置或1d数组索引
        private static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
        private static Vector3 XYZ(Vector4 v) => new Vector3(v.x, v.y, v.z);

        // ReSharper disable once InconsistentNaming
        private static Vector3 XYY(Vector2 v) => new Vector3(v.x, v.y, v.y);

        // ReSharper disable once InconsistentNaming
        private static Vector3 YYX(Vector2 v) => new Vector3(v.y, v.y, v.x);

        // ReSharper disable once InconsistentNaming
        private static Vector3 YXY(Vector2 v) => new Vector3(v.y, v.x, v.y);

        // ReSharper disable once InconsistentNaming
        private static Vector3 XXX(Vector2 v) => new Vector3(v.x, v.x, v.x);
        private static Vector3 YZX(Vector3 v) => new Vector3(v.y, v.z, v.x);
        private static Vector3 ZXY(Vector3 v) => new Vector3(v.z, v.x, v.y);

        private static Vector3 Mul(Vector3 a, Vector3 b) =>
            new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);

        private static float Step(float edge, float x) =>
            x < edge ? 0f : 1f;

        private static Vector3 Step(Vector3 val, Vector3 threshold) =>
            new Vector3(Step(val.x, threshold.x), Step(val.y, threshold.y), Step(val.z, threshold.z));

        private static Vector3 Clamp(Vector3 input, float min, float max) =>
            new Vector3(Mathf.Clamp(input.x, min, max), Mathf.Clamp(input.y, min, max), Mathf.Clamp(input.z, min, max));

        private static Vector3 Clamp(Vector3 input, Vector3 min, Vector3 max) =>
            new Vector3(Mathf.Clamp(input.x, min.x, max.x), Mathf.Clamp(input.y, min.y, max.y),
                Mathf.Clamp(input.z, min.z, max.z));

        private static Vector3 Sign(Vector3 input) =>
            new Vector3(input.x <= 0f ? -1f : 1f, input.y <= 0f ? -1f : 1f, input.z <= 0f ? -1f : 1f);

        private static Vector3 Abs(Vector3 input) =>
            new Vector3(Mathf.Abs(input.x), Mathf.Abs(input.y), Mathf.Abs(input.z));

        private static Vector2 Abs(Vector2 input) =>
            new Vector2(Mathf.Abs(input.x), Mathf.Abs(input.y));

        private static Vector3 Frac(Vector3 input) =>
            new Vector3(input.x - Mathf.Floor(input.x), input.y - Mathf.Floor(input.y), input.z - Mathf.Floor(input.z));

        private static Vector3 Floor(Vector3 input) =>
            new Vector3(Mathf.Floor(input.x), Mathf.Floor(input.y), Mathf.Floor(input.z));

        private static Vector3 Min(Vector3 input1, Vector3 input2) =>
            new Vector3(Mathf.Min(input1.x, input2.x), Mathf.Min(input1.y, input2.y), Mathf.Min(input1.z, input2.z));

        private static Vector3 Max(Vector3 input1, Vector3 input2) =>
            new Vector3(Mathf.Max(input1.x, input2.x), Mathf.Max(input1.y, input2.y), Mathf.Max(input1.z, input2.z));

        private static Vector3 Min(Vector3 input1, int input2) =>
            new Vector3(Mathf.Min(input1.x, input2), Mathf.Min(input1.y, input2), Mathf.Min(input1.z, input2));

        private static Vector3 Max(Vector3 input1, int input2) =>
            new Vector3(Mathf.Max(input1.x, input2), Mathf.Max(input1.y, input2), Mathf.Max(input1.z, input2));

        private static Vector3 Min(Vector3 input1, float input2) =>
            new Vector3(Mathf.Min(input1.x, input2), Mathf.Min(input1.y, input2), Mathf.Min(input1.z, input2));

        private static Vector3 Max(Vector3 input1, float input2) =>
            new Vector3(Mathf.Max(input1.x, input2), Mathf.Max(input1.y, input2), Mathf.Max(input1.z, input2));

        private static Vector2 Saturate(Vector2 input) =>
            new Vector2(Mathf.Clamp01(input.x), Mathf.Clamp01(input.y));

        private static Vector3 Saturate(Vector3 input) =>
            new Vector3(Mathf.Clamp01(input.x), Mathf.Clamp01(input.y), Mathf.Clamp01(input.z));

        private static Vector4 Saturate(Vector4 input) =>
            new Vector4(Mathf.Clamp01(input.x), Mathf.Clamp01(input.y), Mathf.Clamp01(input.z), Mathf.Clamp01(input.w));

        private static Vector3 CellCoordinateToVertex(int x, int y, int z, SdfGpuData data)
        {
            float gridSize = data.Size - 1f;
            Vector3 minBounds = data.MinBounds;
            Vector3 maxBounds = data.MaxBounds;
            float xPos = Mathf.Lerp(minBounds.x, maxBounds.x, x / gridSize);
            float yPos = Mathf.Lerp(minBounds.y, maxBounds.y, y / gridSize);
            float zPos = Mathf.Lerp(minBounds.z, maxBounds.z, z / gridSize);

            return new Vector3(xPos, yPos, zPos);
        }

        private static int CellCoordinateToIndex(int x, int y, int z, SdfGpuData data)
        {
            int size = data.Size;
            return data.SampleStartIndex + (x + y * size + z * size * size);
        }

        #endregion
    }
}