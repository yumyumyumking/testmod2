using System.Diagnostics.CodeAnalysis;

// View-model logging runs on UI events (button clicks, settings save, batch summary),
// not on a hot path, so the LoggerMessage source generator (CA1848) and the
// deferred-evaluation guard (CA1873) are not warranted for these call sites.
[assembly: SuppressMessage(
    "Performance",
    "CA1848:Use the LoggerMessage delegates",
    Justification = "UI event-handler logging is not on a hot path.")]
[assembly: SuppressMessage(
    "Performance",
    "CA1873:Avoid potentially expensive logging",
    Justification = "UI event-handler logging is not on a hot path.")]
