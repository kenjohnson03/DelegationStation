namespace DelegationStation.Services
{
    public class PageStateService
    {
        public TagPageState Tags { get; } = new TagPageState();
        public TagPageState SystemStatus { get; } = new TagPageState();
    }

    public class TagPageState
    {
        public int PageNumber { get; set; } = 1;
        public string SearchName { get; set; } = string.Empty;
    }
}
