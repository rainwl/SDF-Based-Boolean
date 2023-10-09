using System.Collections.Generic;
using System.Linq;
using Source.Utilities;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Source.SDFs
{
    /// <summary>
    /// An SDF group is a collection of sdf primitives, meshes, and operations which mutually interact.
    /// This class is not responsible for rendering the result or indeed doing anything with it.
    /// Instead, it dispatches the resulting buffers to SDF Components.
    /// These components must be a child of the group and implement the I SDF Component interface.
    /// This class also contains the functionality to directly provide information about the SDF, like ray casting.
    /// </summary>
    [ExecuteInEditMode]
    public class SDFGroup : MonoBehaviour
    {
        private static class GlobalProperties
        {
            public static readonly int MeshSamplesStructuredBuffer = Shader.PropertyToID("_SDFMeshSamples");
            public static readonly int MeshPackedUVsStructuredBuffer = Shader.PropertyToID("_SDFMeshPackedUVs");
        }

        #region Fields and Properties

        private const float MinSmoothing = 0.00001f;

        // this bool is toggled off/on whenever the Unity callbacks OnEnable/OnDisable are called.
        // note that this doesn't always give the same result as "enabled" because OnEnable/OnDisable are
        // called during recompiles etc. you can basically read this bool as "is recompiling"
        [SerializeField] [HideInInspector] private bool isEnabled = false;
        [SerializeField] private float thicknessMaxDistance = 0f;
        [SerializeField] private float thicknessFalloff = 0f;
        [SerializeField] private float normalSmoothing = 0.015f;
        [SerializeField] private bool isRunning = true;
        private bool _forceUpdateNextFrame;

        public bool IsRunning => isRunning; // Whether this group is actively updating.

        // Whether this group is fully set up, e.g. all the buffers are created.
        public bool IsReady { get; private set; }
        private bool _isLocalDataDirty = true;
        private bool _isLocalDataOrderDirty = true;
        private static bool _isGlobalMeshDataDirty = true;

        public bool IsEmpty => _sdfObjects.IsNullOrEmpty();

        // The mapper allows you to quickly query the SDF without involving the GPU.
        private Mapper Mapper { get; } = new Mapper();
        public float NormalSmoothing => normalSmoothing;

        private ComputeBuffer _dataBuffer;
        private ComputeBuffer _materialBuffer;
        private ComputeBuffer _settingsBuffer;
        private static ComputeBuffer _meshSamplesBuffer;
        private static ComputeBuffer _meshPackedUVsBuffer;
        public ComputeBuffer SettingsBuffer => _settingsBuffer;

        private readonly Settings[] _settingsArray = new Settings[1];
        private readonly List<ISDFGroupComponent> _sdfComponents = new List<ISDFGroupComponent>();

        private static readonly List<SDFMesh> GlobalSDFMeshes = new List<SDFMesh>();
        private static readonly Dictionary<int, int> MeshSdfSampleStartIndices = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> MeshSdfUVStartIndices = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> MeshCounts = new Dictionary<int, int>();
        private static readonly List<float> MeshSamples = new List<float>();
        private static readonly List<float> MeshPackedUVs = new List<float>();

        private readonly List<SdfGpuData> _data = new List<SdfGpuData>();
        private readonly List<SDFMaterialGPU> _materials = new List<SDFMaterialGPU>();
        private readonly List<int> _dataSiblingIndices = new List<int>();
        private List<SDFObject> _sdfObjects = new List<SDFObject>();

        #endregion

        #region Registration

        public void Register(SDFObject sdfObject)
        {
            if (_sdfObjects.Contains(sdfObject))
                return;

            if (sdfObject is SDFMesh sdfMesh)
            {
                // check if this is a totally new mesh that no group has seen
                if (!GlobalSDFMeshes.Contains(sdfMesh))
                {
                    GlobalSDFMeshes.Add(sdfMesh);
                    _isGlobalMeshDataDirty = true;
                }

                // keep track of how many groups contain a local reference to this sdfmesh
                MeshCounts.TryAdd(sdfMesh.ID, 0);

                MeshCounts[sdfMesh.ID]++;
            }

            var wasEmpty = IsEmpty;

            _sdfObjects.Add(sdfObject);
            _isLocalDataDirty = true;
            _isLocalDataOrderDirty = true;

            // this is almost certainly overkill, but i like the kind of guaranteed stability
            ClearNulls(_sdfObjects);

            if (wasEmpty && !IsEmpty)
                foreach (var t in _sdfComponents)
                    t.OnNotEmpty();

            RequestUpdate();
        }

        public void Deregister(SDFObject sdfObject)
        {
            bool wasEmpty = IsEmpty;

            if (!_sdfObjects.Remove(sdfObject))
                return;

            if (sdfObject is SDFMesh sdfMesh)
            {
                // if this was the only group referencing this sdfmesh, we can remove it from the global buffer too
                if (MeshCounts.ContainsKey(sdfMesh.ID))
                {
                    MeshCounts[sdfMesh.ID]--;

                    if (MeshCounts[sdfMesh.ID] <= 0 && GlobalSDFMeshes.Remove(sdfMesh))
                        _isGlobalMeshDataDirty = true;
                }
            }

            _isLocalDataDirty = true;
            _isLocalDataOrderDirty = true;

            // this is almost certainly overkill
            ClearNulls(_sdfObjects);

            if (!wasEmpty && IsEmpty)
                for (int i = 0; i < _sdfComponents.Count; i++)
                    _sdfComponents[i].OnEmpty();

            RequestUpdate();
        }

        public bool IsRegistered(SDFObject sdfObject) =>
            !_sdfObjects.IsNullOrEmpty() && _sdfObjects.Contains(sdfObject);

        #endregion

        #region MonoBehaviour Callbacks

        private void OnEnable()
        {
#if UNITY_EDITOR
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif

            isEnabled = true;
            _isGlobalMeshDataDirty = true;
            _isLocalDataDirty = true;
            _isLocalDataOrderDirty = true;

            RequestUpdate(onlySendBufferOnChange: false);
            _forceUpdateNextFrame = true;
        }

        private void Start()
        {
            isEnabled = true;
            _isGlobalMeshDataDirty = true;
            _isLocalDataDirty = true;
            _isLocalDataOrderDirty = true;

            RequestUpdate(onlySendBufferOnChange: false);
            _forceUpdateNextFrame = true;
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif

            isEnabled = false;
            IsReady = false;

            _dataBuffer?.Dispose();
            _materialBuffer?.Dispose();
            _settingsBuffer?.Dispose();
        }

        private void OnApplicationQuit()
        {
            // static buffers can't be cleared in ondisable or something,
            // because lots of objects might be using them
            isEnabled = false;

            _meshSamplesBuffer?.Dispose();
            _meshPackedUVsBuffer?.Dispose();
        }

        private void LateUpdate()
        {
            if (!isRunning)
                return;

            if (!IsReady)
                RequestUpdate();

            var nullHit = false;
            foreach (var sdfObject in _sdfObjects)
            {
                var isNull = !sdfObject;

                nullHit |= isNull;

                if (isNull) continue;
                _isLocalDataDirty |= sdfObject.IsDirty;
                _isLocalDataOrderDirty |= sdfObject.IsOrderDirty;
            }

            if (nullHit)
                ClearNulls(_sdfObjects);

            var changed = false;

            if (_isLocalDataOrderDirty)
            {
                ReorderObjects();
                changed = true;
            }

            if (changed || _forceUpdateNextFrame || _isGlobalMeshDataDirty || _isLocalDataDirty ||
                transform.hasChanged)
            {
                changed = true;
                RebuildData();
            }

            _forceUpdateNextFrame = false;
            transform.hasChanged = false;

            if (!changed || IsEmpty) return;
            foreach (var sdfComponent in _sdfComponents)
                sdfComponent.Run();
        }

        #endregion

        #region Buffer Updating

        /// <summary>
        /// Request a complete buffer rebuild.
        /// </summary>
        /// <param name="onlySendBufferOnChange">Whether to invoke the components and inform them the buffer has changed. This is only really necessary when the size changes.</param>
        public void RequestUpdate(bool onlySendBufferOnChange = true)
        {
            if (!isEnabled)
                return;

            // blocking readiness because we're updating 
            // all the information at once, we don't want groups to start acting
            // on the info immediately
            IsReady = false;

            _sdfComponents.Clear();
            _sdfComponents.AddRange(GetComponentsInChildren<ISDFGroupComponent>());

            if (IsEmpty)
                foreach (var sdfComponent in _sdfComponents)
                    sdfComponent.OnEmpty();
            else
                foreach (var sdfComponent in _sdfComponents)
                    sdfComponent.OnNotEmpty();

            RebuildData(onlySendBufferOnChange);
            OnSettingsChanged();

            IsReady = true;

            if (IsEmpty) return;
            {
                foreach (var sdfComponent in _sdfComponents)
                    sdfComponent.Run();
            }
        }

        /// <summary>
        /// Some mesh data is shared across all instances, such as the sample and UV information as well as the start indices in those static buffers
        /// for all meshes. Returns true if the static buffers have been changed and need to be resent to the groups.
        /// </summary>
        /// <param name="locals">List of SDFMesh objects to ensure are in the global list.</param>
        /// <param name="onlySendBufferOnChange">Whether to invoke the components and inform them the buffer has changed. This is only really necessary when the size changes.</param>
        private static bool RebuildGlobalMeshData(IList<SDFObject> locals, bool onlySendBufferOnChange = true)
        {
            var previousMeshSamplesCount = MeshSamples.Count;
            var previousMeshUVsCount = MeshPackedUVs.Count;

            MeshSamples.Clear();
            MeshPackedUVs.Clear();

            MeshSdfSampleStartIndices.Clear();
            MeshSdfUVStartIndices.Clear();

            // remove null refs
            for (var i = GlobalSDFMeshes.Count - 1; i >= 0; --i)
                if (GlobalSDFMeshes[i] == null || GlobalSDFMeshes[i].Asset == null)
                    GlobalSDFMeshes.RemoveAt(i);

            foreach (var sdfObj in locals)
                if (sdfObj is SDFMesh mesh && !GlobalSDFMeshes.Contains(mesh))
                    GlobalSDFMeshes.Add(mesh);

            // 循环遍历每个网格，将其样本/uv添加到样本缓冲区中，并注意每个网格样本在缓冲区中的起始位置。检查重复，这样我们就不会两次向样本缓冲区添加相同的网格
            // loop over each mesh, adding its samples/uvs to the sample buffer
            // and taking note of where each meshes samples start in the buffer.
            // check for repeats so we don't add the same mesh to the samples buffer twice
            foreach (var mesh in GlobalSDFMeshes)
            {
                // ignore meshes which are in the list but not present in any group
                if (MeshCounts.TryGetValue(mesh.ID, out int count) && count <= 0)
                    continue;

                mesh.Asset.GetDataArrays(out float[] samples, out float[] packedUVs);

                if (!MeshSdfSampleStartIndices.ContainsKey(mesh.ID))
                {
                    var startIndex = MeshSamples.Count;
                    MeshSamples.AddRange(samples);
                    MeshSdfSampleStartIndices.Add(mesh.ID, startIndex);
                }

                if (mesh.Asset.HasUVs && !MeshSdfUVStartIndices.ContainsKey(mesh.ID))
                {
                    var startIndex = MeshPackedUVs.Count;
                    MeshPackedUVs.AddRange(packedUVs);
                    MeshSdfUVStartIndices.Add(mesh.ID, startIndex);
                }
            }

            var newBuffers = false;

            if (_meshSamplesBuffer == null || !_meshSamplesBuffer.IsValid() ||
                previousMeshSamplesCount != MeshSamples.Count)
            {
                _meshSamplesBuffer?.Dispose();
                _meshSamplesBuffer = new ComputeBuffer(Mathf.Max(1, MeshSamples.Count), sizeof(float),
                    ComputeBufferType.Structured);
                newBuffers = true;
            }

            if (MeshSamples.Count > 0)
                _meshSamplesBuffer.SetData(MeshSamples);

            if (_meshPackedUVsBuffer == null || !_meshPackedUVsBuffer.IsValid() ||
                previousMeshUVsCount != MeshPackedUVs.Count)
            {
                _meshPackedUVsBuffer?.Dispose();
                _meshPackedUVsBuffer = new ComputeBuffer(Mathf.Max(1, MeshPackedUVs.Count), sizeof(float),
                    ComputeBufferType.Structured);
                newBuffers = true;
            }

            if (MeshPackedUVs.Count > 0)
                _meshPackedUVsBuffer.SetData(MeshPackedUVs);

            _isGlobalMeshDataDirty = false;

            return newBuffers;
        }

        /// <summary>
        /// 按兄弟索引对sdf对象列表排序，这确保该列表始终与unity层次结构中显示的顺序相同。这很重要，因为一些sdf操作是有序的
        /// Sort the list of sdf objects by sibling index, which ensures that this list is always in the same
        /// order as is shown in the unity hierarchy. This is important because some of the sdf operations are ordered
        /// </summary>
        private void ReorderObjects()
        {
            _dataSiblingIndices.Clear();

            ClearNulls(_sdfObjects);

            foreach (var sdfObj in _sdfObjects)
            {
                _dataSiblingIndices.Add(sdfObj.transform.GetSiblingIndex());
                sdfObj.SetOrderClean();
            }

            _sdfObjects = _sdfObjects.OrderBy(d => _dataSiblingIndices[_sdfObjects.IndexOf(d)]).ToList();

            _isLocalDataOrderDirty = false;
        }

        /// <summary>
        /// 重新填充与SDF原语(球体，环体，长方体等)和SDF网格(指向样本和uv数据列表中的起始位置，以及它们的大小)相关的数据。
        /// Repopulate the data relating to SDF primitives (spheres, toruses, cuboids etc) and SDF meshes (which point to where in the list of sample and uv data they begin, and how large they are)
        /// </summary>
        /// <param name="onlySendBufferOnChange">Whether to invoke the components and inform them the buffer has changed. This is only really necessary when the size changes.
        /// 是否调用组件并通知它们缓冲区已更改。只有当大小发生变化时才需要这样做。</param>
        private void RebuildData(bool onlySendBufferOnChange = true)
        {
            _isLocalDataDirty = false;

            // should we rebuild the buffers which contain mesh sample + uv data?
            var globalBuffersChanged = false;
            if (_meshSamplesBuffer == null || !_meshSamplesBuffer.IsValid() || _meshPackedUVsBuffer == null ||
                !_meshPackedUVsBuffer.IsValid() || _isGlobalMeshDataDirty)
                globalBuffersChanged = RebuildGlobalMeshData(_sdfObjects, onlySendBufferOnChange);

            // memorize the size of the array before clearing it, for later comparison
            var previousCount = _data.Count;
            _data.Clear();
            _materials.Clear();

            // add all the sdf objects
            foreach (var sdfObject in _sdfObjects.Where(sdfObject => sdfObject))
            {
                sdfObject.SetClean();

                var meshStartIndex = -1;
                var uvStartIndex = -1;

                if (sdfObject is SDFMesh mesh)
                {
                    // get the index in the global samples buffer where this particular mesh's samples begin
                    if (!MeshSdfSampleStartIndices.TryGetValue(mesh.ID, out meshStartIndex))
                        globalBuffersChanged =
                            RebuildGlobalMeshData(_sdfObjects,
                                onlySendBufferOnChange); // we don't recognize this mesh so we may need to rebuild the entire global list of mesh samples and UVs

                    // likewise, if this mesh has UVs, get the index where they begin too
                    if (mesh.Asset.HasUVs)
                        MeshSdfUVStartIndices.TryGetValue(mesh.ID, out uvStartIndex);
                }

                _data.Add(sdfObject.GetSdfGpuData(meshStartIndex, uvStartIndex));
                _materials.Add(sdfObject.GetMaterial());
            }

            var sendBuffer = !onlySendBufferOnChange;

            // check whether we need to create a new buffer. buffers are fixed sizes so the most common occasion for this is simply a change of size
            if (_dataBuffer == null || !_dataBuffer.IsValid() || previousCount != _data.Count)
            {
                sendBuffer = true;

                _dataBuffer?.Dispose();
                _dataBuffer = new ComputeBuffer(Mathf.Max(1, _data.Count), SdfGpuData.Stride,
                    ComputeBufferType.Structured);
            }

            // check whether we need to create a new buffer. buffers are fixed sizes so the most common occasion for this is simply a change of size
            if (_materialBuffer == null || !_materialBuffer.IsValid() || previousCount != _data.Count)
            {
                sendBuffer = true;

                _materialBuffer?.Dispose();
                _materialBuffer = new ComputeBuffer(Mathf.Max(1, _data.Count), SDFMaterialGPU.Stride,
                    ComputeBufferType.Structured);
            }

            // if the buffer is new, the size has changed, or if it's forced, we resend the buffer to the sdf group component classes
            if (sendBuffer)
            {
                foreach (var sdfGroupCom in _sdfComponents)
                    sdfGroupCom.UpdateDataBuffer(_dataBuffer, _materialBuffer, _data.Count);
            }

            if (_data.Count > 0)
            {
                _dataBuffer.SetData(_data);
                _materialBuffer.SetData(_materials);
            }

            Mapper.SetData(_data, _materials);

            // if we also changed the global mesh data buffer in this method, we need to send that as well
            if (!onlySendBufferOnChange || globalBuffersChanged)
            {
                Shader.SetGlobalBuffer(GlobalProperties.MeshSamplesStructuredBuffer, _meshSamplesBuffer);
                Shader.SetGlobalBuffer(GlobalProperties.MeshPackedUVsStructuredBuffer, _meshPackedUVsBuffer);

                Mapper.SetMeshData(MeshSamples, MeshPackedUVs);
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// To be called whenever the settings universal to this group change.
        /// </summary>
        private void OnSettingsChanged()
        {
            _settingsArray[0] = new Settings()
            {
                //Smoothing = Mathf.Max(MIN_SMOOTHING, m_smoothing),
                NormalSmoothing = Mathf.Max(MinSmoothing, normalSmoothing),
                ThicknessMaxDistance = thicknessMaxDistance,
                ThicknessFalloff = thicknessFalloff,
            };

            if (_settingsBuffer == null || !_settingsBuffer.IsValid())
            {
                _settingsBuffer?.Dispose();
                _settingsBuffer = new ComputeBuffer(1, Settings.Stride, ComputeBufferType.Structured);
            }

            foreach (var sdfGroupCom in _sdfComponents)
                sdfGroupCom.UpdateSettingsBuffer(_settingsBuffer);

            Mapper.SetSettings(_settingsArray[0]);

            _settingsBuffer.SetData(_settingsArray);
        }

        private void OnCompilationStarted(object param)
        {
            isEnabled = false;

            _meshSamplesBuffer?.Dispose();
            _meshPackedUVsBuffer?.Dispose();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange stateChange)
        {
            // this ensures "m_isEnabled" is set to false while transitioning between play modes
            isEnabled = stateChange == PlayModeStateChange.EnteredPlayMode ||
                        stateChange == PlayModeStateChange.EnteredEditMode;

            _meshSamplesBuffer?.Dispose();
            _meshPackedUVsBuffer?.Dispose();
        }

        #endregion

        #region Structs

        public struct Settings
        {
            public static int Stride => sizeof(float) * 3;

            // 用于计算梯度的'epsilon'值会影响法线的平滑程度
            public float
                NormalSmoothing; // the 'epsilon' value for computing the gradient, affects how smoothed out the normals are

            public float ThicknessMaxDistance;
            public float ThicknessFalloff;
        }

        #endregion

        #region Public Methods

        public Vector3 GetNearestPointOnSurface(Vector3 p) => GetNearestPointOnSurface(p, out _, out _);

        private Vector3 GetNearestPointOnSurface(Vector3 p, out float signedDistance) =>
            GetNearestPointOnSurface(p, out signedDistance, out _);

        private Vector3 GetNearestPointOnSurface(Vector3 p, out float signedDistance, out Vector3 direction)
        {
            signedDistance = Mapper.Map(p);
            direction = -Mapper.MapNormal(p);

            return p + signedDistance * direction;
        }

        public Vector3 GetSurfaceNormal(Vector3 p) => Mapper.MapNormal(p);

        public float GetDistanceToSurface(Vector3 p) => Mapper.Map(p);

        private bool OverlapSphere(Vector3 centre, float radius) => Mapper.Map(centre) <= radius;

        //public bool OverlapBox(Vector3 centre, Vector3 halfExtents, bool surfaceOnly = true) => OverlapBox(centre, halfExtents, Quaternion.identity, surfaceOnly);

        public bool OverlapBox(Vector3 centre, Vector3 halfExtents)
        {
            if (!OverlapSphere(centre, halfExtents.magnitude))
                return false;

            var maxBounds = centre + halfExtents;
            var minBounds = centre - halfExtents;

            return (Check(centre) ||
                    Check(centre + halfExtents) ||
                    Check(centre - halfExtents) ||
                    Check(centre + new Vector3(halfExtents.x, halfExtents.y, -halfExtents.z)) ||
                    Check(centre + new Vector3(halfExtents.x, -halfExtents.y, halfExtents.z)) ||
                    Check(centre + new Vector3(halfExtents.x, -halfExtents.y, -halfExtents.z)) ||
                    Check(centre + new Vector3(-halfExtents.x, halfExtents.y, halfExtents.z)) ||
                    Check(centre + new Vector3(-halfExtents.x, halfExtents.y, -halfExtents.z)) ||
                    Check(centre + new Vector3(-halfExtents.x, -halfExtents.y, halfExtents.z)));

            bool Check(Vector3 p)
            {
                var surfaceP = GetNearestPointOnSurface(p, out float signedDistance);

                var isInside =
                    surfaceP.x >= minBounds.x && surfaceP.x <= maxBounds.x &&
                    surfaceP.y >= minBounds.y && surfaceP.y <= maxBounds.y &&
                    surfaceP.z >= minBounds.z && surfaceP.z <= maxBounds.z;

                return isInside;
            }
        }

        /// <summary>
        /// Raycast the sdf group. This is done via raymarching on the CPU side.
        /// </summary>
        public bool Raycast(Vector3 origin, Vector3 direction, out Vector3 hitPoint, out Vector3 hitNormal,
            float maxDist = 350f) =>
            Mapper.RayMarch(origin, direction, out hitPoint, out hitNormal, maxDist);

        #endregion

        #region Helper Methods

        private static void ClearNulls<T>(IList<T> list) where T : MonoBehaviour
        {
            for (var i = list.Count - 1; i >= 0; --i)
                if (!list[i])
                    list.RemoveAt(i);
        }

        #endregion
    }
}