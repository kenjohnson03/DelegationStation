using Microsoft.AspNetCore.Components;

namespace DelegationStation.Shared
{
    public partial class ConfirmMessage
    {
        [Parameter]
        public string MessageTitle { get; set; } = "Are you sure?";
        [Parameter]
        public MarkupString MessageBody { get; set; } = (MarkupString)"This action is permanent";
        [Parameter]
        public Action? ConfirmAction { get; set; }
        [Parameter]
        public Action? CancelAction { get; set; }

        private string backDrop = "hideModal";

        private void Close()
        {
            backDrop = "hideModal";
            CancelAction?.Invoke();
        }

        private void Confirm()
        {
            backDrop = "hideModal";
            ConfirmAction?.Invoke();
        }

        public void Show()
        {
            backDrop = "show showModal modalBackdrop";
        }
    }
}