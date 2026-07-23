using System.Diagnostics.CodeAnalysis;

// One-time loader / settings / workspace logging is not on a hot path; converting it
// to LoggerMessage delegates would add boilerplate for no measurable benefit. The
// per-file / per-pass hot-path logging uses the source-generated EngineLog delegates
// (see EngineLog.cs), so CA1848 is suppressed assembly-wide only for the cold calls.
[assembly: SuppressMessage(
    "Performance",
    "CA1848:Use the LoggerMessage delegates",
    Justification = "Cold one-time logging; per-file/per-pass hot path uses EngineLog delegates.")]
