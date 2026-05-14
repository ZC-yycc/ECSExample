using Unity.Entities;

namespace ECSExample
{
    /// <summary>
    /// Player 移动数据
    /// </summary>
    public struct PlayerData : IComponentData
    {
        public float move_speed;
    }
}
