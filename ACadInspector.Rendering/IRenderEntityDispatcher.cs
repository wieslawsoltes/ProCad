using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public interface IRenderEntityDispatcher
{
    void Append(Entity entity, Transform transform, RenderBuildContext context);
}
