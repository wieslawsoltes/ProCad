using System;
using System.Linq;
using System.Reactive.Linq;
using ProCad.Core;
using ProCad.Rendering;
using ACadSharp;
using ACadSharp.Header;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class PropertyGridRowViewModel : ViewModelBase
{
    private readonly object _target;
    private readonly CadPropertyDescriptor _descriptor;
    private readonly ICadPropertyEditPipeline _pipeline;
    private readonly IRenderCacheStampProvider _stampProvider;
    private readonly Action _refreshDependencies;
    private bool _suppressUpdate;

    public string Name { get; }
    public string TypeName { get; }
    public string DxfCodes { get; }
    public string? DxfReferenceType { get; }
    public string DxfReferenceTypeText => DxfReferenceType ?? string.Empty;
    public bool CanWrite { get; }
    public bool IsEditable { get; }

    [Reactive]
    public partial string ValueText { get; set; }

    [Reactive]
    public partial bool HasError { get; set; }

    [Reactive]
    public partial string? ValidationMessage { get; set; }

    public string ValidationMessageText => ValidationMessage ?? string.Empty;

    public PropertyGridRowViewModel(
        object target,
        CadPropertyDescriptor descriptor,
        ICadPropertyEditPipeline pipeline,
        IRenderCacheStampProvider stampProvider,
        Action refreshDependencies)
    {
        _target = target;
        _descriptor = descriptor;
        _pipeline = pipeline;
        _stampProvider = stampProvider;
        _refreshDependencies = refreshDependencies;

        Name = descriptor.Name;
        TypeName = FormatTypeName(descriptor.PropertyType);
        DxfCodes = descriptor.DxfCodes.Length == 0 ? string.Empty : string.Join(", ", descriptor.DxfCodes);
        DxfReferenceType = descriptor.DxfReferenceType;
        CanWrite = descriptor.CanWrite && descriptor.Setter is not null;
        IsEditable = CanWrite && CadValueConverter.CanEdit(descriptor.PropertyType);

        RefreshValue();

        this.WhenAnyValue(x => x.ValueText)
            .Skip(1)
            .Subscribe(UpdateValue);

        this.WhenAnyValue(x => x.ValidationMessage)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(ValidationMessageText)));
    }

    public void RefreshValue()
    {
        _suppressUpdate = true;
        var value = _descriptor.Getter(_target);
        ValueText = CadValueConverter.FormatValue(value, _descriptor.PropertyType);
        _suppressUpdate = false;
    }

    private void UpdateValue(string? valueText)
    {
        if (_suppressUpdate || !IsEditable || _descriptor.Setter is null)
        {
            return;
        }

        var input = valueText ?? string.Empty;
        if (!CadValueConverter.TryConvert(input, _descriptor.PropertyType, out var converted))
        {
            HasError = true;
            ValidationMessage = $"Invalid value for {TypeName}.";
            return;
        }

        var result = _pipeline.TryApply(_target, _descriptor, converted);
        if (!result.IsValid)
        {
            HasError = true;
            ValidationMessage = result.Message;
            RefreshValue();
            return;
        }

        HasError = false;
        ValidationMessage = null;
        NotifyDocumentChanged();
        RefreshValue();
        _refreshDependencies();
    }

    private void NotifyDocumentChanged()
    {
        var document = ResolveDocument(_target);
        if (document is null)
        {
            return;
        }

        _stampProvider.AdvanceStamp(document);
    }

    private static CadDocument? ResolveDocument(object target)
    {
        return target switch
        {
            CadDocument document => document,
            CadObject cadObject when cadObject.Document is not null => cadObject.Document,
            CadHeader header when header.Document is not null => header.Document,
            _ => null
        };
    }

    private static string FormatTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return $"{FormatTypeName(underlying)}?";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name;
        var tickIndex = name.IndexOf('`', StringComparison.Ordinal);
        if (tickIndex >= 0)
        {
            name = name[..tickIndex];
        }

        var args = string.Join(", ", type.GetGenericArguments().Select(FormatTypeName));
        return $"{name}<{args}>";
    }
}
