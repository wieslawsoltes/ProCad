using System;
using ProCad.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Objects.Evaluations;
using ACadSharp.XData;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderDynamicBlockTests
{
    [Fact]
    public void ResolveStateName_ReadsEnhancedBlockDataDictionary()
    {
        var parameter = new BlockVisibilityParameter();
        var state = new BlockVisibilityParameter.State { Name = "StateA" };
        parameter.AddState(state);

        var dictionary = BuildEnhancedBlockDictionary("StateA");

        var stateName = DynamicBlockVisibilityResolver.ResolveStateName(dictionary, parameter.States);

        Assert.Equal("StateA", stateName);
    }

    [Fact]
    public void DynamicBlockVisibilityFilter_RespectsStateEntities()
    {
        var visibleLine = new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(1, 0, 0) };
        var hiddenLine = new Line { StartPoint = new XYZ(0, 1, 0), EndPoint = new XYZ(1, 1, 0) };
        var uncontrolled = new Line { StartPoint = new XYZ(0, 2, 0), EndPoint = new XYZ(1, 2, 0) };

        AttachBlockRepHandle(visibleLine, 100);
        AttachBlockRepHandle(hiddenLine, 200);
        AttachBlockRepHandle(uncontrolled, 300);

        var parameter = new BlockVisibilityParameter();
        parameter.Entities.Add(visibleLine);
        parameter.Entities.Add(hiddenLine);

        var state = new BlockVisibilityParameter.State { Name = "StateA" };
        state.Entities.Add(visibleLine);
        parameter.AddState(state);

        var filter = new DynamicBlockVisibilityFilter(parameter, state);

        Assert.True(filter.IsVisible(visibleLine));
        Assert.False(filter.IsVisible(hiddenLine));
        Assert.True(filter.IsVisible(uncontrolled));
    }

    [Fact]
    public void DynamicBlockActionResolver_AppliesFlipTransform()
    {
        var block = new ACadSharp.Tables.BlockRecord("BLOCK");
        var line = new Line { StartPoint = new XYZ(0, 1, 0), EndPoint = new XYZ(0, 2, 0) };
        block.Entities.Add(line);

        var flipParameter = new BlockFlipParameter
        {
            FirstPoint = new XYZ(0, 0, 0),
            SecondPoint = new XYZ(10, 0, 0),
            FlippedStateName = "Flipped"
        };
        var flipAction = new BlockFlipAction();
        flipAction.Entities.Add(line);

        var properties = BuildPropertySet("Flipped");
        var map = DynamicBlockActionResolver.TryCreateFromExpressions(
            new EvaluationExpression[] { flipParameter, flipAction },
            properties);

        Assert.NotNull(map);
        Assert.True(map!.TryGetTransform(line, out var transform));

        var flipped = transform.ApplyTransform(new XYZ(0, 1, 0));
        Assert.True(Math.Abs(flipped.Y + 1) < 0.001);
    }

    [Fact]
    public void DynamicBlockActionResolver_AppliesStretchTransform()
    {
        var block = new ACadSharp.Tables.BlockRecord("BLOCK");
        var line = new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(1, 0, 0) };
        block.Entities.Add(line);

        var linearParameter = new BlockLinearParameter
        {
            ElementName = "Length",
            FirstPoint = new XYZ(0, 0, 0),
            SecondPoint = new XYZ(10, 0, 0)
        };
        var stretchAction = new BlockActionStub();
        stretchAction.Entities.Add(line);

        var properties = BuildPropertySet("Length", numericValue: 15);
        var map = DynamicBlockActionResolver.TryCreateFromExpressions(
            new EvaluationExpression[] { linearParameter, stretchAction },
            properties);

        Assert.NotNull(map);
        Assert.True(map!.TryGetTransform(line, out var transform));

        var moved = transform.ApplyTransform(new XYZ(0, 0, 0));
        Assert.True(Math.Abs(moved.X - 5) < 0.001);
    }

    private static CadDictionary BuildEnhancedBlockDictionary(string stateName)
    {
        var root = new CadDictionary();
        var representation = new CadDictionary("AcDbBlockRepresentation");
        root.Add("AcDbBlockRepresentation", representation);

        var appDataCache = new CadDictionary("AppDataCache");
        representation.Add("AppDataCache", appDataCache);

        var enhanced = new CadDictionary("ACAD_ENHANCEDBLOCKDATA");
        appDataCache.Add("ACAD_ENHANCEDBLOCKDATA", enhanced);

        var record = new XRecord("1");
        record.CreateEntry(1, stateName);
        enhanced.Add("1", record);

        return root;
    }

    private static DynamicBlockPropertySet BuildPropertySet(string stateName, double? numericValue = null)
    {
        var enhanced = new CadDictionary("ACAD_ENHANCEDBLOCKDATA");
        var record = new XRecord("1");
        record.CreateEntry(1, stateName);
        if (numericValue.HasValue)
        {
            record.CreateEntry(40, numericValue.Value);
        }

        enhanced.Add("1", record);
        return DynamicBlockPropertySet.Create(enhanced)!;
    }

    private static void AttachBlockRepHandle(Entity entity, ulong handle)
    {
        var data = new ExtendedData(new ExtendedDataRecord[]
        {
            new ExtendedDataHandle(handle)
        });

        entity.ExtendedData.Add("AcDbBlockRepETag", data);
    }

    private sealed class BlockActionStub : BlockAction
    {
        public override string ObjectName => DxfFileToken.ObjectBlockFlipAction;
    }
}
