[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'

function Get-Sha256([string] $Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Artifact([string] $Path) {
    $file = Get-Item -LiteralPath $Path
    [ordered]@{
        mediaType = 'image/png'
        byteLength = $file.Length
        width = 1536
        height = 1024
        sha256 = Get-Sha256 $Path
    }
}

function Resolve-ProjectPath([string] $RelativePath) {
    Join-Path $RepositoryRoot ($RelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
}

$sourceRelative = 'content/story/red-mist/red-mist-multiview-adoption.cx518.json'
$sourcePath = Resolve-ProjectPath $sourceRelative
$source = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json -Depth 32

$emotions = @(
    'neutral',
    'calm_warmth',
    'restrained_joy',
    'resolve',
    'vigilance',
    'suspicion',
    'concern',
    'grief',
    'anger_controlled',
    'fear_suppressed',
    'pain',
    'astonishment'
)
$quadrants = @('top_left', 'top_right', 'bottom_left', 'bottom_right')
$groups = @(
    [ordered]@{ group = 'a'; emotions = @($emotions[0..3]); quadrants = $quadrants },
    [ordered]@{ group = 'b'; emotions = @($emotions[4..7]); quadrants = $quadrants },
    [ordered]@{ group = 'c'; emotions = @($emotions[8..11]); quadrants = $quadrants }
)
$definitions = @(
    [ordered]@{ subjectId = 'character.shen_yan'; slug = 'shen_yan'; identityAssetId = 'identity.shen_yan.v1'; identityPath = 'content/visual-candidates/cx508/identity-anchors/identity_shen_yan_v1.png'; outfitAssetId = 'turnaround.outfit.shen_yan_qinglan_travel.v1' },
    [ordered]@{ subjectId = 'character.chu_mingqi'; slug = 'chu_mingqi'; identityAssetId = 'identity.chu_mingqi.v2'; identityPath = 'content/visual-candidates/cx509/identity-anchors/identity_chu_mingqi_v2.png'; outfitAssetId = 'turnaround.outfit.chu_mingqi_jiyue_travel.v1' },
    [ordered]@{ subjectId = 'character.shi_jun'; slug = 'shi_jun'; identityAssetId = 'identity.shi_jun.v3'; identityPath = 'content/visual-candidates/cx509/identity-anchors/identity_shi_jun_v3.png'; outfitAssetId = 'turnaround.outfit.shi_jun_cangjin_command.v1' },
    [ordered]@{ subjectId = 'character.celadon_scout'; slug = 'celadon_scout'; identityAssetId = 'identity.celadon_scout.v3'; identityPath = 'content/visual-candidates/cx510/identity-anchors/identity_celadon_scout_v3.png'; outfitAssetId = 'turnaround.outfit.celadon_scout_listening.v1' },
    [ordered]@{ subjectId = 'character.cavern_ally'; slug = 'cavern_ally'; identityAssetId = 'identity.cavern_ally.v1'; identityPath = 'content/visual-candidates/cx508/identity-anchors/identity_cavern_ally_v1.png'; outfitAssetId = 'turnaround.outfit.cavern_ally_stone_lamp.v1' },
    [ordered]@{ subjectId = 'character.formation_spirit'; slug = 'formation_spirit'; identityAssetId = 'identity.formation_spirit.v2'; identityPath = 'content/visual-candidates/cx509/identity-anchors/identity_formation_spirit_v2.png'; outfitAssetId = 'turnaround.outfit.formation_spirit_star_balance.v1' }
)
$reviewChecks = @(
    'identity_consistency',
    'expression_readability',
    'eye_anatomy',
    'skin_texture',
    'neckline_consistency',
    'no_text_or_watermark',
    'no_actor_likeness'
)

$subjectRecords = foreach ($definition in $definitions) {
    $identityFullPath = Resolve-ProjectPath $definition.identityPath
    $sourceOutfit = $source.assets | Where-Object assetId -eq $definition.outfitAssetId
    if (-not $sourceOutfit) {
        throw "Missing adopted outfit $($definition.outfitAssetId)"
    }

    $boardRecords = foreach ($group in $groups) {
        $groupId = $group.group
        $boardRelative = "content/visual-candidates/cx519/expression-boards/expression_board_$($definition.slug)_group_${groupId}_v1.png"
        $boardPath = Resolve-ProjectPath $boardRelative
        $emotionText = $group.emotions -join ', '
        $prompt = "Preserve the exact fictional identity, age, face, hair, outfit neckline, 85mm straight-on head-and-shoulders framing, high-key warm-ivory studio light and precise 2x2 layout for $($definition.subjectId). Change facial muscles only for the ordered expressions $emotionText. Keep natural pores, realistic eyelids, wet eye highlights and physical cloth. No text, labels, watermark, hands, props or extra people. No actor likeness. Avoid identity drift, age drift, clothing drift, malformed or crossed eyes, waxy skin, visible teeth, tears, injury and theatrical gestures."
        [ordered]@{
            assetId = "expression_board.$($definition.slug).group_${groupId}.v1"
            group = $groupId
            emotions = @($group.emotions)
            outputPath = $boardRelative
            generationMode = 'codex_builtin_imagegen'
            prompt = $prompt
            adoptionStatus = 'needs_review'
            adopted = $false
            runtimeUse = $false
            shipInBuild = $false
            artifact = Get-Artifact $boardPath
        }
    }

    $expressionRecords = for ($index = 0; $index -lt $emotions.Count; $index++) {
        $emotion = $emotions[$index]
        $groupIndex = [Math]::Floor($index / 4)
        $quadrantIndex = $index % 4
        $groupId = $groups[$groupIndex].group
        $quadrant = $quadrants[$quadrantIndex]
        $x = if (($quadrantIndex % 2) -eq 0) { 6 } else { 774 }
        $y = if ($quadrantIndex -lt 2) { 4 } else { 516 }
        $boardRelative = "content/visual-candidates/cx519/expression-boards/expression_board_$($definition.slug)_group_${groupId}_v1.png"
        $outputRelative = "content/visual-candidates/cx519/expressions/expression_$($definition.slug)_${emotion}_v1.png"
        [ordered]@{
            assetId = "expression.$($definition.slug).${emotion}.v1"
            emotionId = $emotion
            outputPath = $outputRelative
            sourceBoardPath = $boardRelative
            sourceGroup = $groupId
            sourceQuadrant = $quadrant
            crop = [ordered]@{
                x = $x
                y = $y
                width = 756
                height = 504
                scaleWidth = 1536
                scaleHeight = 1024
                filter = 'lanczos'
            }
            generationMode = 'deterministic_ffmpeg_crop'
            adoptionStatus = 'needs_review'
            adopted = $false
            runtimeUse = $false
            shipInBuild = $false
            reviewStatus = 'pending_human_review'
            reviewChecks = $reviewChecks
            artifact = Get-Artifact (Resolve-ProjectPath $outputRelative)
        }
    }

    [ordered]@{
        subjectId = $definition.subjectId
        slug = $definition.slug
        identityAnchor = [ordered]@{
            assetId = $definition.identityAssetId
            path = $definition.identityPath
            sha256 = Get-Sha256 $identityFullPath
        }
        outfitReference = [ordered]@{
            assetId = $sourceOutfit.assetId
            path = $sourceOutfit.sourcePath
            sha256 = $sourceOutfit.artifact.sha256
        }
        boards = @($boardRecords)
        expressions = @($expressionRecords)
    }
}

$manifest = [ordered]@{
    schemaVersion = '1.0.0'
    cardId = 'CX-519'
    worldId = 'lingmai-ember-red-mist'
    createdOn = '2026-08-03'
    sourceAdoption = [ordered]@{
        cardId = 'CX-518'
        path = $sourceRelative
        sha256 = Get-Sha256 $sourcePath
        expressionGenerationAllowed = $true
    }
    generationPolicy = [ordered]@{
        imageProvider = 'codex_builtin_imagegen'
        stillImagesOnly = $true
        generationCalls = 24
        successfulOutputs = 19
        failedCalls = 5
        discardedOutputs = 1
        compositeBoardFiles = 18
        independentExpressionFiles = 72
        cropMethod = 'ffmpeg_crop_756x504_lanczos_to_1536x1024'
        overwriteExistingAssets = $false
        runtimeUse = $false
        shipInBuild = $false
        videoOrAudioUsed = $false
        flow2ApiUsed = $false
        geminiUsed = $false
        veoUsed = $false
        cx514Status = 'paused'
    }
    emotionOrder = $emotions
    groupDefinitions = $groups
    subjects = @($subjectRecords)
    markdownContract = [ordered]@{
        relativePath = 'content/story/red-mist/red-mist-expression-references.cx519.md'
        listsEveryBoardAndExpression = $true
    }
    reviewBoundary = [ordered]@{
        humanReviewRequired = $true
        automaticAdoption = $false
        unityIntegration = $false
        nextCard = 'human_review_then_expression_adoption'
    }
}

$manifestRelative = 'content/story/red-mist/red-mist-expression-references.cx519.json'
$markdownRelative = 'content/story/red-mist/red-mist-expression-references.cx519.md'
$manifestPath = Resolve-ProjectPath $manifestRelative
$markdownPath = Resolve-ProjectPath $markdownRelative
$manifest | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

$markdown = [Collections.Generic.List[string]]::new()
$markdown.Add('# CX-519 六名人物十二表情参考资产')
$markdown.Add('')
$markdown.Add('状态：待人工审核；未采用；不进入 Unity；不随构建发布。')
$markdown.Add('')
$markdown.Add('- 角色：6')
$markdown.Add('- 表情：每人 12 种，共 72 张独立 PNG')
$markdown.Add('- 母板：每人 A/B/C 三组，共 18 张')
$markdown.Add('- 实际内置 ImageGen 调用：24（19 次产生图像、5 次无产出失败/挂起）')
$markdown.Add('- 淘汰输出：1 张方形洞窟盟友 C 组候选；最终母板均为 1536×1024')
$markdown.Add('- 视频/音频/Flow2API/Gemini/Veo 调用：0；CX-514 继续暂停')
$markdown.Add('')
$markdown.Add("源采用清单：$sourceRelative，SHA-256 $($manifest['sourceAdoption']['sha256'])。")

foreach ($subject in $subjectRecords) {
    $markdown.Add('')
    $markdown.Add("## $($subject.subjectId)")
    $markdown.Add('')
    $markdown.Add("身份锚点：$($subject.identityAnchor.path)；服装锚点：$($subject.outfitReference.path)。")
    $markdown.Add('')
    $markdown.Add('母板：')
    foreach ($board in $subject.boards) {
        $markdown.Add("")
        $markdown.Add("- $($board.assetId) — $(Split-Path $board.outputPath -Leaf)；表情顺序：$($board.emotions -join '、')。")
        $markdown.Add("  - 规范化提示词：$($board.prompt)")
    }
    $markdown.Add('')
    $markdown.Add('独立表情：')
    foreach ($expression in $subject.expressions) {
        $markdown.Add("")
        $markdown.Add("- $($expression.assetId) — $(Split-Path $expression.outputPath -Leaf)；来源 $($expression.sourceGroup)/$($expression.sourceQuadrant)；needs_review。")
    }
}

$markdown.Add('')
$markdown.Add('人工审核：逐张检查身份一致、表情可读、双眼解剖、皮肤质感、领口一致、无文字水印、非演员相似。通过前不得采用或写入运行时。')
$markdown | Set-Content -LiteralPath $markdownPath -Encoding utf8NoBOM

Write-Output "CX-519 manifest written: $manifestPath"
Write-Output "CX-519 review record written: $markdownPath"
Write-Output "Subjects=$($subjectRecords.Count) Boards=$(($subjectRecords.boards | Measure-Object).Count) Expressions=$(($subjectRecords.expressions | ForEach-Object { $_ } | Measure-Object).Count)"
