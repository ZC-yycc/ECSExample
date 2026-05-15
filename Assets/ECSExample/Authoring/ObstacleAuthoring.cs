using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// 障碍物 Authoring：挂载到场景中的障碍物 GameObject（如 Cube、Sphere）
    /// 烘焙为带 ObstacleTag + ObstacleData + LocalTransform 的 Entity
    /// </summary>
    public class ObstacleAuthoring : MonoBehaviour
    {
        [Header("障碍物参数")]
        [Tooltip("障碍物碰撞半径（米）")]
        public float radius = 1.5f;

        [Tooltip("避让力度权重（越大越强，1 = 与分离力同级）")]
        [Range(0f, 10f)]
        public float avoidance_weight = 2f;

        class Baker : Baker<ObstacleAuthoring>
        {
            public override void Bake(ObstacleAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent<ObstacleTag>(entity);
                AddComponent(entity, new ObstacleData
                {
                    radius = authoring.radius,
                    avoidance_weight = authoring.avoidance_weight
                });

                Debug.Log($"[ObstacleAuthoring] Obstacle Entity: {entity}, " +
                    $"radius={authoring.radius}, weight={authoring.avoidance_weight}, " +
                    $"pos={authoring.transform.position}");
            }
        }
    }
}
