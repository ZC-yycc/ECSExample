using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// 流场配置 Authoring：挂载到场景空物体上
    /// </summary>
    public class FlowFieldConfigAuthoring : MonoBehaviour
    {
        [Header("网格参数")]
        public int                          grid_width_ = 100;
        public int                          grid_height_ = 100;
        public float                        cell_size_ = 1f;

        [Header("重建间隔（秒）")]
        public float                        rebuild_interval_ = 0.3f;

        class Baker : Baker<FlowFieldConfigAuthoring>
        {
            public override void Bake(FlowFieldConfigAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                float3 origin = new float3(
                    -authoring.grid_width_ * authoring.cell_size_ * 0.5f,
                    0f,
                    -authoring.grid_height_ * authoring.cell_size_ * 0.5f
                );

                AddComponent(entity, new FlowFieldGridConfig
                {
                    grid_width = authoring.grid_width_,
                    grid_height = authoring.grid_height_,
                    cell_size = authoring.cell_size_,
                    grid_origin = origin,
                    rebuild_interval = authoring.rebuild_interval_,
                    last_rebuild_time = 0.0
                });

                // 方向 Buffer
                var cell_buffer = AddBuffer<FlowFieldCellBuffer>(entity);
                cell_buffer.Resize(authoring.grid_width_ * authoring.grid_height_,
                    Unity.Collections.NativeArrayOptions.ClearMemory);

                // 静态阻挡标记 Buffer（全部初始为 false）
                var blocked_buffer = AddBuffer<FlowFieldBlockedBuffer>(entity);
                blocked_buffer.Resize(authoring.grid_width_ * authoring.grid_height_,
                    Unity.Collections.NativeArrayOptions.ClearMemory);

                Debug.Log($"[FlowFieldConfigAuthoring] 流场 Entity: {entity}, " +
                    $"grid={authoring.grid_width_}x{authoring.grid_height_}, " +
                    $"cell={authoring.cell_size_}m, origin={origin}");
            }
        }
    }
}
