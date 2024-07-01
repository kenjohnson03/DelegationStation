using DelegationStation.Shared;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace DelegationStation.Pages
{
    public partial class Roles
    {
        [CascadingParameter]
        public Task<AuthenticationState>? AuthState { get; set; }
        private System.Security.Claims.ClaimsPrincipal user = new System.Security.Claims.ClaimsPrincipal();
        private string userId = string.Empty;
        private string userName = string.Empty;

        private List<Role> roles = new List<Role>();
        private bool loading = true;

        private ConfirmMessage? ConfirmDelete;

        private string userMessage = "";
        private Role deleteRole = new Role() { Id = Guid.Empty };

        protected override async Task OnInitializedAsync()
        {
            if (AuthState is not null)
            {
                var authState = await AuthState;
                user = authState?.User ?? new System.Security.Claims.ClaimsPrincipal();
                userName = user.Claims.Where(c => c.Type == "name").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
                userId = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
            }

            Guid c = Guid.NewGuid();
            try
            {
                roles = await roleDBService.GetRolesAsync();
            }
            catch (Exception ex)
            {
                string message = $"Correlation Id: {c.ToString()}\nError retrieving roles.";
                logger.LogError(ex, message);
                userMessage = message;
            }
            finally
            {
                loading = false;
            }
        }

        private void RemoveRole(Role role)
        {
            Guid c = Guid.NewGuid();

            if (authorizationService.AuthorizeAsync(user, "DelegationStationAdmin").Result.Succeeded == false)
            {
                string message = $"Error deleting role {deleteRole.Name}.\nCorrelation Id: {c.ToString()}. Insufficient access.";
                logger.LogError($"{message}\nUser: {userName} {userId}");
                userMessage = message;
                return;
            }
            deleteRole = role;
            Show();
        }

        private void DeleteRole()
        {
            Guid c = Guid.NewGuid();
            if (deleteRole.Id == Guid.Empty)
            {
                return;
            }

            if (authorizationService.AuthorizeAsync(user, "DelegationStationAdmin").Result.Succeeded == false)
            {
                string message = $"Error deleting role {deleteRole.Name}.\nCorrelation Id: {c.ToString()}. Insufficient access.";
                logger.LogError($"{message}\nUser: {userName} {userId}");
                userMessage = message;
                return;
            }

            try
            {
                roleDBService.DeleteRoleAsync(deleteRole);
                roles.Remove(deleteRole);
                string message = $"Correlation Id: {c.ToString()}\nRole {deleteRole.Name} deleted successfully";
                userMessage = "";
                logger.LogInformation($"{message}\nUser: {userName} {userId}");
            }
            catch (Exception ex)
            {
                string message = $"Error deleting role {deleteRole.Name}.\nCorrelation Id: {c.ToString()}";
                logger.LogError(ex, $"{message}\nUser: {userName} {userId}");
                userMessage = message;
            }
            deleteRole = new Role() { Id = Guid.Empty };
            StateHasChanged();
        }

        private void Show()
        {
            ConfirmDelete?.Show();
        }
    }
}