namespace DelegationStation.Services
{
    public class RecentUpdatesNotificationService
    {
        public event Action? OnUpdatesViewed;

        public void MarkAsViewed()
        {
            OnUpdatesViewed?.Invoke();
        }
    }
}
