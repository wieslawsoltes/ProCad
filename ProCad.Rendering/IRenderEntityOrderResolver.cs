using System.Collections.Generic;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ProCad.Rendering;

public interface IRenderEntityOrderResolver
{
    IReadOnlyList<Entity> OrderEntities(IEnumerable<Entity> entities, BlockRecord? block);
}
