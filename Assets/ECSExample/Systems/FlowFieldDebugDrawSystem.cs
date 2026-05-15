using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// 调试绘制：Scene View 中可视化流场网格 + 方向箭头（距离渐变） + 障碍物 + 静态阻挡格子
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct FlowFieldDebugDrawSystem : ISystem
    {
        private int frame_counter;
        private EntityQuery config_query;
        private EntityQuery player_query;
        private EntityQuery obstacle_query;

        // 绘制参数
        private const int ARROW_STEP = 4;               // 每隔 N 个格子画一个箭头
        private const float ARROW_SCALE = 0.8f;          // 箭头长度系数（相对 cell_size）
        private const float GRID_Y = 0.02f;              // 网格绘制高度（略高于地面）
        private const int CIRCLE_SEGMENTS = 24;          // 障碍物圆圈段数

        public void OnCreate(ref SystemState state)
        {
            config_query = SystemAPI.QueryBuilder()
                .WithAll<FlowFieldGridConfig>()
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
            var config = SystemAPI.GetSingleton<FlowFieldGridConfig>();
            int gw = config.grid_width;
            int gh = config.grid_height;
            float cs = config.cell_size;
            float3 origin = config.grid_origin;

            // 获取流场 Buffer
            var singleton_entity = SystemAPI.GetSingletonEntity<FlowFieldGridConfig>();
            var cell_buffer = SystemAPI.GetBuffer<FlowFieldCellBuffer>(singleton_entity);
            if (cell_buffer.Length == 0) return;

            // 获取 Player 位置（用于距离渐变）
            float3 player_pos = float3.zero;
            bool has_player = !player_query.IsEmpty;
            if (has_player)
            {
                player_pos = player_query.GetSingleton<LocalTransform>().Position;
            }

            // 网格最大跨度（用于距离归一化）
            float grid_diag = math.sqrt(gw * gw + gh * gh) * cs;

            // ── 1. 绘制网格外框 ──
            float grid_w = gw * cs;
            float grid_h = gh * cs;
            float3 p0 = origin + new float3(0, GRID_Y, 0);
            float3 p1 = origin + new float3(grid_w, GRID_Y, 0);
            float3 p2 = origin + new float3(grid_w, GRID_Y, grid_h);
            float3 p3 = origin + new float3(0, GRID_Y, grid_h);

            Debug.DrawLine(p0, p1, Color.white);
            Debug.DrawLine(p1, p2, Color.white);
            Debug.DrawLine(p2, p3, Color.white);
            Debug.DrawLine(p3, p0, Color.white);

            // ── 2. 绘制小网格（白色细线，每格一条） ──
            int fine_step = 1;
            var fine_grid_color = new Color(0f, 1f, 0f, 0.3f);

            for (int i = fine_step; i < gw; i += fine_step)
            {
                float x = origin.x + i * cs;
                Debug.DrawLine(
                    new float3(x, GRID_Y, origin.z),
                    new float3(x, GRID_Y, origin.z + grid_h),
                    fine_grid_color);
            }
            for (int j = fine_step; j < gh; j += fine_step)
            {
                float z = origin.z + j * cs;
                Debug.DrawLine(
                    new float3(origin.x, GRID_Y, z),
                    new float3(origin.x + grid_w, GRID_Y, z),
                    fine_grid_color);
            }

            // ── 3. 绘制方向箭头（按距离玩家远近渐变） ──
            int arrow_drawn = 0;
            int max_arrows = 800;  // 上限防止性能问题

            for (int j = ARROW_STEP / 2; j < gh && arrow_drawn < max_arrows; j += ARROW_STEP)
            {
                for (int i = ARROW_STEP / 2; i < gw && arrow_drawn < max_arrows; i += ARROW_STEP)
                {
                    int index = j * gw + i;
                    if (index >= cell_buffer.Length) break;

                    float2 dir = cell_buffer[index].direction;
                    if (math.lengthsq(dir) < 0.0001f) continue;  // 跳过零向量（含阻挡格子）

                    float3 cell_center = origin + new float3(
                        (i + 0.5f) * cs,
                        GRID_Y + 0.05f,
                        (j + 0.5f) * cs
                    );

                    float3 arrow_dir = new float3(dir.x, 0f, dir.y);
                    float arrow_len = cs * ARROW_SCALE;
                    float3 tip = cell_center + arrow_dir * arrow_len;

                    // 颜色：距玩家越近越暖（红→黄→绿→蓝，由近到远）
                    Color arrow_color;
                    if (has_player)
                    {
                        float dist = math.distance(cell_center, player_pos);
                        float t = math.saturate(dist / (grid_diag * 0.7f));  // 归一化距离
                        arrow_color = DistanceGradient(t);
                    }
                    else
                    {
                        arrow_color = Color.gray;
                    }

                    // 箭杆
                    Debug.DrawLine(cell_center, tip, arrow_color);

                    // 箭头（两侧小翼）
                    float3 perp = new float3(-arrow_dir.z, 0f, arrow_dir.x) * arrow_len * 0.25f;
                    Debug.DrawLine(tip, tip - arrow_dir * arrow_len * 0.3f + perp, arrow_color);
                    Debug.DrawLine(tip, tip - arrow_dir * arrow_len * 0.3f - perp, arrow_color);

                    arrow_drawn++;
                }
            }

            // ── 4. 绘制障碍物 ──
            int obs_drawn = 0;
            if (!obstacle_query.IsEmpty)
            {
                var obs_transforms = obstacle_query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                var obs_datas = obstacle_query.ToComponentDataArray<ObstacleData>(Allocator.Temp);

                for (int i = 0; i < obs_transforms.Length; i++)
                {
                    float3 obs_pos = obs_transforms[i].Position;
                    float radius = obs_datas[i].radius;

                    // 绘制圆环
                    DrawCircle(obs_pos, radius, new Color(1f, 0.3f, 0.1f, 0.9f));

                    // 绘制十字标记中心
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

            // ── 5. 绘制被静态障碍阻挡的格子（红色 × 标记） ──
            int blocked_drawn = 0;
            if (SystemAPI.HasBuffer<FlowFieldBlockedBuffer>(singleton_entity))
            {
                var blocked_buffer = SystemAPI.GetBuffer<FlowFieldBlockedBuffer>(singleton_entity);

                for (int j = 0; j < gh && blocked_drawn < max_arrows; j++)
                {
                    for (int i = 0; i < gw && blocked_drawn < max_arrows; i++)
                    {
                        int index = j * gw + i;
                        if (index >= blocked_buffer.Length) break;
                        if (!blocked_buffer[index].blocked) continue;

                        float3 cell_center = origin + new float3(
                            (i + 0.5f) * cs,
                            GRID_Y + 0.03f,
                            (j + 0.5f) * cs
                        );

                        float mark_size = cs * 0.3f;
                        var blocked_color = new Color(1f, 0.2f, 0.1f, 0.6f);

                        // × 标记
                        Debug.DrawLine(
                            cell_center + new float3(-mark_size, 0, -mark_size),
                            cell_center + new float3(mark_size, 0, mark_size),
                            blocked_color);
                        Debug.DrawLine(
                            cell_center + new float3(mark_size, 0, -mark_size),
                            cell_center + new float3(-mark_size, 0, mark_size),
                            blocked_color);

                        blocked_drawn++;
                    }
                }
            }

            //Debug.Log($"[FlowFieldDebug] 网格={gw}x{gh} cell={cs}m 箭头={arrow_drawn} 障碍物={obs_drawn} 阻挡格={blocked_drawn}");
        }

        // ──────────────── 工具方法 ────────────────

        /// <summary>
        /// 距离渐变：近→远 映射为 红→黄→绿→蓝
        /// </summary>
        private static Color DistanceGradient(float t)
        {
            // t: 0 = 最近（玩家位置）= 红
            // t: 1 = 最远（网格角落）= 蓝
            // 四段渐变：红(0) → 橙(0.25) → 黄(0.5) → 绿(0.75) → 蓝(1)
            if (t < 0.25f)
            {
                return Color.Lerp(new Color(1f, 0.1f, 0f), new Color(1f, 0.6f, 0f), t / 0.25f);
            }
            else if (t < 0.5f)
            {
                return Color.Lerp(new Color(1f, 0.6f, 0f), new Color(1f, 1f, 0f), (t - 0.25f) / 0.25f);
            }
            else if (t < 0.75f)
            {
                return Color.Lerp(new Color(1f, 1f, 0f), new Color(0f, 0.8f, 0.2f), (t - 0.5f) / 0.25f);
            }
            else
            {
                return Color.Lerp(new Color(0f, 0.8f, 0.2f), new Color(0f, 0.3f, 1f), (t - 0.75f) / 0.25f);
            }
        }

        /// <summary>
        /// 用 Debug.DrawLine 在 XZ 平面绘制圆环
        /// </summary>
        private static void DrawCircle(float3 center, float radius, Color color)
        {
            float angle_step = math.PI * 2f / CIRCLE_SEGMENTS;
            float y = center.y + 0.03f;  // 略高于地面

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
