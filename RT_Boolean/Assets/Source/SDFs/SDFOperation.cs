using Source.Utilities;
using UnityEditor;
using UnityEngine;

// ReSharper disable Unity.NoNullPropagation

namespace Source.SDFs
{
    public class SDFOperation : SDFObject
    {
        #region Fields

        public SDFOperationType Type => SDFOperationType.Elongate;
        [SerializeField] private Vector4 data = new Vector4(0, 0, 0, 0);
        public Vector4 Data => data;

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

        public void SetData(Vector4 v4data)
        {
            data = v4data;
            SetDirty();
        }

        public override SdfGpuData GetSdfGpuData(int sampleStartIndex = -1, int uvStartIndex = -1)
        {
            var v4data = data;
            if (Type == SDFOperationType.Elongate)
            {
                v4data = new Vector4(Mathf.Max(0, v4data.x), Mathf.Max(0, v4data.y), Mathf.Max(0, v4data.z),
                    Mathf.Max(0, v4data.w));
            }

            return new SdfGpuData()
            {
                Type = -(int)Type - 1,
                Transform = transform.worldToLocalMatrix,
                Data = v4data
            };
        }

        #endregion

        #region Create Menu Items

#if UNITY_EDITOR
        private static void CreateNewOperation(SDFOperationType type)
        {
            var selection = Selection.activeGameObject;
            var child = new GameObject(type.ToString());
            child.transform.SetParent(selection.transform);
            child.transform.Reset();
            var newPrimitive = child.AddComponent<SDFOperation>();
            Selection.activeGameObject = child;
        }

        [MenuItem("GameObject/SDFs/Operation/Elongate", false, priority: 2)]
        private static void CreateElongateOperation(MenuCommand menuCommand) =>
            CreateNewOperation(SDFOperationType.Elongate);

        //[MenuItem("GameObject/SDFs/Operation/Round", false, priority: 2)]
        //private static void CreateRoundOperation(MenuCommand menuCommand) => CreateNewOperation(SDFOperationType.Round);

        //[MenuItem("GameObject/SDFs/Operation/Onion", false, priority: 2)]
        //private static void CreateOnionOperation(MenuCommand menuCommand) => CreateNewOperation(SDFOperationType.Onion);
#endif

        #endregion
    }

    public enum SDFOperationType
    {
        Elongate,
        Round,
        Onion
    }
}