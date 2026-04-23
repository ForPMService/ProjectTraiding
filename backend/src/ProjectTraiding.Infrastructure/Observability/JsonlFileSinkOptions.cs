using System;

namespace ProjectTraiding.Infrastructure.Observability;

public sealed class JsonlFileSinkOptions
{
    // When true, the JSONL sink is enabled and will attempt to write events to disk.
    public bool Enabled { get; set; } = false;

    // Template path for JSONL files. Use tokens like {yyyyMMdd} for daily rotation.
    // If relative, it's resolved against the current working directory.
    public string FilePath { get; set; } = "storage/logs/api.{yyyyMMdd}.jsonl";
}
