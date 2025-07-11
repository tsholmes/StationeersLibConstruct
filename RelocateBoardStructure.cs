using System.Collections.Generic;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using LaunchPadBooster.Networking;
using UnityEngine;

namespace LibConstruct
{
  public class RelocateBoardStructureInstance
  {
    public IPlacementBoardRelocatable Structure;
    public Grid3 Position;
    public int Rotation;

    public RelocateBoardStructureInstance(IPlacementBoardRelocatable structure, Grid3 position, int rotation)
    {
      this.Structure = structure;
      this.Position = position;
      this.Rotation = rotation;
    }

    public RelocateBoardStructureInstance(RelocateBoardStructureMessage message)
    {
      this.Structure = Thing.Find<IPlacementBoardRelocatable>(message.StructureId);
      this.Position = message.Position;
      this.Rotation = message.Rotation;
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
      this.StructureId = relocate.Structure.ReferenceId;
      this.Position = relocate.Position;
      this.Rotation = relocate.Rotation;
    }

    public override void Deserialize(RocketBinaryReader reader)
    {
      this.StructureId = reader.ReadInt64();
      this.Position = reader.ReadGrid3();
      this.Rotation = (int)reader.ReadByte();
    }

    public override void Serialize(RocketBinaryWriter writer)
    {
      writer.WriteInt64(this.StructureId);
      writer.WriteGrid3(this.Position);
      writer.WriteByte((byte)this.Rotation);
    }

    public override void Process(long hostId)
    {
      base.Process(hostId);

      var structure = Thing.Find<IPlacementBoardRelocatable>(this.StructureId);
      if (structure == null)
      {
        var ids = new List<long> { this.StructureId };
        this.WaitUntilFound(hostId, this.Process, this.Process, ids, 3f, "RelocateBoardStructure", true);
      }
      else
      {
        PlacementBoard.RelocateBoardStructure(new RelocateBoardStructureInstance(this));
      }
    }
  }
}