private void RunSamplePhase(bool hasUVs, out float[] samples, out float[] packedUVs, Vector3 minBounds,Vector3 maxBounds)
{
    int threadGroups = Mathf.CeilToInt(m_size / 8f);
    MeshSampleComputeShader.Dispatch(GetTextureWholeKernel, threadGroups, threadGroups, threadGroups); // 'CS_SampleMeshDistances'

    SamplesBuffer.GetData(m_samples); // RWStructuredBuffer<float> _Samples
    samples = m_samples; // RWStructuredBuffer<float> _Samples
}

private ComputeBuffer SamplesBuffer { get; }
SamplesBuffer = new ComputeBuffer(Dimensions, sizeof(float));//public int Dimensions => m_size * m_size * m_size;
MeshSampleComputeShader.SetBuffer(GetTextureWholeKernel, Properties.Samples_RWStructuredBuffer,SamplesBuffer);

MeshSampleComputeShader.Dispatch(GetTextureWholeKernel, threadGroups, threadGroups, threadGroups);
SamplesBuffer.GetData(m_samples);


///////////////
private ComputeBuffer m_samplesBuffer;
m_samplesBuffer = new ComputeBuffer(countCubed, sizeof(float), ComputeBufferType.Structured);
m_computeShaderInstance.SetBuffer(m_kernels.Map, Properties.Samples_RWBuffer, m_samplesBuffer);

m_computeShaderInstance.Dispatch(m_kernels.Map, Mathf.CeilToInt(m_voxelSettings.SamplesPerSide / (float)x),Mathf.CeilToInt(m_voxelSettings.SamplesPerSide / (float)y),Mathf.CeilToInt(m_voxelSettings.SamplesPerSide / (float)z));
m_samplesBuffer.GetData(_boneSamples);
