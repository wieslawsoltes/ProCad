namespace ProCad.Core;

public interface ICadPropertyEditPipeline
{
    CadPropertyEditResult TryApply(object target, CadPropertyDescriptor descriptor, object? value);
}
