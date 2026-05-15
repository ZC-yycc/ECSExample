using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// 调试绘制：代价场梯度下降箭头（每格一个） + 障碍物 + Player 标记
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct CostFieldDebugDrawSystem : ISystem
    {
        private EntityQuery config_query;
        private EntityQuery player_query;
        private EntityQuery obstacle_query;

        private const float ARROW_SCALE = 0.7f;
        private const int CIRCLE_SEGMENTS = 24;

        public void OnCreate(ref SystemState state)
        {
            config_query = SystemAPI.QueryBuilder()
                .WithAll<CostFieldGridConfig>()
                .Build();
            player_query = SystemAPI.QueryBuilder()
                .WithAll<PlayerTag, LocalTransform>()
                .Build();
            obstacle_query = SystemAPI.QueryBuilder()
                .WithAll<ObstacleTag, ObstacleData, LocalTransform>()
                .Build();
            state.RequireForUpdate(config_query);
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<CostFieldGridConfig>();
            int gw = config.grid_width;
            int gh = config.grid_height;
            float cs = config.cell_size;
            float3 origin = config.grid_origin;

            var singleton_entity = SystemAPI.GetSingletonEntity<CostFieldGridConfig>();
            var cell_buffer = SystemAPI.GetBuffer<CostFieldCellBuffer>(singleton_entity);
            if (cell_buffer.Length == 0) return;

            float3 player_pos = float3.zero;
            bool has_player = !player_query.IsEmpty;
            if (has_player)
                player_pos = player_query.GetSingleton<LocalTransform>().Position;

            float min_cost = float.MaxValue;
            float max_cost = 0f;
            for (int i = 0; i < cell_buffer.Length; i++)
            {
                float c = cell_buffer[i].cost;
                if (c < float.MaxValue * 0.5f)
                {
                    if (c < min_cost) min_cost = c;
                    if (c > max_cost) max_cost = c;
                }
            }
            if (max_cost <= min_cost) max_cost = min_cost + 1f;

            // ── 1. 梯度下降箭头（每格一个） ──
            int arrow_count = 0;

            for (int j = 0; j < gh; j++)
            {
                for (int i = 0; i < gw; i++)
                {
                    int idx = j * gw + i;

                    float current_cost = cell_buffer[idx].cost;
                    if (current_cost >= float.MaxValue * 0.5f) continue;

                    float best_cost = current_cost;
                    int best_dx = 0, best_dz = 0;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            int nx = i + dx;
                            int nz = j + dz;
                            if (nx < 0 || nx >= gw || nz < 0 || nz >= gh) continue;

                            float nc = cell_buffer[nz * gw + nx].cost;
                            if (nc < best_cost)
                            {
                                best_cost = nc;
                                best_dx = dx;
                                best_dz = dz;
                            }
                        }
                    }

                    if (best_cost >= current_cost) continue;

                    float3 cell_center = origin + new float3(
                        (i + 0.5f) * cs, 0.06f, (j + 0.5f) * cs);

                    float3 gradient_dir = math.normalizesafe(new float3(
                        best_dx * cs, 0f, best_dz * cs), float3.zero);

                    float arrow_len = cs * ARROW_SCALE;
                    Color arrow_color = CostToColor(current_cost, min_cost, max_cost);

                    float3 tip = cell_center + gradient_dir * arrow_len;
                    Debug.DrawLine(cell_center, tip, arrow_color);

                    float3 perp = new float3(-gradient_dir.z, 0f, gradient_dir.x) * arrow_len * 0.25f;
                    Debug.DrawLine(tip, tip - gradient_dir * arrow_len * 0.3f + perp, arrow_color);
                    Debug.DrawLine(tip, tip - gradient_dir * arrow_len * 0.3f - perp, arrow_color);

                    arrow_count++;
                }
            }

            // ── 2. Player 标记 ──
            if (has_player)
            {
                Debug.DrawLine(player_pos, player_pos + new float3(0, 1.5f, 0), new Color(0f, 1f, 1f, 1f));
                DrawCircle(player_pos, 0.5f, new Color(0f, 1f, 1f, 1f));
            }

            // ── 3. 障碍物 ──
            int obs_drawn = 0;
            if (!obstacle_query.IsEmpty)
            {
                var obs_transforms = obstacle_query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                var obs_datas = obstacle_query.ToComponentDataArray<ObstacleData>(Allocator.Temp);

                for (int i = 0; i < obs_transforms.Length; i++)
                {
                    float3 obs_pos = obs_transforms[i].Position;
                    float radius = obs_datas[i].radius;

                    DrawCircle(obs_pos, radius, new Color(1f, 0.3f, 0.1f, 0.9f));

                    float marker_h = math.max(radius * 0.5f, 0.5f);
                    Debug.DrawLine(
                        obs_pos + new float3(0, 0.01f, 0),
                        obs_pos + new float3(0, marker_h, 0),
                        new Color(1f, 0.5f, 0f, 0.8f));

                    obs_drawn++;
                }

                obs_transforms.Dispose();
                obs_datas.Dispose();
            }

            Debug.Log($"[CostFieldDebug] 网格={gw}x{gh} cell={cs}m " +
                $"代价范围=[{min_cost:F1}, {max_cost:F1}] " +
                $"箭头={arrow_count} 障碍物={obs_drawn}");
        }

        private static Color CostToColor(float cost, float min_cost, float max_cost)
        {
            float t = math.saturate((cost - min_cost) / (max_cost - min_cost));

            if (t < 0.25f)
                return Color.Lerp(new Color(1f, 0.1f, 0f), new Color(1f, 0.6f, 0f), t / 0.25f);
            else if (t < 0.5f)
                return Color.Lerp(new Color(1f, 0.6f, 0f), new Color(1f, 1f, 0f), (t - 0.25f) / 0.25f);
            else if (t < 0.75f)
                return Color.Lerp(new Color(1f, 1f, 0f), new Color(0f, 0.8f, 0.2f), (t - 0.5f) / 0.25f);
            else
                return Color.Lerp(new Color(0f, 0.8f, 0.2f), new Color(0f, 0.3f, 1f), (t - 0.75f) / 0.25f);
        }

        private static void DrawCircle(float3 center, float radius, Color color)
        {
            float angle_step = math.PI * 2f / CIRCLE_SEGMENTS;
            float y = center.y + 0.03f;

            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                float a0 = i * angle_step;
                float a1 = (i + 1) * angle_step;
                float3 p0 = center + new float3(math.cos(a0) * radius, y - center.y, math.sin(a0) * radius);
                float3 p1 = center + new float3(math.cos(a1) * radius, y - center.y, math.sin(a1) * radius);
                Debug.DrawLine(p0, p1, color);
            }
        }
    }
}
