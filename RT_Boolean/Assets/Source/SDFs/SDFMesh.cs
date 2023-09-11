using Source.Utilities;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// ReSharper disable Unity.NoNullPropagation

namespace Source.SDFs
{
    public class SDFMesh : SDFObject
    {
        #region Fields

        [SerializeField] private SDFMeshAsset asset;
        [SerializeField] protected SDFCombineType operation;
        [SerializeField] protected bool flip;

        public SDFMeshAsset Asset => asset;
        public int ID => asset.GetInstanceID();

        #endregion

        #region Methods

        protected override void TryRegister()
        {
            if (!asset) return;
            base.TryRegister();
            Group?.Register(this);
        }

        protected override void TryDeregister()
        {
            if (!asset)
            {
                return;
            }

            base.TryDeregister();
            Group?.Deregister(this);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            if (Group && !Group.IsRegistered(this) && asset)
            {
                TryRegister();
            }
        }

        #endregion

        public override SdfGpuData GetSdfGpuData(int sampleStartIndex = -1, int uvStartIndex = -1)
        {
            return new SdfGpuData()
            {
                Type = 0,
                Data = new Vector4(asset.Size, sampleStartIndex, uvStartIndex),
                Transform = transform.worldToLocalMatrix,
                CombineType = (int)operation,
                Flip = flip ? -1 : 1,
                MinBounds = asset.MinBounds,
                MaxBounds = asset.MaxBounds,
                Smoothing = Mathf.Max(MinSmoothing, smoothing)
            };
        }
        
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!asset)
                return;

            Handles.color = Color.white;
            Handles.matrix = transform.localToWorldMatrix;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.DrawWireCube((asset.MaxBounds + asset.MinBounds) * 0.5f, (asset.MaxBounds - asset.MinBounds));
        }
#endif

        #region Create Menu Items

        [MenuItem("GameObject/SDFs/Mesh", false, priority: 2)]
        private static void CreateSDFMesh(MenuCommand menuCommand)
        {
            var selection = Selection.activeGameObject;

            var child = new GameObject("Mesh");
            child.transform.SetParent(selection.transform);
            child.transform.Reset();

            var newMesh = child.AddComponent<SDFMesh>();

            Selection.activeGameObject = child;
        }

        #endregion
    }
}