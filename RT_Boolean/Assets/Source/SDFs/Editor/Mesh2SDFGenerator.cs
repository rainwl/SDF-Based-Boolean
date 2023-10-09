using System;
using JetBrains.Annotations;
using Source.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Source.SDFs.Editor
{
    public class Mesh2SDFGenerator : EditorWindow
    {
        #region Fields

        private static Mesh2SDFGenerator _window;

        private int _lastSubdivisionLevel;
        private float[] _samples;
        private float[] _packedUVs;
        private Vector2 _scrollPos;
        private Vector3 _minBounds;
        private Vector3 _maxBounds;
        private const string AssetSavePath = "Assets/Data/SDFMeshes/";
        private SerializedObject _serializedObject;
        private SerializedProperties _serializedProperties;
        private UnityEditor.Editor _inputMeshPreview;
        private UnityEditor.Editor _tessellatedMeshPreview;
        private bool MissingMesh => mesh == null;
        private bool CanSave => !MissingMesh && !_samples.IsNullOrEmpty();
        private bool _transformBoxOpened;
        private Matrix4x4 ModelTransform => Matrix4x4.TRS(translation, Quaternion.Euler(rotation), scale);

        [SerializeField] [HideInInspector] private ComputeShader meshSampleComputeShader;
        [SerializeField] [HideInInspector] private ComputeShader tesselationComputeShader;
        [SerializeField] private Mesh tessellatedMesh;
        [SerializeField] private Mesh mesh;
        [SerializeField] [Min(1)] private int size = 128;
        [SerializeField] private int subdivisions = 1;
        [SerializeField] private float padding = 0.2f;
        [SerializeField] private float minimumEdgeLength = 0.15f;
        [SerializeField] private bool isSampleUVs;
        [SerializeField] private bool isTessellateMesh;
        [SerializeField] private bool isAutoSaveOnComplete;
        [SerializeField] private Vector3 translation = Vector3.zero;
        [SerializeField] private Vector3 rotation = Vector3.zero;
        [SerializeField] private Vector3 scale = Vector3.zero;

        #endregion

        #region Methods

        [MenuItem("MeshToSDF/Generator")]
        public static void ShowWindow()
        {
            _window = GetWindow<Mesh2SDFGenerator>($"Mesh To SDF Generator");
            _window._serializedObject = new SerializedObject(_window);
            _window._serializedProperties = new SerializedProperties(_window._serializedObject);
        }

        private void Generate()
        {
            tessellatedMesh = null;
            using (var session = new ComputeSession(meshSampleComputeShader, tesselationComputeShader, size, padding,
                       isSampleUVs, ModelTransform))
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                if (tessellatedMesh)
                    session.DispatchWithTesselation(mesh, subdivisions, minimumEdgeLength, out _samples, out _packedUVs,
                        out _minBounds, out _maxBounds, out tessellatedMesh);
                else
                    session.Dispatch(mesh, out _samples, out _packedUVs, out _minBounds, out _maxBounds);

                _lastSubdivisionLevel = isTessellateMesh ? subdivisions : 0;
                stopwatch.Stop();
                Debug.Log($"Generator took {stopwatch.Elapsed:g}. [h/m/s/ms]");
            }

            if (isAutoSaveOnComplete)
                Save();
        }

        private void Save()
        {
            if (!CanSave) return;
            SDFMeshAsset.Create(AssetSavePath, "SDFMesh_" + mesh.name, _samples, _packedUVs, _lastSubdivisionLevel,
                size, padding, mesh, _minBounds, _maxBounds);
        }

        private void OnGUI()
        {
            using var scroll = new EditorGUILayout.ScrollViewScope(_scrollPos);
            _scrollPos = scroll.scrollPosition;
            var newMesh = (Mesh)EditorGUILayout.ObjectField("Mesh", mesh, typeof(Mesh), allowSceneObjects: false);
            if (mesh != newMesh)
            {
                mesh = newMesh;
                if (_inputMeshPreview != null)
                {
                    DestroyImmediate(_inputMeshPreview);
                    _inputMeshPreview = null;
                }
            }

            if (mesh != null)
            {
                if (_inputMeshPreview == null)
                {
                    _inputMeshPreview = UnityEditor.Editor.CreateEditor(mesh);
                }

                _inputMeshPreview.DrawPreview(GUILayoutUtility.GetRect(300, 300));
            }

            size = Mathf.Max(1, EditorGUILayout.IntField($"Size", size));
            padding = Mathf.Max(0f, EditorGUILayout.FloatField($"Padding", padding));


            _transformBoxOpened = EditorGUILayout.Foldout(_transformBoxOpened, "Transform", true);

            if (_transformBoxOpened)
            {
                using var box = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
                using var indent = new EditorGUI.IndentLevelScope();
                translation = EditorGUILayout.Vector3Field("Translation", translation);
                rotation = EditorGUILayout.Vector3Field("Rotation", rotation);
                scale = EditorGUILayout.Vector3Field("Scale", scale);
            }

            isTessellateMesh = EditorGUILayout.Toggle("Tessellate Mesh First", isTessellateMesh);
            isSampleUVs = EditorGUILayout.Toggle("Sample UVs", isTessellateMesh);

            if (isTessellateMesh)
            {
                using var box = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
                using var indent = new EditorGUI.IndentLevelScope();
                if (tessellatedMesh != null)
                {
                    if (_tessellatedMeshPreview != null &&
                        _tessellatedMeshPreview.serializedObject.targetObject as Mesh != tessellatedMesh)
                    {
                        DestroyImmediate(_tessellatedMeshPreview);
                        _tessellatedMeshPreview = null;
                    }

                    if (_tessellatedMeshPreview == null)
                        _tessellatedMeshPreview = UnityEditor.Editor.CreateEditor(tessellatedMesh);

                    _tessellatedMeshPreview.DrawPreview(GUILayoutUtility.GetRect(200, 200));
                }

                subdivisions = Mathf.Clamp(EditorGUILayout.IntField("Subdivisions", subdivisions), 0,
                    4);
                minimumEdgeLength =
                    Mathf.Max(EditorGUILayout.FloatField("Minimum Edge Length", minimumEdgeLength), 0f);
            }

            isAutoSaveOnComplete = EditorGUILayout.Toggle("Auto Save On Complete", isAutoSaveOnComplete);

            GUI.enabled = mesh != null;

            if (GUI.Button(GUILayoutUtility.GetRect(200, 40), "Generate"))
                Generate();

            if (GUI.Button(GUILayoutUtility.GetRect(200, 40), "Save"))
                Save();

            GUI.enabled = true;
        }

        #endregion

        #region Classes

        private class SerializedProperties
        {
            public SerializedProperty Padding { get; }
            public SerializedProperty Translation { get; }
            public SerializedProperty Rotation { get; }
            public SerializedProperty Scale { get; }
            public SerializedProperty MinimumEdgeLength { get; }

            public SerializedProperties(SerializedObject serializedObject)
            {
                Padding = serializedObject.FindProperty("_padding");
                Translation = serializedObject.FindProperty("_translation");
                Rotation = serializedObject.FindProperty("_rotation");
                Scale = serializedObject.FindProperty("_scale");
                MinimumEdgeLength = serializedObject.FindProperty("_minimumEdgeLength");
            }
        }

        private class ComputeSession : IDisposable
        {
            #region Fields

            private ComputeBuffer _inputVerticesBuffer;//StructuredBuffer<float3> _InputVertices;
            private ComputeBuffer _inputTrianglesBuffer;//StructuredBuffer<int> _InputTriangles;
            private ComputeBuffer _inputNormalsBuffer;//StructuredBuffer<float3> _InputNormals;
            private ComputeBuffer _inputTangentsBuffer;
            private ComputeBuffer _inputUVsBuffer;//StructuredBuffer<float2> _InputUVs;

            // OutPut --> ComputeShader_Tessellate.compute
            private ComputeBuffer _outputVerticesBuffer; 
            private ComputeBuffer _outputTrianglesBuffer;
            private ComputeBuffer _outputNormalsBuffer;
            private ComputeBuffer _outputTangentsBuffer;
            private ComputeBuffer _outputUVsBuffer;

            private ComputeBuffer SamplesBuffer { get; }
            private ComputeBuffer PackedUVsBuffer { get; }
            private ComputeBuffer BoundsBuffer { get; }

            private const string ComputeBoundsKernelName = "CS_ComputeMeshBounds";
            private const string GetTextureWholeKernelName = "CS_SampleMeshDistances";
            private const string TessellateKernelName = "CS_Tessellate";
            private const string PreprocessMeshKernelName = "CS_PreprocessMesh";
            private const string WriteUVsKeyword = "WRITE_UVS";

            //public int GetTextureSliceKernel { get; }
            private int GetTextureWholeKernel { get; }
            private int ComputeBoundsKernel { get; }
            private int TessellateKernel { get; }
            private int PreprocessMeshKernel { get; }

            private ComputeShader MeshSampleComputeShader { get; }
            private ComputeShader TessellationComputeShader { get; }

            private readonly bool _isSampleUVs;

            private int[] _triangles;
            private Vector3[] _vertices;
            private Vector3[] _normals;
            private Vector4[] _tangents;
            private Vector2[] _uvs;

            private readonly int _size;
            private readonly float _padding;
            private readonly float[] _samples;
            private readonly float[] _packedUVs;

            private int Dimensions => _size * _size * _size;

            #endregion

            #region Methods

            public ComputeSession(ComputeShader meshSampleComputeShader, ComputeShader tesselationComputeShader,
                int size, float padding, bool sampleUVs, Matrix4x4 transform)
            {
                MeshSampleComputeShader = Instantiate(meshSampleComputeShader);
                TessellationComputeShader = Instantiate(tesselationComputeShader);

                GetTextureWholeKernel = MeshSampleComputeShader.FindKernel(GetTextureWholeKernelName);
                ComputeBoundsKernel = MeshSampleComputeShader.FindKernel(ComputeBoundsKernelName);
                TessellateKernel = TessellationComputeShader.FindKernel(TessellateKernelName);
                PreprocessMeshKernel = TessellationComputeShader.FindKernel(PreprocessMeshKernelName);

                _isSampleUVs = sampleUVs;
                _size = size;
                _padding = padding;
                _samples = new float[Dimensions];
                _packedUVs = new float[Dimensions];

                BoundsBuffer = new ComputeBuffer(6, sizeof(int));
                MeshSampleComputeShader.SetBuffer(ComputeBoundsKernel, Properties.MeshBoundsRWStructuredBuffer,
                    BoundsBuffer);

                SamplesBuffer = new ComputeBuffer(Dimensions, sizeof(float));
                MeshSampleComputeShader.SetBuffer(GetTextureWholeKernel, Properties.SamplesRWStructuredBuffer,
                    SamplesBuffer);

                PackedUVsBuffer = new ComputeBuffer(Dimensions, sizeof(float));
                MeshSampleComputeShader.SetBuffer(GetTextureWholeKernel, Properties.PackedUVsRWStructuredBuffer,
                    PackedUVsBuffer);

                MeshSampleComputeShader.SetInt(Properties.SizeInt, _size);
                MeshSampleComputeShader.SetFloat(Properties.PaddingFloat, _padding);
                MeshSampleComputeShader.SetMatrix(Properties.ModelTransformMatrixMatrix, transform);
            }

            public void Dispose()
            {
                BoundsBuffer?.Dispose();
                SamplesBuffer?.Dispose();
                PackedUVsBuffer?.Dispose();

                _inputVerticesBuffer?.Dispose();
                _inputTrianglesBuffer?.Dispose();
                _inputNormalsBuffer?.Dispose();
                _inputTangentsBuffer?.Dispose();
                _inputUVsBuffer?.Dispose();

                _outputVerticesBuffer?.Dispose();
                _outputTrianglesBuffer?.Dispose();
                _outputNormalsBuffer?.Dispose();
                _outputTangentsBuffer?.Dispose();
                _outputUVsBuffer?.Dispose();

                DestroyImmediate(MeshSampleComputeShader);
                DestroyImmediate(TessellationComputeShader);
            }

            public void Dispatch(Mesh mesh, out float[] samples, out float[] packedUVs, out Vector3 minBounds,
                out Vector3 maxBounds)
            {
                _triangles = mesh.triangles;
                _vertices = mesh.vertices;
                _normals = mesh.normals;
                _uvs = mesh.uv;

                _inputTrianglesBuffer = new ComputeBuffer(_triangles.Length, sizeof(int), ComputeBufferType.Structured);
                _inputVerticesBuffer =
                    new ComputeBuffer(_vertices.Length, sizeof(float) * 3, ComputeBufferType.Structured);
                _inputNormalsBuffer =
                    new ComputeBuffer(_normals.Length, sizeof(float) * 3, ComputeBufferType.Structured);

                _inputTrianglesBuffer.SetData(_triangles);
                _inputNormalsBuffer.SetData(_normals);
                _inputVerticesBuffer.SetData(_vertices);

                MeshSampleComputeShader.SetBuffer(ComputeBoundsKernel, Properties.InputTrianglesStructuredBuffer,
                    _inputTrianglesBuffer);//Compute_SDFMesh.compute , kernel CS_ComputeMeshBounds
                MeshSampleComputeShader.SetBuffer(ComputeBoundsKernel, Properties.InputVerticesStructuredBuffer,
                    _inputVerticesBuffer);

                MeshSampleComputeShader.SetBuffer(GetTextureWholeKernel, Properties.InputTrianglesStructuredBuffer,
                    _inputTrianglesBuffer);// kernel CS_SampleMeshDistances
                MeshSampleComputeShader.SetBuffer(GetTextureWholeKernel, Properties.InputNormalsStructuredBuffer,
                    _inputNormalsBuffer);
                MeshSampleComputeShader.SetBuffer(GetTextureWholeKernel, Properties.InputVerticesStructuredBuffer,
                    _inputVerticesBuffer);

                var hasUVs = !_uvs.IsNullOrEmpty();
                if (_isSampleUVs && hasUVs)
                {
                    MeshSampleComputeShader.EnableKeyword(WriteUVsKeyword);
                    _inputUVsBuffer = new ComputeBuffer(_uvs.Length, sizeof(float) * 2, ComputeBufferType.Structured);
                    _inputUVsBuffer.SetData(_uvs);
                    MeshSampleComputeShader.SetBuffer(GetTextureWholeKernel, Properties.InputUVsStructuredBuffer,
                        _inputUVsBuffer);
                }
                else
                {
                    MeshSampleComputeShader.DisableKeyword(WriteUVsKeyword);
                }

                MeshSampleComputeShader.SetInt(Properties.TriangleCountInt, _triangles.Length);//uint _TriangleCount;
                MeshSampleComputeShader.SetInt(Properties.VertexCountInt, _vertices.Length);//uint _VertexCount;

                RunBoundsPhase(mesh, out minBounds, out maxBounds);
                // 计算完 min max bounds后,传递到计算采样点进行计算
                // 这里是不是可以直接取maxBounds,作为一个cube
                
                maxBounds.x = Mathf.CeilToInt(maxBounds.x);
                maxBounds.y = Mathf.CeilToInt(maxBounds.y);
                maxBounds.z = Mathf.CeilToInt(maxBounds.z);
                minBounds = maxBounds;
                
                RunSamplePhase(hasUVs, out samples, out packedUVs, minBounds, maxBounds);
            }

            public void DispatchWithTesselation(Mesh mesh, int subdivisions, float minimumEdgeLength,
                out float[] samples, out float[] packedUVs, out Vector3 minBounds, out Vector3 maxBounds,
                out Mesh tessellatedMesh)
            {
                _triangles = mesh.triangles;
                _vertices = mesh.vertices;
                _normals = mesh.normals;
                _tangents = mesh.tangents;
                _uvs = mesh.uv;

                tessellatedMesh = RunTesselationPhase(mesh, subdivisions, minimumEdgeLength);
                Dispatch(tessellatedMesh, out samples, out packedUVs, out minBounds, out maxBounds);
            }

            private void RunBoundsPhase(Mesh mesh, out Vector3 minBounds, out Vector3 maxBounds)
            {
                var meshBounds = new int[6];
                BoundsBuffer.SetData(meshBounds);
                MeshSampleComputeShader.Dispatch(ComputeBoundsKernel, Mathf.CeilToInt(mesh.triangles.Length / 64f), 1,
                    1);
                BoundsBuffer.GetData(meshBounds);

                const float packingMultiplier = 1000f;

                minBounds = new Vector3(meshBounds[0] / packingMultiplier, meshBounds[1] / packingMultiplier,
                    meshBounds[2] / packingMultiplier);
                maxBounds = new Vector3(meshBounds[3] / packingMultiplier, meshBounds[4] / packingMultiplier,
                    meshBounds[5] / packingMultiplier);

                minBounds -= _padding * Vector3.one;
                maxBounds += _padding * Vector3.one;
            }

            /// <summary>
            /// Note: I chose to use buffers instead of writing to texture 3ds directly on the GPU because for some reason it's
            /// stupidly complicated to write to a texture3d on the gpu and then get that data back to the cpu for serialization.
            /// </summary>
            private void RunSamplePhase(bool hasUVs, out float[] samples, out float[] packedUVs, Vector3 minBounds,
                Vector3 maxBounds)
            {
                MeshSampleComputeShader.SetVector(Properties.MinBoundsVector3, minBounds);
                MeshSampleComputeShader.SetVector(Properties.MaxBoundsVector3, maxBounds);

                var threadGroups = Mathf.CeilToInt(_size / 8f);
                MeshSampleComputeShader.Dispatch(GetTextureWholeKernel, threadGroups, threadGroups, threadGroups);

                SamplesBuffer.GetData(_samples);
                samples = _samples;

                if (hasUVs)
                {
                    PackedUVsBuffer.GetData(_packedUVs);
                    packedUVs = _packedUVs;
                }
                else
                {
                    packedUVs = null;
                }
            }

            private Mesh RunTesselationPhase(Mesh inputMesh, int subdivisions, float minimumEdgeLength)
            {
                // triangle count is the big number here for dispatch sizes, and the final mesh
                // will have (triangle count * 4^subdivisions) for triangles, vertices, and normals

                var inputTriangleCount = _triangles.Length;

                Preprocess(inputTriangleCount);
                Tessellate(inputTriangleCount, subdivisions, minimumEdgeLength);

                var finalOutputSize = inputTriangleCount * (int)Mathf.Pow(4, subdivisions);
                var outputVertices = new Vector3[finalOutputSize];
                var outputNormals = new Vector3[finalOutputSize];
                var outputTangents = new Vector4[finalOutputSize];
                var outputUVs = new Vector2[finalOutputSize];
                var outputTriangles = new int[finalOutputSize];

                _outputVerticesBuffer.GetData(outputVertices);
                _outputNormalsBuffer.GetData(outputNormals);
                _outputTangentsBuffer.GetData(outputTangents);
                _outputTrianglesBuffer.GetData(outputTriangles);
                _outputUVsBuffer.GetData(outputUVs);

                return new Mesh
                {
                    indexFormat = IndexFormat.UInt32,
                    vertices = outputVertices,
                    normals = outputNormals,
                    tangents = outputTangents,
                    uv = outputUVs,
                    triangles = outputTriangles
                };
            }

            private void Preprocess(int triangleCount)
            {
                void SetInputPreprocess<T>(T[] array, int stride, int nameID, [NotNull] ref ComputeBuffer buffer)
                {
                    if (buffer == null) throw new ArgumentNullException(nameof(buffer));
                    buffer = new ComputeBuffer(array.Length, stride, ComputeBufferType.Structured);
                    buffer.SetData(array);
                    TessellationComputeShader.SetBuffer(PreprocessMeshKernel, nameID, buffer);
                }

                void SetOutputPreprocess(int stride, int nameID, [NotNull] ref ComputeBuffer buffer)
                {
                    if (buffer == null) throw new ArgumentNullException(nameof(buffer));
                    buffer = new ComputeBuffer(triangleCount, stride, ComputeBufferType.Structured);
                    TessellationComputeShader.SetBuffer(PreprocessMeshKernel, nameID, buffer);
                }

                SetInputPreprocess(_vertices, sizeof(float) * 3, Properties.InputVerticesStructuredBuffer,
                    ref _inputVerticesBuffer);
                SetInputPreprocess(_normals, sizeof(float) * 3, Properties.InputNormalsStructuredBuffer,
                    ref _inputNormalsBuffer);
                SetInputPreprocess(_tangents, sizeof(float) * 4, Properties.InputTangentsStructuredBuffer,
                    ref _inputTangentsBuffer);
                SetInputPreprocess(_uvs, sizeof(float) * 2, Properties.InputUVsStructuredBuffer, ref _inputUVsBuffer);
                SetInputPreprocess(_triangles, sizeof(int), Properties.InputTrianglesStructuredBuffer,
                    ref _inputTrianglesBuffer);

                SetOutputPreprocess(sizeof(float) * 3, Properties.OutputVerticesStructuredBuffer,
                    ref _outputVerticesBuffer);
                SetOutputPreprocess(sizeof(float) * 3, Properties.OutputNormalsStructuredBuffer,
                    ref _outputNormalsBuffer);
                SetOutputPreprocess(sizeof(float) * 4, Properties.OutputTangentsStructuredBuffer,
                    ref _outputTangentsBuffer);
                SetOutputPreprocess(sizeof(float) * 2, Properties.OutputUVsStructuredBuffer, ref _outputUVsBuffer);
                SetOutputPreprocess(sizeof(int), Properties.OutputTrianglesStructuredBuffer,
                    ref _outputTrianglesBuffer);

                TessellationComputeShader.SetInt(Properties.TriangleCountInt, triangleCount);

                var threadGroupX = Mathf.CeilToInt((triangleCount / 3f) / 64f);

                TessellationComputeShader.Dispatch(PreprocessMeshKernel, threadGroupX, 1, 1);
            }

            private void Tessellate(int triangles, int subdivisions, float minimumEdgeLength)
            {
                TessellationComputeShader.SetFloat(Properties.MinimumEdgeLengthFloat, minimumEdgeLength);

                for (var i = 0; i < subdivisions; i++)
                {
                    var inputTriangles = triangles;
                    var outputTriangles = triangles * 4;

                    // output from preprocess goes to input of tessellate
                    TessellationComputeShader.SetBuffer(TessellateKernel, Properties.InputVerticesStructuredBuffer,
                        _outputVerticesBuffer);
                    TessellationComputeShader.SetBuffer(TessellateKernel, Properties.InputNormalsStructuredBuffer,
                        _outputNormalsBuffer);
                    TessellationComputeShader.SetBuffer(TessellateKernel, Properties.InputTangentsStructuredBuffer,
                        _outputTangentsBuffer);
                    TessellationComputeShader.SetBuffer(TessellateKernel, Properties.InputUVsStructuredBuffer,
                        _outputUVsBuffer);
                    TessellationComputeShader.SetBuffer(TessellateKernel, Properties.InputTrianglesStructuredBuffer,
                        _outputTrianglesBuffer);

                    void SetOutputTessellate(int stride, int nameID, [NotNull] ref ComputeBuffer buffer)
                    {
                        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
                        buffer = new ComputeBuffer(outputTriangles, stride, ComputeBufferType.Structured);
                        TessellationComputeShader.SetBuffer(TessellateKernel, nameID, buffer);
                    }

                    var oldOutputVertices = _outputVerticesBuffer;
                    var oldOutputNormals = _outputNormalsBuffer;
                    var oldOutputTangents = _outputTangentsBuffer;
                    var oldOutputUVs = _outputUVsBuffer;
                    var oldOutputTriangles = _outputTrianglesBuffer;

                    SetOutputTessellate(sizeof(float) * 3, Properties.OutputVerticesStructuredBuffer,
                        ref _outputVerticesBuffer);
                    SetOutputTessellate(sizeof(float) * 3, Properties.OutputNormalsStructuredBuffer,
                        ref _outputNormalsBuffer);
                    SetOutputTessellate(sizeof(float) * 4, Properties.OutputTangentsStructuredBuffer,
                        ref _outputTangentsBuffer);
                    SetOutputTessellate(sizeof(float) * 2, Properties.OutputUVsStructuredBuffer, ref _outputUVsBuffer);
                    SetOutputTessellate(sizeof(int), Properties.OutputTrianglesStructuredBuffer,
                        ref _outputTrianglesBuffer);

                    TessellationComputeShader.SetInt(Properties.TriangleCountInt, inputTriangles);

                    var threadGroupX = Mathf.CeilToInt((inputTriangles / 3f) / 64f);
                    TessellationComputeShader.Dispatch(TessellateKernel, threadGroupX, 1, 1);

                    oldOutputVertices?.Dispose();
                    oldOutputNormals?.Dispose();
                    oldOutputTangents?.Dispose();
                    oldOutputUVs?.Dispose();
                    oldOutputTriangles?.Dispose();

                    triangles = outputTriangles;
                }
            }

            #endregion

            private static class Properties
            {
                public static readonly int InputVerticesStructuredBuffer = Shader.PropertyToID("_InputVertices");// StructuredBuffer<float3> _InputVertices;
                public static readonly int InputNormalsStructuredBuffer = Shader.PropertyToID("_InputNormals");// StructuredBuffer<float3> _InputNormals;
                public static readonly int InputTrianglesStructuredBuffer = Shader.PropertyToID("_InputTriangles");//StructuredBuffer<int> _InputTriangles;
                public static readonly int InputTangentsStructuredBuffer = Shader.PropertyToID("_InputTangents");
                public static readonly int InputUVsStructuredBuffer = Shader.PropertyToID("_InputUVs");//_InputUVs;

                public static readonly int OutputVerticesStructuredBuffer = Shader.PropertyToID("_OutputVertices");
                public static readonly int OutputNormalsStructuredBuffer = Shader.PropertyToID("_OutputNormals");
                public static readonly int OutputTrianglesStructuredBuffer = Shader.PropertyToID("_OutputTriangles");
                public static readonly int OutputTangentsStructuredBuffer = Shader.PropertyToID("_OutputTangents");
                public static readonly int OutputUVsStructuredBuffer = Shader.PropertyToID("_OutputUVs");

                public static readonly int TriangleCountInt = Shader.PropertyToID("_TriangleCount");
                public static readonly int VertexCountInt = Shader.PropertyToID("_VertexCount");

                public static readonly int MinimumEdgeLengthFloat = Shader.PropertyToID("_MinimumEdgeLength");

                public static readonly int MinBoundsVector3 = Shader.PropertyToID("_MinBounds");
                public static readonly int MaxBoundsVector3 = Shader.PropertyToID("_MaxBounds");
                public static readonly int PaddingFloat = Shader.PropertyToID("_Padding");
                public static readonly int SizeInt = Shader.PropertyToID("_Size");
                public static readonly int ModelTransformMatrixMatrix = Shader.PropertyToID("_ModelTransformMatrix");

                public static readonly int SamplesRWStructuredBuffer = Shader.PropertyToID("_Samples");
                public static readonly int PackedUVsRWStructuredBuffer = Shader.PropertyToID("_PackedUVs");
                public static readonly int MeshBoundsRWStructuredBuffer = Shader.PropertyToID("_BoundsBuffer");
            }
        }

        #endregion
    }
}