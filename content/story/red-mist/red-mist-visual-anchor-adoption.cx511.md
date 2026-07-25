# CX-511 青衣侦察者 v3 采用记录

用户已明确批准采用 `identity.celadon_scout.v3`。本卡将 CX-510 的候选以新文件复制到 Unity `Resources`，不修改 CX-510 的 review-only 历史清单，不覆盖 v2，也不改剧情、台词或视频首帧。

## 传播

- 来源：`content/visual-candidates/cx510/identity-anchors/identity_celadon_scout_v3.png`
- Unity 资源：`unity/RedMistVerticalSlice/Assets/Resources/Generated/Characters/identity_celadon_scout_v3.png`
- Resources 加载键：`Generated/Characters/identity_celadon_scout_v3`
- 注册入口：`GeneratedArtCatalog.CeladonScoutIdentity`，并由 `VerticalSliceBuilder` 纳入内容完整性检查。
- Unity `.meta` 使用新的 GUID；v2 和其它身份锚点均保留。

## 约束

采用记录固定为 `adopted=true`、`shipInBuild=true`、`overwriteExistingAssets=false`，要求用户明确审核、源文件与 Unity 文件逐字节相同、PNG 为 1664×936，并保留 CX-510 候选清单作为 provenance。未调用 Gemini/Veo；后续视频首帧传播仍需单独任务卡。
