namespace ProCad.Core;

public sealed class CadPropertyDiff
{
    public CadPropertyDiff(string name, string leftValue, string rightValue)
    {
        Name = name;
        LeftValue = leftValue;
        RightValue = rightValue;
    }

    public string Name { get; }

    public string LeftValue { get; }

    public string RightValue { get; }
}
