namespace Transpiler.Engine.Transform;

/// <summary>Deterministic label/temporary allocator (SPEC §8.2), reset per routine.</summary>
public sealed class LabelAllocator
{
    private readonly string _prefix;
    private int _ifCounter;
    private int _whileCounter;
    private int _repeatCounter;

    public LabelAllocator(string prefix)
    {
        _prefix = prefix;
    }

    public void ResetRoutine()
    {
        // Labels are routine-scoped in CL, so IF/WHILE numbering restarts per routine.
        // REPEAT counters become file-scope LOCAL declarations, so their numbering is
        // deliberately file-global to avoid duplicate declarations across routines.
        _ifCounter = 0;
        _whileCounter = 0;
    }

    public (string ElseLabel, string EndLabel) NextIf()
    {
        var n = ++_ifCounter;
        return ($"{_prefix}IF{n}_ELSE", $"{_prefix}IF{n}_END");
    }

    public (string TopLabel, string EndLabel) NextWhile()
    {
        var n = ++_whileCounter;
        return ($"{_prefix}WH{n}_TOP", $"{_prefix}WH{n}_END");
    }

    public (string TopLabel, string EndLabel, string CounterVariable) NextRepeat()
    {
        var n = ++_repeatCounter;
        return ($"{_prefix}RP{n}_TOP", $"{_prefix}RP{n}_END", $"{_prefix}RP{n}_I");
    }
}
