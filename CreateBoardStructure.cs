
using Assets.Scripts.GridSystem;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using LaunchPadBooster.Networking;
using UnityEngine;

namespace LibConstruct;

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
    int colorIndex = -1)
  {
    Constructor = constructor;
    Board = board;
    StructurePrefab = prefabToCreate;
    Position = position;
    Rotation = rotation;
    OwnerClientId = ownerClientId;
    CustomColor = colorIndex;
    AuthoringMode = authoringMode;
  }

  public CreateBoardStructureInstance(CreateBoardStructureMessage message)
  {
    Constructor = message.AuthoringMode ?
      Prefab.Find<MultiConstructor>((int)message.ConstructorId) :
      Thing.Find<MultiConstructor>(message.ConstructorId);
    Board = PlacementBoard.FindExisting(message.BoardID, message.BoardHostID);
    StructurePrefab = (IPlacementBoardStructure)Prefab.Find<Structure>(message.PrefabHash);
    Position = message.Position;
    Rotation = message.Rotation;
    OwnerClientId = message.OwnerClientId;
    CustomColor = message.CustomColor;
    AuthoringMode = message.AuthoringMode;
  }

  public Vector3 WorldPosition => Board.GridToWorld(Position);
  public Quaternion WorldRotation => Board.IndexToRotation(Rotation);
}

public class CreateBoardStructureMessage : INetworkMessage
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
    ConstructorId = create.AuthoringMode ? create.Constructor.PrefabHash : create.Constructor.ReferenceId;
    BoardID = create.Board.ID;
    BoardHostID = create.Board.PrimaryHost.ReferenceId;
    PrefabHash = prefabStructure.PrefabHash;
    Position = create.Position;
    Rotation = create.Rotation;
    CustomColor = create.CustomColor;
    OwnerClientId = create.OwnerClientId;
    AuthoringMode = create.AuthoringMode;
  }

  public void Deserialize(RocketBinaryReader reader)
  {
    ConstructorId = reader.ReadInt64();
    BoardID = reader.ReadInt64();
    BoardHostID = reader.ReadInt64();
    PrefabHash = reader.ReadInt32();
    Position = reader.ReadGrid3();
    Rotation = reader.ReadSByte();
    CustomColor = reader.ReadInt32();
    OwnerClientId = reader.ReadUInt64();
    AuthoringMode = reader.ReadBoolean();
  }

  public void Serialize(RocketBinaryWriter writer)
  {
    writer.WriteInt64(ConstructorId);
    writer.WriteInt64(BoardID);
    writer.WriteInt64(BoardHostID);
    writer.WriteInt32(PrefabHash);
    writer.WriteGrid3(Position);
    writer.WriteSByte((sbyte)Rotation);
    writer.WriteInt32(CustomColor);
    writer.WriteUInt64(OwnerClientId);
    writer.WriteBoolean(AuthoringMode);
  }

  public void Process(long clientId)
  {
    var constructor = AuthoringMode ?
      Prefab.Find<MultiConstructor>((int)ConstructorId) :
      Thing.Find<MultiConstructor>(ConstructorId);
    var host = Thing.Find<IPlacementBoardHost>(BoardHostID);
    if (constructor != null && host != null)
      PlacementBoard.CreateBoardStructure(new CreateBoardStructureInstance(this));
  }
}