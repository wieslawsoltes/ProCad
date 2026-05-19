using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

public interface IRenderEntityDispatcher
{
    void Append(Entity entity, Transform transform, RenderBuildContext context);
}
