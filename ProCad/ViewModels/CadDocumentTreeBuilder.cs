using System;
using System.Collections.Generic;
using ACadSharp;
using ACadSharp.Classes;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using ACadSharp.Tables.Collections;

namespace ProCad.ViewModels;

internal static class CadDocumentTreeBuilder
{
    public static IReadOnlyList<CadDocumentTreeNode> Build(CadDocument document, string? displayName)
    {
        var rootChildren = new List<CadDocumentTreeNode>();

        if (document.Header is not null)
        {
            rootChildren.Add(CreateNode("Header", "Header", document.Header, Array.Empty<CadDocumentTreeNode>()));
        }

        if (document.SummaryInfo is not null)
        {
            rootChildren.Add(CreateNode("SummaryInfo", "Summary", document.SummaryInfo, Array.Empty<CadDocumentTreeNode>()));
        }

        rootChildren.Add(BuildClasses(document.Classes));
        rootChildren.Add(BuildTables(document));
        rootChildren.Add(BuildBlocks(document));
        rootChildren.Add(BuildEntities(document));
        rootChildren.Add(BuildCollections(document));
        rootChildren.Add(BuildDictionaries(document.RootDictionary));

        var rootName = string.IsNullOrWhiteSpace(displayName) ? "Document" : displayName;
        return new[]
        {
            CreateNode(rootName, "Document", document, rootChildren)
        };
    }

    private static CadDocumentTreeNode BuildClasses(DxfClassCollection? classes)
    {
        var children = new List<CadDocumentTreeNode>();
        if (classes is not null)
        {
            foreach (var entry in classes)
            {
                var name = string.IsNullOrWhiteSpace(entry.DxfName) ? entry.CppClassName : entry.DxfName;
                children.Add(CreateNode(name, "Class", entry, Array.Empty<CadDocumentTreeNode>()));
            }
        }

        SortByName(children);
        return CreateNode("Classes", "Collection", nameof(DxfClassCollection), null, children);
    }

    private static CadDocumentTreeNode BuildTables(CadDocument document)
    {
        var tables = new List<CadDocumentTreeNode>
        {
            BuildTable("AppIds", document.AppIds),
            BuildTable("Layers", document.Layers),
            BuildTable("LineTypes", document.LineTypes),
            BuildTable("TextStyles", document.TextStyles),
            BuildTable("DimensionStyles", document.DimensionStyles),
            BuildTable("Views", document.Views),
            BuildTable("UCSs", document.UCSs),
            BuildTable("VPorts", document.VPorts)
        };

        return CreateNode("Tables", "Group", "Tables", null, tables);
    }

    private static CadDocumentTreeNode BuildBlocks(CadDocument document)
    {
        var children = new List<CadDocumentTreeNode>();
        if (document.BlockRecords is not null)
        {
            foreach (var record in document.BlockRecords)
            {
                var entities = new List<CadDocumentTreeNode>();
                foreach (var entity in record.Entities)
                {
                    entities.Add(CreateNode(GetEntityName(entity), "Entity", entity, Array.Empty<CadDocumentTreeNode>()));
                }

                SortByName(entities);
                children.Add(CreateNode(record.Name, "Block", record, entities));
            }
        }

        SortByName(children);
        return CreateNode("Blocks", "Collection", nameof(BlockRecordsTable), null, children);
    }

    private static CadDocumentTreeNode BuildEntities(CadDocument document)
    {
        var modelSpaceEntities = GetBlockEntities(document, BlockRecord.ModelSpaceName);
        var paperSpaceEntities = GetBlockEntities(document, BlockRecord.PaperSpaceName);
        var children = new List<CadDocumentTreeNode>
        {
            BuildEntityCollection("ModelSpace", modelSpaceEntities),
            BuildEntityCollection("PaperSpace", paperSpaceEntities)
        };

        return CreateNode("Entities", "Group", "Entities", null, children);
    }

    private static CadDocumentTreeNode BuildEntityCollection(string name, IEnumerable<Entity>? entities)
    {
        var children = new List<CadDocumentTreeNode>();
        if (entities is not null)
        {
            foreach (var entity in entities)
            {
                children.Add(CreateNode(GetEntityName(entity), "Entity", entity, Array.Empty<CadDocumentTreeNode>()));
            }
        }

        SortByName(children);
        return CreateNode(name, "Collection", nameof(Entity), null, children);
    }

    private static CadDocumentTreeNode BuildCollections(CadDocument document)
    {
        var children = new List<CadDocumentTreeNode>
        {
            BuildCollection("Layouts", document.Layouts),
            BuildCollection("Groups", document.Groups),
            BuildCollection("Materials", document.Materials),
            BuildCollection("MLeaderStyles", document.MLeaderStyles),
            BuildCollection("MLineStyles", document.MLineStyles),
            BuildCollection("Scales", document.Scales),
            BuildCollection("TableStyles", document.TableStyles),
            BuildCollection("ImageDefinitions", document.ImageDefinitions),
            BuildCollection("PdfDefinitions", document.PdfDefinitions),
            BuildCollection("Colors", document.Colors),
            BuildCollection("DictionaryVariables", document.DictionaryVariables)
        };

        return CreateNode("Collections", "Group", "Collections", null, children);
    }

    private static CadDocumentTreeNode BuildDictionaries(CadDictionary? root)
    {
        var children = new List<CadDocumentTreeNode>();
        if (root is not null)
        {
            children.Add(BuildDictionary(root, root.Name));
        }

        return CreateNode("Dictionaries", "Group", nameof(CadDictionary), null, children);
    }

    private static CadDocumentTreeNode BuildDictionary(CadDictionary dictionary, string? displayName)
    {
        var children = new List<CadDocumentTreeNode>();
        foreach (var entryName in dictionary.EntryNames)
        {
            if (!dictionary.TryGetEntry<NonGraphicalObject>(entryName, out var entry) || entry is null)
            {
                continue;
            }

            if (entry is CadDictionary subDictionary)
            {
                children.Add(BuildDictionary(subDictionary, entryName));
            }
            else
            {
                children.Add(CreateNode(entryName, "DictionaryEntry", entry, Array.Empty<CadDocumentTreeNode>()));
            }
        }

        SortByName(children);
        var name = string.IsNullOrWhiteSpace(displayName) ? "Dictionary" : displayName;
        return CreateNode(name, "Dictionary", dictionary, children);
    }

    private static CadDocumentTreeNode BuildTable<T>(string name, IEnumerable<T>? table)
        where T : CadObject
    {
        var children = new List<CadDocumentTreeNode>();
        if (table is not null)
        {
            foreach (var entry in table)
            {
                children.Add(CreateNode(GetCadObjectName(entry), "TableEntry", entry, Array.Empty<CadDocumentTreeNode>()));
            }
        }

        SortByName(children);
        return CreateNode(name, "Table", typeof(T).Name, null, children);
    }

    private static CadDocumentTreeNode BuildCollection<T>(string name, IEnumerable<T>? items)
        where T : CadObject
    {
        var children = new List<CadDocumentTreeNode>();
        if (items is not null)
        {
            foreach (var entry in items)
            {
                children.Add(CreateNode(GetCadObjectName(entry), "Item", entry, Array.Empty<CadDocumentTreeNode>()));
            }
        }

        SortByName(children);
        return CreateNode(name, "Collection", typeof(T).Name, null, children);
    }

    private static CadDocumentTreeNode CreateNode(
        string name,
        string kind,
        object? source,
        IReadOnlyList<CadDocumentTreeNode> children)
    {
        var typeName = source?.GetType().Name ?? "Unknown";
        var handle = GetHandle(source);
        return new CadDocumentTreeNode(name, kind, typeName, handle, children, source);
    }

    private static CadDocumentTreeNode CreateNode(
        string name,
        string kind,
        string typeName,
        string? handle,
        IReadOnlyList<CadDocumentTreeNode> children)
    {
        return new CadDocumentTreeNode(name, kind, typeName, handle, children, null);
    }

    private static string GetCadObjectName(CadObject obj)
    {
        if (obj is TableEntry tableEntry && !string.IsNullOrWhiteSpace(tableEntry.Name))
        {
            return tableEntry.Name;
        }

        if (obj is Entity entity)
        {
            return GetEntityName(entity);
        }

        return obj.ObjectName;
    }

    private static string GetEntityName(Entity entity)
    {
        return string.IsNullOrWhiteSpace(entity.ObjectName)
            ? entity.GetType().Name
            : entity.ObjectName;
    }

    private static string? GetHandle(object? source)
    {
        return source is IHandledCadObject handled
            ? handled.Handle.ToString("X")
            : null;
    }

    private static IEnumerable<Entity>? GetBlockEntities(CadDocument document, string blockName)
    {
        if (document.BlockRecords is null)
        {
            return null;
        }

        return document.BlockRecords.TryGetValue(blockName, out var record)
            ? record.Entities
            : null;
    }

    private static void SortByName(List<CadDocumentTreeNode> nodes)
    {
        nodes.Sort(static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
    }
}
