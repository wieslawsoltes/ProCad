using ACadSharp.Entities;

namespace ACadInspector.Editing.Dependencies;

public interface ICadDependencyResolver
{
    CadDependencySet CollectForCopy(IEnumerable<Entity> entities);
}
