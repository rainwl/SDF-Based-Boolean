using Source.Utilities;
using UnityEditor;
using UnityEngine;

// ReSharper disable Unity.NoNullPropagation

namespace Source.SDFs
{
    [ExecuteInEditMode]
    public class SDFPrimitive : SDFObject
    {
        #region Fields

        [SerializeField] private SDFPrimitiveType type;
        [SerializeField] private Vector4 data = new Vector4(1f, 1f, 1f, 0f);
        [SerializeField] protected SDFCombineType operation;
        [SerializeField] protected bool flip = false;

        public SDFPrimitiveType Type => type;
        public SDFCombineType Operation => operation;

        #endregion

        #region Methods

        protected override void TryDeregister()
        {
            base.TryDeregister();
            Group?.Deregister(this);
        }

        protected override void TryRegister()
        {
            base.TryDeregister();
            Group?.Register(this);
        }

        public Vector3 CubeBounds
        {
            get
            {
                if (type == SDFPrimitiveType.BoxFrame || type == SDFPrimitiveType.Cuboid)
                    return new Vector3(data.x, data.y, data.z);

                return Vector3.zero;
            }
        }

        public float SphereRadius
        {
            get
            {
                if (type == SDFPrimitiveType.Sphere)
                    return data.x;

                return 0f;
            }
        }

        public void SetCubeBounds(Vector3 vec)
        {
            if (type == SDFPrimitiveType.BoxFrame || type == SDFPrimitiveType.Cuboid)
            {
                data = new Vector4(vec.x, vec.y, vec.z, data.w);
                SetDirty();
            }
        }

        public void SetSphereRadius(float radius)
        {
            if (type == SDFPrimitiveType.Sphere)
            {
                data = data.SetX(Mathf.Max(0f, radius));
                SetDirty();
            }
        }

        public override SdfGpuData GetSdfGpuData(int sampleStartIndex = -1, int uvStartIndex = -1)
        {
            // note: has room for six more floats (min bounds, max bounds)
            return new SdfGpuData
            {
                Type = (int)type + 1,
                Data = data,
                Transform = transform.worldToLocalMatrix,
                CombineType = (int)operation,
                Flip = flip ? -1 : 1,
                Smoothing = Mathf.Max(MinSmoothing, smoothing)
            };
        }

        #endregion


#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var col = Operation == SDFCombineType.SmoothSubtract ? Color.red : Color.blue;
            Handles.color = col;
            Handles.matrix = transform.localToWorldMatrix;

            switch (Type)
            {
                case SDFPrimitiveType.BoxFrame:
                case SDFPrimitiveType.Cuboid:
                    Handles.DrawWireCube(Vector3.zero, data.XYZ() * 2f);
                    break;
                case SDFPrimitiveType.Sphere:
                case SDFPrimitiveType.Torus:
                case SDFPrimitiveType.Cylinder:
                default:
                    Handles.DrawWireDisc(Vector3.zero, Vector3.up, data.x);
                    break;
            }
        }

#endif

        #region Create Menu Items

#if UNITY_EDITOR
        private static void CreateNewPrimitive(SDFPrimitiveType type, Vector4 startData)
        {
            var selection = Selection.activeGameObject;

            var child = new GameObject(type.ToString());
            child.transform.SetParent(selection.transform);
            child.transform.Reset();

            var newPrimitive = child.AddComponent<SDFPrimitive>();
            newPrimitive.type = type;
            newPrimitive.data = startData;
            newPrimitive.SetDirty();

            Selection.activeGameObject = child;
        }

        [MenuItem("GameObject/SDFs/Sphere", false, priority: 2)]
        private static void CreateSphere(MenuCommand menuCommand) =>
            CreateNewPrimitive(SDFPrimitiveType.Sphere, new Vector4(1f, 0f, 0f, 0f));

        [MenuItem("GameObject/SDFs/Cuboid", false, priority: 2)]
        private static void CreateCuboid(MenuCommand menuCommand) =>
            CreateNewPrimitive(SDFPrimitiveType.Cuboid, new Vector4(1f, 1f, 1f, 0f));

        [MenuItem("GameObject/SDFs/Torus", false, priority: 2)]
        private static void CreateTorus(MenuCommand menuCommand) =>
            CreateNewPrimitive(SDFPrimitiveType.Torus, new Vector4(1f, 0.5f, 0f, 0f));

        [MenuItem("GameObject/SDFs/Frame", false, priority: 2)]
        private static void CreateFrame(MenuCommand menuCommand) =>
            CreateNewPrimitive(SDFPrimitiveType.BoxFrame, new Vector4(1f, 1f, 1f, 0.2f));

        [MenuItem("GameObject/SDFs/Cylinder", false, priority: 2)]
        private static void CreateCylinder(MenuCommand menuCommand) =>
            CreateNewPrimitive(SDFPrimitiveType.Cylinder, new Vector4(1f, 1f, 0f, 0f));

#endif

        #endregion
    }

    public enum SDFPrimitiveType
    {
        Sphere,
        Torus,
        Cuboid,
        BoxFrame,
        Cylinder
    }
}