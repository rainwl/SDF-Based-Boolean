using UnityEngine;

namespace Source.SDFs
{
    public interface ISDFGroupComponent
    {
        void UpdateSettingsBuffer(ComputeBuffer computeBuffer);
        void UpdateDataBuffer(ComputeBuffer computeBuffer, ComputeBuffer materialBuffer, int count);
        void Run();
        void OnEmpty();
        void OnNotEmpty();
    }
}