using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;

namespace ECSExample
{
    /// <summary>
    /// 代价场构建系统：以 Player 位置为零代价源点，通过波前传播计算每个格子的距离代价。
    /// 代价 = 从源点沿网格曼哈顿路径到达该格子的累计距离。
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMoveSystem))]
    public partial struct CostFieldBuildSystem : ISystem
    {
        private EntityQuery                     config_query;
        private EntityQuery                     player_query;

        public void OnCreate(ref SystemState state)
        {
            config_query = SystemAPI.QueryBuilder()
                .WithAll<CostFieldGridConfig>()
                .Build();
            player_query = SystemAPI.QueryBuilder()
                .WithAll<PlayerTag, LocalTransform>()
                .Build();
            state.RequireForUpdate(config_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingletonRW<CostFieldGridConfig>();

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

            // 获取代价场 Buffer
            var singleton_entity = SystemAPI.GetSingletonEntity<CostFieldGridConfig>();
            var cell_buffer = SystemAPI.GetBuffer<CostFieldCellBuffer>(singleton_entity);

            int gw = config.ValueRO.grid_width;
            int gh = config.ValueRO.grid_height;
            float cs = config.ValueRO.cell_size;
            float3 origin = config.ValueRO.grid_origin;

            int total_cells = gw * gh;
            cell_buffer.Resize(total_cells, NativeArrayOptions.UninitializedMemory);

            // ── 1. 初始化所有格子代价为极大值 ──
            for (int i = 0; i < total_cells; i++)
            {
                cell_buffer[i] = new CostFieldCellBuffer { cost = float.MaxValue };
            }

            // ── 2. Player 所在格子代价设为 0 ──
            float3 player_relative = player_pos - origin;
            int pcx = math.clamp((int)(player_relative.x / cs), 0, gw - 1);
            int pcz = math.clamp((int)(player_relative.z / cs), 0, gh - 1);
            cell_buffer[pcz * gw + pcx] = new CostFieldCellBuffer { cost = 0f };

            // ── 3. 波前传播（迭代松弛） ──
            // 每个格子的代价 = min(自身, 邻居代价 + cell_size)
            // 最多迭代 (gw + gh) 次保证覆盖整张网格
            int max_passes = gw + gh;
            for (int pass = 0; pass < max_passes; pass++)
            {
                bool changed = false;

                for (int j = 0; j < gh; j++)
                {
                    for (int i = 0; i < gw; i++)
                    {
                        int idx = j * gw + i;
                        float current = cell_buffer[idx].cost;

                        float best_neighbor = current;

                        // 左
                        if (i > 0)
                        {
                            float nc = cell_buffer[j * gw + (i - 1)].cost + cs;
                            if (nc < best_neighbor) best_neighbor = nc;
                        }
                        // 右
                        if (i < gw - 1)
                        {
                            float nc = cell_buffer[j * gw + (i + 1)].cost + cs;
                            if (nc < best_neighbor) best_neighbor = nc;
                        }
                        // 下（z-）
                        if (j > 0)
                        {
                            float nc = cell_buffer[(j - 1) * gw + i].cost + cs;
                            if (nc < best_neighbor) best_neighbor = nc;
                        }
                        // 上（z+）
                        if (j < gh - 1)
                        {
                            float nc = cell_buffer[(j + 1) * gw + i].cost + cs;
                            if (nc < best_neighbor) best_neighbor = nc;
                        }

                        if (best_neighbor < current - 0.0001f)
                        {
                            cell_buffer[idx] = new CostFieldCellBuffer { cost = best_neighbor };
                            changed = true;
                        }
                    }
                }

                // 收敛则提前退出
                if (!changed) break;
            }
        }
    }
}
