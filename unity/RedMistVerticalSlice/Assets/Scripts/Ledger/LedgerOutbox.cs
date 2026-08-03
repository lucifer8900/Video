using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Lingmai.RedMist
{
    public enum LedgerEnqueueResult
    {
        Added,
        AlreadyQueued
    }

    public sealed class LedgerOutboxException : Exception
    {
        public LedgerOutboxException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// A crash-tolerant local outbox. Each committed event has one deterministic, opaque file,
    /// so a damaged entry cannot turn other valid entries into default/deserialized data.
    /// </summary>
    public sealed class LedgerOutbox
    {
        private const string CommittedPattern = "*.ledger.json";
        private readonly object _gate = new object();
        private readonly string _root;
        private readonly int _maxEntries;

        public LedgerOutbox(string root, int maxEntries)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("The ledger outbox root is required.", nameof(root));
            if (maxEntries < 1 || maxEntries > 100000)
                throw new ArgumentOutOfRangeException(nameof(maxEntries));
            try
            {
                _root = Path.GetFullPath(root);
                Directory.CreateDirectory(_root);
            }
            catch (Exception error) when (
                error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is NotSupportedException)
            {
                throw Failure();
            }
            _maxEntries = maxEntries;
        }

        public bool HasCorruptEntries { get; private set; }

        public LedgerEnqueueResult Enqueue(LedgerEvent item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            string encoded = LedgerEventCodec.Encode(item);
            lock (_gate)
            {
                string destination = PathFor(item.Key);
                if (File.Exists(destination))
                    return CompareExisting(destination, item, encoded);

                if (Directory.GetFiles(_root, CommittedPattern).Length >= _maxEntries)
                    throw new LedgerOutboxException("The ledger outbox has reached its capacity.");

                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    WriteDurable(temporary, encoded);
                    try
                    {
                        File.Move(temporary, destination);
                    }
                    catch (IOException) when (File.Exists(destination))
                    {
                        return CompareExisting(destination, item, encoded);
                    }
                    return LedgerEnqueueResult.Added;
                }
                catch (LedgerOutboxException)
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

        public IReadOnlyList<LedgerEvent> ReadBatch(int maxCount)
        {
            if (maxCount < 1 || maxCount > 50)
                throw new ArgumentOutOfRangeException(nameof(maxCount));
            lock (_gate)
            {
                var items = new List<LedgerEvent>();
                bool corrupt = false;
                string[] paths;
                try
                {
                    paths = Directory.GetFiles(_root, CommittedPattern);
                }
                catch (Exception error) when (
                    error is IOException || error is UnauthorizedAccessException)
                {
                    throw Failure();
                }

                for (int index = 0; index < paths.Length; index++)
                {
                    if (TryRead(paths[index], out LedgerEvent item)) items.Add(item);
                    else corrupt = true;
                }
                HasCorruptEntries = corrupt;
                items.Sort(CompareEvents);
                if (items.Count > maxCount) items.RemoveRange(maxCount, items.Count - maxCount);
                return items.AsReadOnly();
            }
        }

        public void Acknowledge(IReadOnlyCollection<LedgerEntryKey> keys)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            lock (_gate)
            {
                var unique = new HashSet<LedgerEntryKey>();
                foreach (LedgerEntryKey key in keys)
                {
                    if (key == null || !unique.Add(key)) continue;
                    string path = PathFor(key);
                    if (!File.Exists(path)) continue;
                    if (!TryRead(path, out LedgerEvent existing))
                    {
                        HasCorruptEntries = true;
                        continue;
                    }
                    if (!existing.Key.Equals(key))
                    {
                        HasCorruptEntries = true;
                        continue;
                    }
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception error) when (
                        error is IOException || error is UnauthorizedAccessException)
                    {
                        throw Failure();
                    }
                }
            }
        }

        /// <summary>
        /// Removes only fully decoded queued entries for one opaque player. This is not a
        /// server-deletion request. Corrupt local files are preserved for explicit recovery.
        /// </summary>
        public int DeleteQueuedPlayerEntries(string playerId)
        {
            if (!LedgerContractRules.IsPlayerId(playerId))
                throw new ArgumentException("The player identifier is invalid.", nameof(playerId));
            lock (_gate)
            {
                int removed = 0;
                string[] paths = Directory.GetFiles(_root, CommittedPattern);
                for (int index = 0; index < paths.Length; index++)
                {
                    if (!TryRead(paths[index], out LedgerEvent item))
                    {
                        HasCorruptEntries = true;
                        continue;
                    }
                    if (!string.Equals(item.PlayerId, playerId, StringComparison.Ordinal)) continue;
                    try
                    {
                        File.Delete(paths[index]);
                        removed++;
                    }
                    catch (Exception error) when (
                        error is IOException || error is UnauthorizedAccessException)
                    {
                        throw Failure();
                    }
                }
                return removed;
            }
        }

        public override string ToString()
        {
            int count;
            try { count = Directory.GetFiles(_root, CommittedPattern).Length; }
            catch { count = -1; }
            return "LedgerOutbox(PendingFileCount=" + count +
                   ", HasCorruptEntries=" + HasCorruptEntries + ")";
        }

        private LedgerEnqueueResult CompareExisting(
            string path,
            LedgerEvent requested,
            string requestedJson)
        {
            if (!TryRead(path, out LedgerEvent existing))
            {
                HasCorruptEntries = true;
                throw new LedgerOutboxException(
                    "A committed ledger entry is corrupt and was preserved.");
            }
            if (!existing.Key.Equals(requested.Key) ||
                !string.Equals(LedgerEventCodec.Encode(existing), requestedJson, StringComparison.Ordinal))
            {
                throw new LedgerOutboxException(
                    "The ledger idempotency key already has different content.");
            }
            return LedgerEnqueueResult.AlreadyQueued;
        }

        private bool TryRead(string path, out LedgerEvent item)
        {
            item = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 ||
                    info.Length > LedgerEventCodec.MaximumSerializedBytes) return false;
                string json = File.ReadAllText(path, new UTF8Encoding(false, true));
                item = LedgerEventCodec.Decode(json);
                return true;
            }
            catch (Exception error) when (
                error is IOException || error is UnauthorizedAccessException ||
                error is DecoderFallbackException || error is LedgerContractException ||
                error is ArgumentException)
            {
                return false;
            }
        }

        private string PathFor(LedgerEntryKey key)
        {
            byte[] input = Encoding.UTF8.GetBytes(key.PlayerId + "\n" + key.EntryId);
            byte[] digest;
            using (SHA256 sha = SHA256.Create()) digest = sha.ComputeHash(input);
            Array.Clear(input, 0, input.Length);
            var name = new StringBuilder(64 + ".ledger.json".Length);
            for (int index = 0; index < digest.Length; index++)
                name.Append(digest[index].ToString("x2"));
            Array.Clear(digest, 0, digest.Length);
            name.Append(".ledger.json");
            return Path.Combine(_root, name.ToString());
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
                // A non-committed temporary file is ignored on every restart.
            }
        }

        private static int CompareEvents(LedgerEvent left, LedgerEvent right)
        {
            int clock = left.WorldClock.CompareTo(right.WorldClock);
            if (clock != 0) return clock;
            int player = string.Compare(left.PlayerId, right.PlayerId, StringComparison.Ordinal);
            if (player != 0) return player;
            return string.Compare(left.EntryId, right.EntryId, StringComparison.Ordinal);
        }

        private static LedgerOutboxException Failure()
        {
            return new LedgerOutboxException("The ledger outbox operation failed safely.");
        }
    }
}
