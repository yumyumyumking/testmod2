namespace Transpiler.Core.Syntax;

/// <summary>
/// The position of a vendor marker line within its tier-2 mapping: the begin/middle/end
/// of a frame (TRY/CATCH), or a standalone statement line.
/// </summary>
public enum MarkerRole
{
    Begin,
    Middle,
    End,
    Statement,
}
