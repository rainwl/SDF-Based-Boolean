using System.Runtime.InteropServices;
using UnityEngine;

namespace Source.SDFs
{
    // 对象的成员按导出到非托管内存时出现的顺序排列。成员是根据包装中指定的包装布局的，并且可以是不连续的。
    // The members of the object are laid out sequentially, in the order in which they appear when exported to unmanaged memory.
    // The members are laid out according to the packing specified in Pack, and can be non-contiguous.
    [StructLayout(LayoutKind.Sequential)]
    [System.Serializable]
    public struct SdfGpuData
    {
        #region Fields

        public static int Stride => sizeof(int) * 3 + sizeof(float) * 11 + sizeof(float) * 16;

        public int Type; // negative if operation, 0 if mesh, else it's an enum value

        // if primitive, this could be anything. if mesh, it's (size, sample start index, uv start index, 0)
        public Vector4 Data;

        public Matrix4x4 Transform; // translation/rotation/scale
        public int CombineType; // how this sdf is combined with previous 
        public int Flip; // whether to multiply by -1, turns inside out
        public Vector3 MinBounds; // only used by sdf mesh, near bottom left

        public Vector3 MaxBounds; // only used by sdf mesh, far top right

        // the input to the smooth min function, how smoothly this sdf blends with the previous ones
        public float Smoothing;

        public bool IsMesh => Type == 0;
        public bool IsOperation => Type < 0;
        public bool IsPrimitive => Type > 0;
        public int Size => (int)Data.x;
        public int SampleStartIndex => (int)Data.y;
        public int UVStartIndex => (int)Data.z;

        public SDFPrimitiveType PrimitiveType => (SDFPrimitiveType)(Type - 1);
        public SDFOperationType OperationType => (SDFOperationType)(-Type - 1);

        #endregion

        public override string ToString()
        {
            return IsMesh
                ? $"[Mesh] Size = {(int)Data.x}, MinBounds = {MinBounds}, MaxBounds = {MaxBounds}, StartIndex = {(int)Data.y}, UVStartIndex = {(int)Data.z}"
                : IsOperation
                    ? $"[{OperationType}] Data = {Data}"
                    : $"[{PrimitiveType}] Data = {Data}";
        }
    }
}