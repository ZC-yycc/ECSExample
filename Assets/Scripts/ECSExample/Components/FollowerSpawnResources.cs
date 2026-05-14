using Unity.Entities;
using UnityEngine;

namespace ECSExample
{
    /// <summary>
    /// 备用渲染资源引用（managed component，存放 UnityEngine.Object 引用）
    /// </summary>
    public class FollowerSpawnResources : IComponentData
    {
        public Mesh fallback_mesh;
        public Material fallback_material;
    }
}
