using Unity.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace Source.SDFs
{
    public abstract class SDFObject : MonoBehaviour
    {
        #region Fields

        protected const float MinSmoothing = 0.000000001f;
        [SerializeField] protected float smoothing = MinSmoothing;
        [SerializeField] [ReadOnly] private SDFGroup sdfGroup;

        [SerializeField] private readonly SDFMaterial _material =
            new SDFMaterial(Color.white, Color.black, 0.5f, 0.5f, Color.black, 0f, 0.1f);

        private bool _isDirty = false;
        private bool _isOrderDirty = false;
        private int _lastSeenSiblingIndex = -1;


        public SDFGroup Group
        {
            get
            {
                if (!sdfGroup)
                    sdfGroup = GetComponentInParent<SDFGroup>();
                return sdfGroup;
            }
        }

        public SDFMaterial Material => _material;
        public bool IsDirty => _isDirty;
        public bool IsOrderDirty => _isOrderDirty;

        #endregion

        #region Virtual Methods

        protected virtual void Awake() => TryRegister();
        protected virtual void Reset() => TryRegister();
        protected virtual void OnEnable() => TryRegister();
        protected virtual void OnDisable() => TryDeregister();
        protected virtual void OnDestroy() => TryDeregister();
        protected virtual void OnValidate() => SetDirty();

        #endregion

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

        public abstract SdfGpuData GetSdfGpuData(int sampleStartIndex = -1, int uvStartIndex = -1);
        public SDFMaterialGPU GetMaterial() => new SDFMaterialGPU(_material);

        protected void SetDirty() => _isDirty = true;

        public void SetClean()
        {
            _isDirty = false;
            transform.hasChanged = false;
        }

        public void SetOrderClean()
        {
            _isOrderDirty = false;
        }

        protected virtual void Update()
        {
            _isDirty |= transform.hasChanged;

            int siblingIndex = transform.GetSiblingIndex();

            if (siblingIndex != _lastSeenSiblingIndex)
            {
                if (_lastSeenSiblingIndex != -1)
                    _isOrderDirty = true;

                _lastSeenSiblingIndex = siblingIndex;
            }

            transform.hasChanged = false;
        }
    }

    public enum SDFCombineType
    {
        SmoothUnion,
        SmoothSubtract,
        SmoothIntersect
    }
}