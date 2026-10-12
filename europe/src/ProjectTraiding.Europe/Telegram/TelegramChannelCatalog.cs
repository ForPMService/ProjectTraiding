using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProjectTraiding.Europe.Telegram;

// Owns the version/schema invariant and isolates atomic filesystem replacement (rule 13: 1 and 3).
internal sealed class TelegramChannelCatalog(TelegramOptions options, ILogger logger, IMeterFactory meterFactory)
{
    private readonly Histogram<double> _duration = meterFactory.Create("ProjectTraiding.Europe.Telegram").CreateHistogram<double>("telegram.catalog.duration", "s");
    private readonly Counter<long> _outcomes = meterFactory.Create("ProjectTraiding.Europe.Telegram").CreateCounter<long>("telegram.catalog.operations");

    internal async Task<string[]> LoadAsync(CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        string outcome = "success";
        try
        {
            await using FileStream stream = new(options.ChannelsPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            // The size bound follows the operator's channel bound and the public-name syntax.
            if (stream.Length > 1024L + options.MaxChannels * 128L)
                throw new TelegramStorageException("catalog-load");
            TelegramCatalogDocument? document = await JsonSerializer.DeserializeAsync(stream,
                TelegramCatalogJsonContext.Default.TelegramCatalogDocument, cancellationToken);
            if (document is null || document.Version != 1 || document.Channels is null
                || document.Channels.Length > options.MaxChannels)
                throw new TelegramStorageException("catalog-load");
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (string? username in document.Channels)
            {
                if (!TryNormalize(username, out string normalized) || normalized != username || !seen.Add(normalized))
                    throw new TelegramStorageException("catalog-load");
            }
            return document.Channels;
        }
        catch (FileNotFoundException) { outcome = "missing"; return []; }
        catch (DirectoryNotFoundException) { outcome = "missing"; return []; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { outcome = "cancelled"; throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or TelegramStorageException)
        {
            outcome = "StorageFailure";
            TelegramLog.Failure(logger, "catalog-load", TelegramErrorCategory.StorageFailure);
            throw new TelegramStorageException("catalog-load");
        }
        finally { Record("load", outcome, started); }
    }

    internal async Task SaveAsync(string[] usernames, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        string outcome = "success";
        string temporary = options.ChannelsPath + ".tmp";
        bool created = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.ChannelsPath)!);
            await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                created = true;
                await JsonSerializer.SerializeAsync(stream, new TelegramCatalogDocument { Version = 1, Channels = usernames },
                    TelegramCatalogJsonContext.Default.TelegramCatalogDocument, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Same-directory rename commits the complete snapshot; no awaits after this commit point.
            File.Move(temporary, options.ChannelsPath, overwrite: true);
            created = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { outcome = "cancelled"; throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            outcome = "StorageFailure";
            TelegramLog.Failure(logger, "catalog-save", TelegramErrorCategory.StorageFailure);
            throw new TelegramStorageException("catalog-save");
        }
        finally
        {
            if (created)
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    outcome = "StorageFailure";
                    TelegramLog.Failure(logger, "catalog-cleanup", TelegramErrorCategory.StorageFailure);
                }
            }
            Record("save", outcome, started);
        }
    }

    internal static bool TryNormalize(string? input, out string username)
    {
        username = string.Empty;
        if (input is null) return false;
        string candidate = input.Trim();
        if (candidate.StartsWith('@')) candidate = candidate[1..];
        if (candidate.Length is < 5 or > 32 || !char.IsAsciiLetter(candidate[0])) return false;
        foreach (char character in candidate)
            if (!char.IsAsciiLetterOrDigit(character) && character != '_') return false;
        username = candidate.ToLowerInvariant();
        return true;
    }

    private void Record(string operation, string outcome, long started)
    {
        _duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new("operation", operation), new("outcome", outcome));
        _outcomes.Add(1, new("operation", operation), new("outcome", outcome));
    }
}

internal sealed class TelegramStorageException(string operation)
    : TelegramBoundaryException(TelegramErrorCategory.StorageFailure, operation);
