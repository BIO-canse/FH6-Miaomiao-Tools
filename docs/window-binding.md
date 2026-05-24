# 窗口绑定与等比缩放

## 用户原话

```text
算了就改成绑定窗口，然后就是如果是分辨率在等比变动，那么画好的虚拟表格映射也会自动跟着变动，如果是非等比需要提示重新绑定，比如可以搞成小窗口放到副屏，然后主屏继续玩。
```

## 当前目标

窗口绑定只解决坐标和截图基准问题：

1. 启动确认后，脚本绑定当前前台的非脚本窗口，后续截图范围使用该窗口客户区，而不是整块屏幕。
2. 鼠标移动到右下角、屏幕中心、车辆格子右侧等避让动作，都使用绑定窗口客户区。
3. OCR 返回坐标仍然统一转换成屏幕绝对坐标，已有格子映射和叠加层继续使用绝对坐标。
4. 这不是后台输入。当前输入仍然是 Windows 全局 `SendInput`，因此 FH6 仍然需要是当前前台窗口才能稳定接收按键。

## 框选设置

首次框选完整可见车辆格子整体区域时，程序会尝试用框选区域中心点找到下面的游戏窗口，并保存当时的窗口客户区：

```text
calibration_client_left
calibration_client_top
calibration_client_width
calibration_client_height
```

已保存的车辆格子仍然是屏幕绝对坐标：

```text
grid_cell_left
grid_cell_top
grid_cell_width
grid_cell_height
```

运行时绑定目标窗口后，按当前客户区和框选时客户区的比例迁移：

```text
scaleX = current_client_width  / calibration_client_width
scaleY = current_client_height / calibration_client_height

new_grid_left   = current_client_left + (grid_cell_left - calibration_client_left) * scaleX
new_grid_top    = current_client_top  + (grid_cell_top  - calibration_client_top)  * scaleY
new_cell_width  = grid_cell_width  * scaleX
new_cell_height = grid_cell_height * scaleY
```

## 非等比处理

如果 `scaleX` 和 `scaleY` 差距超过容忍值，说明窗口比例已经变了。此时程序必须停止并提示删除或重设 `config/user-settings.json` 后重新框选，不能强行套旧格子。

## 旧设置

旧版 `user-settings.json` 没有窗口客户区基准时，程序仍然可以按旧绝对坐标运行，但无法自动等比迁移。需要迁移能力时，选择 `3` 重设设置重新框选。
