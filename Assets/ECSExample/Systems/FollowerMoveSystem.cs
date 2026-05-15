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
    /// Follower 移动系统：代价场梯度下降 + 空间哈希分离 + 障碍物避让 + 到达减速
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CostFieldBuildSystem))]
    public partial struct FollowerMoveSystem : ISystem
    {
        private EntityQuery                     config_query;
        private EntityQuery                     follower_query;
        private EntityQuery                     player_query;
        private EntityQuery                     obstacle_query;

        // 分离参数
        private const float                     MIN_SEPARATION = 3.0f;      // 最小间距
        private const float                     SEPARATION_WEIGHT = 8.0f;    // 分离力权重
        private const float                     SEPARATION_CELL = 2.0f;      // 分离格子大小
        private const float                     SLOW_RADIUS = 4.0f;          // 到达减速半径
        private const float                     STOP_RADIUS = 2.0f;          // 停止半径
        private const float                     MAX_SPEED = 8f;              // 最大速度限制

        // 障碍物避让参数
        private const float                     OBSTACLE_CELL = 2.0f;        // 障碍物空间哈希格子大小
        private const float                     OBSTACLE_AVOID_MARGIN = 2.0f; // 障碍物避让检测边距
        private const float                     OBSTACLE_AVOID_WEIGHT = 6.0f; // 障碍物避让力全局权重

        public void OnCreate(ref SystemState state)
        {
            config_query = SystemAPI.QueryBuilder()
                .WithAll<CostFieldGridConfig>()
                .Build();
            follower_query = SystemAPI.QueryBuilder()
                .WithAll<FollowerData, LocalTransform>()
                .WithNone<PlayerTag>()
                .Build();
            player_query = SystemAPI.QueryBuilder()
                .WithAll<PlayerTag, LocalTransform>()
                .Build();
            obstacle_query = SystemAPI.QueryBuilder()
                .WithAll<ObstacleTag, ObstacleData, LocalTransform>()
                .Build();
            state.RequireForUpdate(config_query);
            state.RequireForUpdate(follower_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (follower_query.IsEmpty) return;

            var config = SystemAPI.GetSingleton<CostFieldGridConfig>();
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

            // ── 构建障碍物空间哈希 ──
            int obs_grid_width = math.max(1, (int)(config.grid_width * config.cell_size / OBSTACLE_CELL));
            int obs_grid_height = math.max(1, (int)(config.grid_height * config.cell_size / OBSTACLE_CELL));
            int obstacle_count = obstacle_query.CalculateEntityCount();
            var obstacle_grid = new NativeParallelMultiHashMap<int, Entity>(
                math.max(1, obstacle_count), Allocator.TempJob);

            JobHandle obs_fill_handle = fill_handle;  // 默认无依赖
            if (obstacle_count > 0)
            {
                var obs_fill_job = new BuildObstacleHashJob
                {
                    grid_writer = obstacle_grid.AsParallelWriter(),
                    cell_size = OBSTACLE_CELL,
                    grid_width = obs_grid_width,
                    grid_height = obs_grid_height,
                    grid_origin = config.grid_origin
                };
                obs_fill_handle = obs_fill_job.ScheduleParallel(obstacle_query, fill_handle);
            }

            // ── 代价场 Buffer ──
            var singleton_entity = SystemAPI.GetSingletonEntity<CostFieldGridConfig>();
            var cell_buffer = SystemAPI.GetBuffer<CostFieldCellBuffer>(singleton_entity);
            var transform_lookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            // ── 移动 Job ──
            var obstacle_data_lookup = SystemAPI.GetComponentLookup<ObstacleData>(true);

            var move_job = new FollowerMoveJob
            {
                spatial_grid = spatial_grid,
                transform_lookup = transform_lookup,
                cell_buffer = cell_buffer.AsNativeArray(),
                obstacle_grid = obstacle_grid,
                obstacle_data_lookup = obstacle_data_lookup,
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
                obs_cell_size = OBSTACLE_CELL,
                obs_grid_width = obs_grid_width,
                obs_grid_height = obs_grid_height,
                obs_grid_origin = config.grid_origin,
                obstacle_avoid_margin = OBSTACLE_AVOID_MARGIN,
                obstacle_avoid_weight = OBSTACLE_AVOID_WEIGHT,
                slow_radius = SLOW_RADIUS,
                stop_radius = STOP_RADIUS,
                max_speed = MAX_SPEED,
                player_position = player_pos,
                delta_time = SystemAPI.Time.DeltaTime
            };
            var move_handle = move_job.ScheduleParallel(follower_query, obs_fill_handle);

            // ── 合并 + 清理 ──
            var combined = JobHandle.CombineDependencies(fill_handle, move_handle);
            state.Dependency = spatial_grid.Dispose(combined);
            state.Dependency = obstacle_grid.Dispose(state.Dependency);
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

    // ──────────────── 障碍物空间哈希构建 Job ────────────────
    [BurstCompile]
    public partial struct BuildObstacleHashJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, Entity>.ParallelWriter grid_writer;
        public float                            cell_size;
        public int                              grid_width;
        public int                              grid_height;
        public float3                           grid_origin;

        public void Execute(Entity entity, in LocalTransform transform, in ObstacleTag tag)
        {
            float3 relative = transform.Position - grid_origin;
            int cx = math.clamp((int)(relative.x / cell_size), 0, grid_width - 1);
            int cz = math.clamp((int)(relative.z / cell_size), 0, grid_height - 1);
            int key = cz * grid_width + cx;
            grid_writer.Add(key, entity);
        }
    }

    // ──────────────── 移动 + 梯度下降 + 分离 + 避让 Job ────────────────
    [BurstCompile]
    public partial struct FollowerMoveJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, Entity>           spatial_grid;
        [ReadOnly] public NativeParallelMultiHashMap<int, Entity>           obstacle_grid;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform>                              transform_lookup;
        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<ObstacleData>                                obstacle_data_lookup;
        [ReadOnly] public NativeArray<CostFieldCellBuffer>                  cell_buffer;

        // 代价场参数
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

        // 障碍物避让参数
        public float                                    obs_cell_size;
        public int                                      obs_grid_width;
        public int                                      obs_grid_height;
        public float3                                   obs_grid_origin;
        public float                                    obstacle_avoid_margin;
        public float                                    obstacle_avoid_weight;

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

            // ── 1. 代价场梯度下降 ──
            // 查找当前格及 8 邻格中代价最小的格子，朝其中心移动
            float3 flow_relative = my_pos - flow_grid_origin;
            int flow_cx = math.clamp((int)(flow_relative.x / flow_cell_size), 0, flow_grid_width - 1);
            int flow_cz = math.clamp((int)(flow_relative.z / flow_cell_size), 0, flow_grid_height - 1);

            float best_cost = float.MaxValue;
            int best_nx = flow_cx;
            int best_nz = flow_cz;

            // 检查 3×3 邻域（含当前格），找最小代价
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = flow_cx + dx;
                    int nz = flow_cz + dz;
                    if (nx < 0 || nx >= flow_grid_width || nz < 0 || nz >= flow_grid_height)
                        continue;

                    float cost = cell_buffer[nz * flow_grid_width + nx].cost;
                    if (cost < best_cost)
                    {
                        best_cost = cost;
                        best_nx = nx;
                        best_nz = nz;
                    }
                }
            }

            // 朝最低代价邻居方向移动；若当前格即最优则朝玩家直走
            float3 move_dir;
            if (best_nx != flow_cx || best_nz != flow_cz)
            {
                float3 target_center = flow_grid_origin + new float3(
                    (best_nx + 0.5f) * flow_cell_size,
                    0f,
                    (best_nz + 0.5f) * flow_cell_size
                );
                move_dir = math.normalizesafe(target_center - my_pos, float3.zero);
            }
            else
            {
                move_dir = math.normalizesafe(player_position - my_pos, float3.zero);
            }

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

            // ── 3. 障碍物避让力 ──
            float3 obstacle_avoidance = float3.zero;
            int my_obs_cx = math.clamp((int)((my_pos.x - obs_grid_origin.x) / obs_cell_size), 0, obs_grid_width - 1);
            int my_obs_cz = math.clamp((int)((my_pos.z - obs_grid_origin.z) / obs_cell_size), 0, obs_grid_height - 1);
            int obs_checked = 0;
            const int MAX_OBS_CHECKS = 30;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = my_obs_cx + dx;
                    int nz = my_obs_cz + dz;
                    if (nx < 0 || nx >= obs_grid_width || nz < 0 || nz >= obs_grid_height) continue;

                    int key = nz * obs_grid_width + nx;
                    if (!obstacle_grid.TryGetFirstValue(key, out Entity obs, out var obs_iter)) continue;

                    do
                    {
                        if (obs_checked++ >= MAX_OBS_CHECKS) break;

                        if (!obstacle_data_lookup.TryGetComponent(obs, out ObstacleData obs_data))
                            continue;
                        if (!transform_lookup.TryGetComponent(obs, out LocalTransform obs_transform))
                            continue;

                        float3 to_obstacle = obs_transform.Position - my_pos;
                        float dist = math.length(to_obstacle);
                        float avoid_dist = obs_data.radius + obstacle_avoid_margin;

                        if (dist < avoid_dist && dist > 0.00001f)
                        {
                            float3 away_dir = -to_obstacle / dist;  // 远离障碍物
                            float strength = (avoid_dist - dist) / avoid_dist;  // 越近越强
                            obstacle_avoidance += away_dir * strength * obs_data.avoidance_weight;
                        }
                    }
                    while (obstacle_grid.TryGetNextValue(out obs, ref obs_iter));
                }
            }

            // ── 4. 合成为最终方向 ──
            float3 final_dir = move_dir + separation * separation_weight + obstacle_avoidance * obstacle_avoid_weight;
            float dir_length = math.length(final_dir);
            if (dir_length > 0.0001f)
            {
                final_dir /= dir_length;
            }
            else
            {
                final_dir = move_dir;  // 分离力为0时回到代价场方向
            }

            // ── 5. 到达减速 ──
            float dist_to_player = math.distance(my_pos, player_position);
            float speed_multiplier = math.saturate(dist_to_player / slow_radius);
            // 确保最小速度（不完全停住）
            speed_multiplier = math.lerp(0.15f, 1f, speed_multiplier);

            float speed = math.min(follower.move_speed * speed_multiplier, max_speed);

            // ── 6. 应用移动 ──
            float3 new_pos = my_pos + final_dir * speed * delta_time;
            transform.Position = new_pos;
        }
    }
}
