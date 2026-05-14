using Unity.Entities;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// Player Authoring：挂载到 Player GameObject 上
    /// </summary>
    public class PlayerAuthoring : MonoBehaviour
    {
        public float move_speed = 10f;

        class Baker : Baker<PlayerAuthoring>
        {
            public override void Bake(PlayerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<PlayerTag>(entity);
                AddComponent(entity, new PlayerData { move_speed = authoring.move_speed });
                Debug.Log($"[PlayerAuthoring] Player Entity: {entity}, move_speed={authoring.move_speed}");
            }
        }
    }
}
