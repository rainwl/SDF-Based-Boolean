using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Serialization;

namespace Source.SDFs
{
    [System.Serializable]
    public struct SDFMaterial
    {
        public enum MaterialType
        {
            None,
            Color,
            Texture
        }

        #region Serialize Fields

        [SerializeField] private MaterialType type;
        [SerializeField] private Texture2D texture2D;
        [SerializeField] private float materialSmoothing;
        [SerializeField] [Range(0f, 1f)] private float metallic;
        [SerializeField] [Range(0f, 1f)] private float smoothness;
        [SerializeField] [Min(0f)] private float subsurfaceScatteringPower;

        [SerializeField] [ColorUsage(showAlpha: false)]
        private Color color;

        [SerializeField] [ColorUsage(showAlpha: false, hdr: true)]
        private Color emission;

        [SerializeField] [ColorUsage(showAlpha: false)]
        private Color subsurfaceColour;

        #endregion

        #region Public Fields

        public const float MinSmoothing = 0.0001f;
        public MaterialType Type => type;
        public Texture2D Texture => texture2D;
        public Color Color => color;
        public Color Emission => emission;
        public float MaterialSmoothing => materialSmoothing;
        public float Metallic => metallic;
        public float Smoothness => smoothness;
        public Color SubsurfaceColour => subsurfaceColour;
        public float SubsurfaceScatteringPower => subsurfaceScatteringPower;

        #endregion

        public SDFMaterial(Color mainCol, Color emission, float metallic, float smoothness, Color subsurfaceColour,
            float subsurfaceScatteringPower, float materialSmoothing)
        {
            type = MaterialType.Color;
            texture2D = default;
            color = mainCol;
            this.emission = emission;
            this.metallic = metallic;
            this.smoothness = smoothness;
            this.subsurfaceColour = subsurfaceColour;
            this.subsurfaceScatteringPower = subsurfaceScatteringPower;
            this.materialSmoothing = materialSmoothing;
        }
    }

    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct SDFMaterialGPU
    {
        public static int Stride => sizeof(float) * 14 + sizeof(int) * 2;

        public int MaterialType;
        public int TextureIndex;
        public Vector3 Color;
        public Vector3 Emission;
        public float Metallic;
        public float Smoothness;
        public float Thickness;
        public Vector3 SubsurfaceColor;
        public float SubsurfaceScatteringPower;
        public float MaterialSmoothing;

        public SDFMaterialGPU(SDFMaterial material)
        {
            MaterialType = (int)material.Type;
            TextureIndex = 0;
            Color = (Vector4)material.Color;
            Emission = (Vector4)material.Emission;
            Metallic = Mathf.Clamp01(material.Metallic);
            Smoothness = Mathf.Clamp01(material.Smoothness);
            Thickness = 0f;
            SubsurfaceColor = (Vector4)material.SubsurfaceColour;
            SubsurfaceScatteringPower =
                material.SubsurfaceScatteringPower; //Mathf.Lerp(5f, 0f, material.SubsurfaceScatteringPower);
            MaterialSmoothing = material.MaterialSmoothing;
        }
    }
}