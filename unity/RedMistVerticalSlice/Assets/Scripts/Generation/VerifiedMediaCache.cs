using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Lingmai.RedMist
{
    public interface IVerifiedMediaDownloader
    {
        Task DownloadAsync(
            GenerationDownloadTicket ticket,
            string destinationPath,
            CancellationToken cancellationToken);
    }

    public sealed class VerifiedMediaCacheException : Exception
    {
        public VerifiedMediaCacheException(string code)
            : base("The generated media cache rejected the operation.")
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException(nameof(code));
            Code = code;
        }

        public string Code { get; }
    }

    /// <summary>
    /// Stores generated video only after URL policy, length and SHA-256 validation. Temporary
    /// files and final files share one directory so File.Move is an atomic same-volume commit.
    /// </summary>
    public sealed class VerifiedMediaCache : IVerifiedMediaCache
    {
        private readonly object _gate = new object();
        private readonly string _root;
        private readonly HashSet<string> _allowedHosts;
        private readonly long _maxBytes;
        private readonly IVerifiedMediaDownloader _downloader;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Dictionary<string, Task<VerifiedMediaHandle>> _inflight =
            new Dictionary<string, Task<VerifiedMediaHandle>>(StringComparer.Ordinal);

        public VerifiedMediaCache(
            string root,
            IEnumerable<string> allowedHosts,
            long maxBytes,
            IVerifiedMediaDownloader downloader,
            Func<DateTimeOffset> utcNow)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException(nameof(root));
            if (allowedHosts == null) throw new ArgumentNullException(nameof(allowedHosts));
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _maxBytes = maxBytes;

            _root = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            _allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string host in allowedHosts)
            {
                if (string.IsNullOrWhiteSpace(host) || host.Contains("/") || host.Contains("@"))
                    throw new ArgumentException("A download host allowlist entry is invalid.", nameof(allowedHosts));
                _allowedHosts.Add(host.Trim());
            }
            if (_allowedHosts.Count == 0)
                throw new ArgumentException("At least one download host is required.", nameof(allowedHosts));
            Directory.CreateDirectory(_root);
        }

        public Task<VerifiedMediaHandle> GetOrDownloadAsync(
            GenerationDownloadTicket ticket,
            CancellationToken cancellationToken)
        {
            ValidateTicket(ticket);
            cancellationToken.ThrowIfCancellationRequested();
            string digest = ticket.ContentHash.Substring(7);

            Task<VerifiedMediaHandle> shared;
            lock (_gate)
            {
                if (!_inflight.TryGetValue(digest, out shared))
                {
                    shared = DownloadAndCommitAsync(ticket, digest, cancellationToken);
                    _inflight.Add(digest, shared);
                }
            }
            return AwaitAndRemoveAsync(digest, shared);
        }

        private async Task<VerifiedMediaHandle> AwaitAndRemoveAsync(
            string digest,
            Task<VerifiedMediaHandle> shared)
        {
            try
            {
                return await shared;
            }
            finally
            {
                lock (_gate)
                {
                    if (_inflight.TryGetValue(digest, out Task<VerifiedMediaHandle> current) &&
                        ReferenceEquals(current, shared))
                    {
                        _inflight.Remove(digest);
                    }
                }
            }
        }

        private async Task<VerifiedMediaHandle> DownloadAndCommitAsync(
            GenerationDownloadTicket ticket,
            string digest,
            CancellationToken cancellationToken)
        {
            string finalPath = Path.Combine(_root, digest + ".mp4");
            if (TryUseExisting(finalPath, ticket, out VerifiedMediaHandle existing)) return existing;

            string temporaryPath = Path.Combine(
                _root,
                digest + "." + Guid.NewGuid().ToString("N") + ".part");
            try
            {
                await _downloader.DownloadAsync(ticket, temporaryPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                VerifyFile(temporaryPath, ticket);

                if (File.Exists(finalPath))
                {
                    if (TryUseExisting(finalPath, ticket, out existing))
                    {
                        DeleteIfPresent(temporaryPath);
                        return existing;
                    }
                    File.Delete(finalPath);
                }

                try
                {
                    File.Move(temporaryPath, finalPath);
                }
                catch (IOException)
                {
                    if (!TryUseExisting(finalPath, ticket, out existing)) throw;
                    DeleteIfPresent(temporaryPath);
                    return existing;
                }
                return new VerifiedMediaHandle(ticket.ContentHash, finalPath, ticket.Length);
            }
            catch (OperationCanceledException)
            {
                DeleteIfPresent(temporaryPath);
                throw;
            }
            catch (GenerationTransportException)
            {
                DeleteIfPresent(temporaryPath);
                throw;
            }
            catch (VerifiedMediaCacheException)
            {
                DeleteIfPresent(temporaryPath);
                throw;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is UnauthorizedAccessException)
            {
                DeleteIfPresent(temporaryPath);
                throw new VerifiedMediaCacheException("media.download_failed");
            }
        }

        private void ValidateTicket(GenerationDownloadTicket ticket)
        {
            if (ticket == null) throw new ArgumentNullException(nameof(ticket));
            if (!GenerationHashValidation.IsSha256(ticket.ContentHash))
                throw new VerifiedMediaCacheException("media.hash_invalid");
            if (ticket.Length <= 0 || ticket.Length > _maxBytes)
                throw new VerifiedMediaCacheException("media.length_invalid");
            if (!string.Equals(ticket.ContentType, "video/mp4", StringComparison.Ordinal))
                throw new VerifiedMediaCacheException("media.content_type_invalid");
            if (ticket.ExpiresAtUtc <= _utcNow().ToUniversalTime())
                throw new VerifiedMediaCacheException("media.ticket_expired");
            if (!Uri.TryCreate(ticket.DownloadUrl, UriKind.Absolute, out Uri uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !uri.IsDefaultPort)
            {
                throw new VerifiedMediaCacheException("media.ticket_url_invalid");
            }
            if (!_allowedHosts.Contains(uri.IdnHost))
                throw new VerifiedMediaCacheException("media.ticket_host_rejected");
        }

        private static bool TryUseExisting(
            string path,
            GenerationDownloadTicket ticket,
            out VerifiedMediaHandle handle)
        {
            handle = null;
            if (!File.Exists(path)) return false;
            try
            {
                VerifyFile(path, ticket);
                handle = new VerifiedMediaHandle(ticket.ContentHash, path, ticket.Length);
                return true;
            }
            catch (VerifiedMediaCacheException)
            {
                return false;
            }
        }

        private static void VerifyFile(string path, GenerationDownloadTicket ticket)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != ticket.Length)
                throw new VerifiedMediaCacheException("media.length_mismatch");
            string actual = HashFile(path);
            if (!string.Equals(actual, ticket.ContentHash, StringComparison.Ordinal))
                throw new VerifiedMediaCacheException("media.hash_mismatch");
        }

        private static string HashFile(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       81920,
                       FileOptions.SequentialScan))
            {
                byte[] hash = algorithm.ComputeHash(stream);
                var characters = new char[hash.Length * 2];
                const string alphabet = "0123456789abcdef";
                for (int index = 0; index < hash.Length; index++)
                {
                    characters[index * 2] = alphabet[hash[index] >> 4];
                    characters[index * 2 + 1] = alphabet[hash[index] & 15];
                }
                return "sha256:" + new string(characters);
            }
        }

        private static void DeleteIfPresent(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // A later startup cleanup can remove an inaccessible partial file.
            }
            catch (UnauthorizedAccessException)
            {
                // Do not replace the original failure with cleanup details.
            }
        }
    }
}
