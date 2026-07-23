namespace Transpiler.Infrastructure;

/// <summary>Outcome of transpiling a single file from disk.</summary>
public sealed class FileTranspileOutcome
{
    public FileTranspileOutcome(string sourcePath, string? outputPath, TranspileResult? result, string? failureReason)
    {
        SourcePath = sourcePath;
        OutputPath = outputPath;
        Result = result;
        FailureReason = failureReason;
    }

    public string SourcePath { get; }

    public string? OutputPath { get; }

    public TranspileResult? Result { get; }

    public string? FailureReason { get; }

    public bool Success => FailureReason is null && Result is { Success: true } && OutputPath is not null;
}
