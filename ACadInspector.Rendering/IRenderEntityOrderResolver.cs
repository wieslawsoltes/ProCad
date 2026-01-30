using System.Collections.Generic;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

public interface IRenderEntityOrderResolver
{
    IReadOnlyList<Entity> OrderEntities(IEnumerable<Entity> entities, BlockRecord? block);
}
