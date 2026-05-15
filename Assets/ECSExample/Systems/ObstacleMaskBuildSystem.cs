using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ECSExample
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct ObstacleMaskBuildSystem : ISystem
    {
        private bool has_built;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CostFieldGridConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (has_built) return;
            has_built = true;

            var config = SystemAPI.GetSingleton<CostFieldGridConfig>();
            int gw = config.grid_width;
            int gh = config.grid_height;
            float cs = config.cell_size;
            float3 origin = config.grid_origin;
            int layer_mask = config.obstacle_layer_mask;

            if (layer_mask == 0)
            {
                Debug.Log("[ObstacleMaskBuild] obstacle_layer_mask = 0");
                return;
            }

            var singleton_entity = SystemAPI.GetSingletonEntity<CostFieldGridConfig>();
            var mask_buffer = SystemAPI.GetBuffer<ObstacleMaskElement>(singleton_entity);

            var half_extents = new Vector3(cs * 0.55f, 50f, cs * 0.55f);
            var results = new Collider[1];
            int blocked_count = 0;

            for (int j = 0; j < gh; j++)
            {                
                for (int i = 0; i < gw; i++)
                {
                    int idx = j * gw + i;
                    float3 cell_center = origin + new float3((i + 0.5f) * cs, 0f, (j + 0.5f) * cs);
                    if (Physics.OverlapBoxNonAlloc(cell_center, half_extents, results, Quaternion.identity, layer_mask) > 0)
                    {
                        mask_buffer[idx] = new ObstacleMaskElement { blocked = true };
                        blocked_count++;
                    }
                }
            }
        }
    }
}
