using System;
using System.IO;
using System.Text;

namespace Lingmai.RedMist
{
    public sealed class PlayerIdentityException : Exception
    {
        public PlayerIdentityException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Persists one random, opaque player identifier. It never reads a device, account, user,
    /// network, platform, or hardware identifier.
    /// </summary>
    public sealed class PlayerIdentityStore
    {
        public const string FileName = "ledger-player-identity.json";
        private const string SchemaVersion = "1.0.0";
        private const int MaximumDocumentBytes = 256;

        private readonly object _gate = new object();
        private readonly string _path;
        private readonly Func<Guid> _guidFactory;

        public PlayerIdentityStore(string root, Func<Guid> guidFactory = null)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("The identity storage root is required.", nameof(root));
            try
            {
                string canonicalRoot = Path.GetFullPath(root);
                Directory.CreateDirectory(canonicalRoot);
                _path = Path.Combine(canonicalRoot, FileName);
            }
            catch (Exception error) when (
                error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is NotSupportedException)
            {
                throw Failure();
            }
            _guidFactory = guidFactory ?? Guid.NewGuid;
        }

        public string LoadOrCreate()
        {
            lock (_gate)
            {
                if (File.Exists(_path)) return LoadExisting();

                Guid generated = _guidFactory();
                if (generated == Guid.Empty) throw Failure();
                string playerId = "p." + generated.ToString("N").ToLowerInvariant();
                string json = "{\"schemaVersion\":\"" + SchemaVersion +
                              "\",\"playerId\":\"" + playerId + "\"}";
                string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    WriteDurable(temporary, json);
                    try
                    {
                        File.Move(temporary, _path);
                    }
                    catch (IOException) when (File.Exists(_path))
                    {
                        return LoadExisting();
                    }
                    return playerId;
                }
                catch (PlayerIdentityException)
                {
                    throw;
                }
                catch (Exception error) when (
                    error is IOException || error is UnauthorizedAccessException ||
                    error is ArgumentException || error is NotSupportedException)
                {
                    throw Failure();
                }
                finally
                {
                    TryDeleteTemporary(temporary);
                }
            }
        }

        public override string ToString() => "PlayerIdentityStore(Redacted)";

        private string LoadExisting()
        {
            try
            {
                var info = new FileInfo(_path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaximumDocumentBytes)
                    throw Failure();
                string json = File.ReadAllText(_path, new UTF8Encoding(false, true));
                StoryJsonValue root = StoryJson.Parse(json);
                if (root.Kind != StoryJsonKind.Object || root.ObjectValue.Count != 2 ||
                    !root.TryGetProperty("schemaVersion", out StoryJsonValue schema) ||
                    schema.Kind != StoryJsonKind.String ||
                    !string.Equals(schema.StringValue, SchemaVersion, StringComparison.Ordinal) ||
                    !root.TryGetProperty("playerId", out StoryJsonValue player) ||
                    player.Kind != StoryJsonKind.String ||
                    !IsGeneratedPlayerId(player.StringValue))
                {
                    throw Failure();
                }
                return player.StringValue;
            }
            catch (PlayerIdentityException)
            {
                throw;
            }
            catch (Exception error) when (
                error is IOException || error is UnauthorizedAccessException ||
                error is DecoderFallbackException || error is StoryJsonException ||
                error is ArgumentException)
            {
                throw Failure();
            }
        }

        private static bool IsGeneratedPlayerId(string value)
        {
            if (value == null || value.Length != 34 ||
                !value.StartsWith("p.", StringComparison.Ordinal)) return false;
            for (int index = 2; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f'))) return false;
            }
            return true;
        }

        private static void WriteDurable(string path, string json)
        {
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(json);
            try
            {
                using (var stream = new FileStream(
                           path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                           4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static void TryDeleteTemporary(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // A temporary file is never treated as a committed identity.
            }
        }

        private static PlayerIdentityException Failure()
        {
            return new PlayerIdentityException(
                "The anonymous player identity is invalid or unavailable.");
        }
    }
}
