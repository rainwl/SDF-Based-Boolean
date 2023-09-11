using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.Serialization;

namespace Source.SDFs.Settings
{
    [System.Serializable]
    public class MainSettings
    {
        [SerializeField] private bool autoUpdate = true;
        [SerializeField] private OutputMode outputMode = OutputMode.Procedural;
        [SerializeField] private bool isAsynchronous;
        [SerializeField] private Material proceduralMaterial;

        public bool AutoUpdate
        {
            get => autoUpdate;
            set => autoUpdate = value;
        }

        public OutputMode OutputMode => outputMode;

        public bool IsAsynchronous => isAsynchronous;

        public Material ProceduralMaterial => proceduralMaterial;

        public void CopySettings(MainSettings source)
        {
            autoUpdate = source.autoUpdate;
            outputMode = source.outputMode;
            isAsynchronous = source.isAsynchronous;
            proceduralMaterial = source.proceduralMaterial;
        }
    }
}