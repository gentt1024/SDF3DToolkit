using Unity.Mathematics;

namespace GTT.SDFTK
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct SdfNodeParams
    {
        public float VoxelSize;
        public uint3 Dimensions;
        public float4x4 Matrix;
    }
}