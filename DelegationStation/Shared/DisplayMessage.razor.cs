using Microsoft.AspNetCore.Components;

namespace DelegationStation.Shared
{
    public partial class DisplayMessage
    {
        [Parameter]
        public string Message { get; set; } = "";
        [Parameter]
        public EventCallback<string> MessageChanged { get; set; }
    }
}