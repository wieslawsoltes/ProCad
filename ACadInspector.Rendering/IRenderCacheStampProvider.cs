using ACadSharp;

namespace ACadInspector.Rendering;

public interface IRenderCacheStampProvider
{
    long GetStamp(CadDocument document);
    long AdvanceStamp(CadDocument document);
}
