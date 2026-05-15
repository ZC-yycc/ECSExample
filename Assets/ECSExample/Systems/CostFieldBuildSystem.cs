using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;

namespace ECSExample
{
    /// <summary>
    /// 代价场构建系统：波前传播，障碍物格子（由 ObstacleMaskElement 标记）不可通行
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMoveSystem))]
    public partial struct CostFieldBuildSystem : ISystem
    {
        private EntityQuery config_query;
        private EntityQuery player_query;

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
            // 首次构建(last_rebuild_time=0)强制执行
            if (config.ValueRO.last_rebuild_time > 0 &&
                current_time - config.ValueRO.last_rebuild_time < config.ValueRO.rebuild_interval)
                return;

            config.ValueRW.last_rebuild_time = current_time;

            if (player_query.IsEmpty) return;
            var player_transform = player_query.GetSingleton<LocalTransform>();
            float3 player_pos = player_transform.Position;

            var singleton_entity = SystemAPI.GetSingletonEntity<CostFieldGridConfig>();
            var cell_buffer = SystemAPI.GetBuffer<CostFieldCellBuffer>(singleton_entity);
            var mask_buffer = SystemAPI.GetBuffer<ObstacleMaskElement>(singleton_entity);

            int gw = config.ValueRO.grid_width;
            int gh = config.ValueRO.grid_height;
            float cs = config.ValueRO.cell_size;
            float3 origin = config.ValueRO.grid_origin;

            int total_cells = gw * gh;
            cell_buffer.Resize(total_cells, NativeArrayOptions.UninitializedMemory);

            // ── 1. 初始化 ──
            for (int i = 0; i < total_cells; i++)
                cell_buffer[i] = new CostFieldCellBuffer { cost = float.MaxValue };

            // ── 2. Player 格代价 = 0 ──
            float3 player_relative = player_pos - origin;
            int pcx = math.clamp((int)(player_relative.x / cs), 0, gw - 1);
            int pcz = math.clamp((int)(player_relative.z / cs), 0, gh - 1);
            cell_buffer[pcz * gw + pcx] = new CostFieldCellBuffer { cost = 0f };

            // ── 3. 波前传播（跳过障碍物格） ──
            int max_passes = gw + gh;
            for (int pass = 0; pass < max_passes; pass++)
            {
                bool changed = false;

                for (int j = 0; j < gh; j++)
                {
                    for (int i = 0; i < gw; i++)
                    {
                        int idx = j * gw + i;
                        if (mask_buffer[idx].blocked) continue;

                        float current = cell_buffer[idx].cost;
                        float best_neighbor = current;

                        if (i > 0 && !mask_buffer[j * gw + (i - 1)].blocked)
                        {
                            float nc = cell_buffer[j * gw + (i - 1)].cost + cs;
                            if (nc < best_neighbor) best_neighbor = nc;
                        }
                        if (i < gw - 1 && !mask_buffer[j * gw + (i + 1)].blocked)
                        {
                            float nc = cell_buffer[j * gw + (i + 1)].cost + cs;
                            if (nc < best_neighbor) best_neighbor = nc;
                        }
                        if (j > 0 && !mask_buffer[(j - 1) * gw + i].blocked)
                        {
                            float nc = cell_buffer[(j - 1) * gw + i].cost + cs;
                            if (nc < best_neighbor) best_neighbor = nc;
                        }
                        if (j < gh - 1 && !mask_buffer[(j + 1) * gw + i].blocked)
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

                if (!changed) break;
            }
        }
    }
}
