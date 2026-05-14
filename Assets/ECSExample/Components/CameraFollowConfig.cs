using Unity.Entities;
using Unity.Mathematics;

namespace ECSExample
{
    /// <summary>
    /// 摄像机跟随配置（Singleton Component）
    /// 定义摄像机相对于 Player 的偏移量
    /// </summary>
    public struct CameraFollowConfig : IComponentData
    {
        /// <summary>摄像机相对于 Player 位置的偏移（世界空间）</summary>
        public float3 offset;
    }
}
