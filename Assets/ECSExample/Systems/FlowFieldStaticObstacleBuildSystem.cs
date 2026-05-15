using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// 静态障碍物构建系统：游戏开始时运行一次，
    /// 逐格使用 Physics.CheckBox 检测指定 Layer 的碰撞体，标记被阻挡的格子。
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(FlowFieldBuildSystem))]
    public partial struct FlowFieldStaticObstacleBuildSystem : ISystem
    {
        private bool has_run;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FlowFieldGridConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (has_run) return;

            // 如果没有静态障碍配置，跳过（全部格子保持默认 false）
            if (!SystemAPI.HasSingleton<FlowFieldStaticObstacleConfig>())
            {
                has_run = true;
                return;
            }

            var grid_config = SystemAPI.GetSingleton<FlowFieldGridConfig>();
            var obs_config = SystemAPI.GetSingleton<FlowFieldStaticObstacleConfig>();

            var singleton_entity = SystemAPI.GetSingletonEntity<FlowFieldGridConfig>();
            var blocked_buffer = SystemAPI.GetBuffer<FlowFieldBlockedBuffer>(singleton_entity);

            int gw = grid_config.grid_width;
            int gh = grid_config.grid_height;
            float cs = grid_config.cell_size;
            float3 origin = grid_config.grid_origin;
            int total_cells = gw * gh;

            // 确保 buffer 大小正确（烘焙时已预分配，此处作为安全保障）
            blocked_buffer.Resize(total_cells, Unity.Collections.NativeArrayOptions.ClearMemory);

            float3 half_extents = new float3(cs * 0.5f, obs_config.check_height * 0.5f, cs * 0.5f);
            int blocked_count = 0;

            for (int j = 0; j < gh; j++)
            {
                for (int i = 0; i < gw; i++)
                {
                    int index = j * gw + i;
                    float3 cell_center = origin + new float3(
                        (i + 0.5f) * cs,
                        0f,
                        (j + 0.5f) * cs
                    );

                    bool blocked = Physics.CheckBox(
                        cell_center,
                        half_extents,
                        Quaternion.identity,
                        obs_config.layer_mask
                    );

                    blocked_buffer[index] = new FlowFieldBlockedBuffer { blocked = blocked };
                    if (blocked) blocked_count++;
                }
            }

            Debug.Log($"[StaticObstacleBuild] 网格={gw}x{gh} cell={cs}m, " +
                $"检测到 {blocked_count}/{total_cells} 个阻挡格子 " +
                $"({(float)blocked_count / total_cells * 100f:F1}%), " +
                $"layer_mask=0x{obs_config.layer_mask:X}");
            has_run = true;
        }
    }
}
