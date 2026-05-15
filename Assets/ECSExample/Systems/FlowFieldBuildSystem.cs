using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;

namespace ECSExample
{
    /// <summary>
    /// 从 Player 位置构建流场（Flow Field）
    /// 所有格子存储指向 Player 的方向向量，被静态障碍阻挡的格子方向为零
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMoveSystem))]
    public partial struct FlowFieldBuildSystem : ISystem
    {
        private EntityQuery                     config_query;
        private EntityQuery                     player_query;

        public void OnCreate(ref SystemState state)
        {
            config_query = SystemAPI.QueryBuilder()
                .WithAll<FlowFieldGridConfig>()
                .Build();
            player_query = SystemAPI.QueryBuilder()
                .WithAll<PlayerTag, LocalTransform>()
                .Build();
            state.RequireForUpdate(config_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingletonRW<FlowFieldGridConfig>();

            double current_time = SystemAPI.Time.ElapsedTime;
            if (current_time - config.ValueRO.last_rebuild_time < config.ValueRO.rebuild_interval)
            {
                return;
            }

            config.ValueRW.last_rebuild_time = current_time;

            // 获取 Player 位置（若无 Player 则跳过）
            if (player_query.IsEmpty) return;
            var player_transform = player_query.GetSingleton<LocalTransform>();
            float3 player_pos = player_transform.Position;

            // 获取流场 Buffer
            var singleton_entity = SystemAPI.GetSingletonEntity<FlowFieldGridConfig>();
            var cell_buffer = SystemAPI.GetBuffer<FlowFieldCellBuffer>(singleton_entity);
            var blocked_buffer = SystemAPI.GetBuffer<FlowFieldBlockedBuffer>(singleton_entity);

            int grid_width = config.ValueRO.grid_width;
            int grid_height = config.ValueRO.grid_height;
            float cell_size = config.ValueRO.cell_size;
            float3 grid_origin = config.ValueRO.grid_origin;

            int total_cells = grid_width * grid_height;
            cell_buffer.Resize(total_cells, NativeArrayOptions.UninitializedMemory);

            // 构建方向场：每个格子方向 = normalize(Player位置 - 格子中心)
            // 被静态障碍阻挡的格子 → 方向为零向量
            for (int j = 0; j < grid_height; j++)
            {
                for (int i = 0; i < grid_width; i++)
                {
                    int index = j * grid_width + i;

                    // 静态阻挡检查
                    if (blocked_buffer.Length > index && blocked_buffer[index].blocked)
                    {
                        cell_buffer[index] = new FlowFieldCellBuffer { direction = float2.zero };
                        continue;
                    }

                    float3 cell_center = grid_origin + new float3(
                        (i + 0.5f) * cell_size,
                        0f,
                        (j + 0.5f) * cell_size
                    );
                    float2 dir = math.normalizesafe(
                        new float2(player_pos.x - cell_center.x, player_pos.z - cell_center.z),
                        float2.zero
                    );
                    cell_buffer[index] = new FlowFieldCellBuffer { direction = dir };
                }
            }
        }
    }
}
