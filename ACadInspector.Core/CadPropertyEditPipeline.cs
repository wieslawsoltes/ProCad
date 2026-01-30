using System;
using System.Collections.Generic;

namespace ACadInspector.Core;

public sealed class CadPropertyEditPipeline : ICadPropertyEditPipeline
{
    private readonly IReadOnlyList<ICadPropertyValidator> _validators;

    public CadPropertyEditPipeline(IEnumerable<ICadPropertyValidator> validators)
    {
        _validators = validators is IReadOnlyList<ICadPropertyValidator> list
            ? list
            : new List<ICadPropertyValidator>(validators);
    }

    public CadPropertyEditResult TryApply(object target, CadPropertyDescriptor descriptor, object? value)
    {
        if (descriptor.Setter is null || !descriptor.CanWrite)
        {
            return CadPropertyEditResult.Failure("Property is read-only.");
        }

        var context = new CadPropertyEditContext(target, descriptor, value);
        foreach (var validator in _validators)
        {
            var result = validator.Validate(in context);
            if (!result.IsValid)
            {
                return result;
            }

            if (!ReferenceEquals(result.CoercedValue, context.Value))
            {
                context = context with { Value = result.CoercedValue };
            }
        }

        try
        {
            descriptor.Setter(target, context.Value);
            return CadPropertyEditResult.Success(context.Value);
        }
        catch (Exception ex)
        {
            return CadPropertyEditResult.Failure(ex.Message);
        }
    }
}
