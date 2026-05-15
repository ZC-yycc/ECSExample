using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

namespace ECSExample
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CostFieldBuildSystem))]
    public partial struct FollowerMoveSystem : ISystem
    {
        private EntityQuery                         config_query;
        private EntityQuery                         follower_query;
        private EntityQuery                         player_query;

        private const float                         MIN_SEPARATION = 3.0f;
        private const float                         SEPARATION_WEIGHT = 3.0f;
        private const float                         SEPARATION_CELL = 2.0f;
        private const float                         SLOW_RADIUS = 4.0f;
        private const float                         STOP_RADIUS = 2.0f;
        private const float                         MAX_SPEED = 8f;

        public void OnCreate(ref SystemState state)
        {
            config_query = SystemAPI.QueryBuilder().WithAll<CostFieldGridConfig>().Build();
            follower_query = SystemAPI.QueryBuilder().WithAll<FollowerData, LocalTransform>().WithNone<PlayerTag>().Build();
            player_query = SystemAPI.QueryBuilder().WithAll<PlayerTag, LocalTransform>().Build();
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

            float3 player_pos = float3.zero;
            if (!player_query.IsEmpty)
                player_pos = player_query.GetSingleton<LocalTransform>().Position;

            int sep_grid_width = math.max(1, (int)(config.grid_width * config.cell_size / SEPARATION_CELL));
            int sep_grid_height = math.max(1, (int)(config.grid_height * config.cell_size / SEPARATION_CELL));
            var spatial_grid = new NativeParallelMultiHashMap<int, Entity>(follower_count, Allocator.TempJob);
            var fill_job = new BuildSpatialHashJob
            {
                grid_writer = spatial_grid.AsParallelWriter(),
                cell_size = SEPARATION_CELL, grid_width = sep_grid_width,
                grid_height = sep_grid_height, grid_origin = config.grid_origin
            };
            var fill_handle = fill_job.ScheduleParallel(follower_query, state.Dependency);

            var singleton_entity = SystemAPI.GetSingletonEntity<CostFieldGridConfig>();
            var cell_buffer = SystemAPI.GetBuffer<CostFieldCellBuffer>(singleton_entity);
            var mask_buffer = SystemAPI.GetBuffer<ObstacleMaskElement>(singleton_entity);
            var transform_lookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            var move_job = new FollowerMoveJob
            {
                spatial_grid = spatial_grid, transform_lookup = transform_lookup,
                cell_buffer = cell_buffer.AsNativeArray(),
                mask_buffer = mask_buffer.AsNativeArray(),
                flow_grid_width = config.grid_width, flow_grid_height = config.grid_height,
                flow_cell_size = config.cell_size, flow_grid_origin = config.grid_origin,
                sep_cell_size = SEPARATION_CELL, sep_grid_width = sep_grid_width,
                sep_grid_height = sep_grid_height, sep_grid_origin = config.grid_origin,
                min_separation = MIN_SEPARATION, separation_weight = SEPARATION_WEIGHT,
                slow_radius = SLOW_RADIUS, stop_radius = STOP_RADIUS, max_speed = MAX_SPEED,
                player_position = player_pos, delta_time = SystemAPI.Time.DeltaTime
            };
            var move_handle = move_job.ScheduleParallel(follower_query, fill_handle);
            state.Dependency = spatial_grid.Dispose(move_handle);
        }
    }

    [BurstCompile]
    public partial struct BuildSpatialHashJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, Entity>.ParallelWriter grid_writer;
        public float cell_size; public int grid_width; public int grid_height; public float3 grid_origin;
        public void Execute(Entity entity, in LocalTransform transform)
        {
            float3 relative = transform.Position - grid_origin;
            int cx = math.clamp((int)(relative.x / cell_size), 0, grid_width - 1);
            int cz = math.clamp((int)(relative.z / cell_size), 0, grid_height - 1);
            grid_writer.Add(cz * grid_width + cx, entity);
        }
    }

    [BurstCompile]
    public partial struct FollowerMoveJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, Entity>               spatial_grid;
        [ReadOnly, NativeDisableContainerSafetyRestriction] public ComponentLookup<LocalTransform> transform_lookup;
        [ReadOnly] public NativeArray<CostFieldCellBuffer>                      cell_buffer;
        [ReadOnly] public NativeArray<ObstacleMaskElement>                      mask_buffer;

        public int                                              flow_grid_width;
        public int                                              flow_grid_height;
        public float                                            flow_cell_size;
        public float3                                           flow_grid_origin;
        public float                                            sep_cell_size; 
        public int                                              sep_grid_width;
        public int                                              sep_grid_height;
        public float3                                           sep_grid_origin;
        public float                                            min_separation;
        public float                                            separation_weight;
        public float                                            slow_radius;
        public float                                            max_speed;
        public float                                            stop_radius;
        public float3                                           player_position; 
        public float                                            delta_time;

        public void Execute(Entity entity, ref LocalTransform transform, in FollowerData follower)
        {
            float3 my_pos = transform.Position;

            if (math.distance(my_pos, player_position) < stop_radius) return;

            // ── 1. 代价场梯度下降（跳过阻挡格） ──
            float3 flow_relative = my_pos - flow_grid_origin;
            int flow_cx = math.clamp((int)(flow_relative.x / flow_cell_size), 0, flow_grid_width - 1);
            int flow_cz = math.clamp((int)(flow_relative.z / flow_cell_size), 0, flow_grid_height - 1);
            float best_cost = float.MaxValue; int best_nx = flow_cx, best_nz = flow_cz;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = flow_cx + dx, nz = flow_cz + dz;
                    if (nx < 0 || nx >= flow_grid_width || nz < 0 || nz >= flow_grid_height) continue;
                    if (mask_buffer[nz * flow_grid_width + nx].blocked) continue;
                    float cost = cell_buffer[nz * flow_grid_width + nx].cost;
                    if (cost < best_cost) { best_cost = cost; best_nx = nx; best_nz = nz; }
                }

            float3 move_dir;
            if (best_nx != flow_cx || best_nz != flow_cz)
            {
                float3 target_center = flow_grid_origin + new float3((best_nx + 0.5f) * flow_cell_size, 0f, (best_nz + 0.5f) * flow_cell_size);
                float3 diff = target_center - my_pos; diff.y = 0f;
                move_dir = math.normalizesafe(diff, float3.zero);
            }
            else
            {
                float3 to_player = player_position - my_pos; to_player.y = 0f;
                move_dir = math.normalizesafe(to_player, float3.zero);
            }

            // ── 2. 空间哈希分离力 ──
            float3 separation = float3.zero;
            int my_sep_cx = math.clamp((int)((my_pos.x - sep_grid_origin.x) / sep_cell_size), 0, sep_grid_width - 1);
            int my_sep_cz = math.clamp((int)((my_pos.z - sep_grid_origin.z) / sep_cell_size), 0, sep_grid_height - 1);
            int neighbor_count = 0; const int MAX_NEIGHBORS = 20;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = my_sep_cx + dx, nz = my_sep_cz + dz;
                    if (nx < 0 || nx >= sep_grid_width || nz < 0 || nz >= sep_grid_height) continue;
                    int key = nz * sep_grid_width + nx;
                    if (!spatial_grid.TryGetFirstValue(key, out Entity other, out var iter)) continue;
                    int checked_in_cell = 0;
                    do
                    {
                        if (other == entity) continue;
                        if (checked_in_cell++ >= MAX_NEIGHBORS) break;
                        if (!transform_lookup.TryGetComponent(other, out LocalTransform other_transform)) continue;
                        float3 other_pos = other_transform.Position;
                        float3 diff = my_pos - other_pos; diff.y = 0f;
                        float dist_sq = math.lengthsq(diff);
                        if (dist_sq < min_separation * min_separation && dist_sq > 0.00001f)
                        {
                            float dist = math.sqrt(dist_sq);
                            separation += diff / dist * ((min_separation - dist) / min_separation);
                            neighbor_count++;
                        }
                    } while (spatial_grid.TryGetNextValue(out other, ref iter));
                }
            if (neighbor_count > 0) separation /= neighbor_count;

            float3 final_dir = move_dir + separation * separation_weight;
            final_dir.y = 0f;
            float dir_length = math.length(final_dir);
            if (dir_length > 0.0001f) final_dir /= dir_length; else final_dir = move_dir;

            float dist_to_player = math.distance(my_pos, player_position);
            float speed_multiplier = math.saturate(dist_to_player / slow_radius);
            speed_multiplier = math.lerp(0.15f, 1f, speed_multiplier);
            float speed = math.min(follower.move_speed * speed_multiplier, max_speed);

            float3 new_pos = my_pos + final_dir * speed * delta_time;
            new_pos.y = 0f;
            transform.Position = new_pos;
        }
    }
}
