using System;
using System.Collections.Generic;
using System.Globalization;
using ACadSharp;
using ACadSharp.Classes;
using ACadSharp.Entities;
using ACadSharp.Header;
using ACadSharp.Objects;
using ACadSharp.Tables;

namespace ACadInspector.Core;

public sealed class CadBatchQueryEngine
{
    public IReadOnlyList<CadBatchSearchResult> Search(
        CadBatchQuery query,
        CadDocument document,
        string documentName,
        CadFileFormat format,
        string? documentPath)
    {
        var results = new List<CadBatchSearchResult>();
        if (query.Terms.Count == 0)
        {
            return results;
        }

        var entries = CadDocumentIdentityBuilder.Build(document);
        var docPath = documentPath ?? string.Empty;

        foreach (var entry in entries)
        {
            if (entry.Value is null)
            {
                continue;
            }

            if (!TryMatch(entry, entry.Value, query.Terms, documentName, docPath, format, out var matchText))
            {
                continue;
            }

            var name = GetObjectName(entry.Value);
            var handle = GetHandle(entry.Value);
            results.Add(new CadBatchSearchResult(
                documentName,
                docPath,
                format,
                entry.Path,
                entry.Kind,
                entry.TypeName,
                name,
                handle,
                matchText));
        }

        return results;
    }

    private static bool TryMatch(
        CadDocumentIdentityEntry entry,
        object value,
        IReadOnlyList<CadBatchQueryTerm> terms,
        string documentName,
        string documentPath,
        CadFileFormat format,
        out string matchText)
    {
        var matches = new List<string>();
        var name = GetObjectName(value);
        var handle = GetHandle(value);
        var textValues = BuildTextValues(entry, value, documentName, documentPath, name, handle, format);

        foreach (var term in terms)
        {
            if (!MatchTerm(term, entry, value, documentName, documentPath, format, name, handle, textValues, matches))
            {
                matchText = string.Empty;
                return false;
            }
        }

        matchText = string.Join("; ", matches);
        return true;
    }

    private static bool MatchTerm(
        CadBatchQueryTerm term,
        CadDocumentIdentityEntry entry,
        object value,
        string documentName,
        string documentPath,
        CadFileFormat format,
        string name,
        string handle,
        IReadOnlyList<string> textValues,
        List<string> matches)
    {
        var key = term.Key;
        var termValue = term.Value;
        if (string.IsNullOrWhiteSpace(termValue))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(key) || key.Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsAny(textValues, termValue))
            {
                matches.Add($"text:{termValue}");
                return true;
            }

            return false;
        }

        if (key.Equals("type", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(entry.TypeName, termValue))
            {
                matches.Add($"type:{termValue}");
                return true;
            }

            return false;
        }

        if (key.Equals("kind", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(entry.Kind, termValue))
            {
                matches.Add($"kind:{termValue}");
                return true;
            }

            return false;
        }

        if (key.Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(entry.Path, termValue))
            {
                matches.Add($"path:{termValue}");
                return true;
            }

            return false;
        }

        if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(name, termValue))
            {
                matches.Add($"name:{termValue}");
                return true;
            }

            return false;
        }

        if (key.Equals("handle", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(handle, termValue))
            {
                matches.Add($"handle:{termValue}");
                return true;
            }

            return false;
        }

        if (key.Equals("doc", StringComparison.OrdinalIgnoreCase) || key.Equals("document", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(documentName, termValue) || Contains(documentPath, termValue))
            {
                matches.Add($"doc:{termValue}");
                return true;
            }

            return false;
        }

        if (key.Equals("format", StringComparison.OrdinalIgnoreCase))
        {
            if (Contains(format.ToString(), termValue))
            {
                matches.Add($"format:{termValue}");
                return true;
            }

            return false;
        }

        if (key.Equals("layer", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetPropertyValue(value, "Layer", out var layerValue) && Contains(layerValue, termValue))
            {
                matches.Add($"layer:{termValue}");
                return true;
            }

            return false;
        }

        if (TryGetPropertyValue(value, key, out var propertyValue) && Contains(propertyValue, termValue))
        {
            matches.Add($"{key}:{termValue}");
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> BuildTextValues(
        CadDocumentIdentityEntry entry,
        object value,
        string documentName,
        string documentPath,
        string name,
        string handle,
        CadFileFormat format)
    {
        var values = new List<string>(8)
        {
            entry.Path,
            entry.Kind,
            entry.TypeName,
            name,
            handle,
            documentName,
            documentPath,
            format.ToString()
        };

        if (TryGetStringProperties(value, out var propertyValues))
        {
            values.AddRange(propertyValues);
        }

        return values;
    }

    private static bool TryGetStringProperties(object value, out List<string> properties)
    {
        properties = new List<string>();

        if (value is CadSummaryInfo summary)
        {
            AddSummaryValues(summary, properties);
            return properties.Count > 0;
        }

        if (value is CadHeader header)
        {
            foreach (var variable in CadHeaderMetadataRegistry.Variables)
            {
                if (variable.PropertyType != typeof(string))
                {
                    continue;
                }

                var valueText = CadValueConverter.FormatValue(variable.Getter(header), variable.PropertyType);
                if (!string.IsNullOrWhiteSpace(valueText))
                {
                    properties.Add(valueText);
                }
            }

            return properties.Count > 0;
        }

        var descriptor = FindDescriptor(value.GetType());
        if (descriptor is null)
        {
            return false;
        }

        foreach (var property in descriptor.Properties)
        {
            if (property.PropertyType != typeof(string))
            {
                continue;
            }

            try
            {
                var text = CadValueConverter.FormatValue(property.Getter(value), property.PropertyType);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    properties.Add(text);
                }
            }
            catch (Exception)
            {
                // Ignore property getter failures in search.
            }
        }

        return properties.Count > 0;
    }

    private static bool TryGetPropertyValue(object value, string propertyName, out string formatted)
    {
        formatted = string.Empty;
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        if (value is CadHeader header)
        {
            foreach (var variable in CadHeaderMetadataRegistry.Variables)
            {
                if (string.Equals(variable.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(variable.VariableName, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    formatted = CadValueConverter.FormatValue(variable.Getter(header), variable.PropertyType);
                    return true;
                }
            }
        }

        if (value is CadSummaryInfo summary)
        {
            if (TryGetSummaryValue(summary, propertyName, out var summaryValue))
            {
                formatted = summaryValue;
                return true;
            }
        }

        var descriptor = FindDescriptor(value.GetType());
        if (descriptor is null)
        {
            return false;
        }

        foreach (var property in descriptor.Properties)
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                formatted = CadValueConverter.FormatValue(property.Getter(value), property.PropertyType);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        return false;
    }

    private static void AddSummaryValues(CadSummaryInfo summary, List<string> values)
    {
        AddIfNotEmpty(values, summary.Title);
        AddIfNotEmpty(values, summary.Subject);
        AddIfNotEmpty(values, summary.Author);
        AddIfNotEmpty(values, summary.Keywords);
        AddIfNotEmpty(values, summary.Comments);
        AddIfNotEmpty(values, summary.LastSavedBy);
        AddIfNotEmpty(values, summary.RevisionNumber);
        AddIfNotEmpty(values, summary.HyperlinkBase);

        if (summary.Properties is not null)
        {
            foreach (var entry in summary.Properties)
            {
                AddIfNotEmpty(values, entry.Key);
                AddIfNotEmpty(values, entry.Value);
            }
        }
    }

    private static bool TryGetSummaryValue(CadSummaryInfo summary, string propertyName, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var key = propertyName.Trim();
        if (key.StartsWith("Properties.", StringComparison.OrdinalIgnoreCase) && summary.Properties is not null)
        {
            var customKey = key.Substring("Properties.".Length);
            if (summary.Properties.TryGetValue(customKey, out var customValue))
            {
                value = customValue ?? string.Empty;
                return true;
            }
        }

        return key.ToLowerInvariant() switch
        {
            "title" => SetValue(summary.Title, out value),
            "subject" => SetValue(summary.Subject, out value),
            "author" => SetValue(summary.Author, out value),
            "keywords" => SetValue(summary.Keywords, out value),
            "comments" => SetValue(summary.Comments, out value),
            "lastsavedby" => SetValue(summary.LastSavedBy, out value),
            "revisionnumber" => SetValue(summary.RevisionNumber, out value),
            "hyperlinkbase" => SetValue(summary.HyperlinkBase, out value),
            "createddate" => SetValue(summary.CreatedDate.ToString("O", CultureInfo.InvariantCulture), out value),
            "modifieddate" => SetValue(summary.ModifiedDate.ToString("O", CultureInfo.InvariantCulture), out value),
            _ => false
        };
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

    private static bool ContainsAny(IReadOnlyList<string> values, string term)
    {
        foreach (var value in values)
        {
            if (Contains(value, term))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(string? value, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddIfNotEmpty(List<string> values, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            values.Add(text);
        }
    }

    private static bool SetValue(string? value, out string output)
    {
        output = value ?? string.Empty;
        return true;
    }

    private static string GetObjectName(object value)
    {
        if (value is TableEntry tableEntry && !string.IsNullOrWhiteSpace(tableEntry.Name))
        {
            return tableEntry.Name;
        }

        if (value is INamedCadObject named && !string.IsNullOrWhiteSpace(named.Name))
        {
            return named.Name;
        }

        if (value is CadObject cadObject)
        {
            return string.IsNullOrWhiteSpace(cadObject.ObjectName)
                ? cadObject.GetType().Name
                : cadObject.ObjectName;
        }

        return value.GetType().Name;
    }

    private static string GetHandle(object value)
    {
        return value is IHandledCadObject handled
            ? handled.Handle.ToString("X", CultureInfo.InvariantCulture)
            : string.Empty;
    }
}
