using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// 代价场配置 Authoring：挂载到场景空物体上
    /// </summary>
    public class CostFieldConfigAuthoring : MonoBehaviour
    {
        [Header("网格参数")]
        public int                          grid_width_ = 100;
        public int                          grid_height_ = 100;
        public float                        cell_size_ = 1f;

        [Header("重建间隔（秒）")]
        public float                        rebuild_interval_ = 0.3f;

        class Baker : Baker<CostFieldConfigAuthoring>
        {
            public override void Bake(CostFieldConfigAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                float3 origin = new float3(
                    -authoring.grid_width_ * authoring.cell_size_ * 0.5f,
                    0f,
                    -authoring.grid_height_ * authoring.cell_size_ * 0.5f
                );

                AddComponent(entity, new CostFieldGridConfig
                {
                    grid_width = authoring.grid_width_,
                    grid_height = authoring.grid_height_,
                    cell_size = authoring.cell_size_,
                    grid_origin = origin,
                    rebuild_interval = authoring.rebuild_interval_,
                    last_rebuild_time = 0.0
                });

                var buffer = AddBuffer<CostFieldCellBuffer>(entity);
                buffer.Resize(authoring.grid_width_ * authoring.grid_height_, Unity.Collections.NativeArrayOptions.ClearMemory);

                Debug.Log($"[CostFieldConfigAuthoring] 代价场 Entity: {entity}, " +
                    $"grid={authoring.grid_width_}x{authoring.grid_height_}, " +
                    $"cell={authoring.cell_size_}m, origin={origin}");
            }
        }
    }
}
