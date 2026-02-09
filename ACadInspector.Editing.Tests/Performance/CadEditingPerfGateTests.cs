using System.Diagnostics;
using System.Text;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Sessions;
using ACadSharp;
using CSMath;
using Xunit;

namespace ACadInspector.Editing.Tests.Performance;

public sealed class CadEditingPerfGateTests
{
    [Fact]
    public void SessionApply_BulkPointCreateBatch_CompletesWithinBudget()
    {
        const int pointCount = 10_000;
        const int budgetMilliseconds = 2500;
        var session = (CadDocumentSession)new CadEditorSessionFactory().Create(new CadDocument());
        var operations = new CadOperation[pointCount];
        for (var index = 0; index < pointCount; index++)
        {
            operations[index] = CadOperationPayloadCodec.CreatePoint(
                CadEntityId.New(),
                new XYZ(index, index % 512, 0d));
        }

        var batch = CadOperationBatch.Create(
            actorId: session.SessionId.Value,
            baseVersion: session.Revision,
            sequence: session.Revision + 1,
            operations: operations);

        var stopwatch = Stopwatch.StartNew();
        session.Apply(batch);
        stopwatch.Stop();

        Assert.Equal(pointCount, session.Document.Entities.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds <= budgetMilliseconds,
            $"Session apply budget exceeded: {stopwatch.ElapsedMilliseconds} ms > {budgetMilliseconds} ms.");
    }

    [Fact]
    public async Task ScriptHost_BulkPointPlayback_CompletesWithinBudget()
    {
        const int commandCount = 3000;
        const int budgetMilliseconds = 3000;
        var registry = new CadCommandRegistry();
        registry.Register(new PointCadCommand());
        var host = new CadScriptCommandHost(registry);
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        var scriptBuilder = new StringBuilder(commandCount * 12);
        for (var index = 0; index < commandCount; index++)
        {
            scriptBuilder.Append("POINT ");
            scriptBuilder.Append(index);
            scriptBuilder.Append(",0");
            scriptBuilder.AppendLine();
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await host.ExecuteAsync(scriptBuilder.ToString(), session);
        stopwatch.Stop();

        Assert.True(result.Success);
        Assert.Equal(commandCount, result.ExecutedCount);
        Assert.True(
            stopwatch.ElapsedMilliseconds <= budgetMilliseconds,
            $"Script playback budget exceeded: {stopwatch.ElapsedMilliseconds} ms > {budgetMilliseconds} ms.");
    }
}
