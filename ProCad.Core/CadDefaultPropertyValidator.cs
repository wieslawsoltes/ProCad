using System;

namespace ProCad.Core;

public sealed class CadDefaultPropertyValidator : ICadPropertyValidator
{
    public CadPropertyEditResult Validate(in CadPropertyEditContext context)
    {
        var type = context.Descriptor.PropertyType;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (context.Value is null)
        {
            if (underlying.IsValueType && Nullable.GetUnderlyingType(type) is null)
            {
                return CadPropertyEditResult.Failure("Value is required.");
            }

            return CadPropertyEditResult.Success(null);
        }

        if (!underlying.IsInstanceOfType(context.Value))
        {
            return CadPropertyEditResult.Failure("Value type mismatch.");
        }

        return CadPropertyEditResult.Success(context.Value);
    }
}
