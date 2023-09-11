using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;

namespace Source.SDFs.Settings
{
    [System.Serializable]
    public class VoxelSettings
    {
        [SerializeField] private CellSizeMode cellSizeMode = CellSizeMode.Fixed;
        [SerializeField] private float cellSize = 0.2f;
        [SerializeField] private int cellCount = 50;
        [SerializeField] private float volumeSize = 5f;
        [SerializeField] private float cellDensity = 1f;

        #region Public Fields

        public CellSizeMode CellSizeMode => cellSizeMode;

        public float CellSize => cellSizeMode == CellSizeMode.Density ? volumeSize / cellDensity : cellSize;

        public int CellCount =>
            cellSizeMode == CellSizeMode.Density ? Mathf.FloorToInt(volumeSize * cellDensity) : cellCount;

        public float VolumeSize => volumeSize;
        public float CellDensity => cellDensity;
        public int SamplesPerSide => CellCount + 1;

        public int TotalSampleCount
        {
            get
            {
                var samplesPerSide = CellCount + 1;
                return samplesPerSide * samplesPerSide * samplesPerSide;
            }
        }

        public Vector3 Extents => Vector3.one * CellCount * CellSize;

        public float Radius => Extents.magnitude;
        public float OffsetDistance => (CellCount - 2) * CellSize;

        #endregion


        public void CopySettings(VoxelSettings source)
        {
            cellSizeMode = source.cellSizeMode;
            cellSize = source.cellSize;
            cellCount = source.cellCount;
            volumeSize = source.volumeSize;
            cellDensity = source.cellDensity;
        }
    }
}