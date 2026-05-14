using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// 摄像机跟随 Authoring：挂载到 SubScene 中的空物体上，配置摄像机跟随偏移
    /// </summary>
    public class CameraFollowAuthoring : MonoBehaviour
    {
        [Header("摄像机偏移")]
        [Tooltip("摄像机相对于 Player 的世界空间偏移")]
        public Vector3 offset_ = new Vector3(0f, 15f, -10f);

        class Baker : Baker<CameraFollowAuthoring>
        {
            public override void Bake(CameraFollowAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new CameraFollowConfig
                {
                    offset = authoring.offset_
                });
                Debug.Log($"[CameraFollowAuthoring] 摄像机跟随 Entity: {entity}, offset={authoring.offset_}");
            }
        }
    }
}
