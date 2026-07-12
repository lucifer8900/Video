# AI 真人影视互动游戏资源与采购清单

日期：2026-07-12
适用范围：20–30 分钟垂直切片
原则：先购买按量计费和短期试用，不在技术闸门通过前购买年度 AI 套餐或 GPU 服务器。

## 1. 本地开发硬件

### 1.1 推荐工作站

| 资源 | 推荐配置 | 用途 |
|---|---:|---|
| 操作系统 | Windows 11 Pro 64 位 | Unity、视频工具、Steam 测试 |
| CPU | 12–16 个高性能核心 | Unity 导入、C# 编译、FFmpeg、批处理 |
| 内存 | 64 GB | Unity、IDE、浏览器、视频和本地 ASR 并行 |
| GPU | NVIDIA，16 GB 显存级别 | 视频预览、NVENC、whisper.cpp CUDA、本地视觉实验 |
| 系统盘 | 2 TB NVMe SSD | 系统、工具、代码和 Unity 缓存 |
| 视频工作盘 | 4 TB NVMe SSD | 原始生成结果、中间文件和构建缓存 |
| 备份 | 8–16 TB NAS 或外置硬盘 | 原始素材、已批准成片、项目快照 |
| 网络 | 稳定上行，建议 100 Mbps 以上 | 上传 AI 任务、下载结果、Steam 构建 |
| 麦克风 | USB 心形指向麦克风 + 耳机 | 中文语音评测和回声控制 |
| 显示器 | 27 英寸 1440p/4K，可靠 sRGB | 角色和镜头一致性审核 |

云端服务负责 AI 视频推理，因此本地 GPU 不需要运行 Veo。选择 NVIDIA 主要为了 FFmpeg 编码和离线 ASR 开发效率。已有电脑如果达到 8 核 CPU、32 GB 内存、8 GB 显存和 2 TB SSD，可以先做阶段 0–3，再决定是否升级。

### 1.2 必要测试设备

- 一台较低配置 Windows PC：6 核 CPU、16 GB 内存、6 GB 左右显存。
- Steam Deck 可以在垂直切片后期购买或借测，不是第一天必买。
- 至少两种麦克风：普通耳麦和独立 USB 麦克风。

### 1.3 存储纪律

- 代码和 JSON 清单进入 Git；正式视频进入对象存储。
- Git 中只保留小型测试样片，避免 Git LFS 变成完整视频仓库。
- 分开保留原始生成结果、采用成片和生成元数据。
- 至少执行“工作盘 + 本地备份 + 云端对象存储”的三份副本策略。

## 2. 云端服务器资源

垂直切片接入托管 AI 服务时，不需要自购 GPU 服务器。

### 2.1 开发和封闭试玩

| 服务 | 起步配置 | 扩缩策略 |
|---|---:|---|
| API / Cloud Run | 1 vCPU / 1–2 GB | 最小实例 0，最大 5 |
| 任务 Worker | 2 vCPU / 4 GB | 最大 2，处理非转码任务 |
| FFmpeg Job | 4 vCPU / 8 GB | 按任务启动，最大 2 |
| PostgreSQL | 1–2 vCPU / 4 GB | 单区、自动备份；正式发行前再评估高可用 |
| 队列 | Cloud Tasks 或 Pub/Sub | 不自建 Redis 起步 |
| 对象存储 | 500 GB 预算 | 原始结果设置生命周期，成片长期保留 |
| CDN | 按量计费 | 只分发已批准视频 |
| Secret Manager | 供应商密钥各一份 | 客户端不可访问 |
| 日志监控 | 30 天保留 | 对生成失败、成本和延迟设告警 |

转码使用独立 Job，避免拖慢 API。Cloud Run 当前支持从较小实例起步并按需扩展，垂直切片无需常驻大型虚拟机。

### 2.2 小规模生产试运行

| 服务 | 建议配置 |
|---|---:|
| API | 2 vCPU / 4 GB，最小实例 1，最大 10 |
| Worker | 4 vCPU / 8 GB，最大 3–5 |
| PostgreSQL | 2 vCPU / 8 GB，启用时间点恢复 |
| 对象存储 | 1–2 TB 起步，按生命周期分层 |
| CDN | 独立域名、签名 URL、流量告警 |

只有出现真实并发数据后才考虑 Redis、数据库高可用、多区域和 Kubernetes。

## 3. 建议购买或开通的工具

### 3.1 现在开通

| 工具 | 购买方式 | 在切片中的职责 | 建议 |
|---|---|---|---|
| Unity 6 Personal | 免费，符合门槛时 | 游戏客户端 | 先用 Personal；当前官方门槛为 20 万美元收入和融资 |
| Google Cloud / Vertex AI | 按量计费 | Veo、GCS、Cloud Run、任务队列 | 设置项目预算、API 配额和每日告警 |
| OpenAI API | 按量计费 | 剧情、意图、GPT Image 2、在线转写 | 使用独立项目和支出上限 |
| GitHub | 免费或现有账户 | 代码、文档和 CI | 不存储完整视频资产 |
| FFmpeg | 免费开源 | 转码、响度、封装和媒体检查 | 固定版本并记录构建信息 |
| whisper.cpp | 免费开源 | 离线 ASR 实验 | 先测试量化模型，不作为基础包必装 |
| DaVinci Resolve | 免费版起步 | 战斗和结局镜头剪辑 | 需要高级功能时再考虑 Studio |

### 3.2 只购买短期试用

| 类别 | 试用目标 | 购买规则 |
|---|---|---|
| 独立口型同步 API | 对比中文台词、侧脸、动作镜头和面部稳定性 | 只买最小额度；通过 20 条固定测试后再决定 |
| 备用视频供应商 | 验证主供应商失败时能否替代环境或战斗镜头 | 不接客户端，只走服务端 Provider |
| 商用音效/音乐库 | 填补 AI 音频不稳定部分 | 确认 Steam 商用许可和永久发行权 |

不要同时购买多个年度视频生成订阅。必须用同一批镜头做横向测试。

### 3.3 暂时不要购买

- GPU 云服务器、Kubernetes 集群、自托管大型语言模型机器。
- 动作捕捉设备、4K 视频生成套餐、多语言配音年度套餐。
- 大型商业剧情编辑器、多区域数据库和企业级 CDN 合同。

## 4. AI 服务使用建议

### 4.1 Google Veo

- `Veo 3.1 Lite/Fast`：构图测试、环境循环、普通对话候选。
- `Veo 3.1`：英雄镜头、近景表演、关键战斗和结局。
- 正式任务使用 GA 模型 ID，不使用已经停用的 preview ID。

当前官方文档显示 Veo 3.1 通常输出 4、6 或 8 秒片段，支持 720p/1080p、24 FPS 和 MP4；内容生产单位应是“镜头”，随后剪成场景。

截至 2026-07-12，Google 官方公开价示例：

- Veo 3.1 Fast 1080p 视频：约 0.10 美元/秒。
- Veo 3.1 Fast 1080p 视频加音频：约 0.12 美元/秒。
- Veo 3.1 标准 720p/1080p 视频：约 0.20 美元/秒。
- Veo 3.1 标准 720p/1080p 视频加音频：约 0.40 美元/秒。

价格和地区会变化，提交批量任务前必须重新核对官方价格，不能把本文数字当成永久合同价格。

### 4.2 OpenAI

- GPT Image 2 用于角色定妆、服装状态、场景参考和首尾帧，不把它误当成视频模型。
- 高难度小说编译和一致性复核使用高能力文本模型；高频意图分类使用较低成本模型。
- 在线语音对比 `gpt-4o-transcribe` 与 mini 版本；当前官方估算约为 0.006 和 0.003 美元/分钟。
- 固定模型快照、提示词版本和评测集，升级模型必须重新跑回归测试。

### 4.3 离线语音

whisper.cpp 当前支持 Windows、CPU、CUDA 和 Vulkan。需要测试最低配置实时系数、普通话与口音准确率、噪声表现、模型包大小和 Steam 可选 depot 更新成本。

离线 ASR 只负责转写；最终仍由当前场景的本地意图匹配器选择预生成分支。

## 5. 视频预算示例

以下只计算供应商成功输出的视频秒数，不包含重试、人工剪辑、存储和流量：

| 批次 | 计算 | Fast 1080p + 音频 | 标准 1080p + 音频 |
|---|---:|---:|---:|
| 100 个 8 秒镜头，一轮 | 800 秒 | 约 96 美元 | 约 320 美元 |
| 100 个 8 秒镜头，三轮候选 | 2,400 秒 | 约 288 美元 | 约 960 美元 |
| 300 个 8 秒镜头，三轮候选 | 7,200 秒 | 约 864 美元 | 约 2,880 美元 |

建议：普通镜头先用 Lite/Fast，只有采用镜头才升级；预留 2–3 倍候选和重做系数。

- AI 视频总预算先设 1,000–4,000 美元硬上限。
- 图像、文本、语音、存储和云服务另设 300–1,000 美元上限。
- 不含新工作站和人工成本，完整切片准备约 1,300–5,000 美元外部服务预算。
- 先投入约 200–500 美元完成四个风险原型，通过闸门后再释放剩余预算。

## 6. Steam 准备

- Steam Direct 当前每个新应用收取 100 美元或等值费用，可到测试正式 Steam 构建时再支付。
- 内容问卷必须披露预生成 AI 内容和运行时 AI 内容。
- 运行时 AI 必须说明输入限制、内容审核、输出过滤、预算限制和追踪方式。
- 视频按章节、区域、语言和可选 ASR 模型拆 depot。
- 单个大型资源包尽量控制在 1–2 GB，并做真实 A/B 构建的增量更新测试。

## 7. 我能直接协助制作的视觉资产

当前 Codex 会话可以直接生成角色定妆图、场景关键帧、风景概念图和分镜参考图，但不能在本地直接输出数秒 AI 视频文件。视频需要通过你开通的 Veo 或其他视频 API 生成。

推荐协作顺序：

1. 你提供首章文本或一个明确场景。
2. 我提取角色、地点、服装、时间和镜头约束。
3. 我生成角色定妆和场景关键帧供你审核。
4. 我输出 `shot-manifest.json`、英文视频提示词、负面提示词和连续性检查表。
5. 服务端脚本把已批准任务提交给 Veo。
6. 我根据结果整理淘汰原因、重试提示词和剪辑顺序。

在没有首章文本和人物设定前生成正式视频会造成风格返工，所以当前阶段只制作技术测试镜头，不批量生产剧情成片。

## 8. 官方资料

- Google Veo 3.1：
  https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/veo/3-1-generate
- Google 生成式 AI 价格：
  https://cloud.google.com/gemini-enterprise-agent-platform/generative-ai/pricing
- OpenAI GPT Image 2：
  https://developers.openai.com/api/docs/models/gpt-image-2
- OpenAI API 价格：
  https://developers.openai.com/api/docs/pricing
- Unity 6 系统要求：
  https://docs.unity3d.com/6000.0/Documentation/Manual/system-requirements.html
- Unity 授权门槛：
  https://unity.com/products/pricing-updates
- whisper.cpp：
  https://github.com/ggml-org/whisper.cpp
- Steam AI 内容问卷：
  https://partner.steamgames.com/doc/gettingstarted/contentsurvey
- SteamPipe：
  https://partner.steamgames.com/doc/sdk/uploading
- Steam Direct：
  https://partner.steamgames.com/doc/gettingstarted/appfee
