namespace DelegationStation.Models
{
  public class BulkDeviceDownloadEntry
  {
    public string Make { get; set; }
    public string Model { get; set; }
    public string SerialNumber { get; set; }
    public string Action { get; set; }
    public string? AddedBy { get; set; }
  }
}
