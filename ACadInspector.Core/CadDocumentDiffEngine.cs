using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ACadSharp;
using ACadSharp.Classes;
using ACadSharp.Header;

namespace ACadInspector.Core;

public sealed class CadDocumentDiffEngine
{
    public CadDocumentDiffResult Compare(CadDocument left, CadDocument right)
    {
        var leftEntries = CadDocumentIdentityBuilder.Build(left);
        var rightEntries = CadDocumentIdentityBuilder.Build(right);

        var comparer = StringComparer.OrdinalIgnoreCase;
        var leftMap = leftEntries.ToDictionary(entry => entry.Path, entry => entry, comparer);
        var rightMap = rightEntries.ToDictionary(entry => entry.Path, entry => entry, comparer);

        var allKeys = new SortedSet<string>(leftMap.Keys, comparer);
        allKeys.UnionWith(rightMap.Keys);

        var added = new List<CadObjectDiff>();
        var removed = new List<CadObjectDiff>();
        var modified = new List<CadObjectDiff>();
        var unchanged = new List<CadObjectDiff>();

        foreach (var key in allKeys)
        {
            var hasLeft = leftMap.TryGetValue(key, out var leftEntry);
            var hasRight = rightMap.TryGetValue(key, out var rightEntry);

            if (hasLeft && !hasRight && leftEntry is not null)
            {
                removed.Add(new CadObjectDiff(key, leftEntry.Kind, leftEntry.TypeName, CadDiffKind.Removed, Array.Empty<CadPropertyDiff>()));
                continue;
            }

            if (!hasLeft && hasRight && rightEntry is not null)
            {
                added.Add(new CadObjectDiff(key, rightEntry.Kind, rightEntry.TypeName, CadDiffKind.Added, Array.Empty<CadPropertyDiff>()));
                continue;
            }

            if (leftEntry?.Value is null || rightEntry?.Value is null)
            {
                continue;
            }

            var diffs = CompareValues(leftEntry.Value, rightEntry.Value);
            if (diffs.Count == 0)
            {
                unchanged.Add(new CadObjectDiff(key, leftEntry.Kind, leftEntry.TypeName, CadDiffKind.Unchanged, Array.Empty<CadPropertyDiff>()));
            }
            else
            {
                modified.Add(new CadObjectDiff(key, leftEntry.Kind, leftEntry.TypeName, CadDiffKind.Modified, diffs));
            }
        }

        return new CadDocumentDiffResult(added, removed, modified, unchanged);
    }

    private static List<CadPropertyDiff> CompareValues(object left, object right)
    {
        if (left is CadHeader leftHeader && right is CadHeader rightHeader)
        {
            return CompareHeader(leftHeader, rightHeader);
        }

        if (left is CadSummaryInfo leftSummary && right is CadSummaryInfo rightSummary)
        {
            return CompareSummary(leftSummary, rightSummary);
        }

        if (left is DxfClass leftClass && right is DxfClass rightClass)
        {
            return CompareClass(leftClass, rightClass);
        }

        if (left is CadObject leftObject && right is CadObject rightObject)
        {
            return CompareCadObject(leftObject, rightObject);
        }

        return CompareFallback(left, right);
    }

    private static List<CadPropertyDiff> CompareCadObject(CadObject left, CadObject right)
    {
        var diffs = new List<CadPropertyDiff>();
        if (left.GetType() != right.GetType())
        {
            diffs.Add(new CadPropertyDiff("$Type", left.GetType().Name, right.GetType().Name));
            return diffs;
        }

        var descriptor = FindDescriptor(left.GetType());
        if (descriptor is null)
        {
            return diffs;
        }

        foreach (var property in descriptor.Properties)
        {
            if (!CadValueConverter.CanEdit(property.PropertyType))
            {
                continue;
            }

            var leftValue = property.Getter(left);
            var rightValue = property.Getter(right);
            if (AreEqual(leftValue, rightValue))
            {
                continue;
            }

            diffs.Add(new CadPropertyDiff(
                property.Name,
                FormatValue(leftValue, property.PropertyType),
                FormatValue(rightValue, property.PropertyType)));
        }

        return diffs;
    }

    private static CadTypeDescriptor? FindDescriptor(Type type)
    {
        if (CadMetadataRegistry.Types.TryGetValue(type, out var descriptor))
        {
            return descriptor;
        }

        var current = type.BaseType;
        while (current is not null)
        {
            if (CadMetadataRegistry.Types.TryGetValue(current, out descriptor))
            {
                return descriptor;
            }

            current = current.BaseType;
        }

        return null;
    }

    private static List<CadPropertyDiff> CompareHeader(CadHeader left, CadHeader right)
    {
        var diffs = new List<CadPropertyDiff>();
        foreach (var variable in CadHeaderMetadataRegistry.Variables)
        {
            if (!CadValueConverter.CanEdit(variable.PropertyType))
            {
                continue;
            }

            var leftValue = variable.Getter(left);
            var rightValue = variable.Getter(right);
            if (AreEqual(leftValue, rightValue))
            {
                continue;
            }

            diffs.Add(new CadPropertyDiff(
                variable.VariableName,
                FormatValue(leftValue, variable.PropertyType),
                FormatValue(rightValue, variable.PropertyType)));
        }

        return diffs;
    }

    private static List<CadPropertyDiff> CompareSummary(CadSummaryInfo left, CadSummaryInfo right)
    {
        var diffs = new List<CadPropertyDiff>();
        AddIfDifferent(diffs, "Title", left.Title, right.Title, typeof(string));
        AddIfDifferent(diffs, "Subject", left.Subject, right.Subject, typeof(string));
        AddIfDifferent(diffs, "Author", left.Author, right.Author, typeof(string));
        AddIfDifferent(diffs, "Keywords", left.Keywords, right.Keywords, typeof(string));
        AddIfDifferent(diffs, "Comments", left.Comments, right.Comments, typeof(string));
        AddIfDifferent(diffs, "LastSavedBy", left.LastSavedBy, right.LastSavedBy, typeof(string));
        AddIfDifferent(diffs, "RevisionNumber", left.RevisionNumber, right.RevisionNumber, typeof(string));
        AddIfDifferent(diffs, "HyperlinkBase", left.HyperlinkBase, right.HyperlinkBase, typeof(string));
        AddIfDifferent(diffs, "CreatedDate", left.CreatedDate, right.CreatedDate, typeof(DateTime));
        AddIfDifferent(diffs, "ModifiedDate", left.ModifiedDate, right.ModifiedDate, typeof(DateTime));

        var leftProperties = left.Properties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rightProperties = right.Properties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var keys = new SortedSet<string>(leftProperties.Keys, StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(rightProperties.Keys);

        foreach (var key in keys)
        {
            leftProperties.TryGetValue(key, out var leftValue);
            rightProperties.TryGetValue(key, out var rightValue);
            AddIfDifferent(diffs, $"Properties.{key}", leftValue ?? string.Empty, rightValue ?? string.Empty, typeof(string));
        }

        return diffs;
    }

    private static List<CadPropertyDiff> CompareClass(DxfClass left, DxfClass right)
    {
        var diffs = new List<CadPropertyDiff>();
        AddIfDifferent(diffs, nameof(DxfClass.ApplicationName), left.ApplicationName, right.ApplicationName, typeof(string));
        AddIfDifferent(diffs, nameof(DxfClass.CppClassName), left.CppClassName, right.CppClassName, typeof(string));
        AddIfDifferent(diffs, nameof(DxfClass.DxfName), left.DxfName, right.DxfName, typeof(string));
        AddIfDifferent(diffs, nameof(DxfClass.ClassNumber), left.ClassNumber, right.ClassNumber, typeof(short));
        AddIfDifferent(diffs, nameof(DxfClass.DwgVersion), left.DwgVersion, right.DwgVersion, typeof(ACadVersion));
        AddIfDifferent(diffs, nameof(DxfClass.ProxyFlags), left.ProxyFlags, right.ProxyFlags, typeof(ProxyFlags));
        AddIfDifferent(diffs, nameof(DxfClass.IsAnEntity), left.IsAnEntity, right.IsAnEntity, typeof(bool));
        AddIfDifferent(diffs, nameof(DxfClass.WasZombie), left.WasZombie, right.WasZombie, typeof(bool));
        AddIfDifferent(diffs, nameof(DxfClass.InstanceCount), left.InstanceCount, right.InstanceCount, typeof(int));
        AddIfDifferent(diffs, nameof(DxfClass.ItemClassId), left.ItemClassId, right.ItemClassId, typeof(short));
        return diffs;
    }

    private static List<CadPropertyDiff> CompareFallback(object left, object right)
    {
        var diffs = new List<CadPropertyDiff>();
        if (!AreEqual(left, right))
        {
            diffs.Add(new CadPropertyDiff("Value", left.ToString() ?? string.Empty, right.ToString() ?? string.Empty));
        }

        return diffs;
    }

    private static void AddIfDifferent(List<CadPropertyDiff> diffs, string name, object? left, object? right, Type type)
    {
        if (AreEqual(left, right))
        {
            return;
        }

        diffs.Add(new CadPropertyDiff(name, FormatValue(left, type), FormatValue(right, type)));
    }

    private static bool AreEqual(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Equals(right);
    }

    private static string FormatValue(object? value, Type type)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is INamedCadObject named)
        {
            return named.Name;
        }

        if (value is CadObject cadObject)
        {
            return string.IsNullOrWhiteSpace(cadObject.ObjectName) ? cadObject.GetType().Name : cadObject.ObjectName;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }
}
