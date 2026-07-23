namespace Transpiler.Core.Languages;

/// <summary>How statement blocks are delimited in a language.</summary>
public enum BlockDelimiterStyle
{
    /// <summary>
    /// Blocks end with keywords (VBA-like): IF…ENDIF, WHILE…ENDWHILE,
    /// STEP name…END name. This is the default.
    /// </summary>
    Keyword,

    /// <summary>
    /// Blocks are wrapped in delimiter symbols (C#-like): IF cond THEN { … } ELSE { … },
    /// STEP name { … }. The end keywords are not used.
    /// </summary>
    Braces,
}
