using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

namespace ECSExample
{
    /// <summary>
    /// Follower 移动系统：流场方向 + 空间哈希分离 + 到达减速
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FlowFieldBuildSystem))]
    public partial struct FollowerMoveSystem : ISystem
    {
        private EntityQuery                     config_query;
        private EntityQuery                     follower_query;
        private EntityQuery                     player_query;
                    
        // 分离参数                 
        private const float                     MIN_SEPARATION = 3.0f;      // 最小间距
        private const float                     SEPARATION_WEIGHT = 8.0f;    // 分离力权重
        private const float                     SEPARATION_CELL = 2.0f;      // 分离格子大小
        private const float                     SLOW_RADIUS = 4.0f;          // 到达减速半径
        private const float                     STOP_RADIUS = 2.0f;          // 停止半径
        private const float                     MAX_SPEED = 8f;              // 最大速度限制

        public void OnCreate(ref SystemState state)
        {
            config_query = SystemAPI.QueryBuilder()
                .WithAll<FlowFieldGridConfig>()
                .Build();
            follower_query = SystemAPI.QueryBuilder()
                .WithAll<FollowerData, LocalTransform>()
                .WithNone<PlayerTag>()
                .Build();
            player_query = SystemAPI.QueryBuilder()
                .WithAll<PlayerTag, LocalTransform>()
                .Build();
            state.RequireForUpdate(config_query);
            state.RequireForUpdate(follower_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (follower_query.IsEmpty) return;

            var config = SystemAPI.GetSingleton<FlowFieldGridConfig>();
            int follower_count = follower_query.CalculateEntityCount();
            if (follower_count == 0) return;

            // ── 获取 Player 位置 ──
            float3 player_pos = float3.zero;
            if (!player_query.IsEmpty)
            {
                player_pos = player_query.GetSingleton<LocalTransform>().Position;
            }

            // ── 构建空间哈希（分离用） ──
            int sep_grid_width = math.max(1, (int)(config.grid_width * config.cell_size / SEPARATION_CELL));
            int sep_grid_height = math.max(1, (int)(config.grid_height * config.cell_size / SEPARATION_CELL));
            var spatial_grid = new NativeParallelMultiHashMap<int, Entity>(
                follower_count, Allocator.TempJob);

            var fill_job = new BuildSpatialHashJob
            {
                grid_writer = spatial_grid.AsParallelWriter(),
                cell_size = SEPARATION_CELL,
                grid_width = sep_grid_width,
                grid_height = sep_grid_height,
                grid_origin = config.grid_origin
            };
            var fill_handle = fill_job.ScheduleParallel(follower_query, state.Dependency);

            // ── 流场 Buffer ──
            var singleton_entity = SystemAPI.GetSingletonEntity<FlowFieldGridConfig>();
            var cell_buffer = SystemAPI.GetBuffer<FlowFieldCellBuffer>(singleton_entity);
            var transform_lookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            // ── 移动 Job ──
            var move_job = new FollowerMoveJob
            {
                spatial_grid = spatial_grid,
                transform_lookup = transform_lookup,
                cell_buffer = cell_buffer.AsNativeArray(),
                flow_grid_width = config.grid_width,
                flow_grid_height = config.grid_height,
                flow_cell_size = config.cell_size,
                flow_grid_origin = config.grid_origin,
                sep_cell_size = SEPARATION_CELL,
                sep_grid_width = sep_grid_width,
                sep_grid_height = sep_grid_height,
                sep_grid_origin = config.grid_origin,
                min_separation = MIN_SEPARATION,
                separation_weight = SEPARATION_WEIGHT,
                slow_radius = SLOW_RADIUS,
                stop_radius = STOP_RADIUS,
                max_speed = MAX_SPEED,
                player_position = player_pos,
                delta_time = SystemAPI.Time.DeltaTime
            };
            var move_handle = move_job.ScheduleParallel(follower_query, fill_handle);

            // ── 合并 + 清理 ──
            var combined = JobHandle.CombineDependencies(fill_handle, move_handle);
            state.Dependency = spatial_grid.Dispose(combined);
        }
    }

    // ──────────────── 空间哈希构建 Job ────────────────
    [BurstCompile]
    public partial struct BuildSpatialHashJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, Entity>.ParallelWriter grid_writer;
        public float                            cell_size;
        public int                              grid_width;
        public int                              grid_height;
        public float3                           grid_origin;

        public void Execute(Entity entity, in LocalTransform transform)
        {
            float3 relative = transform.Position - grid_origin;
            int cx = math.clamp((int)(relative.x / cell_size), 0, grid_width - 1);
            int cz = math.clamp((int)(relative.z / cell_size), 0, grid_height - 1);
            int key = cz * grid_width + cx;
            grid_writer.Add(key, entity);
        }
    }

    // ──────────────── 移动 + 分离 Job ────────────────
    [BurstCompile]
    public partial struct FollowerMoveJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, Entity>           spatial_grid;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform>                              transform_lookup;
        [ReadOnly] public NativeArray<FlowFieldCellBuffer>                  cell_buffer;

        // 流场参数
        public int                                      flow_grid_width;
        public int                                      flow_grid_height;
        public float                                    flow_cell_size;
        public float3                                   flow_grid_origin;

        // 分离参数
        public float                                    sep_cell_size;
        public int                                      sep_grid_width;
        public int                                      sep_grid_height;
        public float3                                   sep_grid_origin;
        public float                                    min_separation;
        public float                                    separation_weight;

        // 减速参数
        public float                                    slow_radius;
        public float                                    stop_radius;
        public float                                    max_speed;

        // 全局数据
        public float3                                   player_position;
        public float                                    delta_time;

        public void Execute(Entity entity, ref LocalTransform transform, in FollowerData follower)
        {
            float3 my_pos = transform.Position;

            // ── 0. 到达停止距离则不动 ──
            if (math.distance(my_pos, player_position) < stop_radius)
                return;

            // ── 1. 流场方向 ──
            float3 flow_relative = my_pos - flow_grid_origin;
            int flow_cx = math.clamp((int)(flow_relative.x / flow_cell_size), 0, flow_grid_width - 1);
            int flow_cz = math.clamp((int)(flow_relative.z / flow_cell_size), 0, flow_grid_height - 1);
            int flow_index = flow_cz * flow_grid_width + flow_cx;
            float2 flow_dir = cell_buffer[flow_index].direction;
            float3 move_dir = new float3(flow_dir.x, 0f, flow_dir.y);

            // ── 2. 空间哈希分离力 ──
            float3 separation = float3.zero;
            int my_sep_cx = math.clamp((int)((my_pos.x - sep_grid_origin.x) / sep_cell_size), 0, sep_grid_width - 1);
            int my_sep_cz = math.clamp((int)((my_pos.z - sep_grid_origin.z) / sep_cell_size), 0, sep_grid_height - 1);

            int neighbor_count = 0;
            const int MAX_NEIGHBORS = 20;  // 每方向最多检查20个邻居

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = my_sep_cx + dx;
                    int nz = my_sep_cz + dz;
                    if (nx < 0 || nx >= sep_grid_width || nz < 0 || nz >= sep_grid_height) continue;

                    int key = nz * sep_grid_width + nx;
                    if (!spatial_grid.TryGetFirstValue(key, out Entity other, out var iter)) continue;

                    int checked_in_cell = 0;
                    do
                    {
                        if (other == entity) continue;
                        if (checked_in_cell++ >= MAX_NEIGHBORS) break;

                        if (!transform_lookup.TryGetComponent(other, out LocalTransform other_transform))
                            continue;

                        float3 other_pos = other_transform.Position;
                        float3 diff = my_pos - other_pos;
                        float dist_sq = math.lengthsq(diff);
                        float min_dist = min_separation;
                        float min_dist_sq = min_dist * min_dist;

                        if (dist_sq < min_dist_sq && dist_sq > 0.00001f)
                        {
                            float dist = math.sqrt(dist_sq);
                            float3 dir = diff / dist;
                            float strength = (min_dist - dist) / min_dist;
                            separation += dir * strength;
                            neighbor_count++;
                        }
                    }
                    while (spatial_grid.TryGetNextValue(out other, ref iter));
                }
            }

            // 归一化分离力
            if (neighbor_count > 0)
            {
                separation /= neighbor_count;
            }

            // ── 3. 合成为最终方向 ──
            float3 final_dir = move_dir + separation * separation_weight;
            float dir_length = math.length(final_dir);
            if (dir_length > 0.0001f)
            {
                final_dir /= dir_length;
            }
            else
            {
                final_dir = move_dir;  // 分离力为0时回到流场方向
            }

            // ── 4. 到达减速 ──
            float dist_to_player = math.distance(my_pos, player_position);
            float speed_multiplier = math.saturate(dist_to_player / slow_radius);
            // 确保最小速度（不完全停住）
            speed_multiplier = math.lerp(0.15f, 1f, speed_multiplier);

            float speed = math.min(follower.move_speed * speed_multiplier, max_speed);

            // ── 5. 应用移动 ──
            float3 new_pos = my_pos + final_dir * speed * delta_time;
            transform.Position = new_pos;
        }
    }
}
