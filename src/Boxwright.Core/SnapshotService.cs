using System.Text.Json;

namespace Boxwright.Core;

/// <summary>
/// Manages qcow2 internal snapshots by invoking <c>qemu-img snapshot</c> (ADR-0001/0005).
/// The VM must be stopped: internal snapshots require exclusive access to the image, so
/// these run against a disk that no QEMU process currently holds open.
/// </summary>
public sealed class SnapshotService : ISnapshotService
{
    private static readonly JsonSerializerOptions InfoOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IProcessRunner _processRunner;
    private readonly QemuLocator _locator;

    /// <summary>Creates a snapshot service over the given process runner and QEMU locator.</summary>
    public SnapshotService(IProcessRunner processRunner, QemuLocator locator)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(locator);
        _processRunner = processRunner;
        _locator = locator;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VmSnapshot>> ListAsync(string diskPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diskPath);

        ProcessResult result = await _processRunner.RunAsync(
            _locator.ResolveImageTool(),
            ["info", "--output=json", diskPath],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new DiskException($"qemu-img info failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }

        try
        {
            DiskInfo? info = JsonSerializer.Deserialize<DiskInfo>(result.StandardOutput, InfoOptions);
            return info?.Snapshots ?? [];
        }
        catch (JsonException ex)
        {
            throw new DiskException("Could not parse qemu-img info output.", ex);
        }
    }

    /// <inheritdoc />
    public Task CreateAsync(string diskPath, string tag, CancellationToken cancellationToken = default) =>
        RunSnapshotAsync("-c", tag, diskPath, cancellationToken);

    /// <inheritdoc />
    public Task RestoreAsync(string diskPath, string tag, CancellationToken cancellationToken = default) =>
        RunSnapshotAsync("-a", tag, diskPath, cancellationToken);

    /// <inheritdoc />
    public Task DeleteAsync(string diskPath, string tag, CancellationToken cancellationToken = default) =>
        RunSnapshotAsync("-d", tag, diskPath, cancellationToken);

    private async Task RunSnapshotAsync(string op, string tag, string diskPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diskPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        ProcessResult result = await _processRunner.RunAsync(
            _locator.ResolveImageTool(),
            ["snapshot", op, tag, diskPath],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new DiskException($"qemu-img snapshot {op} failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }
    }
}
