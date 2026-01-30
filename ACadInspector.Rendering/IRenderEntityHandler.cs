using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public interface IRenderEntityHandler
{
    bool CanHandle(Entity entity);
    void Append(Entity entity, Transform transform, RenderBuildContext context);
}
