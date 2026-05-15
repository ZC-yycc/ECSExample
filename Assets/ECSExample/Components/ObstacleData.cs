using Unity.Entities;

namespace ECSExample
{
    /// <summary>
    /// 障碍物数据 — 定义障碍物的避让参数
    /// </summary>
    public struct ObstacleData : IComponentData
    {
        /// <summary>障碍物碰撞半径</summary>
        public float radius;

        /// <summary>避让力度权重（1 = 与分离力同级）</summary>
        public float avoidance_weight;
    }
}
