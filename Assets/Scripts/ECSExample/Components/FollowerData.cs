using Unity.Entities;

namespace ECSExample
{
    /// <summary>
    /// Follower 移动数据
    /// </summary>
    public struct FollowerData : IComponentData
    {
        public float move_speed;
    }
}
