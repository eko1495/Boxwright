using System.Text.Json;

namespace Boxwright.Core;

/// <summary>
/// Creates and inspects disk images by invoking <c>qemu-img</c> as a subprocess
/// (ADR-0001/0005). qcow2 is the default format. Snapshots and conversion are
/// later (Stage 2) work.
/// </summary>
public sealed class DiskService : IDiskService
{
    private static readonly JsonSerializerOptions InfoOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IProcessRunner _processRunner;
    private readonly QemuLocator _locator;

    /// <summary>Creates a disk service over the given process runner and QEMU locator.</summary>
    public DiskService(IProcessRunner processRunner, QemuLocator locator)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(locator);
        _processRunner = processRunner;
        _locator = locator;
    }

    /// <summary>Creates a disk image of <paramref name="sizeBytes"/> bytes at <paramref name="path"/>.</summary>
    /// <exception cref="DiskException">The <c>qemu-img create</c> invocation failed.</exception>
    public async Task CreateAsync(string path, long sizeBytes, string format = "qcow2", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);

        string qemuImg = _locator.ResolveImageTool();
        ProcessResult result = await _processRunner.RunAsync(
            qemuImg,
            ["create", "-f", format, path, $"{sizeBytes}"],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new DiskException($"qemu-img create failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }
    }

    /// <summary>Grows the disk image at <paramref name="path"/> to <paramref name="sizeBytes"/> bytes.</summary>
    /// <exception cref="DiskException">The <c>qemu-img resize</c> invocation failed.</exception>
    public async Task ResizeAsync(string path, long sizeBytes, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);

        ProcessResult result = await _processRunner.RunAsync(
            _locator.ResolveImageTool(),
            ["resize", path, $"{sizeBytes}"],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new DiskException($"qemu-img resize failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }
    }

    /// <summary>Inspects a disk image, returning its parsed metadata.</summary>
    /// <exception cref="DiskException">The <c>qemu-img info</c> invocation failed or its output could not be parsed.</exception>
    public async Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string qemuImg = _locator.ResolveImageTool();
        ProcessResult result = await _processRunner.RunAsync(
            qemuImg,
            ["info", "--output=json", path],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new DiskException($"qemu-img info failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }

        try
        {
            return JsonSerializer.Deserialize<DiskInfo>(result.StandardOutput, InfoOptions)
                ?? throw new DiskException("qemu-img info returned no data.");
        }
        catch (JsonException ex)
        {
            throw new DiskException("Could not parse qemu-img info output.", ex);
        }
    }

    /// <inheritdoc />
    public async Task CopyAsync(string sourcePath, string destinationPath, string format = "qcow2", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        // convert flattens any backing chain, so the copy is fully independent.
        ProcessResult result = await _processRunner.RunAsync(
            _locator.ResolveImageTool(),
            ["convert", "-O", format, sourcePath, destinationPath],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new DiskException($"qemu-img convert failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }
    }

    /// <inheritdoc />
    public async Task CreateOverlayAsync(string backingPath, string overlayPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backingPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayPath);

        ProcessResult result = await _processRunner.RunAsync(
            _locator.ResolveImageTool(),
            ["create", "-f", "qcow2", "-b", backingPath, "-F", "qcow2", overlayPath],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new DiskException($"qemu-img create overlay failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }
    }
}
