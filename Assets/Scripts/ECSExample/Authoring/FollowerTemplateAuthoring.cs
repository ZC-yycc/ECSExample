using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// Follower 模板标记：挂载到模板 Cube 上。
    /// Baker 添加 Prefab + URPMaterialPropertyBaseColor
    /// </summary>
    public class FollowerTemplateAuthoring : MonoBehaviour
    {
        [Header("Instantiate 后覆盖的颜色")]
        public Color follower_color = Color.red;

        class Baker : Baker<FollowerTemplateAuthoring>
        {
            public override void Bake(FollowerTemplateAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<Prefab>(entity);

                // 添加 URP 颜色属性（确保 Entities Graphics 可见）
                AddComponent(entity, new URPMaterialPropertyBaseColor
                {
                    Value = new float4(
                        authoring.follower_color.r,
                        authoring.follower_color.g,
                        authoring.follower_color.b,
                        authoring.follower_color.a)
                });
            }
        }
    }
}
