using Unity.Entities;

namespace ECSExample
{
    /// <summary>
    /// 静态障碍物检测配置（Singleton）
    /// 存在此组件时，FlowFieldStaticObstacleBuildSystem 会在游戏开始时做碰撞检测
    /// </summary>
    public struct FlowFieldStaticObstacleConfig : IComponentData
    {
        /// <summary>用于检测静态碰撞体的 LayerMask 整数值</summary>
        public int layer_mask;

        /// <summary>检测盒的高度（米），应能覆盖场景中最高障碍物的碰撞体</summary>
        public float check_height;
    }
}
