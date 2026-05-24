# 全自动找状态 5 重构文档

## 范围

本轮只重构 `FH6FullAuto.exe` 中“找状态 5 开蓝图车辆”的车辆列表查找流程。

暂不重构：

- 大世界进入车库标准位。
- 买车前置流程。
- 创意中心和蓝图选择流程。
- 自动点技术点、自动删车子程序。

## 当前问题

`FindDriveVehicleCell()` 里直接混合了这些判断：

- 当前可见是否已有状态 `5`。
- 后方是否已知状态 `5`。
- 是否到达状态 `0` 边界。
- 是否应该跳过已知非 `5` 区间。
- 是否 OCR。
- 没找到时是否复位并默认第一格。

这些判断和自动点、删车一样分散，容易出现重复 OCR、滚动过头、默认第一格触发时机不清楚的问题。

## 稳定对象

- `VirtualVehicleList`：读取删车后的虚拟表，并在 OCR 后继续更新状态。
- `BuildVehicleGridObservation()` / `ApplyVehicleGridObservation()`：一次 OCR 统一识别 `IMPREZA 22B-STI`、`全新`、`斯巴鲁`、`600`、`900` 并写表。
- `DrivePlanner`：找状态 `5` 阶段唯一的下一步决策者。
- 执行器：沿用 `ScrollVehicleListDown()`、`RecordVisibleDriveGridFromOcr()`、`UseDefaultDriveVehicleAfterSearchEnds()`、`MoveDeleteSelectionToCell()`。

## 动作集合

```text
Select(localCell)
Scroll(ticks, reason)
Observe(reason)
UseDefault(reason)
```

含义：

- `Select`：当前可见范围已有状态 `5`，直接选中。
- `Scroll`：虚拟表已经能判断应该滚到更有价值的位置。
- `Observe`：当前页还缺可信状态，需要 OCR 当前车辆列表区域并写表。
- `UseDefault`：确认前方没有状态 `5`，复位到斯巴鲁列表起点后默认第一列第一行。

## 状态 5 定义

状态 `5` 表示：

```text
目标车 IMPREZA 22B-STI
并且同一格 OCR 到 900
```

只有经过统一 OCR 确认没有 `900` 的目标车格，才可以当作“已知非 5”参与跳过。继承自动点技术点或删车留下的普通状态 `2`，不能直接证明不是状态 `5`。

## 决策顺序

```text
if 当前可见范围有状态 5:
    Select(最左最上的状态 5)

else if 当前可见范围没有状态 5，但后方已知有状态 5:
    Scroll(滚到能看到该状态 5)

else if 上一次 OCR 已确认没有可处理斯巴鲁:
    UseDefault(斯巴鲁列表结束)

else if 当前可见范围已知出现状态 0:
    UseDefault(已经到其它制造商或未知区)

else if 当前可见范围还有未知格子:
    Observe(当前页未完整观察)

else if 当前没有状态 5，且从当前 offset 开始有连续已知非 5 列:
    Scroll(连续已知非 5 列数量 - 最左侧保留的 1 列已知格子)

else:
    Scroll(1)
```

## 默认第一格规则

如果 `UseDefault` 发生在 offset `0`，直接返回本页第一列第一行。

如果 `UseDefault` 发生在 offset 大于 `0`，先执行既有复位流程：

```text
Esc
等待 0.5 秒
Enter
等待 0.5 秒
Backspace
等待 0.5 秒
向下滚动 10 格
OCR 点击斯巴鲁
```

然后默认第一列第一行。

## 检查点

- 读取删车后的旧表，如果可见范围已经有状态 `5`，不能 OCR，直接选。
- 后方已知状态 `5` 时，滚动到能看到它，而不是一格一格扫。
- 当前页未知时先 OCR，不能直接跳过。
- 当前页已知状态 `0` 或没有可处理 `斯巴鲁` 时，不再滚动，走默认第一格。
- 只有经过 900 OCR 确认的目标车格才参与“已知非 5”跳过。
