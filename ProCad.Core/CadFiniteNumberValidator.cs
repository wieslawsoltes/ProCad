namespace ProCad.Core;

public sealed class CadFiniteNumberValidator : ICadPropertyValidator
{
    public CadPropertyEditResult Validate(in CadPropertyEditContext context)
    {
        var value = context.Value;
        if (value is double d)
        {
            return double.IsFinite(d)
                ? CadPropertyEditResult.Success(value)
                : CadPropertyEditResult.Failure("Value must be finite.");
        }

        if (value is float f)
        {
            return float.IsFinite(f)
                ? CadPropertyEditResult.Success(value)
                : CadPropertyEditResult.Failure("Value must be finite.");
        }

        return CadPropertyEditResult.Success(value);
    }
}
