using System.Collections;
using System.Runtime.InteropServices;
using Source.SDFs.Settings;
using Source.Utilities;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Source.SDFs
{
    /// <summary>
    /// This class passes SDF data to an iso surface extraction compute shader and returns a mesh.
    /// This mesh can be passed directly to a material as a triangle and index buffer in 'Procedural' mode,
    /// or transfer to the CPU and sent to a MeshFilter in 'Mesh' mode.
    /// 这个类将SDF数据传递给iso表面提取计算着色器并返回一个网格。
    /// 这个网格可以在“过程”模式下作为三角形和索引缓冲区直接传递给材质，或者在“网格”模式下传递给CPU并发送给MeshFilter。
    /// </summary>
    public class SDFGroupMeshGenerator : MonoBehaviour, ISDFGroupComponent
    {
        private static class Properties
        {
            public static readonly int PointsPerSideInt = Shader.PropertyToID("_PointsPerSide");
            public static readonly int CellSizeFloat = Shader.PropertyToID("_CellSize");
            public static readonly int BinarySearchIterationsInt = Shader.PropertyToID("_BinarySearchIterations");
            public static readonly int IsoSurfaceExtractionTypeInt = Shader.PropertyToID("_IsosurfaceExtractionType");
            public static readonly int MaxAngleCosineFloat = Shader.PropertyToID("_MaxAngleCosine");
            public static readonly int VisualNormalSmoothing = Shader.PropertyToID("_VisualNormalSmoothing");
            public static readonly int GradientDescentIterationsInt =
                Shader.PropertyToID("_GradientDescentIterations");
            public static readonly int SettingsStructuredBuffer = Shader.PropertyToID("_Settings");
            public static readonly int TransformMatrix4X4 = Shader.PropertyToID("_GroupTransform");
            public static readonly int SDFDataStructuredBuffer = Shader.PropertyToID("_SDFData");
            public static readonly int SDFMaterialsStructuredBuffer = Shader.PropertyToID("_SDFMaterials");
            public static readonly int SDFDataCountInt = Shader.PropertyToID("_SDFDataCount");
            public static readonly int SamplesRWBuffer = Shader.PropertyToID("_Samples");
            public static readonly int VertexDataAppendBuffer = Shader.PropertyToID("_VertexDataPoints");
            public static readonly int CellDataRWBuffer = Shader.PropertyToID("_CellDataPoints");
            public static readonly int CounterRWBuffer = Shader.PropertyToID("_Counter");
            public static readonly int TriangleDataAppendBuffer = Shader.PropertyToID("_TriangleDataPoints");
            public static readonly int VertexDataStructuredBuffer =
                Shader.PropertyToID("_VertexDataPoints_Structured");
            public static readonly int TriangleDataStructuredBuffer =
                Shader.PropertyToID("_TriangleDataPoints_Structured");
            public static readonly int MeshTransformMatrix4X4 = Shader.PropertyToID("_MeshTransform");
            public static readonly int MeshVerticesRWBuffer = Shader.PropertyToID("_MeshVertices");
            public static readonly int MeshNormalsRWBuffer = Shader.PropertyToID("_MeshNormals");
            public static readonly int MeshTrianglesRWBuffer = Shader.PropertyToID("_MeshTriangles");
            //public static readonly int MeshUVs_RWBuffer = Shader.PropertyToID("_MeshUVs");
            public static readonly int MeshVertexColoursRWBuffer = Shader.PropertyToID("_MeshVertexColours");
            public static readonly int MeshVertexMaterialsRWBuffer = Shader.PropertyToID("_MeshVertexMaterials");
            public static readonly int IntermediateVertexBufferAppendBuffer =
                Shader.PropertyToID("_IntermediateVertexBuffer");
            public static readonly int IntermediateVertexBufferStructuredBuffer =
                Shader.PropertyToID("_IntermediateVertexBuffer_Structured");
            public static readonly int ProceduralArgsRWBuffer = Shader.PropertyToID("_ProceduralArgs");
        }

        #region Fields and Properties

        private struct Kernels
        {
            private const string MapKernelName = "Isosurface_Map";
            private const string GenerateVerticesKernelName = "Isosurface_GenerateVertices";
            private const string NumberVerticesKernelName = "Isosurface_NumberVertices";
            private const string GenerateTrianglesKernelName = "Isosurface_GenerateTriangles";
            private const string BuildIndexBufferKernelName = "Isosurface_BuildIndexBuffer";
            private const string AddIntermediateVerticesToIndexBufferKernelName =
                "Isosurface_AddIntermediateVerticesToIndexBuffer";

            public int Map { get; }
            public int GenerateVertices { get; }
            public int NumberVertices { get; }
            public int GenerateTriangles { get; }
            public int BuildIndexBuffer { get; }
            public int AddIntermediateVerticesToIndexBuffer { get; }

            public Kernels(ComputeShader shader)
            {
                Map = shader.FindKernel(MapKernelName);
                GenerateVertices = shader.FindKernel(GenerateVerticesKernelName);
                NumberVertices = shader.FindKernel(NumberVerticesKernelName);
                GenerateTriangles = shader.FindKernel(GenerateTrianglesKernelName);
                BuildIndexBuffer = shader.FindKernel(BuildIndexBufferKernelName);
                AddIntermediateVerticesToIndexBuffer =
                    shader.FindKernel(AddIntermediateVerticesToIndexBufferKernelName);
            }
        }

        private static Kernels _kernels;

        // counter buffer has 18 integers: [vertex count, 1, 1, triangle count, 1, 1, vertex count / 64, 1, 1, triangle count / 64, 1, 1, intermediate vertex count, 1, 1, intermediate vertex count / 64, 1, 1]
        private readonly int[] _counterArray = new int[] { 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 1, 1 };
        private NativeArray<int> _outputCounterNativeArray;
        private readonly int[] _proceduralArgsArray = new int[] { 0, 1, 0, 0, 0 };

        private const int VertexCounter = 0;
        private const int TriangleCounter = 3;
        private const int VertexCounterDiv64 = 6;
        private const int TriangleCounterDiv64 = 9;
        private const int IntermediateVertexCounter = 12;
        private const int IntermediateVertexCounterDiv64 = 15;

        private const string ComputeShaderResourceName = "Compute_IsoSurfaceExtraction";

        private ComputeBuffer _samplesBuffer;
        private ComputeBuffer _cellDataBuffer;
        private ComputeBuffer _vertexDataBuffer;
        private ComputeBuffer _triangleDataBuffer;
        private ComputeBuffer _meshVerticesBuffer;
        private ComputeBuffer _meshNormalsBuffer;

        private ComputeBuffer _meshTrianglesBuffer;

        //private ComputeBuffer _meshUVsBuffer;
        private ComputeBuffer _meshVertexMaterialsBuffer;
        private ComputeBuffer _meshVertexColoursBuffer;
        private ComputeBuffer _intermediateVertexBuffer;
        private ComputeBuffer _counterBuffer;
        private ComputeBuffer _proceduralArgsBuffer;

        private NativeArray<Vector3> _nativeArrayVertices;

        private NativeArray<Vector3> _nativeArrayNormals;

        //private NativeArray<Vector2> _nativeArrayUVs;
        private NativeArray<Color> _nativeArrayColours;
        private NativeArray<int> _nativeArrayTriangles;

        private VertexData[] _vertices;
        private TriangleData[] _triangles;

        [SerializeField] private ComputeShader computeShader;

        private ComputeShader ComputeShader
        {
            get
            {
                if (computeShader)
                    return computeShader;

                Debug.Log("Attempting to load resource: " + ComputeShaderResourceName);

                computeShader = Resources.Load<ComputeShader>(ComputeShaderResourceName);

                if (!computeShader)
                    Debug.Log("Failed to load.");
                else
                    Debug.Log("Successfully loaded.");

                return computeShader;
            }
        }

        private ComputeShader _computeShaderInstance;

        [SerializeField] private SDFGroup group;

        public SDFGroup Group
        {
            get
            {
                if (group)
                    return group;

                if (TryGetComponent(out group))
                    return group;

                if (transform.parent.TryGetComponent(out group))
                    return group;

                return null;
            }
        }

        [SerializeField] [UnityEngine.Serialization.FormerlySerializedAs("m_meshGameObject")]
        private GameObject meshGameObject;

        [SerializeField] private MeshFilter meshFilter;

        private MeshFilter MeshFilter
        {
            get
            {
                if (!meshFilter && TryGetOrCreateMeshGameObject(out var meshGo))
                {
                    meshFilter = meshGo.GetOrAddComponent<MeshFilter>();
                    return meshFilter;
                }

                return meshFilter;
            }
        }

        [SerializeField] private MeshCollider meshCollider;

        private MeshCollider MeshCollider
        {
            get
            {
                if (!meshCollider && TryGetOrCreateMeshGameObject(out var meshGo))
                {
                    meshCollider = meshGo.GetComponent<MeshCollider>();
                    return meshCollider;
                }

                return meshCollider;
            }
        }

        [SerializeField] private Material proceduralMeshMaterial;

        private Material ProceduralMeshMaterial
        {
            get
            {
                if (!proceduralMeshMaterial)
                    proceduralMeshMaterial = Resources.Load<Material>($"Procedural_MeshMaterial");

                return proceduralMeshMaterial;
            }
        }

        [SerializeField] private MeshRenderer meshRenderer;

        private MeshRenderer MeshRenderer
        {
            get
            {
                if (!meshRenderer && TryGetOrCreateMeshGameObject(out var meshGo))
                {
                    meshRenderer = meshGo.GetOrAddComponent<MeshRenderer>();

                    if (!meshRenderer.sharedMaterial)
                        meshRenderer.sharedMaterial = ProceduralMeshMaterial;

                    return meshRenderer;
                }

                if (!meshRenderer.sharedMaterial)
                    meshRenderer.sharedMaterial = ProceduralMeshMaterial;

                return meshRenderer;
            }
        }

        private Mesh _mesh;

        private Bounds _bounds;

        private MaterialPropertyBlock _propertyBlock;

        [SerializeField] private MainSettings mainSettings = new MainSettings();
        public MainSettings MainSettings => mainSettings;

        [SerializeField] private VoxelSettings voxelSettings = new VoxelSettings();
        public VoxelSettings VoxelSettings => voxelSettings;

        [SerializeField] private AlgorithmSettings algorithmSettings = new AlgorithmSettings();
        public AlgorithmSettings AlgorithmSettings => algorithmSettings;

        private bool _initialized = false;

        [SerializeField] private bool showGrid = false;
        public bool ShowGrid => showGrid;

        private bool _isEnabled = false;

        #endregion

        #region MonoBehaviour Callbacks

        private void Reset()
        {
            _initialized = false;
            OnOutputModeChanged();
        }

        private void OnEnable()
        {
            _isEnabled = true;
            _initialized = false;

            if (Group.IsReady)
            {
                InitializeComputeShaderSettings();
                Group.RequestUpdate(onlySendBufferOnChange: false);
            }

#if UNITY_EDITOR
            Undo.undoRedoPerformed += OnUndo;
#endif
        }

        private void OnDisable()
        {
            _isEnabled = false;
            ReleaseUnmanagedMemory();

#if UNITY_EDITOR
            Undo.undoRedoPerformed -= OnUndo;
#endif
        }

        private void OnUndo()
        {
            if (_initialized)
            {
                _initialized = false;
                InitializeComputeShaderSettings();
                group.RequestUpdate();
            }
        }

        #endregion

        #region Mesh Stuff

        private void Update()
        {
            if ((transform.hasChanged || (mainSettings.OutputMode == OutputMode.MeshFilter &&
                                          TryGetOrCreateMeshGameObject(out var meshGo) &&
                                          meshGo.transform.hasChanged)) && Group.IsReady && !Group.IsEmpty &&
                Group.IsRunning)
            {
                if (TryGetOrCreateMeshGameObject(out meshGo))
                    meshGo.transform.hasChanged = false;

                SendTransformToGPU();
                UpdateMesh();
            }

            transform.hasChanged = false;

            if (this.meshGameObject)
                this.meshGameObject.transform.hasChanged = false;
        }

        private void LateUpdate()
        {
            if (!_initialized || !Group.IsReady || Group.IsEmpty)
                return;

            if (mainSettings.OutputMode == OutputMode.Procedural && mainSettings.ProceduralMaterial &&
                _proceduralArgsBuffer != null && _proceduralArgsBuffer.IsValid() && mainSettings.AutoUpdate)
                Graphics.DrawProceduralIndirect(mainSettings.ProceduralMaterial, _bounds, MeshTopology.Triangles,
                    _proceduralArgsBuffer, properties: _propertyBlock);
        }

        public void UpdateMesh()
        {
            if (!_initialized || !Group.IsReady || Group.IsEmpty)
                return;

            if (mainSettings.OutputMode == OutputMode.MeshFilter)
            {
                if (mainSettings.IsAsynchronous)
                {
                    if (!m_isCoroutineRunning)
                        StartCoroutine(Cr_GetMeshDataFromGPUAsync());
                }
                else
                {
                    GetMeshDataFromGPU();
                }
            }
            else
            {
                Dispatch();
            }
        }

        private void ReallocateNativeArrays(int vertexCount, int triangleCount, ref NativeArray<Vector3> vertices,
            ref NativeArray<Vector3> normals, ref NativeArray<Color> colours /*, ref NativeArray<Vector2> uvs*/,
            ref NativeArray<int> indices)
        {
            // to avoid lots of allocations here, i only create new arrays when
            // 1) there's no array to begin with
            // 2) the number of items to store is greater than the size of the current array
            // 3) the size of the current array is greater than the size of the entire buffer
            void ReallocateArrayIfNeeded<T>(ref NativeArray<T> array, int count) where T : struct
            {
                if (array == null || !array.IsCreated || array.Length < count)
                {
                    if (array != null && array.IsCreated)
                        array.Dispose();

                    array = new NativeArray<T>(count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                }
            }

            ReallocateArrayIfNeeded(ref vertices, vertexCount);
            ReallocateArrayIfNeeded(ref normals, vertexCount);
            //ReallocateArrayIfNeeded(ref uvs, vertexCount);
            ReallocateArrayIfNeeded(ref colours, vertexCount);
            ReallocateArrayIfNeeded(ref indices, triangleCount * 3);
        }

        private void GetMeshDataFromGPU()
        {
            Dispatch();

            if (_outputCounterNativeArray == null || !_outputCounterNativeArray.IsCreated)
                _outputCounterNativeArray = new NativeArray<int>(_counterBuffer.count, Allocator.Persistent);

            AsyncGPUReadbackRequest counterRequest =
                AsyncGPUReadback.RequestIntoNativeArray(ref _outputCounterNativeArray, _counterBuffer);

            if (counterRequest.hasError)
            {
                Debug.LogError("AsyncGPUReadbackRequest encountered an error.");
                return;
            }

            counterRequest.WaitForCompletion();

            if (counterRequest.hasError)
            {
                Debug.LogError("AsyncGPUReadbackRequest encountered an error.");
                return;
            }

            GetCounts(_outputCounterNativeArray, out int vertexCount, out int triangleCount);

            if (triangleCount > 0)
            {
                ReallocateNativeArrays(vertexCount, triangleCount, ref _nativeArrayVertices,
                    ref _nativeArrayNormals /*, ref m_nativeArrayUVs*/, ref _nativeArrayColours,
                    ref _nativeArrayTriangles);

                int vertexRequestSize =
                    Mathf.Min(_nativeArrayVertices.Length, _meshVerticesBuffer.count, vertexCount);
                int triangleRequestSize = Mathf.Min(_nativeArrayTriangles.Length, _meshTrianglesBuffer.count,
                    triangleCount * 3);

                AsyncGPUReadbackRequest vertexRequest = AsyncGPUReadback.RequestIntoNativeArray(
                    ref _nativeArrayVertices, _meshVerticesBuffer, vertexRequestSize * sizeof(float) * 3, 0);
                AsyncGPUReadbackRequest normalRequest = AsyncGPUReadback.RequestIntoNativeArray(
                    ref _nativeArrayNormals, _meshNormalsBuffer, vertexRequestSize * sizeof(float) * 3, 0);
                //AsyncGPUReadbackRequest uvRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_nativeArrayUVs, m_meshUVsBuffer, vertexRequestSize * sizeof(float) * 2, 0);
                AsyncGPUReadbackRequest colourRequest = AsyncGPUReadback.RequestIntoNativeArray(
                    ref _nativeArrayColours, _meshVertexColoursBuffer, vertexRequestSize * sizeof(float) * 4, 0);
                AsyncGPUReadbackRequest triangleRequest =
                    AsyncGPUReadback.RequestIntoNativeArray(ref _nativeArrayTriangles, _meshTrianglesBuffer,
                        triangleRequestSize * sizeof(int), 0);

                if (vertexRequest.hasError || normalRequest.hasError || colourRequest.hasError ||
                    triangleRequest.hasError)
                {
                    Debug.LogError("AsyncGPUReadbackRequest encountered an error.");
                    return;
                }

                AsyncGPUReadback.WaitAllRequests();

                if (vertexRequest.hasError || normalRequest.hasError || colourRequest.hasError ||
                    triangleRequest.hasError)
                {
                    Debug.LogError("AsyncGPUReadbackRequest encountered an error.");
                    return;
                }

                SetMeshData(_nativeArrayVertices, _nativeArrayNormals /*, m_nativeArrayUVs*/, _nativeArrayColours,
                    _nativeArrayTriangles, vertexCount, triangleCount);
            }
            else
            {
                if (MeshRenderer)
                    MeshRenderer.enabled = false;

                if (MeshCollider)
                    MeshCollider.enabled = false;
            }
        }

        private bool m_isCoroutineRunning = false;

        /// <summary>
        /// This is the asynchronous version of <see cref="GetMeshDataFromGPU"/>. Use it as a coroutine. It uses a member variable to prevent duplicates from running at the same time.
        /// </summary>
        private IEnumerator Cr_GetMeshDataFromGPUAsync()
        {
            if (m_isCoroutineRunning)
                yield break;

            m_isCoroutineRunning = true;

            Dispatch();

            if (_outputCounterNativeArray == null || !_outputCounterNativeArray.IsCreated)
                _outputCounterNativeArray = new NativeArray<int>(_counterBuffer.count, Allocator.Persistent);

            AsyncGPUReadbackRequest counterRequest =
                AsyncGPUReadback.RequestIntoNativeArray(ref _outputCounterNativeArray, _counterBuffer);

            while (!counterRequest.done)
                yield return null;

            GetCounts(_outputCounterNativeArray, out int vertexCount, out int triangleCount);

            if (triangleCount > 0)
            {
                ReallocateNativeArrays(vertexCount, triangleCount, ref _nativeArrayVertices, ref _nativeArrayNormals,
                    ref _nativeArrayColours /*m_nativeArrayUVs*/, ref _nativeArrayTriangles);

                int vertexRequestSize =
                    Mathf.Min(_nativeArrayVertices.Length, _meshVerticesBuffer.count, vertexCount);
                int triangleRequestSize = Mathf.Min(_nativeArrayTriangles.Length, _meshTrianglesBuffer.count,
                    triangleCount * 3);

                AsyncGPUReadbackRequest vertexRequest = AsyncGPUReadback.RequestIntoNativeArray(
                    ref _nativeArrayVertices, _meshVerticesBuffer, vertexRequestSize * sizeof(float) * 3, 0);
                AsyncGPUReadbackRequest normalRequest = AsyncGPUReadback.RequestIntoNativeArray(
                    ref _nativeArrayNormals, _meshNormalsBuffer, vertexRequestSize * sizeof(float) * 3, 0);
                //AsyncGPUReadbackRequest uvRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_nativeArrayUVs, m_meshUVsBuffer, vertexRequestSize * sizeof(float) * 2, 0);
                AsyncGPUReadbackRequest colourRequest = AsyncGPUReadback.RequestIntoNativeArray(
                    ref _nativeArrayColours, _meshVertexColoursBuffer, vertexRequestSize * sizeof(float) * 4, 0);
                AsyncGPUReadbackRequest triangleRequest =
                    AsyncGPUReadback.RequestIntoNativeArray(ref _nativeArrayTriangles, _meshTrianglesBuffer,
                        triangleRequestSize * sizeof(int), 0);

                while (!vertexRequest.done && !normalRequest.done && !colourRequest.done /*!uvRequest.done*/ &&
                       !triangleRequest.done)
                    yield return null;

                SetMeshData(_nativeArrayVertices, _nativeArrayNormals, _nativeArrayColours /*m_nativeArrayUVs*/,
                    _nativeArrayTriangles, vertexCount, triangleCount);
            }
            else
            {
                if (MeshRenderer)
                    MeshRenderer.enabled = false;

                if (MeshCollider)
                    MeshCollider.enabled = false;
            }

            m_isCoroutineRunning = false;
        }

        private void SetMeshData(NativeArray<Vector3> vertices,
            NativeArray<Vector3> normals /*, NativeArray<Vector2> uvs*/, NativeArray<Color> colours,
            NativeArray<int> indices, int vertexCount, int triangleCount)
        {
            if (MeshRenderer)
                MeshRenderer.enabled = true;

            if (MeshCollider)
                MeshCollider.enabled = true;

            if (_mesh == null)
            {
                _mesh = new Mesh()
                {
                    indexFormat = IndexFormat.UInt32
                };
            }
            else
            {
                _mesh.Clear();
            }

            _mesh.SetVertices(vertices, 0, vertexCount);
            _mesh.SetNormals(normals, 0, vertexCount);
            //m_mesh.SetUVs(0, uvs, 0, vertexCount);
            _mesh.SetColors(colours, 0, vertexCount);
            _mesh.SetIndices(indices, 0, triangleCount * 3, MeshTopology.Triangles, 0, calculateBounds: true);

            MeshFilter.mesh = _mesh;

            if (MeshCollider)
                MeshCollider.sharedMesh = _mesh;
        }

        private bool TryGetOrCreateMeshGameObject(out GameObject meshGameObject)
        {
            meshGameObject = null;

            // this looks weird, but remember that unity overrides the behaviour of 
            // implicit boolean casting to mean "check whether the underlying c++ object is null"
            if (!this)
                return false;

            meshGameObject = this.meshGameObject;

            if (meshGameObject)
                return true;

            meshGameObject = new GameObject(name + " Generated Mesh");
            meshGameObject.transform.SetParent(transform);
            meshGameObject.transform.Reset();

            this.meshGameObject = meshGameObject;

            return true;
        }

        public void DereferenceMeshObject()
        {
            meshGameObject = null;
            meshFilter = null;
            meshRenderer = null;
        }

        /// <summary>
        /// Read the mesh counter buffer output and convert it into a simple vertex and triangle count.
        /// </summary>
        private void GetCounts(NativeArray<int> counter, out int vertexCount, out int triangleCount)
        {
            vertexCount = counter[VertexCounter] + counter[IntermediateVertexCounter];
            triangleCount = counter[TriangleCounter];
        }

        #endregion

        #region Internal Compute Shader Stuff + Other Boring Boilerplate Methods

        private bool m_isInitializing = false;

        /// <summary>
        /// Do all the initial setup. This function should only be called once per 'session' because it does a lot of
        /// setup for buffers of constant size.
        /// </summary>
        private void InitializeComputeShaderSettings()
        {
            if (_initialized || !_isEnabled)
                return;

            ReleaseUnmanagedMemory();

            m_isInitializing = true;
            _initialized = true;

            _computeShaderInstance = Instantiate(ComputeShader);

            SendTransformToGPU();

            _kernels = new Kernels(ComputeShader);

            // counter buffer has 18 integers: [vertex count, 1, 1, triangle count, 1, 1, vertex count / 64, 1, 1, triangle count / 64, 1, 1, intermediate vertex count, 1, 1, intermediate vertex count / 64, 1, 1]
            _counterBuffer =
                new ComputeBuffer(_counterArray.Length, sizeof(int), ComputeBufferType.IndirectArguments);
            _proceduralArgsBuffer = new ComputeBuffer(_proceduralArgsArray.Length, sizeof(int),
                ComputeBufferType.IndirectArguments);

            _computeShaderInstance.SetBuffer(_kernels.NumberVertices, Properties.CounterRWBuffer, _counterBuffer);
            _computeShaderInstance.SetBuffer(_kernels.GenerateTriangles, Properties.CounterRWBuffer,
                _counterBuffer);
            _computeShaderInstance.SetBuffer(_kernels.GenerateVertices, Properties.CounterRWBuffer, _counterBuffer);
            _computeShaderInstance.SetBuffer(_kernels.NumberVertices, Properties.CounterRWBuffer, _counterBuffer);
            _computeShaderInstance.SetBuffer(_kernels.BuildIndexBuffer, Properties.CounterRWBuffer, _counterBuffer);
            _computeShaderInstance.SetBuffer(_kernels.AddIntermediateVerticesToIndexBuffer,
                Properties.CounterRWBuffer, _counterBuffer);

            _computeShaderInstance.SetBuffer(_kernels.GenerateTriangles, Properties.ProceduralArgsRWBuffer,
                _proceduralArgsBuffer);

            CreateVariableBuffers();

            // ensuring all these setting variables are sent to the gpu.
            OnCellSizeChanged();
            OnBinarySearchIterationsChanged();
            OnIsosurfaceExtractionTypeChanged();
            OnVisualNormalSmoothingChanged();
            OnMaxAngleToleranceChanged();
            OnGradientDescentIterationsChanged();
            OnOutputModeChanged();

            m_isInitializing = false;
        }

        /// <summary>
        /// Create the buffers which will need to be recreated and reset if certain settings change, such as cell count.
        /// </summary>
        private void CreateVariableBuffers()
        {
            int countCubed = voxelSettings.TotalSampleCount;

            _computeShaderInstance.SetInt(Properties.PointsPerSideInt, voxelSettings.SamplesPerSide);

            if (_vertices.IsNullOrEmpty() || _vertices.Length != countCubed)
                _vertices = new VertexData[countCubed];

            if (_triangles.IsNullOrEmpty() || _triangles.Length != countCubed)
                _triangles = new TriangleData[countCubed];

            _samplesBuffer?.Dispose();
            _cellDataBuffer?.Dispose();
            _vertexDataBuffer?.Dispose();
            _triangleDataBuffer?.Dispose();

            _meshVerticesBuffer?.Dispose();
            _meshNormalsBuffer?.Dispose();
            _meshTrianglesBuffer?.Dispose();
            //m_meshUVsBuffer?.Dispose();
            _meshVertexColoursBuffer?.Dispose();
            _meshVertexMaterialsBuffer?.Dispose();

            _intermediateVertexBuffer?.Dispose();

            _samplesBuffer = new ComputeBuffer(countCubed, sizeof(float), ComputeBufferType.Structured);
            _cellDataBuffer = new ComputeBuffer(countCubed, CellData.Stride, ComputeBufferType.Structured);
            _vertexDataBuffer = new ComputeBuffer(countCubed, VertexData.Stride, ComputeBufferType.Append);
            _triangleDataBuffer = new ComputeBuffer(countCubed, TriangleData.Stride, ComputeBufferType.Append);

            _meshVerticesBuffer = new ComputeBuffer(countCubed * 3, sizeof(float) * 3, ComputeBufferType.Structured);
            _meshNormalsBuffer = new ComputeBuffer(countCubed * 3, sizeof(float) * 3, ComputeBufferType.Structured);
            _meshTrianglesBuffer = new ComputeBuffer(countCubed * 3, sizeof(int), ComputeBufferType.Structured);
            //m_meshUVsBuffer = new ComputeBuffer(countCubed * 3, sizeof(float) * 2, ComputeBufferType.Structured);
            _meshVertexColoursBuffer =
                new ComputeBuffer(countCubed * 3, sizeof(float) * 4, ComputeBufferType.Structured);
            _meshVertexMaterialsBuffer =
                new ComputeBuffer(countCubed * 3, SDFMaterialGPU.Stride, ComputeBufferType.Structured);

            _intermediateVertexBuffer =
                new ComputeBuffer(countCubed * 3, NewVertexData.Stride, ComputeBufferType.Append);

            if (mainSettings.ProceduralMaterial)
            {
                if (_propertyBlock == null)
                    _propertyBlock = new MaterialPropertyBlock();

                _propertyBlock.SetBuffer(Properties.MeshVerticesRWBuffer, _meshVerticesBuffer);
                _propertyBlock.SetBuffer(Properties.MeshTrianglesRWBuffer, _meshTrianglesBuffer);
                _propertyBlock.SetBuffer(Properties.MeshNormalsRWBuffer, _meshNormalsBuffer);
                //m_propertyBlock.SetBuffer(Properties.MeshUVs_RWBuffer, m_meshUVsBuffer);
                _propertyBlock.SetBuffer(Properties.MeshVertexMaterialsRWBuffer, _meshVertexMaterialsBuffer);
            }

            UpdateMapKernels(Properties.SamplesRWBuffer, _samplesBuffer);

            _computeShaderInstance.SetBuffer(_kernels.GenerateVertices, Properties.CellDataRWBuffer,
                _cellDataBuffer);
            _computeShaderInstance.SetBuffer(_kernels.NumberVertices, Properties.CellDataRWBuffer, _cellDataBuffer);
            _computeShaderInstance.SetBuffer(_kernels.GenerateTriangles, Properties.CellDataRWBuffer,
                _cellDataBuffer);

            _computeShaderInstance.SetBuffer(_kernels.GenerateTriangles, Properties.MeshVerticesRWBuffer,
                _meshVerticesBuffer);
            _computeShaderInstance.SetBuffer(_kernels.GenerateTriangles, Properties.MeshNormalsRWBuffer,
                _meshNormalsBuffer);
            _computeShaderInstance.SetBuffer(_kernels.GenerateTriangles, Properties.MeshTrianglesRWBuffer,
                _meshTrianglesBuffer);
            //m_computeShaderInstance.SetBuffer(m_kernels.GenerateTriangles, Properties.MeshUVs_RWBuffer, m_meshUVsBuffer);
            _computeShaderInstance.SetBuffer(_kernels.GenerateTriangles, Properties.MeshVertexMaterialsRWBuffer,
                _meshVertexMaterialsBuffer);

            _computeShaderInstance.SetBuffer(_kernels.BuildIndexBuffer, Properties.MeshTrianglesRWBuffer,
                _meshTrianglesBuffer);
            _computeShaderInstance.SetBuffer(_kernels.BuildIndexBuffer,
                Properties.IntermediateVertexBufferAppendBuffer, _intermediateVertexBuffer);
            _computeShaderInstance.SetBuffer(_kernels.BuildIndexBuffer, Properties.MeshVertexColoursRWBuffer,
                _meshVertexColoursBuffer);
            _computeShaderInstance.SetBuffer(_kernels.BuildIndexBuffer, Properties.MeshVertexMaterialsRWBuffer,
                _meshVertexMaterialsBuffer);

            _computeShaderInstance.SetBuffer(_kernels.AddIntermediateVerticesToIndexBuffer,
                Properties.MeshVerticesRWBuffer, _meshVerticesBuffer);
            _computeShaderInstance.SetBuffer(_kernels.AddIntermediateVerticesToIndexBuffer,
                Properties.MeshNormalsRWBuffer, _meshNormalsBuffer);
            _computeShaderInstance.SetBuffer(_kernels.AddIntermediateVerticesToIndexBuffer,
                Properties.MeshTrianglesRWBuffer, _meshTrianglesBuffer);
            _computeShaderInstance.SetBuffer(_kernels.AddIntermediateVerticesToIndexBuffer,
                Properties.MeshVertexMaterialsRWBuffer, _meshVertexMaterialsBuffer);
            //m_computeShaderInstance.SetBuffer(m_kernels.AddIntermediateVerticesToIndexBuffer, Properties.MeshUVs_RWBuffer, m_meshUVsBuffer);
            _computeShaderInstance.SetBuffer(_kernels.AddIntermediateVerticesToIndexBuffer,
                Properties.IntermediateVertexBufferStructuredBuffer, _intermediateVertexBuffer);
            _computeShaderInstance.SetBuffer(_kernels.AddIntermediateVerticesToIndexBuffer,
                Properties.MeshVertexColoursRWBuffer, _meshVertexColoursBuffer);

            _bounds = new Bounds { extents = voxelSettings.Extents };

            ResetCounters();
            SetVertexData();
            SetTriangleData();
        }

        /// <summary>
        /// Buffers and NativeArrays are unmanaged and Unity will cry if we don't do this.
        /// </summary>
        private void ReleaseUnmanagedMemory()
        {
            StopAllCoroutines();
            m_isCoroutineRunning = false;

            _counterBuffer?.Dispose();
            _proceduralArgsBuffer?.Dispose();

            _samplesBuffer?.Dispose();
            _cellDataBuffer?.Dispose();
            _vertexDataBuffer?.Dispose();
            _triangleDataBuffer?.Dispose();

            _meshVerticesBuffer?.Dispose();
            _meshNormalsBuffer?.Dispose();
            _meshTrianglesBuffer?.Dispose();
            //m_meshUVsBuffer?.Dispose();
            _meshVertexColoursBuffer?.Dispose();
            _meshVertexMaterialsBuffer?.Dispose();

            _intermediateVertexBuffer?.Dispose();

            // need to do this because some of the below native arrays might be 'in use' by requests
            AsyncGPUReadback.WaitAllRequests();

            if (_outputCounterNativeArray != null && _outputCounterNativeArray.IsCreated)
                _outputCounterNativeArray.Dispose();

            if (_nativeArrayVertices != null && _nativeArrayVertices.IsCreated)
                _nativeArrayVertices.Dispose();

            if (_nativeArrayNormals != null && _nativeArrayNormals.IsCreated)
                _nativeArrayNormals.Dispose();

            //if (m_nativeArrayUVs != null && m_nativeArrayUVs.IsCreated)
            //    m_nativeArrayUVs.Dispose();

            if (_nativeArrayColours != null && _nativeArrayColours.IsCreated)
                _nativeArrayColours.Dispose();

            if (_nativeArrayTriangles != null && _nativeArrayTriangles.IsCreated)
                _nativeArrayTriangles.Dispose();

            _initialized = false;

            if (_computeShaderInstance)
                DestroyImmediate(_computeShaderInstance);
        }

        /// <summary>
        /// Send a buffer to all kernels which use the map function.
        /// </summary>
        private void UpdateMapKernels(int id, ComputeBuffer buffer)
        {
            if (!_initialized || !_isEnabled)
                return;

            if (buffer == null || !buffer.IsValid())
            {
                Debug.Log("Attempting to pass null buffer to map kernels.");
                return;
            }

            _computeShaderInstance.SetBuffer(_kernels.Map, id, buffer);
            _computeShaderInstance.SetBuffer(_kernels.GenerateVertices, id, buffer);
            _computeShaderInstance.SetBuffer(_kernels.GenerateTriangles, id, buffer);
        }

        /// <summary>
        /// Sets the vertex data as empty and then sends it back to all three kernels? TODO: get rid of this
        /// </summary>
        private void SetVertexData()
        {
            _vertexDataBuffer.SetData(_vertices);
            _computeShaderInstance.SetBuffer(_kernels.GenerateVertices, Properties.VertexDataAppendBuffer,
                _vertexDataBuffer);
            _computeShaderInstance.SetBuffer(_kernels.NumberVertices, Properties.VertexDataStructuredBuffer,
                _vertexDataBuffer);
            _computeShaderInstance.SetBuffer(_kernels.GenerateTriangles, Properties.VertexDataStructuredBuffer,
                _vertexDataBuffer);
            _computeShaderInstance.SetBuffer(_kernels.BuildIndexBuffer, Properties.VertexDataStructuredBuffer,
                _vertexDataBuffer);
        }

        /// <summary>
        /// Sets the triangle data as empty and then sends it back? TODO: get rid of this
        /// </summary>
        private void SetTriangleData()
        {
            _triangleDataBuffer.SetData(_triangles);
            _computeShaderInstance.SetBuffer(_kernels.GenerateTriangles, Properties.TriangleDataAppendBuffer,
                _triangleDataBuffer);
            _computeShaderInstance.SetBuffer(_kernels.BuildIndexBuffer, Properties.TriangleDataStructuredBuffer,
                _triangleDataBuffer);
        }

        public void Run()
        {
            // this only happens when unity is recompiling. this is annoying and hacky but it works.
            if (_counterBuffer == null || !_counterBuffer.IsValid())
                return;

            if (mainSettings.AutoUpdate && !m_isInitializing)
            {
                if (!_initialized)
                    InitializeComputeShaderSettings();

                UpdateMesh();
            }
        }

        /// <summary>
        /// Dispatch all the compute kernels in the correct order. Basically... do the thing.
        /// </summary>
        private void Dispatch()
        {
            // this only happens when unity is recompiling. this is annoying and hacky but it works.
            if (_counterBuffer == null || !_counterBuffer.IsValid())
                return;

            ResetCounters();

            DispatchMap();
            DispatchGenerateVertices();
            DispatchNumberVertices();
            DispatchGenerateTriangles();
            DispatchBuildIndexBuffer();
            DispatchAddIntermediateVerticesToIndexBuffer();
        }

        /// <summary>
        /// Reset count of append buffers.
        /// </summary>
        private void ResetCounters()
        {
            _counterBuffer.SetData(_counterArray);

            _vertexDataBuffer?.SetCounterValue(0);
            _triangleDataBuffer?.SetCounterValue(0);

            _meshVerticesBuffer?.SetCounterValue(0);
            _meshNormalsBuffer?.SetCounterValue(0);
            _meshTrianglesBuffer?.SetCounterValue(0);

            _intermediateVertexBuffer?.SetCounterValue(0);

            _proceduralArgsBuffer?.SetData(_proceduralArgsArray);
        }

        private void DispatchMap()
        {
            UpdateMapKernels(Properties.SettingsStructuredBuffer, Group.SettingsBuffer);

            _computeShaderInstance.GetKernelThreadGroupSizes(_kernels.Map, out uint x, out uint y, out uint z);
            _computeShaderInstance.Dispatch(_kernels.Map, Mathf.CeilToInt(voxelSettings.SamplesPerSide / (float)x),
                Mathf.CeilToInt(voxelSettings.SamplesPerSide / (float)y),
                Mathf.CeilToInt(voxelSettings.SamplesPerSide / (float)z));
        }

        private void DispatchGenerateVertices()
        {
            _computeShaderInstance.GetKernelThreadGroupSizes(_kernels.GenerateVertices, out uint x, out uint y,
                out uint z);
            _computeShaderInstance.Dispatch(_kernels.GenerateVertices,
                Mathf.CeilToInt(voxelSettings.SamplesPerSide / (float)x),
                Mathf.CeilToInt(voxelSettings.SamplesPerSide / (float)y),
                Mathf.CeilToInt(voxelSettings.SamplesPerSide / (float)z));
        }

        private void DispatchNumberVertices()
        {
            // counter buffer has 18 integers: [vertex count, 1, 1, triangle count, 1, 1, vertex count / 64, 1, 1, triangle count / 64, 1, 1, intermediate vertex count, 1, 1, intermediate vertex count / 64, 1, 1]
            _computeShaderInstance.DispatchIndirect(_kernels.NumberVertices, _counterBuffer,
                VertexCounterDiv64 * sizeof(int));
        }

        private void DispatchGenerateTriangles()
        {
            // counter buffer has 18 integers: [vertex count, 1, 1, triangle count, 1, 1, vertex count / 64, 1, 1, triangle count / 64, 1, 1, intermediate vertex count, 1, 1, intermediate vertex count / 64, 1, 1]
            _computeShaderInstance.DispatchIndirect(_kernels.GenerateTriangles, _counterBuffer,
                VertexCounterDiv64 * sizeof(int));
        }

        private void DispatchBuildIndexBuffer()
        {
            // counter buffer has 18 integers: [vertex count, 1, 1, triangle count, 1, 1, vertex count / 64, 1, 1, triangle count / 64, 1, 1, intermediate vertex count, 1, 1, intermediate vertex count / 64, 1, 1]
            _computeShaderInstance.DispatchIndirect(_kernels.BuildIndexBuffer, _counterBuffer,
                TriangleCounterDiv64 * sizeof(int));
        }

        private void DispatchAddIntermediateVerticesToIndexBuffer()
        {
            // counter buffer has 18 integers: [vertex count, 1, 1, triangle count, 1, 1, vertex count / 64, 1, 1, triangle count / 64, 1, 1, intermediate vertex count, 1, 1, intermediate vertex count / 64, 1, 1]
            _computeShaderInstance.DispatchIndirect(_kernels.AddIntermediateVerticesToIndexBuffer, _counterBuffer,
                IntermediateVertexCounterDiv64 * sizeof(int));
        }

        private void SendTransformToGPU()
        {
            if (!_initialized || !_isEnabled)
                return;

            _computeShaderInstance.SetMatrix(Properties.TransformMatrix4X4, transform.localToWorldMatrix);

            if (mainSettings.OutputMode == OutputMode.MeshFilter &&
                TryGetOrCreateMeshGameObject(out GameObject meshGameObject))
                _computeShaderInstance.SetMatrix(Properties.MeshTransformMatrix4X4,
                    meshGameObject.transform.worldToLocalMatrix);
            else if (mainSettings.OutputMode == OutputMode.Procedural)
                _computeShaderInstance.SetMatrix(Properties.MeshTransformMatrix4X4, Matrix4x4.identity);
        }

        public void SetVoxelSettings(VoxelSettings voxelSettings)
        {
            m_isInitializing = true;
            this.voxelSettings.CopySettings(voxelSettings);

            OnCellCountChanged();
            OnCellSizeChanged();

            m_isInitializing = false;

            if (mainSettings.AutoUpdate)
                UpdateMesh();
        }

        public void SetMainSettings(MainSettings mainSettings)
        {
            m_isInitializing = true;
            this.mainSettings.CopySettings(mainSettings);

            OnOutputModeChanged();

            m_isInitializing = false;

            if (this.mainSettings.AutoUpdate)
                UpdateMesh();
        }

        public void SetAlgorithmSettings(AlgorithmSettings algorithmSettings)
        {
            m_isInitializing = true;
            this.algorithmSettings.CopySettings(algorithmSettings);

            OnVisualNormalSmoothingChanged();
            OnMaxAngleToleranceChanged();
            OnGradientDescentIterationsChanged();
            OnBinarySearchIterationsChanged();
            OnIsosurfaceExtractionTypeChanged();
            OnOutputModeChanged();

            m_isInitializing = false;

            if (mainSettings.AutoUpdate)
                UpdateMesh();
        }

        public void OnCellCountChanged()
        {
            _bounds = new Bounds { extents = voxelSettings.Extents };

            if (!_initialized || !_isEnabled)
                return;

            CreateVariableBuffers();

            if (mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnCellSizeChanged()
        {
            _bounds = new Bounds { extents = voxelSettings.Extents };

            if (!_initialized || !_isEnabled)
                return;

            _computeShaderInstance.SetFloat(Properties.CellSizeFloat, voxelSettings.CellSize);

            if (mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnVisualNormalSmoothingChanged()
        {
            if (!_initialized || !_isEnabled)
                return;

            _computeShaderInstance.SetFloat(Properties.VisualNormalSmoothing,
                algorithmSettings.VisualNormalSmoothing);

            if (mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnMaxAngleToleranceChanged()
        {
            if (!_initialized || !_isEnabled)
                return;

            _computeShaderInstance.SetFloat(Properties.MaxAngleCosineFloat,
                Mathf.Cos(algorithmSettings.MaxAngleTolerance * Mathf.Deg2Rad));

            if (mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnGradientDescentIterationsChanged()
        {
            if (!_initialized || !_isEnabled)
                return;

            _computeShaderInstance.SetInt(Properties.GradientDescentIterationsInt,
                algorithmSettings.GradientDescentIterations);

            if (mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnBinarySearchIterationsChanged()
        {
            if (!_initialized || !_isEnabled)
                return;

            _computeShaderInstance.SetInt(Properties.BinarySearchIterationsInt,
                algorithmSettings.BinarySearchIterations);

            if (mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnIsosurfaceExtractionTypeChanged()
        {
            if (!_initialized || !_isEnabled)
                return;

            _computeShaderInstance.SetInt(Properties.IsoSurfaceExtractionTypeInt,
                (int)algorithmSettings.IsoSurfaceExtractionType);

            if (mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnOutputModeChanged()
        {
            if (TryGetOrCreateMeshGameObject(out GameObject meshGameObject))
            {
                meshGameObject.SetActive(true);

                if (MeshRenderer)
                    MeshRenderer.enabled = !Group.IsEmpty;
            }
            else if (mainSettings.OutputMode == OutputMode.Procedural)
            {
                if (meshGameObject)
                    meshGameObject.SetActive(false);
            }

            SendTransformToGPU();
            Group.RequestUpdate(onlySendBufferOnChange: false);
        }

        public void OnDensitySettingChanged()
        {
            OnCellSizeChanged();
            CreateVariableBuffers();
        }

        #endregion

        #region SDF Group Methods

        public void UpdateDataBuffer(ComputeBuffer computeBuffer, ComputeBuffer materialBuffer, int count)
        {
            if (!_isEnabled)
                return;

            if (!_initialized)
                InitializeComputeShaderSettings();

            UpdateMapKernels(Properties.SDFDataStructuredBuffer, computeBuffer);
            UpdateMapKernels(Properties.SDFMaterialsStructuredBuffer, materialBuffer);
            _computeShaderInstance.SetInt(Properties.SDFDataCountInt, count);

            if (mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void UpdateSettingsBuffer(ComputeBuffer computeBuffer)
        {
            if (!_isEnabled)
                return;

            if (!_initialized)
                InitializeComputeShaderSettings();

            UpdateMapKernels(Properties.SettingsStructuredBuffer, computeBuffer);

            if (mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnEmpty()
        {
            if (MeshRenderer)
                MeshRenderer.enabled = false;
        }

        public void OnNotEmpty()
        {
            if (MeshRenderer)
                MeshRenderer.enabled = mainSettings.OutputMode == OutputMode.MeshFilter;
        }

        public void OnPrimitivesChanged()
        {
            if (mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        #endregion

        #region Grid Helper Functions

        private Vector3 CellCoordinateToVertex(int x, int y, int z)
        {
            var gridSize = (float)(voxelSettings.SamplesPerSide - 1f);
            var bound = (gridSize / 2f) * voxelSettings.CellSize;

            var xPos = Mathf.LerpUnclamped(-bound, bound, x / gridSize);
            var yPos = Mathf.LerpUnclamped(-bound, bound, y / gridSize);
            var zPos = Mathf.LerpUnclamped(-bound, bound, z / gridSize);

            return new Vector3(xPos, yPos, zPos);
        }

        public Vector3 CellCoordinateToVertex(Vector3Int vec) =>
            CellCoordinateToVertex(vec.x, vec.y, vec.z);

        private Vector3Int IndexToCellCoordinate(int index)
        {
            int samplesPerSide = voxelSettings.SamplesPerSide;

            var z = index / (samplesPerSide * samplesPerSide);
            index -= (z * samplesPerSide * samplesPerSide);
            var y = index / samplesPerSide;
            var x = index % samplesPerSide;

            return new Vector3Int(x, y, z);
        }

        public Vector3 IndexToVertex(int index)
        {
            var coords = IndexToCellCoordinate(index);
            return CellCoordinateToVertex(coords.x, coords.y, coords.z);
        }

        private int CellCoordinateToIndex(int x, int y, int z) =>
            (x + y * voxelSettings.SamplesPerSide +
             z * voxelSettings.SamplesPerSide * voxelSettings.SamplesPerSide);

        public int CellCoordinateToIndex(Vector3Int vec) =>
            CellCoordinateToIndex(vec.x, vec.y, vec.z);

        #endregion

        #region Chunk + Editor Methods

        [SerializeField] private bool settingsControlledByGrid = false;

        public void SetSettingsControlledByGrid(bool theSettingsControlledByGrid) =>
            settingsControlledByGrid = theSettingsControlledByGrid;

        public static void CloneSettings(SDFGroupMeshGenerator target, Transform parent, SDFGroup group,
            MainSettings mainSettings, AlgorithmSettings algorithmSettings, VoxelSettings voxelSettings,
            bool addMeshRenderer = false, bool addMeshCollider = false, Material meshRendererMaterial = null)
        {
            target.TryGetOrCreateMeshGameObject(out GameObject meshGameObject);

            if (addMeshRenderer)
            {
                var clonedMeshRenderer = meshGameObject.GetOrAddComponent<MeshRenderer>();

                if (meshRendererMaterial)
                    clonedMeshRenderer.sharedMaterial = meshRendererMaterial;
            }

            if (addMeshCollider)
            {
                meshGameObject.GetOrAddComponent<MeshCollider>();
            }

            target.group = group;
            target.mainSettings.CopySettings(mainSettings);
            target.algorithmSettings.CopySettings(algorithmSettings);
            target.voxelSettings.CopySettings(voxelSettings);
        }

        #endregion

        #region Data Structs

        [StructLayout(LayoutKind.Sequential)]
        [System.Serializable]
        public struct CellData
        {
            public static int Stride => sizeof(int) + sizeof(float) * 3;

            public int VertexID;
            public Vector3 SurfacePoint;

            public bool HasSurfacePoint => VertexID >= 0;

            public override string ToString() => $"HasSurfacePoint = {HasSurfacePoint}" +
                                                 (HasSurfacePoint
                                                     ? $", SurfacePoint = {SurfacePoint}, VertexID = {VertexID}"
                                                     : "");
        };

        [StructLayout(LayoutKind.Sequential)]
        [System.Serializable]
        public struct VertexData
        {
            public static int Stride => sizeof(int) * 2 + sizeof(float) * 6;

            public int Index;
            public int CellID;
            public Vector3 Vertex;
            public Vector3 Normal;

            public override string ToString() =>
                $"Index = {Index}, CellID = {CellID}, Vertex = {Vertex}, Normal = {Normal}";
        }

        [StructLayout(LayoutKind.Sequential)]
        [System.Serializable]
        public struct TriangleData
        {
            public static int Stride => sizeof(int) * 3;

            public int P_1;
            public int P_2;
            public int P_3;

            public override string ToString() => $"P_1 = {P_1}, P_2 = {P_2}, P_3 = {P_3}";
        }

        [StructLayout(LayoutKind.Sequential)]
        [System.Serializable]
        public struct NewVertexData
        {
            public static int Stride => sizeof(int) + sizeof(float) * 6;

            public int Index;
            public Vector3 Vertex;
            public Vector3 Normal;

            public override string ToString() => $"Index = {Index}, Vertex = {Vertex}, Normal = {Normal}";
        }

        #endregion
    }

    #region ENUM

    public enum IsoSurfaceExtractionType
    {
        SurfaceNets,
        DualContouring
    };

    public enum EdgeIntersectionType
    {
        Interpolation,
        BinarySearch
    };

    public enum CellSizeMode
    {
        Fixed,
        Density
    };

    public enum OutputMode
    {
        MeshFilter,
        Procedural
    };

    #endregion
}