namespace ProCad.Rendering;

public sealed class RenderBudgetViolation
{
    public string Metric { get; }
    public double Actual { get; }
    public double Limit { get; }

    public RenderBudgetViolation(string metric, double actual, double limit)
    {
        Metric = metric;
        Actual = actual;
        Limit = limit;
    }
}
