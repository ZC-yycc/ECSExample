using Unity.Entities;
using Unity.Mathematics;

namespace ECSExample
{
    /// <summary>
    /// 代价场单元格数据（Buffer Element）
    /// 存储从该格子到目标的距离代价（值越小越接近目标）
    /// </summary>
    public struct CostFieldCellBuffer : IBufferElementData
    {
        /// <summary>到达目标的累计距离代价</summary>
        public float                        cost;
    }

    /// <summary>
    /// 代价场网格配置（Singleton Component）
    /// </summary>
    public struct CostFieldGridConfig : IComponentData
    {
        public int                          grid_width;
        public int                          grid_height;
        public float                        cell_size;
        public float3                       grid_origin;
        public float                        rebuild_interval;
        public double                       last_rebuild_time;
    }
}
