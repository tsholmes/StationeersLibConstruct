
using System.Collections.Generic;
using UnityEngine;

namespace LibConstruct
{
  public interface IPlacementBoardHost : IReferencable
  {
    // TODO
    public IEnumerable<BoxCollider> CollidersForBoard(PlacementBoard board);
  }
}