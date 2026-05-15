using Unity.Entities;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// 静态障碍物检测 Authoring：挂载到 SubScene 空物体上。
    /// 配置后，游戏开始时会用 Physics.CheckBox 逐格检测指定 Layer 的碰撞体。
    /// </summary>
    public class FlowFieldStaticObstacleAuthoring : MonoBehaviour
    {
        [Header("静态障碍物检测")]
        [Tooltip("要检测的碰撞体 Layer（例如设为 Obstacle 层）。设为 Everything 则检测全部碰撞体。")]
        public LayerMask obstacle_layer = -1;

        [Tooltip("检测盒的高度（米），应 >= 场景中最高障碍物的碰撞体高度")]
        public float check_height = 5f;

        class Baker : Baker<FlowFieldStaticObstacleAuthoring>
        {
            public override void Bake(FlowFieldStaticObstacleAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new FlowFieldStaticObstacleConfig
                {
                    layer_mask = authoring.obstacle_layer.value,
                    check_height = authoring.check_height
                });
                Debug.Log($"[StaticObstacleAuthoring] Entity: {entity}, " +
                    $"layer_mask=0x{authoring.obstacle_layer.value:X}, check_height={authoring.check_height}");
            }
        }
    }
}
