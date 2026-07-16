# 《灵脉余烬：赤雾秘苑》Unity 垂直切片

Unity version: `2022.3.62f3c1` (中国版 LTS)

## 已实现范围

- 沈砚／楚明绮两个独立入口与逐节点差异文本。
- 14 个节点构成的 20–30 分钟章节闭环。
- 御器飞行、神识侦察、灵药辨识、四门禁制四个小游戏。
- 两阶段墨蛟策略战，包含撤退、保护、隐藏底牌与协作。
- 谨慎同盟、代价胜利、失败后继续三类结局。
- 法力、神识、体魄、灵药、关系、宗门义务、底牌暴露和世界时钟。
- 自动／手动存档、行动志记、文字速度、音量和减少动态选项。
- 程序化环境声与演出提示；所有关键状态先结算再表现。
- 可选本地 H.264 视频覆盖层；没有视频时自动使用动画化概念图。

## 操作

- 鼠标：所有UI与小游戏。
- `WASD`／方向键：御器飞行。
- 数字 `1`–`4`：剧情选择。
- `Space`：立即显示完整文字／跳过可选视频。
- `Esc`：打开设置。

## 生成场景

```powershell
& 'E:\App\Unity\Editors\2022.3.62f3c1\Editor\Unity.exe' -batchmode -quit `
  -projectPath '<repo>\unity\RedMistVerticalSlice' `
  -executeMethod Lingmai.Editor.VerticalSliceBuilder.GenerateProjectAssets `
  -logFile '<repo>\unity-generate.log'
```

## Windows IL2CPP 构建

使用菜单 `Vertical Slice > Build Windows IL2CPP`，或将执行方法改为 `Lingmai.Editor.VerticalSliceBuilder.BuildWindows`。

构建产物写入 `Builds/Windows/`，该目录不进入 Git。
