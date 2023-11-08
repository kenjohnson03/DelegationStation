using DelegationStation.Pages;
using System.Net;
using System.Security.Claims;

namespace DelegationStation.Models
{
    public class ClaimsManager
    {
        public static List<string> GetRoles(IEnumerable<Claim>? claims)
        {
            List<string> roles = new List<string>();
            if(claims == null)
            {
                return roles;
            }

            foreach (Claim claim in claims)
            {
                if (claim.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" || claim.Type == "roles")
                {
                    roles.Add(claim.Value);
                }
            }
            return roles;
        }

        public static bool IsValidRequest(IEnumerable<Claim>? claims, string? iPAddress)
        {
            bool valid = false;
            bool claimNotAvailable = true;
            if (claims == null || string.IsNullOrEmpty(iPAddress))
            {
                return valid;
            }

            foreach (Claim claim in claims)
            {
                if (claim.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/ipaddress" || claim.Type == "ipaddr")
                {
                    claimNotAvailable = false;

                    if (claim.Value == iPAddress.ToString())
                    {
                        valid = true;
                    }
                }
            }
            return (valid || claimNotAvailable);
        }
    }
}
