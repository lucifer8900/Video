# CX-506 真实参考图库

本目录为《灵脉余烬》美术生成提供**不进入游戏构建**的真实世界结构参考。照片不是最终游戏素材，也不得作为角色身份来源。

## 分类

`landscape`、`architecture`、`plants`、`animals`、`waters`、`weather`、`astronomy`、`people`、`costumes_textiles`、`artifacts`、`geology_caves`、`light_fog_fire`。

当前基线固定为每类 4 张、共 48 张。采集计划要求四个检索方向各至少命中一张，并以来源标题做类别相关性证据，避免用同一检索结果凑数。`raw/` 只存在于本机并被 Git 忽略，也不得进入 Unity 构建；可提交的 `catalog.json` 保存来源页、作者、许可证、下载地址、文件哈希、参考角色与禁止发货标记。

## 许可证边界

- 只允许 `CC0`、`Public Domain Mark`、`CC BY`，并要求允许商业使用和修改。
- 不接收 `NC`、`ND`、`SA`、来源不明图片、搜索结果页、影视截图、社交媒体图片。
- `people` 仅参考姿态、表情、视线、手势与重心；`identityReuse` 必须为 `false`。
- 即使许可允许，最终采用前仍须人工复核肖像权、建筑/商标、文化敏感性与相似人物风险。

## 获取

```powershell
dotnet run --project tools/ReferenceLibrary -- acquire `
  --plan content/reference-library/acquisition-plan.json `
  --catalog content/reference-library/catalog.json `
  --downloads-root content/reference-library/raw
```

发现服务只用于检索。清单记录的 `sourcePageUrl` 必须是原始来源页，不能是 Google、百度、Bing 或 Pinterest 搜索页。

## 校验

```powershell
dotnet run --project tools/ReferenceLibrary -- validate `
  --catalog content/reference-library/catalog.json `
  --downloads-root content/reference-library/raw
```

校验会失败关闭：严格 Schema、许可、分类数量、路径越界、Git/构建边界、身份复刻、文件签名、长度或 SHA-256 任一不符都会返回非零退出码。

## ImageGen 试制图

本卡使用 Codex 内置 ImageGen 生成建筑环境、山水气象、原创人物与原创灵兽各一张非覆盖试制图。它们只用于验证参考图库能否改善结构、材质、姿态和光线，不会自动替换既有首帧或进入发布构建：

- 完整参考 ID、提示词、输出路径与人工复核状态：`content/story/red-mist/image-generation-reference-pilots.cx506.json`
- 便于人工阅读的逐图提示词记录：`content/story/red-mist/image-generation-reference-pilots.cx506.md`
- 本地输出：`unity/RedMistVerticalSlice/Assets/Art/Generated/Cx506Pilots/`

四张图均保持 `needs_review`。采用前须人工检查视觉质量、人物相似风险、许可与文化表达；山水气象试制图还记录了“远景人物数量与提示词不符”的已知偏差，不能作为连续性锁定首帧。
