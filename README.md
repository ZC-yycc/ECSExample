# ECSExample — Unity ECS 流场群体移动

基于 Unity Entities 1.3.9 的群体移动案例：数千个跟随者通过**流场（Flow Field）**寻路、**空间哈希（Spatial Hash）**分离，向玩家位置聚拢。

## 运行效果

- 玩家使用 WASD 移动
- 摄像机自动跟随玩家位置
- 跟随者从场景各处沿流场方向向玩家靠拢
- 接近玩家时自动减速
- 距玩家 2 米内停止，彼此通过分离力保持间距

## 项目结构

```
Assets/ECSExample/
├── Components/          # ECS 组件定义
│   ├── CameraFollowConfig.cs   # 摄像机跟随偏移配置（Singleton）
│   ├── FlowFieldGrid.cs         # 流场网格配置 + 格子方向 Buffer
│   ├── FollowerData.cs          # 跟随者移动数据
│   ├── FollowerSpawnConfig.cs   # 生成配置（Singleton）
│   ├── FollowerSpawnResources.cs# 备用渲染资源
│   ├── PlayerData.cs            # 玩家移动速度
│   └── PlayerTag.cs             # 玩家标记
├── Systems/             # ECS 系统
│   ├── CameraFollowSystem.cs    # 摄像机跟随玩家
│   ├── FlowFieldBuildSystem.cs  # 构建流场（每 0.3s 重建）
│   ├── FollowerSpawnSystem.cs   # 批量生成跟随者
│   ├── FollowerMoveSystem.cs    # 核心移动逻辑
│   ├── FollowerDebugDrawSystem.cs# Scene View 调试绘制
│   └── PlayerMoveSystem.cs      # WASD 控制玩家
└── Authoring/           # MonoBehaviour → ECS 转换
    ├── CameraFollowAuthoring.cs  # 摄像机跟随配置
    ├── FlowFieldConfigAuthoring.cs  # 流场参数配置
    ├── FollowerSpawnerAuthoring.cs  # 生成参数配置
    ├── FollowerTemplateAuthoring.cs # 跟随者模板（Prefab）
    └── PlayerAuthoring.cs           # 玩家参数配置
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
SimulationSystemGroup:
  1. FollowerSpawnSystem     (只跑一次，批量生成)
  2. PlayerMoveSystem        (WASD 移动玩家)
  3. FlowFieldBuildSystem    (每 0.3s 重建流场方向)
  4. FollowerMoveSystem      (每帧更新所有跟随者位置)

PresentationSystemGroup:
  5. CameraFollowSystem      (摄像机跟随 Player 位置)
  6. FollowerDebugDrawSystem (Scene View 调试绘制)
```

## 核心算法

### 流场构建（FlowFieldBuildSystem）

将地面划分为 `100×100` 的网格（每格 1m），计算每个格子指向玩家的方向：

```
cell[i][j].direction = normalize(player_pos - cell_center)
```

跟随者只需查询所在格子的方向即可知道"该往哪走"——O(1) 的寻路查询，无需 A*。

### 空间哈希分离（FollowerMoveSystem）

防止跟随者互相穿透的三步流程：

1. **建哈希**：将每个跟随者按位置写入 `2m×2m` 的网格哈希表
2. **查邻居**：每个跟随者检查自身所在格及 8 个邻格（3×3 邻域）
3. **排斥力**：距离 < `MIN_SEPARATION(3.0m)` 的邻居产生排斥

排斥力公式：
```
strength = (3.0 - dist) / 3.0      // 越近力度越大，≥3m 不作用
separation = Σ (远离方向 × strength) / N
final_dir  = normalize(flow_dir + separation × 8.0)
```

### 摄像机跟随（CameraFollowSystem）

每帧在 `PresentationSystemGroup` 中运行，读取 Player 的 `LocalTransform` 和 `CameraFollowConfig` 偏移量，将主摄像机移动到：

```
camera.position = player.position + offset
```

默认偏移 `(0, 15, -10)`，可在 `CameraFollowAuthoring` 的 Inspector 中调整。

### 移动控制参数

| 参数 | 值 | 说明 |
|------|-----|------|
| `SLOW_RADIUS` | 4.0m | 到达减速起始距离 |
| `STOP_RADIUS` | 2.0m | 完全停止距离 |
| `MAX_SPEED` | 8.0 | 速度上限 |
| `MIN_SEPARATION` | 3.0m | 分离力作用半径 |
| `SEPARATION_WEIGHT` | 8.0 | 分离力在最终方向中的权重 |

## 组件速查

| 组件 | 类型 | 说明 |
|------|------|------|
| `CameraFollowConfig` | `IComponentData` (Singleton) | 摄像机跟随偏移量 `offset` |
| `FlowFieldGridConfig` | `IComponentData` (Singleton) | 网格宽高、格子大小、原点、重建间隔 |
| `FlowFieldCellBuffer` | `IBufferElementData` | 每个格子的 `float2` 方向向量 |
| `FollowerData` | `IComponentData` | `move_speed` |
| `FollowerSpawnConfig` | `IComponentData` (Singleton) | 生成数量、速度、半径、Prefab 引用 |
| `PlayerTag` | `IComponentData` | 空标记，标识玩家实体 |
| `PlayerData` | `IComponentData` | `move_speed` |

## 使用步骤

1. **创建 SubScene**，在其中放置：
   - 空物体挂 `FlowFieldConfigAuthoring`（流场参数）
   - 空物体挂 `FollowerSpawnerAuthoring`（生成参数）
   - Cube 挂 `FollowerTemplateAuthoring`（跟随者模板，拖入 Spawner 的 Prefab 字段）
   - 玩家物体挂 `PlayerAuthoring`
   - 空物体挂 `CameraFollowAuthoring`（摄像机跟随偏移，调整 `offset` 可改变摄像机位置）
2. 确保使用 **URP** 渲染管线
3. 确保场景中有一个 **Main Camera**（Tag 为 `MainCamera`）
4. 运行，WASD 移动玩家，摄像机会自动跟随
5. Scene View 中每 30 帧会绘制跟随者位置标记（绿线）

## 技术要点

- **Burst 编译**：`FlowFieldBuildSystem` 和 `FollowerMoveSystem` 的关键 Job 均标记 `[BurstCompile]`，利用多线程加速
- **Job 依赖链**：空间哈希构建 Job → 移动 Job，Unity Job System 自动处理并行安全
- **内存管理**：`spatial_grid` 使用 `Allocator.TempJob`，帧末 `Dispose`
- **Aliasing 安全**：`ComponentLookup<LocalTransform>` 标记 `[NativeDisableContainerSafetyRestriction]`，因为 `IJobEntity` 的 `ref LocalTransform`（写自身）与 `ComponentLookup`（读他人）指向同一类型
