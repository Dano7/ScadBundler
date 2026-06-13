namespace ScadBundler.Web.Components;

/// <summary>
/// A presentation-only map from a diagnostic <c>SBnnnn</c> code to one plain-language sentence for the
/// non-technical maker (Slice W2 §3). This invents <b>no</b> diagnostic codes and changes no Core behavior;
/// a code not in the map simply shows the (already human-facing) raw message with no extra line.
/// </summary>
internal static class FriendlyDiagnostics
{
    private static readonly Dictionary<string, string> Explanations = new(StringComparer.Ordinal)
    {
        ["SB2001"] = "There's a typo or missing punctuation here — OpenSCAD couldn't read this line.",
        ["SB2002"] = "There's a typo or missing punctuation here — OpenSCAD couldn't read this line.",
        ["SB2004"] = "There's a typo or missing punctuation here — OpenSCAD couldn't read this line.",
        ["SB2005"] = "There's a typo or missing punctuation here — OpenSCAD couldn't read this line.",
        ["SB3003"] = "Two files set the same value; the later one wins (this is usually fine).",
        ["SB3004"] = "Two files define the same module/function; the later one is used.",
        ["SB4002"] = "These files include each other in a loop — remove one of the references.",
        ["SB5004"] = "A library name was renamed to avoid a clash — your model still works.",
    };

    /// <summary>The friendly explanation for <paramref name="code"/>, or <c>null</c> when there is none.</summary>
    /// <param name="code">The diagnostic's <c>SBnnnn</c> code.</param>
    /// <returns>A one-sentence explanation, or <c>null</c>.</returns>
    public static string? Explain(string code) => Explanations.GetValueOrDefault(code);
}
