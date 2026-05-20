using ACadSharp;
using ACadSharp.Classes;

namespace ProCad.ViewModels;

public sealed class CadDwgClassRowViewModel
{
    public CadDwgClassRowViewModel(DxfClass dxfClass)
    {
        DxfName = dxfClass.DxfName ?? string.Empty;
        CppClassName = dxfClass.CppClassName ?? string.Empty;
        ApplicationName = dxfClass.ApplicationName ?? string.Empty;
        ClassNumber = dxfClass.ClassNumber;
        DwgVersion = dxfClass.DwgVersion;
        ProxyFlags = dxfClass.ProxyFlags;
        IsAnEntity = dxfClass.IsAnEntity;
        WasZombie = dxfClass.WasZombie;
        InstanceCount = dxfClass.InstanceCount;
        ItemClassId = dxfClass.ItemClassId;
    }

    public string DxfName { get; }

    public string CppClassName { get; }

    public string ApplicationName { get; }

    public short ClassNumber { get; }

    public ACadVersion DwgVersion { get; }

    public ProxyFlags ProxyFlags { get; }

    public bool IsAnEntity { get; }

    public bool WasZombie { get; }

    public int InstanceCount { get; }

    public short ItemClassId { get; }
}
