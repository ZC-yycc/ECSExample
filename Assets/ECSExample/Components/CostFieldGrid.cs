using Unity.Entities;
using Unity.Mathematics;

namespace ECSExample
{
    /// <summary>
    /// 代价场单元格数据（Buffer Element）
    /// </summary>
    public struct CostFieldCellBuffer : IBufferElementData
    {
        public float cost;
    }

    /// <summary>
    /// 代价场网格配置（Singleton Component）
    /// </summary>
    public struct CostFieldGridConfig : IComponentData
    {
        public int grid_width;
        public int grid_height;
        public float cell_size;
        public float3 grid_origin;
        public float rebuild_interval;
        public double last_rebuild_time;
        /// <summary>障碍物检测 Layer（位掩码）</summary>
        public int obstacle_layer_mask;
    }
}
