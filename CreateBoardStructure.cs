
using System.Collections.Generic;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using LaunchPadBooster.Networking;
using UnityEngine;

namespace LibConstruct
{
  public class CreateBoardStructureInstance
  {
    public MultiConstructor Constructor;
    public PlacementBoard Board;
    public IPlacementBoardStructure StructurePrefab;
    public Grid3 Position;
    public int Rotation;
    public int CustomColor = -1;
    public ulong OwnerClientId;
    public bool AuthoringMode;

    public CreateBoardStructureInstance(
      MultiConstructor constructor,
      IPlacementBoardStructure prefabToCreate,
      PlacementBoard board,
      Grid3 position,
      int rotation,
      ulong ownerClientId,
      bool authoringMode,
      int colorIndex = -1
    )
    {
      this.Constructor = constructor;
      this.Board = board;
      this.StructurePrefab = prefabToCreate;
      this.Position = position;
      this.Rotation = rotation;
      this.OwnerClientId = ownerClientId;
      this.CustomColor = colorIndex;
      this.AuthoringMode = authoringMode;
    }

    public CreateBoardStructureInstance(CreateBoardStructureMessage message)
    {
      this.Constructor = message.AuthoringMode ?
        Prefab.Find<MultiConstructor>((int)message.ConstructorId) :
        Thing.Find<MultiConstructor>(message.ConstructorId);
      this.Board = PlacementBoard.FindExisting(message.BoardID, message.BoardHostID);
      this.StructurePrefab = (IPlacementBoardStructure)Prefab.Find<Structure>(message.PrefabHash);
      this.Position = message.Position;
      this.Rotation = message.Rotation;
      this.OwnerClientId = message.OwnerClientId;
      this.CustomColor = message.CustomColor;
      this.AuthoringMode = message.AuthoringMode;
    }

    public Vector3 WorldPosition => this.Board.GridToWorld(this.Position);
    public Quaternion WorldRotation => this.Board.IndexToRotation(this.Rotation);
  }

  public class CreateBoardStructureMessage : ModNetworkMessage<CreateBoardStructureMessage>
  {
    public long ConstructorId;
    public long BoardID;
    public long BoardHostID;
    public int PrefabHash;
    public Grid3 Position;
    public int Rotation;
    public int CustomColor;
    public ulong OwnerClientId;
    public bool AuthoringMode;

    public CreateBoardStructureMessage() { }

    public CreateBoardStructureMessage(CreateBoardStructureInstance create)
    {
      var prefabStructure = (Structure)create.StructurePrefab;
      this.ConstructorId = create.AuthoringMode ? create.Constructor.PrefabHash : create.Constructor.ReferenceId;
      this.BoardID = create.Board.ID;
      this.BoardHostID = create.Board.PrimaryHost.ReferenceId;
      this.PrefabHash = prefabStructure.PrefabHash;
      this.Position = create.Position;
      this.Rotation = create.Rotation;
      this.CustomColor = create.CustomColor;
      this.OwnerClientId = create.OwnerClientId;
      this.AuthoringMode = create.AuthoringMode;
    }

    public override void Deserialize(RocketBinaryReader reader)
    {
      this.ConstructorId = reader.ReadInt64();
      this.BoardID = reader.ReadInt64();
      this.BoardHostID = reader.ReadInt64();
      this.PrefabHash = reader.ReadInt32();
      this.Position = reader.ReadGrid3();
      this.Rotation = reader.ReadSByte();
      this.CustomColor = reader.ReadInt32();
      this.OwnerClientId = reader.ReadUInt64();
      this.AuthoringMode = reader.ReadBoolean();
    }

    public override void Serialize(RocketBinaryWriter writer)
    {
      writer.WriteInt64(this.ConstructorId);
      writer.WriteInt64(this.BoardID);
      writer.WriteInt64(this.BoardHostID);
      writer.WriteInt32(this.PrefabHash);
      writer.WriteGrid3(this.Position);
      writer.WriteSByte((sbyte)this.Rotation);
      writer.WriteInt32(this.CustomColor);
      writer.WriteUInt64(this.OwnerClientId);
      writer.WriteBoolean(this.AuthoringMode);
    }

    public override void Process(long hostId)
    {
      base.Process(hostId);

      var constructor = this.AuthoringMode ?
        Prefab.Find<MultiConstructor>((int)this.ConstructorId) :
        Thing.Find<MultiConstructor>(this.ConstructorId);
      var host = Thing.Find<IPlacementBoardHost>(this.BoardHostID);
      if (constructor == null || host == null)
      {
        var ids = new List<long> { this.BoardHostID };
        if (!this.AuthoringMode)
          ids.Add(this.ConstructorId);
        this.WaitUntilFound(hostId, this.Process, this.Process, ids, 3f, "CreateBoardStructure", true);
      }
      else
      {
        PlacementBoard.CreateBoardStructure(new CreateBoardStructureInstance(this));
      }
    }
  }
}