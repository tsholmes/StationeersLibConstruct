
using Assets.Scripts.Objects;

namespace LibConstruct
{
  public abstract class PlacementBoardStructure : Structure
  {
    // TODO

    public override void Awake()
    {
      base.Awake();
      this.PlacementType = (PlacementSnap)(-1); // skip normal placement behavior
    }
  }
}