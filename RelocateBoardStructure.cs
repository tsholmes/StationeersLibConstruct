using Assets.Scripts.GridSystem;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using LaunchPadBooster.Networking;

namespace LibConstruct;

public class RelocateBoardStructureInstance
{
  public IPlacementBoardRelocatable Structure;
  public Grid3 Position;
  public int Rotation;

  public RelocateBoardStructureInstance(IPlacementBoardRelocatable structure, Grid3 position, int rotation)
  {
    Structure = structure;
    Position = position;
    Rotation = rotation;
  }

  public RelocateBoardStructureInstance(RelocateBoardStructureMessage message)
  {
    Structure = Thing.Find<IPlacementBoardRelocatable>(message.StructureId);
    Position = message.Position;
    Rotation = message.Rotation;
  }
}

public class RelocateBoardStructureMessage : INetworkMessage
{
  public long StructureId;
  public Grid3 Position;
  public int Rotation;

  public RelocateBoardStructureMessage() { }

  public RelocateBoardStructureMessage(RelocateBoardStructureInstance relocate)
  {
    StructureId = relocate.Structure.ReferenceId;
    Position = relocate.Position;
    Rotation = relocate.Rotation;
  }

  public void Deserialize(RocketBinaryReader reader)
  {
    StructureId = reader.ReadInt64();
    Position = reader.ReadGrid3();
    Rotation = reader.ReadByte();
  }

  public void Serialize(RocketBinaryWriter writer)
  {
    writer.WriteInt64(StructureId);
    writer.WriteGrid3(Position);
    writer.WriteByte((byte)Rotation);
  }

  public void Process(long clientId)
  {
    var structure = Thing.Find<IPlacementBoardRelocatable>(StructureId);
    if (structure != null)
      PlacementBoard.RelocateBoardStructure(new RelocateBoardStructureInstance(this));
  }
}