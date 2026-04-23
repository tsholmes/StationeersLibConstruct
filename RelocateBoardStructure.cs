using System.Collections.Generic;
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

public class RelocateBoardStructureMessage : ModNetworkMessage<RelocateBoardStructureMessage>
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

  public override void Deserialize(RocketBinaryReader reader)
  {
    StructureId = reader.ReadInt64();
    Position = reader.ReadGrid3();
    Rotation = reader.ReadByte();
  }

  public override void Serialize(RocketBinaryWriter writer)
  {
    writer.WriteInt64(StructureId);
    writer.WriteGrid3(Position);
    writer.WriteByte((byte)Rotation);
  }

  public override void Process(long hostId)
  {
    base.Process(hostId);

    var structure = Thing.Find<IPlacementBoardRelocatable>(StructureId);
    if (structure == null)
    {
      var ids = new List<long> { StructureId };
      WaitUntilFound(hostId, Process, Process, ids, 3f, "RelocateBoardStructure", true);
    }
    else
      PlacementBoard.RelocateBoardStructure(new RelocateBoardStructureInstance(this));
  }
}