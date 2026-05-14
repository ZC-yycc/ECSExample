using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// 调试绘制：Scene View 中可视化 Follower
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct FollowerDebugDrawSystem : ISystem
    {
        private bool has_logged;
        private int frame_counter;
        private EntityQuery follower_query;

        public void OnCreate(ref SystemState state)
        {
            follower_query = SystemAPI.QueryBuilder()
                .WithAll<FollowerData>()
                .WithAll<LocalTransform>()
                .Build();
            state.RequireForUpdate(follower_query);
        }

        public void OnUpdate(ref SystemState state)
        {
            frame_counter++;

            // 第一帧立即报告总数
            if (!has_logged)
            {
                int count = follower_query.CalculateEntityCount();
                Debug.Log($"[FollowerDebug] EntityQuery 总数: {count}");
                has_logged = count > 0;
            }

            // 每30帧绘制标记
            if (frame_counter % 30 != 0) return;

            var transforms = follower_query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int total = transforms.Length;
            int drawn = 0;
            int step = Mathf.Max(1, total / 300);  // 最多300个标记

            for (int i = 0; i < total; i += step)
            {
                float3 pos = transforms[i].Position;
                Debug.DrawLine(pos, pos + new float3(0, 0.5f, 0), Color.green, 0.1f);
                drawn++;
            }

            transforms.Dispose();
            Debug.Log($"[FollowerDebug] Follower 总数: {total}（绘制 {drawn} 个标记）");
        }
    }
}
