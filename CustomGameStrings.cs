using Assets.Scripts.Localization2;

namespace LibConstruct
{
  public static class CustomGameStrings
  {
    public static readonly GameString BoardStructureRelocateAction = GameString.Create(nameof(BoardStructureRelocateAction), "Relocate");
    public static readonly GameString BoardStructureRelocateDifferentBoard = GameString.Create(nameof(BoardStructureRelocateDifferentBoard), "Cannot relocate to a different board");
    public static readonly GameString BoardStructureNoBoard = GameString.Create(nameof(BoardStructureNoBoard), "Must be placed on board surface");
    public static readonly GameString BoardStructureBoundsOverflow = GameString.Create(nameof(BoardStructureBoundsOverflow), "Cannot place outside board bounds");
  }
}