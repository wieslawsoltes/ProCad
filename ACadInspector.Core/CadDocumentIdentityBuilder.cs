using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ACadSharp;
using ACadSharp.Classes;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using ACadSharp.Tables.Collections;

namespace ACadInspector.Core;

public static class CadDocumentIdentityBuilder
{
    public static IReadOnlyList<CadDocumentIdentityEntry> Build(CadDocument document)
    {
        var entries = new List<CadDocumentIdentityEntry>(512);

        if (document.Header is not null)
        {
            entries.Add(CreateEntry("Header", "Header", document.Header));
        }

        if (document.SummaryInfo is not null)
        {
            entries.Add(CreateEntry("SummaryInfo", "Summary", document.SummaryInfo));
        }

        AddClasses(entries, document.Classes);
        AddTables(entries, document);
        AddBlocks(entries, document.BlockRecords);
        AddCollections(entries, document);
        AddDictionaries(entries, document.RootDictionary);

        return entries;
    }

    private static void AddClasses(List<CadDocumentIdentityEntry> entries, DxfClassCollection? classes)
    {
        if (classes is null)
        {
            return;
        }

        var index = 0;
        foreach (var entry in classes)
        {
            var name = string.IsNullOrWhiteSpace(entry.DxfName) ? entry.CppClassName : entry.DxfName;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Class{index.ToString(CultureInfo.InvariantCulture)}";
            }

            entries.Add(CreateEntry(BuildPath("Classes", name), "Class", entry));
            index++;
        }
    }

    private static void AddTables(List<CadDocumentIdentityEntry> entries, CadDocument document)
    {
        AddTable(entries, "AppIds", document.AppIds);
        AddTable(entries, "Layers", document.Layers);
        AddTable(entries, "LineTypes", document.LineTypes);
        AddTable(entries, "TextStyles", document.TextStyles);
        AddTable(entries, "DimensionStyles", document.DimensionStyles);
        AddTable(entries, "Views", document.Views);
        AddTable(entries, "UCSs", document.UCSs);
        AddTable(entries, "VPorts", document.VPorts);
    }

    private static void AddTable<T>(List<CadDocumentIdentityEntry> entries, string tableName, IEnumerable<T>? table)
        where T : CadObject
    {
        if (table is null)
        {
            return;
        }

        var index = 0;
        foreach (var entry in table)
        {
            var name = GetNamedObjectSegment(entry, index);
            var path = BuildPath("Tables", tableName, name);
            entries.Add(CreateEntry(path, "TableEntry", entry));
            index++;
        }
    }

    private static void AddBlocks(List<CadDocumentIdentityEntry> entries, BlockRecordsTable? blocks)
    {
        if (blocks is null)
        {
            return;
        }

        foreach (var record in blocks)
        {
            var blockName = string.IsNullOrWhiteSpace(record.Name) ? record.ObjectName : record.Name;
            if (string.IsNullOrWhiteSpace(blockName))
            {
                blockName = record.GetType().Name;
            }
            var blockPath = BuildPath("Blocks", blockName);
            entries.Add(CreateEntry(blockPath, "Block", record));

            var entityIndex = 0;
            foreach (var entity in record.Entities)
            {
                var entityName = GetEntitySegment(entity);
                var path = BuildPath(blockPath, "Entities", FormatIndex(entityIndex, entityName));
                entries.Add(CreateEntry(path, "Entity", entity));
                entityIndex++;
            }
        }
    }

    private static void AddCollections(List<CadDocumentIdentityEntry> entries, CadDocument document)
    {
        AddCollection(entries, "Layouts", document.Layouts);
        AddCollection(entries, "Groups", document.Groups);
        AddCollection(entries, "Materials", document.Materials);
        AddCollection(entries, "MLeaderStyles", document.MLeaderStyles);
        AddCollection(entries, "MLineStyles", document.MLineStyles);
        AddCollection(entries, "Scales", document.Scales);
        AddCollection(entries, "TableStyles", document.TableStyles);
        AddCollection(entries, "ImageDefinitions", document.ImageDefinitions);
        AddCollection(entries, "PdfDefinitions", document.PdfDefinitions);
        AddCollection(entries, "Colors", document.Colors);
        AddCollection(entries, "DictionaryVariables", document.DictionaryVariables);
    }

    private static void AddCollection<T>(List<CadDocumentIdentityEntry> entries, string name, IEnumerable<T>? items)
        where T : CadObject
    {
        if (items is null)
        {
            return;
        }

        var index = 0;
        foreach (var entry in items)
        {
            var segment = GetNamedObjectSegment(entry, index);
            var path = BuildPath("Collections", name, segment);
            entries.Add(CreateEntry(path, "CollectionItem", entry));
            index++;
        }
    }

    private static void AddDictionaries(List<CadDocumentIdentityEntry> entries, CadDictionary? root)
    {
        if (root is null)
        {
            return;
        }

        var rootName = string.IsNullOrWhiteSpace(root.Name) ? "Root" : root.Name;
        AddDictionary(entries, root, BuildPath("Dictionaries", rootName));
    }

    private static void AddDictionary(List<CadDocumentIdentityEntry> entries, CadDictionary dictionary, string path)
    {
        entries.Add(CreateEntry(path, "Dictionary", dictionary));

        foreach (var entryName in dictionary.EntryNames)
        {
            if (!dictionary.TryGetEntry<NonGraphicalObject>(entryName, out var entry) || entry is null)
            {
                continue;
            }

            var entryPath = BuildPath(path, entryName);
            if (entry is CadDictionary subDictionary)
            {
                AddDictionary(entries, subDictionary, entryPath);
            }
            else
            {
                entries.Add(CreateEntry(entryPath, "DictionaryEntry", entry));
            }
        }
    }

    private static CadDocumentIdentityEntry CreateEntry(string path, string kind, object value)
    {
        var typeName = value.GetType().Name;
        return new CadDocumentIdentityEntry(path, kind, typeName, value);
    }

    private static string GetNamedObjectSegment(CadObject obj, int index)
    {
        if (obj is TableEntry tableEntry && !string.IsNullOrWhiteSpace(tableEntry.Name))
        {
            return tableEntry.Name;
        }

        if (obj is INamedCadObject named && !string.IsNullOrWhiteSpace(named.Name))
        {
            return named.Name;
        }

        var objectName = string.IsNullOrWhiteSpace(obj.ObjectName) ? obj.GetType().Name : obj.ObjectName;
        return FormatIndex(index, objectName);
    }

    private static string GetEntitySegment(Entity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.ObjectName))
        {
            return entity.ObjectName;
        }

        return entity.GetType().Name;
    }

    private static string FormatIndex(int index, string name)
    {
        var formatted = index.ToString("D6", CultureInfo.InvariantCulture);
        var safeName = string.IsNullOrWhiteSpace(name) ? "Item" : name;
        return $"{formatted}:{safeName}";
    }

    private static string BuildPath(params string[] segments)
    {
        return string.Join('/', segments.Select(SanitizeSegment));
    }

    private static string SanitizeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return "Unknown";
        }

        return segment.Replace('/', '|');
    }
}
