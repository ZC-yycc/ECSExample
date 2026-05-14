using Unity.Entities;

namespace ECSExample
{
    /// <summary>
    /// Follower 生成配置（Singleton）
    /// </summary>
    public struct FollowerSpawnConfig : IComponentData
    {
        public int                          follower_count;
        public float                        move_speed;
        public float                        spawn_radius;
        public Entity                       follower_prefab;
    }
}
