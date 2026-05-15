using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// 摄像机跟随 System：在 PresentationSystemGroup 中运行，
    /// 每帧将主摄像机移动到 Player 位置 + 偏移量
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct CameraFollowSystem : ISystem
    {
        private EntityQuery             player_query;
        private bool                    camera_missing_logged;

        public void OnCreate(ref SystemState state)
        {
            player_query = SystemAPI.QueryBuilder()
                .WithAll<PlayerTag, LocalTransform>()
                .Build();

            state.RequireForUpdate(player_query);
            state.RequireForUpdate<CameraFollowConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // 等待 SimulationSystemGroup 中所有写入 LocalTransform 的 Job 完成
            state.Dependency.Complete();

            // 获取 Player 位置
            if (player_query.IsEmpty) return;
            var player_transform = player_query.GetSingleton<LocalTransform>();
            float3 player_pos = player_transform.Position;

            // 获取摄像机跟随偏移配置
            var config = SystemAPI.GetSingleton<CameraFollowConfig>();
            float3 target_pos = player_pos + config.offset;

            // 更新主摄像机位置
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = target_pos;
                camera_missing_logged = false;
            }
            else if (!camera_missing_logged)
            {
                Debug.LogWarning("[CameraFollowSystem] Camera.main 为 null！请确保主场景中存在 Tag 为 'MainCamera' 的 Camera（不要放在 SubScene 中）。");
                camera_missing_logged = true;
            }
        }
    }
}
