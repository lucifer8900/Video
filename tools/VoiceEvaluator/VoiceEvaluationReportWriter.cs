using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceEvaluator;

public sealed class VoiceEvaluationReportWriter
{
    public const string JsonFileName = "voice-evaluation.report.json";
    public const string MarkdownFileName = "voice-evaluation.report.md";

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public WrittenVoiceEvaluationReport Write(
        VoiceEvaluationReport report,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("The output directory must not be empty.", nameof(outputDirectory));

        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);
        string json = NormalizeNewlines(JsonSerializer.Serialize(report, JsonOptions)) + "\n";
        string markdown = BuildMarkdown(report);
        string jsonPath = Path.Combine(fullDirectory, JsonFileName);
        string markdownPath = Path.Combine(fullDirectory, MarkdownFileName);
        WriteAtomic(jsonPath, json);
        WriteAtomic(markdownPath, markdown);
        return new WrittenVoiceEvaluationReport(json, jsonPath, markdownPath);
    }

    private static string BuildMarkdown(VoiceEvaluationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# 中文语音文本回归报告");
        builder.AppendLine();
        builder.AppendLine("> 范围：合成文本回归；不含录音或 ASR 质量，因此不代表生产语音已就绪。");
        builder.AppendLine();
        builder.AppendLine($"- 数据集：`{EscapeInline(report.DatasetId)}`");
        builder.AppendLine($"- 数据哈希：`{report.DatasetSha256}`");
        builder.AppendLine($"- 分类器：`{report.ClassifierId}`（固定本地实现，不调用云服务）");
        builder.AppendLine($"- 样本：{report.SampleCount} 条文本，{report.AudioSampleCount} 条录音");
        builder.AppendLine($"- 总结：{(report.Passed ? "PASS" : "FAIL")}");
        builder.AppendLine();
        builder.AppendLine("## 指标");
        builder.AppendLine();
        builder.AppendLine("| 指标 | 实际 | 门槛 | 结果 |");
        builder.AppendLine("|---|---:|---:|---|");
        builder.AppendLine(
            $"| 有效意图准确率 | {Percent(report.Metrics.ValidIntentAccuracy)} | ≥ {Percent(report.Thresholds.MinimumValidIntentAccuracy)} | {(report.Metrics.ValidIntentAccuracy >= report.Thresholds.MinimumValidIntentAccuracy ? "PASS" : "FAIL")} |");
        builder.AppendLine(
            $"| 严重意图误触率 | {Percent(report.Metrics.SevereFalsePositiveRate)} | < {Percent(report.Thresholds.MaximumSevereFalsePositiveRateExclusive)} | {(report.Metrics.SevereFalsePositiveRate < report.Thresholds.MaximumSevereFalsePositiveRateExclusive ? "PASS" : "FAIL")} |");
        builder.AppendLine();
        builder.AppendLine("## 分类汇总");
        builder.AppendLine();
        builder.AppendLine("| 期望类别 | 样本 | 正确 | 准确率 |");
        builder.AppendLine("|---|---:|---:|---:|");
        foreach (VoiceEvaluationClassReport group in report.Classes)
        {
            builder.AppendLine(
                $"| `{group.ExpectedOutcome}` | {group.Total} | {group.Correct} | {Percent(group.Accuracy)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## 失败样本（不包含原始文本）");
        builder.AppendLine();
        VoiceEvaluationSampleReport[] failures = report.Samples.Where(sample => !sample.Correct).ToArray();
        if (failures.Length == 0)
        {
            builder.AppendLine("无。");
        }
        else
        {
            builder.AppendLine("| ID | 节点 | 期望 | 实际 | 置信度 | 严重误触 |");
            builder.AppendLine("|---|---|---|---|---:|---|");
            foreach (VoiceEvaluationSampleReport sample in failures)
            {
                string expected = sample.ExpectedIntentId ?? sample.ExpectedOutcome;
                string actual = sample.ActualIntentId ?? sample.ActualOutcome;
                builder.AppendLine(
                    $"| `{EscapeInline(sample.Id)}` | `{EscapeInline(sample.NodeId)}` | `{EscapeInline(expected)}` | `{EscapeInline(actual)}` | {sample.Confidence.ToString("0.000", CultureInfo.InvariantCulture)} | {(sample.SevereFalsePositive ? "YES" : "NO")} |");
            }
        }

        return NormalizeNewlines(builder.ToString());
    }

    private static string Percent(double value) =>
        value.ToString("P2", CultureInfo.InvariantCulture);

    private static string EscapeInline(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("`", "'", StringComparison.Ordinal);

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static void WriteAtomic(string path, string content)
    {
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, Utf8NoBom);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

public sealed record WrittenVoiceEvaluationReport(
    string Json,
    string JsonPath,
    string MarkdownPath);
