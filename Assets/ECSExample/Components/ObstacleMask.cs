using Unity.Entities;

namespace ECSExample
{
    /// <summary>
    /// 障碍物掩码 Buffer — 标记该格是否被碰撞体占据
    /// 由 ObstacleMaskBuildSystem 在游戏开始时通过物理检测构建
    /// </summary>
    public struct ObstacleMaskElement : IBufferElementData
    {
        public bool blocked;
    }
}
