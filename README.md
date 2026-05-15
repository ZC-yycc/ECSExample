# ECSExample — Unity ECS 代价场群体移动

基于 Unity Entities 1.3.9 的群体移动案例：数千个跟随者通过**代价场（Cost Field）**波前传播寻路、**空间哈希（Spatial Hash）**分离，向玩家位置聚拢。支持障碍物物理检测，被障碍物占据的格子不可通行。

## 运行效果

- 玩家使用 WASD 移动
- 摄像机自动跟随玩家位置
- 跟随者从场景各处沿代价场梯度下降方向向玩家靠拢
- 接近玩家时自动减速
- 距玩家 2 米内停止，彼此通过分离力保持间距
- 障碍物自动阻挡跟随者通行

## 项目结构

```
Assets/ECSExample/
├── Components/              # ECS 组件定义
│   ├── CameraFollowConfig.cs    # 摄像机跟随偏移配置（Singleton）
│   ├── CostFieldGrid.cs         # 代价场网格配置 + 代价 Buffer
│   ├── FollowerData.cs          # 跟随者移动数据
│   ├── FollowerSpawnConfig.cs   # 生成配置（Singleton）
│   ├── ObstacleMask.cs          # 障碍物掩码 Buffer Element
│   ├── PlayerData.cs            # 玩家移动速度
│   └── PlayerTag.cs             # 玩家标记（空 Tag）
├── Systems/                 # ECS 系统
│   ├── CameraFollowSystem.cs        # 摄像机跟随玩家
│   ├── CostFieldBuildSystem.cs      # 构建代价场（波前传播，每 0.3s 重建）
│   ├── CostFieldDebugDrawSystem.cs  # Scene View 调试绘制（梯度箭头 + 障碍物高亮）
│   ├── FollowerMoveSystem.cs        # 核心移动逻辑（梯度下降 + 空间哈希分离）
│   ├── FollowerSpawnSystem.cs       # 批量生成跟随者（仅一次）
│   ├── ObstacleMaskBuildSystem.cs   # 物理检测构建障碍物掩码（仅一次）
│   └── PlayerMoveSystem.cs          # WASD 控制玩家
└── Authoring/               # MonoBehaviour → ECS 转换
    ├── CameraFollowAuthoring.cs     # 摄像机跟随偏移
    ├── CostFieldConfigAuthoring.cs  # 代价场参数 + 障碍物 Layer
    ├── FollowerSpawnerAuthoring.cs  # 生成参数 + Prefab 引用
    ├── FollowerTemplateAuthoring.cs # 跟随者模板（Prefab + 颜色）
    └── PlayerAuthoring.cs           # 玩家移动速度
```

## 依赖

| 包 | 版本 | 用途 |
|----|------|------|
| `com.unity.entities` | 1.3.9 | ECS 核心 |
| `com.unity.entities.graphics` | 1.3.9 | Entities Graphics 渲染 |
| `com.unity.transforms` | (内置) | LocalTransform 组件 |
| `com.unity.inputsystem` | 1.19.0 | WASD 输入 |
| `com.unity.render-pipelines.universal` | 17.4.0 | URP 渲染管线 |

## 系统执行顺序

```
InitializationSystemGroup:
  1. ObstacleMaskBuildSystem  (仅一次，物理检测障碍物)

SimulationSystemGroup:
  2. FollowerSpawnSystem      (仅一次，批量生成跟随者)
  3. PlayerMoveSystem         (WASD 移动玩家)
  4. CostFieldBuildSystem     (每 0.3s 波前传播重建代价场)
  5. FollowerMoveSystem       (每帧梯度下降 + 空间哈希分离)

PresentationSystemGroup:
  6. CameraFollowSystem       (摄像机跟随 Player 位置)
  7. CostFieldDebugDrawSystem (Scene View 调试绘制)
```

## 核心算法

### 代价场构建（CostFieldBuildSystem）

将地面划分为 `100×100` 的网格（每格 1m），以玩家位置为源点进行**波前传播**（Wavefront Propagation）：

1. 所有格子代价初始化为 ∞
2. 玩家所在格代价 = 0
3. 多轮迭代：每个格子取四邻格代价 + `cell_size` 的最小值
4. 被 `ObstacleMaskElement.blocked` 标记的障碍物格跳过，不可通行

最终得到距离场，跟随者通过**梯度下降**（向代价最低的邻格移动）即可找到通往玩家的路径。

```
cost[player_cell] = 0
for pass in 1..max_passes:
    for each cell (i,j):
        if blocked: skip
        cost[i][j] = min(cost[i][j], min(4邻格代价) + cell_size)
```

### 空间哈希分离（FollowerMoveSystem）

防止跟随者互相穿透的三步流程：

1. **建哈希**：将每个跟随者按位置写入 `2m×2m` 的网格哈希表
2. **查邻居**：每个跟随者检查自身所在格及 8 个邻格（3×3 邻域）
3. **排斥力**：距离 < `MIN_SEPARATION(3.0m)` 的邻居产生排斥

排斥力公式：

```
strength = (3.0 - dist) / 3.0      // 越近力度越大，≥3m 不作用
separation = Σ (远离方向 × strength) / N
final_dir  = normalize(gradient_dir + separation × 8.0)
```

### 障碍物检测（ObstacleMaskBuildSystem）

游戏启动时在 `InitializationSystemGroup` 中运行，对每个网格格子执行 `Physics.OverlapBox` 检测。检测的 Layer 由 `CostFieldConfigAuthoring.obstacle_layer` 配置。被碰撞体占据的格子标记为 `blocked = true`，后续代价场构建和跟随者移动均跳过。

### 摄像机跟随（CameraFollowSystem）

每帧在 `PresentationSystemGroup` 中运行，读取 Player 的 `LocalTransform` 和 `CameraFollowConfig` 偏移量，将主摄像机移动到：

```
camera.position = player.position + offset
```

默认偏移 `(0, 15, -10)`，可在 `CameraFollowAuthoring` 的 Inspector 中调整。

### 移动控制参数

- `SLOW_RADIUS` = 4.0m — 到达减速起始距离
- `STOP_RADIUS` = 2.0m — 完全停止距离
- `MAX_SPEED` = 8.0 — 速度上限
- `MIN_SEPARATION` = 3.0m — 分离力作用半径
- `SEPARATION_WEIGHT` = 8.0 — 分离力在最终方向中的权重

## 组件速查

| 组件 | 类型 | 说明 |
|------|------|------|
| `CameraFollowConfig` | `IComponentData` (Singleton) | 摄像机跟随偏移 `offset` |
| `CostFieldGridConfig` | `IComponentData` (Singleton) | 网格宽高、格子大小、原点、重建间隔、障碍物 Layer |
| `CostFieldCellBuffer` | `IBufferElementData` | 每个格子的代价 `cost` 值 |
| `ObstacleMaskElement` | `IBufferElementData` | 每个格子是否被障碍物占据 `blocked` |
| `FollowerData` | `IComponentData` | `move_speed` |
| `FollowerSpawnConfig` | `IComponentData` (Singleton) | 生成数量、速度、半径、Prefab 引用 |
| `PlayerTag` | `IComponentData` | 空标记，标识玩家实体 |
| `PlayerData` | `IComponentData` | `move_speed` |

## 使用步骤

1. **创建 SubScene**，在其中放置：
   - 空物体挂 `CostFieldConfigAuthoring`（代价场参数，配置障碍物 Layer）
   - 空物体挂 `FollowerSpawnerAuthoring`（生成参数，拖入模板 Prefab）
   - Cube 挂 `FollowerTemplateAuthoring`（跟随者模板，赋予 URP/Lit 材质）
   - 玩家物体挂 `PlayerAuthoring`
   - 空物体挂 `CameraFollowAuthoring`（摄像机跟随偏移）
2. 如需障碍物，在场景中放置带 Collider 的物体，设置其 Layer 与 `obstacle_layer` 匹配
3. 确保使用 **URP** 渲染管线
4. 确保场景中有一个 **Main Camera**（Tag 为 `MainCamera`，放在主场景而非 SubScene）
5. 运行，WASD 移动玩家，摄像机会自动跟随

## 技术要点

- **Burst 编译**：`CostFieldBuildSystem` 和 `FollowerMoveSystem` 的关键 Job 均标记 `[BurstCompile]`，利用多线程加速
- **Job 依赖链**：空间哈希构建 Job → 移动 Job，Unity Job System 自动处理并行安全
- **内存管理**：`spatial_grid` 使用 `Allocator.TempJob`，帧末 `Dispose`
- **Aliasing 安全**：`ComponentLookup<LocalTransform>` 标记 `[NativeDisableContainerSafetyRestriction]`，写自身与读他人指向同一类型时避免安全冲突
- **波前传播复杂度**：最坏 O(W×H×max_passes)，100×100 网格约 200 次迭代内收敛
- **障碍物检测**：仅在游戏启动时执行一次，使用 `Physics.OverlapBox` 非分配检测
