namespace ScadBundler.Web.State;

/// <summary>
/// The coarse pipeline phase the <see cref="WorkspaceController"/> is currently running, surfaced so the UI
/// can show a determinate "Analyzing… → Bundling…" progress indicator (Slice W5 §C1). The phases mirror the
/// two Web-orchestrated facade calls — <see cref="ProjectAnalyzer.Analyze"/> then <see cref="WebBundler.Bundle"/>.
/// </summary>
public enum BusyPhase
{
    /// <summary>No recompute is in flight; the last result (if any) is settled.</summary>
    Idle = 0,

    /// <summary>Running <see cref="ProjectAnalyzer.Analyze"/> (layout inference, load, semantic pass).</summary>
    Analyzing = 1,

    /// <summary>Running <see cref="WebBundler.Bundle"/> (load, inline, transform, emit).</summary>
    Bundling = 2,
}
