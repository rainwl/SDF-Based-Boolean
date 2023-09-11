using Unity.VisualScripting;
using UnityEditor.MPE;
using UnityEngine;

namespace Source.SDFs.Settings
{
    [System.Serializable]
    public class AlgorithmSettings
    {
        [SerializeField] private float maxAngleTolerance = 20f;
        [SerializeField] private float visualNormalSmoothing = 1e-5f;

        [SerializeField]
        private IsoSurfaceExtractionType isoSurfaceExtractionType = IsoSurfaceExtractionType.DualContouring;

        [SerializeField] private EdgeIntersectionType edgeInterSectionType = EdgeIntersectionType.Interpolation;
        [SerializeField] private int binarySearchIterations = 5;
        [SerializeField] private bool applyGradientDescent;
        [SerializeField] private int gradientDescentIterations = 10;

        public float MaxAngleTolerance => maxAngleTolerance;
        public float VisualNormalSmoothing => visualNormalSmoothing;
        public IsoSurfaceExtractionType IsoSurfaceExtractionType => isoSurfaceExtractionType;
        public EdgeIntersectionType EdgeIntersectionType => edgeInterSectionType;

        public int BinarySearchIterations =>
            edgeInterSectionType == EdgeIntersectionType.Interpolation ? 0 : binarySearchIterations;

        public bool ApplyGradientDescent => applyGradientDescent;
        public int GradientDescentIterations => applyGradientDescent ? gradientDescentIterations : 0;

        public void CopySettings(AlgorithmSettings source)
        {
            maxAngleTolerance = source.maxAngleTolerance;
            visualNormalSmoothing = source.visualNormalSmoothing;
            isoSurfaceExtractionType = source.isoSurfaceExtractionType;
            edgeInterSectionType = source.edgeInterSectionType;
            binarySearchIterations = source.binarySearchIterations;
            applyGradientDescent = source.applyGradientDescent;
            gradientDescentIterations = source.gradientDescentIterations;
        }
    }
}