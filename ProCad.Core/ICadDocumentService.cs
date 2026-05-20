using System.IO;
using ACadSharp;
using ACadSharp.IO;

namespace ProCad.Core;

public interface ICadDocumentService
{
    CadDocument Load(string path, CadReadOptions options, NotificationEventHandler? notification = null);
    CadDocument Load(Stream stream, CadReadOptions options, NotificationEventHandler? notification = null);
    void Save(string path, CadDocument document, CadWriteOptions options, NotificationEventHandler? notification = null);
    void Save(Stream stream, CadDocument document, CadWriteOptions options, NotificationEventHandler? notification = null);
}
