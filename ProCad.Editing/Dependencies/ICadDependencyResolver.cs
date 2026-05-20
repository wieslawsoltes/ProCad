using ACadSharp.Entities;

namespace ProCad.Editing.Dependencies;

public interface ICadDependencyResolver
{
    CadDependencySet CollectForCopy(IEnumerable<Entity> entities);
}
