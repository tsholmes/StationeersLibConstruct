
using System.Xml.Serialization;

namespace LibConstruct
{
  // Include in an IPlacementBoardHost SaveData for each board to save board state
  // Only the primary host includes the actual save data
  [XmlInclude(typeof(PlacementBoardHostSaveData))]
  public class PlacementBoardHostSaveData
  {
    [XmlElement]
    public long BoardId;
    [XmlElement]
    public long PrimaryHostId;
    [XmlElement]
    public PlacementBoardSaveData BoardSaveData;
  }

  // extend this to save board-specific state
  [XmlInclude(typeof(PlacementBoardSaveData))]
  public class PlacementBoardSaveData
  {
  }

  // include once in a board structure to properly parent on load
  [XmlInclude(typeof(PlacementBoardStructureSaveData))]
  public class PlacementBoardStructureSaveData
  {
    [XmlElement]
    public long BoardID;
  }
}