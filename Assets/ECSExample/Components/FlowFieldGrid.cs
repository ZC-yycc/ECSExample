using Unity.Entities;
using Unity.Mathematics;

namespace ECSExample
{
    /// <summary>
    /// 流场单元格方向数据（Buffer Element）
    /// </summary>
    public struct FlowFieldCellBuffer : IBufferElementData
    {
        public float2                       direction;
    }

    /// <summary>
    /// 流场网格配置（Singleton Component）
    /// </summary>
    public struct FlowFieldGridConfig : IComponentData
    {
        public int                          grid_width;
        public int                          grid_height;
        public float                        cell_size;
        public float3                       grid_origin;
        public float                        rebuild_interval;
        public double                       last_rebuild_time;
    }
}
