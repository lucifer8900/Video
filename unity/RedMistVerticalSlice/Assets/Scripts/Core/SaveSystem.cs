using System;
using System.IO;
using UnityEngine;

namespace Lingmai.RedMist
{
    public enum SaveLoadStatus
    {
        Success,
        NotFound,
        Corrupt,
        BundleMismatch
    }

    public sealed class SaveLoadResult
    {
        public SaveLoadStatus Status { get; }
        public GameState State { get; }
        public string UserMessage { get; }

        private SaveLoadResult(SaveLoadStatus status, GameState state, string userMessage)
        {
            Status = status;
            State = state;
            UserMessage = userMessage ?? string.Empty;
        }

        public static SaveLoadResult Success(GameState state)
        {
            return new SaveLoadResult(SaveLoadStatus.Success, state, "存档加载成功。");
        }

        public static SaveLoadResult NotFound()
        {
            return new SaveLoadResult(SaveLoadStatus.NotFound, null, "尚未找到存档。");
        }

        public static SaveLoadResult Corrupt(string message)
        {
            return new SaveLoadResult(SaveLoadStatus.Corrupt, null, message);
        }

        public static SaveLoadResult BundleMismatch(string message)
        {
            return new SaveLoadResult(SaveLoadStatus.BundleMismatch, null, message);
        }
    }

    public static class SaveCodec
    {
        public static string Encode(
            GameState state,
            StoryBundleIdentity storyBundleIdentity,
            string savedAtUtc = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (storyBundleIdentity == null) throw new ArgumentNullException(nameof(storyBundleIdentity));
            if (string.IsNullOrWhiteSpace(storyBundleIdentity.Version))
            {
                throw new ArgumentException("剧情包版本不能为空。", nameof(storyBundleIdentity));
            }
            if (string.IsNullOrWhiteSpace(storyBundleIdentity.ContentHash))
            {
                throw new ArgumentException("剧情包哈希不能为空。", nameof(storyBundleIdentity));
            }

            if (!IsDefinedRoute(state.route))
                throw new InvalidOperationException(
                    "The player route is invalid and cannot be saved.");
            state.Clamp();
            if (!StateEffectAtomicChannel.IsPersistedStateValid(state))
                throw new InvalidOperationException(
                    "StoryThread state is invalid and cannot be saved.");
            var envelope = new SaveEnvelope
            {
                version = SaveEnvelope.CurrentVersion,
                savedAtUtc = string.IsNullOrWhiteSpace(savedAtUtc)
                    ? DateTime.UtcNow.ToString("O")
                    : savedAtUtc,
                storyBundleVersion = storyBundleIdentity.Version,
                storyBundleContentHash = storyBundleIdentity.ContentHash,
                state = state
            };

            return JsonUtility.ToJson(envelope, true);
        }

        public static SaveLoadResult Decode(
            string json,
            StoryBundleIdentity currentIdentity,
            Func<string, bool> nodeExists = null)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return SaveLoadResult.Corrupt("存档已损坏：文件内容为空。");
            }

            try
            {
                // JsonUtility accepts some truncated or otherwise malformed inputs. Parse with
                // the strict bundle JSON reader first so those files are never mistaken for a
                // valid envelope whose fields merely happen to be absent.
                StoryJsonValue root = StoryJson.Parse(json);
                if (root.Kind != StoryJsonKind.Object ||
                    !root.TryGetProperty("version", out StoryJsonValue version) ||
                    version.Kind != StoryJsonKind.Number ||
                    !int.TryParse(version.NumberToken, out _))
                {
                    return SaveLoadResult.Corrupt(
                        "存档已损坏：缺少有效的版本字段。原文件会被保留。");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Save JSON validation failed: " + exception.Message);
                return SaveLoadResult.Corrupt("存档已损坏，无法读取。原文件会被保留。");
            }

            SaveEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<SaveEnvelope>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Save deserialization failed: " + exception.Message);
                return SaveLoadResult.Corrupt("存档已损坏，无法解析。原文件会被保留。");
            }

            if (envelope == null)
            {
                return SaveLoadResult.Corrupt("存档已损坏：缺少存档数据。原文件会被保留。");
            }

            if (envelope.version < SaveEnvelope.CurrentVersion)
            {
                return SaveLoadResult.BundleMismatch(
                    "这是旧版存档，未记录剧情包身份，无法安全载入；原文件会被保留。");
            }

            if (envelope.version > SaveEnvelope.CurrentVersion)
            {
                return SaveLoadResult.BundleMismatch(
                    "存档格式版本高于当前游戏版本，暂时无法载入；原文件会被保留。");
            }

            if (currentIdentity == null || string.IsNullOrWhiteSpace(currentIdentity.Version))
            {
                return SaveLoadResult.BundleMismatch(
                    "当前剧情包版本尚未就绪，无法核对存档；原文件会被保留。");
            }

            if (string.IsNullOrWhiteSpace(envelope.storyBundleVersion) ||
                !string.Equals(
                    envelope.storyBundleVersion,
                    currentIdentity.Version,
                    StringComparison.Ordinal))
            {
                return SaveLoadResult.BundleMismatch(
                    "存档使用的剧情包版本与当前版本不一致，无法安全载入；原文件会被保留。");
            }

            if (currentIdentity == null || string.IsNullOrWhiteSpace(currentIdentity.ContentHash))
            {
                return SaveLoadResult.BundleMismatch(
                    "当前剧情包哈希尚未就绪，无法核对存档；原文件会被保留。");
            }

            if (string.IsNullOrWhiteSpace(envelope.storyBundleContentHash) ||
                !string.Equals(
                    envelope.storyBundleContentHash,
                    currentIdentity.ContentHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                return SaveLoadResult.BundleMismatch(
                    "存档使用的剧情包哈希与当前内容不一致，无法安全载入；原文件会被保留。");
            }

            if (envelope.state == null)
            {
                return SaveLoadResult.Corrupt("存档已损坏：缺少游戏状态。原文件会被保留。");
            }

            if (!IsDefinedRoute(envelope.state.route))
            {
                return SaveLoadResult.Corrupt(
                    "存档中的角色路线无效，无法安全继续；原文件会被保留。");
            }
            envelope.state.Clamp();
            if (!StateEffectAtomicChannel.IsPersistedStateValid(envelope.state))
            {
                return SaveLoadResult.Corrupt(
                    "存档中的关联剧情状态无效，无法安全继续；原文件会被保留。");
            }
            if (nodeExists != null && !nodeExists(envelope.state.currentNodeId))
            {
                return SaveLoadResult.Corrupt(
                    "存档引用的剧情节点已不存在，无法安全继续；原文件会被保留。");
            }
            return SaveLoadResult.Success(envelope.state);
        }

        private static bool IsDefinedRoute(PlayerRoute route)
        {
            return route == PlayerRoute.None ||
                   route == PlayerRoute.ShenYan ||
                   route == PlayerRoute.ChuMingqi;
        }
    }

    public static class SaveSystem
    {
        public static string SavePath => Path.Combine(Application.persistentDataPath, "red-mist-save.json");

        public static bool HasSave => File.Exists(SavePath);

        public static void Save(GameState state)
        {
            string json = SaveCodec.Encode(state, StoryCatalog.Identity);
            string temp = SavePath + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(SavePath)) File.Delete(SavePath);
            File.Move(temp, SavePath);
        }

        public static SaveLoadResult Load()
        {
            if (!HasSave) return SaveLoadResult.NotFound();

            try
            {
                string json = File.ReadAllText(SavePath);
                return SaveCodec.Decode(
                    json,
                    StoryCatalog.Identity,
                    nodeId => StoryCatalog.Get(nodeId) != null);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Save load failed: " + exception.Message);
                return SaveLoadResult.Corrupt(
                    "无法读取存档；文件可能损坏、被占用或无权访问。原文件会被保留。");
            }
        }

        public static void Delete()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
    }
}
