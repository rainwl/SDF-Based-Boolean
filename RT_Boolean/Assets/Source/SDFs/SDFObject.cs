using Unity.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace Source.SDFs
{
    [ExecuteInEditMode]
    public abstract class SDFObject : MonoBehaviour
    {
        #region Fields

        protected const float MinSmoothing = 0.000000001f;
        [SerializeField] protected float smoothing = MinSmoothing;
        [SerializeField] [ReadOnly] private SDFGroup sdfGroup;

        [SerializeField] private SDFMaterial material =
            new SDFMaterial(Color.white, Color.black, 0.5f, 0.5f, Color.black, 0f, 0.1f);

        private int _lastSeenSiblingIndex = -1;

        protected SDFGroup Group
        {
            get
            {
                if (!sdfGroup)
                    sdfGroup = GetComponentInParent<SDFGroup>();
                return sdfGroup;
            }
        }

        public SDFMaterial Material => material;
        public bool IsDirty { get; private set; } = false;

        public bool IsOrderDirty { get; private set; } = false;

        #endregion

        #region Virtual Methods

        protected virtual void Awake() => TryRegister();
        protected virtual void Reset() => TryRegister();
        protected virtual void OnEnable() => TryRegister();
        protected virtual void OnDisable() => TryDeregister();
        protected virtual void OnDestroy() => TryDeregister();
        protected virtual void OnValidate() => SetDirty();

        protected virtual void Update()
        {
            IsDirty |= transform.hasChanged;

            var siblingIndex = transform.GetSiblingIndex();

            if (siblingIndex != _lastSeenSiblingIndex)
            {
                if (_lastSeenSiblingIndex != -1)
                    IsOrderDirty = true;
                _lastSeenSiblingIndex = siblingIndex;
            }

            transform.hasChanged = false;
        }

        /// <summary>
        /// Get 'SDFGroup' Component in Parent
        /// </summary>
        protected virtual void TryDeregister()
        {
            sdfGroup = GetComponentInParent<SDFGroup>();
            SetClean();
        }

        protected virtual void TryRegister()
        {
            _lastSeenSiblingIndex = transform.GetSiblingIndex();
            sdfGroup = GetComponentInParent<SDFGroup>();
            SetDirty();
        }

        #endregion


        #region Other Methods

        public abstract SdfGpuData GetSdfGpuData(int sampleStartIndex = -1, int uvStartIndex = -1);
        public SDFMaterialGPU GetMaterial() => new SDFMaterialGPU(material);

        protected void SetDirty() => IsDirty = true;

        public void SetClean()
        {
            IsDirty = false;
            transform.hasChanged = false;
        }

        public void SetOrderClean()
        {
            IsOrderDirty = false;
        }

        #endregion
    }

    public enum SDFCombineType
    {
        SmoothUnion,
        SmoothSubtract,
        SmoothIntersect
    }
}