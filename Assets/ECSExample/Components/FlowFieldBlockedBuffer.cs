using Unity.Entities;

namespace ECSExample
{
    /// <summary>
    /// 流场格子静态阻挡标记（Buffer Element）
    /// 与 FlowFieldCellBuffer 平行，每个格子一个 bool
    /// </summary>
    public struct FlowFieldBlockedBuffer : IBufferElementData
    {
        public bool blocked;
    }
}
