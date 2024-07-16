using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace DelegationStation.Pages
{
    public partial class Claims
    {
        [CascadingParameter]
        public Task<AuthenticationState>? AuthState { get; set; }
        private AuthenticationState? authState;
        private List<string> roles = new List<string>();

        protected override async Task OnInitializedAsync()
        {
            if (AuthState != null)
            {
                authState = await AuthState;
            }
            UpdateClaims();
        }

        private void UpdateClaims()
        {
            if (authState == null)
            {
                return;
            }

            roles = new List<string>();
            foreach (var c in authState.User.Claims.Where(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" || c.Type == "roles"))
            {
                roles.Add(c.Value);
            }
        }
    }
}