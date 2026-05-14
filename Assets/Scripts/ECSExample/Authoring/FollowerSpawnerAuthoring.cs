using Unity.Entities;
using UnityEngine;

namespace ECSExample
{
    public class FollowerSpawnerAuthoring : MonoBehaviour
    {
        [Header("生成配置")]
        public int                              follower_count_ = 10000;
        public float                            move_speed_ = 5f;
        public float                            spawn_radius_ = 50f;

        [Header("模板引用（首选方案）")]
        [Tooltip("SubScene 中挂有 FollowerTemplateAuthoring 的 GameObject")]
        public GameObject                       follower_prefab_;

        class Baker : Baker<FollowerSpawnerAuthoring>
        {
            public override void Bake(FollowerSpawnerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                Entity prefab_entity = Entity.Null;
                if (authoring.follower_prefab_ != null)
                {
                    DependsOn(authoring.follower_prefab_);
                    prefab_entity = GetEntity(authoring.follower_prefab_, TransformUsageFlags.Dynamic);
                }

                AddComponent(entity, new FollowerSpawnConfig
                {
                    follower_count = authoring.follower_count_,
                    move_speed = authoring.move_speed_,
                    spawn_radius = authoring.spawn_radius_,
                    follower_prefab = prefab_entity
                });
            }
        }
    }
}
