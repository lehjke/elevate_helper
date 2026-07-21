namespace ElevateHelperWinUI.Services;

internal sealed class ProcessingFolderLeaseRegistry
{
    private readonly object syncRoot = new();
    private readonly Dictionary<long, LeaseEntry> leases = [];
    private long nextLeaseId;

    public bool TryAcquire(string path, string ownerId, out IDisposable? lease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        string normalizedPath = Normalize(path);
        lock (syncRoot)
        {
            bool conflicts = leases.Values.Any(existing =>
                !existing.OwnerId.Equals(ownerId, StringComparison.Ordinal) &&
                PathsOverlap(existing.Path, normalizedPath));
            if (conflicts)
            {
                lease = null;
                return false;
            }

            long leaseId = ++nextLeaseId;
            leases.Add(leaseId, new LeaseEntry(normalizedPath, ownerId));
            lease = new ProcessingFolderLease(this, leaseId);
            return true;
        }
    }

    internal static bool PathsOverlap(string firstPath, string secondPath)
    {
        string first = Normalize(firstPath);
        string second = Normalize(secondPath);
        return IsSameOrDescendant(first, second) || IsSameOrDescendant(second, first);
    }

    private static bool IsSameOrDescendant(string parentPath, string candidatePath)
    {
        if (parentPath.Equals(candidatePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string relativePath = Path.GetRelativePath(parentPath, candidatePath);
        return !Path.IsPathRooted(relativePath) &&
               !relativePath.Equals("..", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string Normalize(string path)
    {
        string fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        string root = Path.GetPathRoot(fullPath) ?? string.Empty;
        return fullPath.Length <= root.Length
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private void Release(long leaseId)
    {
        lock (syncRoot)
        {
            leases.Remove(leaseId);
        }
    }

    private sealed record LeaseEntry(string Path, string OwnerId);

    private sealed class ProcessingFolderLease : IDisposable
    {
        private ProcessingFolderLeaseRegistry? registry;
        private readonly long leaseId;

        public ProcessingFolderLease(ProcessingFolderLeaseRegistry registry, long leaseId)
        {
            this.registry = registry;
            this.leaseId = leaseId;
        }

        public void Dispose()
        {
            ProcessingFolderLeaseRegistry? owner = Interlocked.Exchange(ref registry, null);
            owner?.Release(leaseId);
        }
    }
}
