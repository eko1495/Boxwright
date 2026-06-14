using Boxwright.Core;

namespace Boxwright.Cli;

/// <summary>
/// Reports ISO/cloud-image download progress to stderr (keeping stdout clean for scripting), throttled
/// to whole-percent steps so a multi-gigabyte download doesn't flood the terminal. When the total size
/// is unknown it falls back to bytes received.
/// </summary>
internal sealed class ConsoleDownloadProgress : IProgress<IsoDownloadProgress>
{
    private readonly CliOutput _output;
    private int _lastPercent = -1;
    private bool _wroteAny;

    public ConsoleDownloadProgress(CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    public void Report(IsoDownloadProgress value)
    {
        if (value.Percent is { } percent)
        {
            int whole = (int)percent;
            if (whole == _lastPercent)
            {
                return;
            }

            _lastPercent = whole;
            _output.ErrorLine($"Downloading… {whole}% ({Humanize(value.BytesReceived)} / {Humanize(value.TotalBytes!.Value)})");
        }
        else
        {
            // Unknown total: report each callback's running byte count.
            _output.ErrorLine($"Downloading… {Humanize(value.BytesReceived)}");
        }

        _wroteAny = true;
    }

    /// <summary>Emits a final "done" line if any progress was shown.</summary>
    public void Complete()
    {
        if (_wroteAny)
        {
            _output.ErrorLine("Download complete.");
        }
    }

    private static string Humanize(long bytes)
    {
        const double gb = 1_000_000_000d;
        const double mb = 1_000_000d;
        return bytes >= gb ? $"{bytes / gb:0.0} GB" : $"{bytes / mb:0} MB";
    }
}
