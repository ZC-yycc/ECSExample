using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using UnityEngine;

namespace ECSExample
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(CostFieldBuildSystem))]
    public partial struct FollowerSpawnSystem : ISystem
    {
        private bool has_spawned;

        public void OnUpdate(ref SystemState state)
        {
            if (has_spawned) return;
            has_spawned = true;

            if (!SystemAPI.HasSingleton<FollowerSpawnConfig>())
            {
                Debug.LogWarning("[FollowerSpawnSystem] FollowerSpawnConfig 不存在");
                return;
            }

            var config = SystemAPI.GetSingleton<FollowerSpawnConfig>();
            int target_count = config.follower_count;
            if (target_count <= 0) return;

            Debug.Log($"[FollowerSpawnSystem] 目标: {target_count} 个, " +
                $"模板 Entity: {(config.follower_prefab == Entity.Null ? "未设置" : config.follower_prefab.ToString())}");

            var em = state.EntityManager;
            var random = new Unity.Mathematics.Random(42);
            bool has_prefab = config.follower_prefab != Entity.Null
                           && em.Exists(config.follower_prefab);

            if (config.follower_prefab != Entity.Null && !em.Exists(config.follower_prefab))
            {
                Debug.LogWarning($"[FollowerSpawnSystem] 模板 Entity {config.follower_prefab} 不在 World 中！" +
                    "\n可能原因: 1) 模板 Cube 不在 SubScene 中 2) SubScene 未加载");
            }

            if (has_prefab)
            {
                Debug.Log("[FollowerSpawnSystem] 使用 Instantiate 路径（模板渲染）");

                var instances = new NativeArray<Entity>(target_count, Allocator.Temp);
                em.Instantiate(config.follower_prefab, instances);

                for (int i = 0; i < target_count; i++)
                {
                    float angle = random.NextFloat(0f, math.PI * 2f);
                    float radius = random.NextFloat(0f, config.spawn_radius);
                    float3 position = new float3(
                        math.cos(angle) * radius, 0f, math.sin(angle) * radius);

                    em.SetComponentData(instances[i], LocalTransform.FromPositionRotationScale(
                        position, quaternion.identity, 0.5f));
                    em.AddComponentData(instances[i], new FollowerData
                    {
                        move_speed = config.move_speed + random.NextFloat(-1f, 1f)
                    });
                }
                instances.Dispose();
            }
            else
            {
                Debug.LogWarning("[FollowerSpawnSystem] 模板不可用 → 无渲染 → 仅 Scene View 可见标记" +
                    "\n修复: 1) 在 SubScene 中放一个 Cube " +
                    "\n      2) 挂 FollowerTemplateAuthoring 脚本 " +
                    "\n      3) 使用 URP/Lit 材质 " +
                    "\n      4) 拖入 FollowerSpawnerAuthoring.Follower Prefab 字段");

                var archetype = em.CreateArchetype(
                    typeof(LocalTransform),
                    typeof(FollowerData)
                );

                var entities = new NativeArray<Entity>(target_count, Allocator.Temp);
                em.CreateEntity(archetype, entities);

                for (int i = 0; i < target_count; i++)
                {
                    float angle = random.NextFloat(0f, math.PI * 2f);
                    float radius = random.NextFloat(0f, config.spawn_radius);
                    float3 position = new float3(
                        math.cos(angle) * radius, 0f, math.sin(angle) * radius);

                    em.SetComponentData(entities[i], LocalTransform.FromPositionRotationScale(
                        position, quaternion.identity, 0.5f));
                    em.SetComponentData(entities[i], new FollowerData
                    {
                        move_speed = config.move_speed + random.NextFloat(-1f, 1f)
                    });
                }
                entities.Dispose();
            }

            var verify = SystemAPI.QueryBuilder()
                .WithAll<FollowerData>()
                .WithAll<LocalTransform>()
                .Build();
            Debug.Log($"[FollowerSpawnSystem] 实际创建: {verify.CalculateEntityCount()} Entity");
        }
    }
}
