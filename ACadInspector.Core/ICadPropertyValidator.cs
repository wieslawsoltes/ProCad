namespace ACadInspector.Core;

public interface ICadPropertyValidator
{
    CadPropertyEditResult Validate(in CadPropertyEditContext context);
}
