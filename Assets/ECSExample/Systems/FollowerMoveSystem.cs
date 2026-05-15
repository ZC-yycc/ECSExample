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
    /// Follower 移动系统：流场方向 + 空间哈希分离 + 障碍物避让 + 静态障碍阻挡 + 到达减速
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FlowFieldBuildSystem))]
    public partial struct FollowerMoveSystem : ISystem
    {
        private EntityQuery                     config_query;
        private EntityQuery                     follower_query;
        private EntityQuery                     player_query;
        private EntityQuery                     obstacle_query;
                    
        // 分离参数                 
        private const float                     MIN_SEPARATION = 3.0f;
        private const float                     SEPARATION_WEIGHT = 8.0f;
        private const float                     SEPARATION_CELL = 2.0f;
        private const float                     SLOW_RADIUS = 4.0f;
        private const float                     STOP_RADIUS = 2.0f;
        private const float                     MAX_SPEED = 8f;

        // 障碍物避让参数
        private const float                     OBSTACLE_CELL = 2.0f;
        private const float                     OBSTACLE_AVOID_MARGIN = 2.0f;
        private const float                     OBSTACLE_AVOID_WEIGHT = 6.0f;

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

            var config = SystemAPI.GetSingleton<FlowFieldGridConfig>();
            int follower_count = follower_query.CalculateEntityCount();
            if (follower_count == 0) return;

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

            JobHandle obs_fill_handle = fill_handle;
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

            var singleton_entity = SystemAPI.GetSingletonEntity<FlowFieldGridConfig>();
            var cell_buffer = SystemAPI.GetBuffer<FlowFieldCellBuffer>(singleton_entity);
            var blocked_buffer = SystemAPI.GetBuffer<FlowFieldBlockedBuffer>(singleton_entity);
            var transform_lookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var obstacle_data_lookup = SystemAPI.GetComponentLookup<ObstacleData>(true);

            var move_job = new FollowerMoveJob
            {
                spatial_grid = spatial_grid,
                transform_lookup = transform_lookup,
                cell_buffer = cell_buffer.AsNativeArray(),
                blocked_buffer = blocked_buffer.AsNativeArray(),
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
        public float cell_size;
        public int grid_width;
        public int grid_height;
        public float3 grid_origin;

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
        public float cell_size;
        public int grid_width;
        public int grid_height;
        public float3 grid_origin;

        public void Execute(Entity entity, in LocalTransform transform, in ObstacleTag tag)
        {
            float3 relative = transform.Position - grid_origin;
            int cx = math.clamp((int)(relative.x / cell_size), 0, grid_width - 1);
            int cz = math.clamp((int)(relative.z / cell_size), 0, grid_height - 1);
            int key = cz * grid_width + cx;
            grid_writer.Add(key, entity);
        }
    }

    // ──────────────── 移动 + 分离 + 避让 + 静态阻挡 Job ────────────────
    [BurstCompile]
    public partial struct FollowerMoveJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, Entity> spatial_grid;
        [ReadOnly] public NativeParallelMultiHashMap<int, Entity> obstacle_grid;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> transform_lookup;
        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<ObstacleData> obstacle_data_lookup;
        [ReadOnly] public NativeArray<FlowFieldCellBuffer> cell_buffer;
        [ReadOnly] public NativeArray<FlowFieldBlockedBuffer> blocked_buffer;

        public int flow_grid_width;
        public int flow_grid_height;
        public float flow_cell_size;
        public float3 flow_grid_origin;

        public float sep_cell_size;
        public int sep_grid_width;
        public int sep_grid_height;
        public float3 sep_grid_origin;
        public float min_separation;
        public float separation_weight;

        public float obs_cell_size;
        public int obs_grid_width;
        public int obs_grid_height;
        public float3 obs_grid_origin;
        public float obstacle_avoid_margin;
        public float obstacle_avoid_weight;

        public float slow_radius;
        public float stop_radius;
        public float max_speed;

        public float3 player_position;
        public float delta_time;

        public void Execute(Entity entity, ref LocalTransform transform, in FollowerData follower)
        {
            float3 my_pos = transform.Position;

            // ── 0. 到达停止距离则不动 ──
            if (math.distance(my_pos, player_position) < stop_radius)
                return;

            // ── 0.5 计算当前流场格子索引 ──
            float3 flow_relative = my_pos - flow_grid_origin;
            int flow_cx = math.clamp((int)(flow_relative.x / flow_cell_size), 0, flow_grid_width - 1);
            int flow_cz = math.clamp((int)(flow_relative.z / flow_cell_size), 0, flow_grid_height - 1);
            int flow_index = flow_cz * flow_grid_width + flow_cx;

            // ── 0.6 静态障碍阻挡：检测当前格子 ──
            bool in_blocked_cell = (blocked_buffer.Length > flow_index) && blocked_buffer[flow_index].blocked;

            // ── 1. 流场方向（阻挡格内 → 逃离） ──
            float3 move_dir;
            if (in_blocked_cell)
            {
                float3 best_escape = float3.zero;
                float best_d = float.MaxValue;

                // 右
                if (flow_cx + 1 < flow_grid_width)
                {
                    int ni = flow_cz * flow_grid_width + (flow_cx + 1);
                    if (ni < blocked_buffer.Length && !blocked_buffer[ni].blocked)
                    {
                        float3 nc = flow_grid_origin + new float3((flow_cx + 1.5f) * flow_cell_size, 0f, (flow_cz + 0.5f) * flow_cell_size);
                        float d = math.distancesq(my_pos, nc);
                        if (d < best_d) { best_d = d; best_escape = nc - my_pos; }
                    }
                }
                // 左
                if (flow_cx - 1 >= 0)
                {
                    int ni = flow_cz * flow_grid_width + (flow_cx - 1);
                    if (ni < blocked_buffer.Length && !blocked_buffer[ni].blocked)
                    {
                        float3 nc = flow_grid_origin + new float3((flow_cx - 0.5f) * flow_cell_size, 0f, (flow_cz + 0.5f) * flow_cell_size);
                        float d = math.distancesq(my_pos, nc);
                        if (d < best_d) { best_d = d; best_escape = nc - my_pos; }
                    }
                }
                // 前
                if (flow_cz + 1 < flow_grid_height)
                {
                    int ni = (flow_cz + 1) * flow_grid_width + flow_cx;
                    if (ni < blocked_buffer.Length && !blocked_buffer[ni].blocked)
                    {
                        float3 nc = flow_grid_origin + new float3((flow_cx + 0.5f) * flow_cell_size, 0f, (flow_cz + 1.5f) * flow_cell_size);
                        float d = math.distancesq(my_pos, nc);
                        if (d < best_d) { best_d = d; best_escape = nc - my_pos; }
                    }
                }
                // 后
                if (flow_cz - 1 >= 0)
                {
                    int ni = (flow_cz - 1) * flow_grid_width + flow_cx;
                    if (ni < blocked_buffer.Length && !blocked_buffer[ni].blocked)
                    {
                        float3 nc = flow_grid_origin + new float3((flow_cx + 0.5f) * flow_cell_size, 0f, (flow_cz - 0.5f) * flow_cell_size);
                        float d = math.distancesq(my_pos, nc);
                        if (d < best_d) { best_d = d; best_escape = nc - my_pos; }
                    }
                }

                if (best_d < float.MaxValue)
                    move_dir = math.normalizesafe(new float3(best_escape.x, 0f, best_escape.z), float3.zero);
                else
                    return; // 被完全包围
            }
            else
            {
                float2 flow_dir = cell_buffer[flow_index].direction;
                move_dir = new float3(flow_dir.x, 0f, flow_dir.y);
            }

            // ── 2. 空间哈希分离力 ──
            float3 separation = float3.zero;
            int my_sep_cx = math.clamp((int)((my_pos.x - sep_grid_origin.x) / sep_cell_size), 0, sep_grid_width - 1);
            int my_sep_cz = math.clamp((int)((my_pos.z - sep_grid_origin.z) / sep_cell_size), 0, sep_grid_height - 1);
            int neighbor_count = 0;
            const int MAX_NEIGHBORS = 20;

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
                        if (!transform_lookup.TryGetComponent(other, out LocalTransform ot)) continue;
                        float3 diff = my_pos - ot.Position;
                        float dist_sq = math.lengthsq(diff);
                        float ms = min_separation;
                        if (dist_sq < ms * ms && dist_sq > 0.00001f)
                        {
                            float dist = math.sqrt(dist_sq);
                            separation += (diff / dist) * ((ms - dist) / ms);
                            neighbor_count++;
                        }
                    }
                    while (spatial_grid.TryGetNextValue(out other, ref iter));
                }
            }
            if (neighbor_count > 0) separation /= neighbor_count;

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
                        if (!obstacle_data_lookup.TryGetComponent(obs, out ObstacleData od)) continue;
                        if (!transform_lookup.TryGetComponent(obs, out LocalTransform ot)) continue;
                        float3 to_obs = ot.Position - my_pos;
                        float dist = math.length(to_obs);
                        float ad = od.radius + obstacle_avoid_margin;
                        if (dist < ad && dist > 0.00001f)
                        {
                            float3 away = -to_obs / dist;
                            obstacle_avoidance += away * ((ad - dist) / ad) * od.avoidance_weight;
                        }
                    }
                    while (obstacle_grid.TryGetNextValue(out obs, ref obs_iter));
                }
            }

            // ── 4. 合成最终方向 ──
            float3 final_dir = move_dir + separation * separation_weight + obstacle_avoidance * obstacle_avoid_weight;
            float dir_len = math.length(final_dir);
            if (dir_len > 0.0001f) final_dir /= dir_len;
            else final_dir = move_dir;

            // ── 5. 到达减速 ──
            float d2p = math.distance(my_pos, player_position);
            float spd_mul = math.lerp(0.15f, 1f, math.saturate(d2p / slow_radius));
            float speed = math.min(follower.move_speed * spd_mul, max_speed);

            // ── 6. 静态障碍阻挡：目标位置检查 + 边缘滑动 ──
            float3 new_pos = my_pos + final_dir * speed * delta_time;
            int tcx = math.clamp((int)((new_pos.x - flow_grid_origin.x) / flow_cell_size), 0, flow_grid_width - 1);
            int tcz = math.clamp((int)((new_pos.z - flow_grid_origin.z) / flow_cell_size), 0, flow_grid_height - 1);
            int ti = tcz * flow_grid_width + tcx;

            if (ti < blocked_buffer.Length && blocked_buffer[ti].blocked)
            {
                // 尝试 X / Z 分别滑动
                float3 sx = my_pos + new float3(final_dir.x, 0f, 0f) * speed * delta_time;
                float3 sz = my_pos + new float3(0f, 0f, final_dir.z) * speed * delta_time;

                int scx = math.clamp((int)((sx.x - flow_grid_origin.x) / flow_cell_size), 0, flow_grid_width - 1);
                int scz = math.clamp((int)((sx.z - flow_grid_origin.z) / flow_cell_size), 0, flow_grid_height - 1);
                bool x_ok = (scz * flow_grid_width + scx) < blocked_buffer.Length
                         && !blocked_buffer[scz * flow_grid_width + scx].blocked;

                int sctx = math.clamp((int)((sz.x - flow_grid_origin.x) / flow_cell_size), 0, flow_grid_width - 1);
                int sctz = math.clamp((int)((sz.z - flow_grid_origin.z) / flow_cell_size), 0, flow_grid_height - 1);
                bool z_ok = (sctz * flow_grid_width + sctx) < blocked_buffer.Length
                         && !blocked_buffer[sctz * flow_grid_width + sctx].blocked;

                if (x_ok && z_ok)
                    new_pos = math.abs(final_dir.x) > math.abs(final_dir.z) ? sx : sz;
                else if (x_ok)
                    new_pos = sx;
                else if (z_ok)
                    new_pos = sz;
                else
                    return; // 两方向都被阻挡
            }

            transform.Position = new_pos;
        }
    }
}
