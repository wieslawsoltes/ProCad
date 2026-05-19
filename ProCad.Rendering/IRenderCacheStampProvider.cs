using ACadSharp;

namespace ProCad.Rendering;

public interface IRenderCacheStampProvider
{
    long GetStamp(CadDocument document);
    long AdvanceStamp(CadDocument document);
}
