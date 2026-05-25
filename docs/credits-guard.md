# CR 余额保险机制

## 用户原话

“要求填入当前CR点数，点数不足的话自动取消买车并退出。每次买车脚本运行一次就-86000CR（一辆车的价格），每次买车前检验是否有足够CR。改动完后release”

## 行为

全自动主程序启动时要求输入当前 CR 点数。这个值是本次运行的临时值，不保存到永久配置。

自动补买车辆时：

1. 每辆目标车价格固定按 `86000 CR` 计算。
2. 总控在进入买车前会先检查是否至少够买 1 辆。
3. 买车子脚本每一轮买车前也会检查剩余 CR。
4. 买成功 1 轮后扣 `86000 CR`，并写出本次买车结果文件。
5. 如果剩余 CR 不足下一辆，买车子脚本不按 `Space`，直接停止。
6. 总控读取买车结果：
   - 按实际买到的数量更新虚拟表。
   - 正常执行返回菜单的 `Esc x4`。
   - 如果实际买到数量少于本轮需要数量，返回菜单后退出主流程，并提示 CR 不足。

## 结果文件

全自动调用 `SpaceDownEnterLoop.exe` 时传入：

```text
--credits <当前CR>
--credit-cost 86000
--buy-result-file state/buy-result.txt
```

买车脚本会写：

- `requested_rounds`
- `completed_rounds`
- `initial_credits`
- `remaining_credits`
- `credit_cost`
- `stop_reason`

总控只按 `completed_rounds` 扣 CR 和追加虚拟表状态 `3`。
