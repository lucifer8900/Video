# CX-504 Addressables 分包与 A/B 内容更新实验

本卡只验证 Unity Addressables 负载分组与 Content Update 体积，不是运行时迁移或 Steam 发布证明。

- `mode=experiment_only`
- `runtimeMigration=false`
- `steamPipeVerified=false`
- `productionReadiness=not_evaluated`

四个版本化实验包为基础客户端、赤雾章节视频、简体中文音频/字幕和可选离线 ASR。视频组使用 `PackSeparately` 与无压缩 bundle，确保一条视频对应一个 bundle。当前中文文本仍内嵌在 `StreamingAssets/Story`，中文音频与 ASR 模型也尚无生产资产，因此相关组只放置明确标注的实验 manifest，不能视为正式内容拆分。

A/B 实验使用命令行显式传入的两个本地 8 秒 MP4；脚本只把副本放入 Git 忽略的 `Assets/Cx504Generated`，不会修改或提交原视频。报告中的补丁字节是 Addressables Content Update 输出，不是 SteamPipe 下载量。

脚本为四个组和实验资产生成确定性 GUID，固定 `PlayerBuildVersion` 与逻辑 LoadPath。A 先生成不可变的 `addressables_content_state.bin`，B 再以同一路径、同一 GUID 覆盖目标视频并调用 `ContentUpdateScript.BuildContentUpdate`。变化 bundle 必须精确映射回 `chapter-red-mist-video-remote`；基础包、语言包、ASR 包及其他视频 bundle 的基线哈希必须保持不变。

验收入口：

```powershell
$arguments = @(
  '-batchmode', '-nographics',
  '-buildTarget', 'StandaloneWindows64',
  '-projectPath', '<repo>\unity\RedMistVerticalSlice',
  '-executeMethod', 'Lingmai.RedMist.Cx504.IncrementalPatchExperiment.Run',
  '-cx504BaselineVideo', '<ignored-local-8s-a.mp4>',
  '-cx504ReplacementVideo', '<ignored-local-8s-b.mp4>',
  '-cx504Output', '<temporary-output>',
  '-cx504Report', '<repo>\artifacts\cx504\addressables-patch-report.json',
  '-logFile', '<repo>\unity-cx504-ab.log'
)
$process = Start-Process `
  -FilePath 'E:\App\Unity\Editors\2022.3.62f3c1\Editor\Unity.exe' `
  -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden

if ($process.ExitCode -ne 0) { throw "Unity exit code: $($process.ExitCode)" }
$passCount = @(Select-String -Path '<repo>\unity-cx504-ab.log' -Pattern 'CX504_RESULT:PASS' -SimpleMatch).Count
$failCount = @(Select-String -Path '<repo>\unity-cx504-ab.log' -Pattern 'CX504_RESULT:FAIL' -SimpleMatch).Count
if ($passCount -ne 1 -or $failCount -ne 0) {
  throw "CX-504 sentinel mismatch: PASS=$passCount FAIL=$failCount"
}
```

入口自行调用 `EditorApplication.Exit`，不要额外传 `-quit`。外层验收必须同时满足：Unity ExitCode 为 0、日志含唯一 `CX504_RESULT:PASS`、报告存在且通过 Schema/自哈希校验。

## 2026-07-17 本机实测

- Unity `2022.3.62f3c1`、Addressables `1.22.3`、SBP `1.21.25`。
- A/B 输入均为本地 8 秒 MP4；较大输入为 `8,817,938` 字节。
- 玩家补丁为 `1,066,107` 字节，工程门槛为 `14,275,483` 字节，放大率 `0.120902`；最大 bundle 为 `8,822,784` 字节，低于 2 GiB 上限。
- 补丁只含 1 个章节目标视频 bundle、catalog 与 catalog hash；实际变化组仅 `chapter-red-mist-video-remote`，异常变化组为 0，所有基线 bundle 哈希保持不变。
- 相同输入连续两次构建，3 个补丁文件的相对路径、长度与 SHA-256 完全一致。
- 正式证据见 `artifacts/cx504/addressables-patch-report.json`。报告固定声明 `runtimeMigration=false`、`steamPipeVerified=false`、`productionReadiness=not_evaluated`。

真实 Steam App ID、depot 上传、安装、回滚与下载量仍属于后续人工闸门。
