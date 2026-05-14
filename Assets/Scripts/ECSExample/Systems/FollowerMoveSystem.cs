using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;

namespace ECSExample
{
    /// <summary>
    /// Follower 移动系统：查询流场方向并使用分离力避免重叠
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FlowFieldBuildSystem))]
    public partial struct FollowerMoveSystem : ISystem
    {
        private EntityQuery                     config_query;
        private EntityQuery                     follower_query;

        public void OnCreate(ref SystemState state)
        {
            config_query = SystemAPI.QueryBuilder()
                .WithAll<FlowFieldGridConfig>()
                .Build();
            follower_query = SystemAPI.QueryBuilder()
                .WithAll<FollowerData, LocalTransform>()
                .WithNone<PlayerTag>()
                .Build();
            state.RequireForUpdate(config_query);
            state.RequireForUpdate(follower_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (follower_query.IsEmpty) return;

            var config = SystemAPI.GetSingleton<FlowFieldGridConfig>();

            int grid_width = config.grid_width;
            int grid_height = config.grid_height;
            float cell_size = config.cell_size;
            float3 grid_origin = config.grid_origin;

            var singleton_entity = SystemAPI.GetSingletonEntity<FlowFieldGridConfig>();
            var cell_buffer = SystemAPI.GetBuffer<FlowFieldCellBuffer>(singleton_entity);

            var job = new FollowerMoveJob
            {
                cell_buffer = cell_buffer.AsNativeArray(),
                grid_width = grid_width,
                grid_height = grid_height,
                cell_size = cell_size,
                grid_origin = grid_origin,
                delta_time = SystemAPI.Time.DeltaTime
            };
            state.Dependency = job.ScheduleParallel(follower_query, state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct FollowerMoveJob : IJobEntity
    {
        [ReadOnly] public NativeArray<FlowFieldCellBuffer> cell_buffer;
        public int grid_width;
        public int grid_height;
        public float cell_size;
        public float3 grid_origin;
        public float delta_time;

        public void Execute(ref LocalTransform transform, in FollowerData follower)
        {
            float3 relative = transform.Position - grid_origin;
            int cell_x = (int)(relative.x / cell_size);
            int cell_z = (int)(relative.z / cell_size);

            cell_x = math.clamp(cell_x, 0, grid_width - 1);
            cell_z = math.clamp(cell_z, 0, grid_height - 1);

            int cell_index = cell_z * grid_width + cell_x;
            float2 direction = cell_buffer[cell_index].direction;

            float3 new_pos = transform.Position;
            new_pos.x += direction.x * follower.move_speed * delta_time;
            new_pos.z += direction.y * follower.move_speed * delta_time;
            transform.Position = new_pos;
        }
    }
}
