using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using System.Net.Http;

namespace IntuneEnrollment
{
    public static class GetAllNewDevices
    {
        private static ILogger _logger;

        [FunctionName("GetAllNewDevices")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            _logger = log;

            _logger.LogInformation($"C# HTTP trigger function {fullMethodName} processed a request.");

            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            string responseMessage = string.IsNullOrEmpty(name)
                ? "This HTTP triggered function executed successfully. Pass a name in the query string or in the request body for a personalized response."
                : $"Hello, {name}. This HTTP triggered function executed successfully.";

            return new OkObjectResult(responseMessage);
        }

        private static async Task<String> GetAccessTokenAsync(string uri)
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            var AppSecret = Environment.GetEnvironmentVariable("AzureApp:ClientSecret", EnvironmentVariableTarget.Process);
            var AppId = Environment.GetEnvironmentVariable("AzureAd:ClientId", EnvironmentVariableTarget.Process);
            var TenantId = Environment.GetEnvironmentVariable("AzureAd:TenantId", EnvironmentVariableTarget.Process);
            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);

            if (String.IsNullOrEmpty(AppSecret) || String.IsNullOrEmpty(AppId) || String.IsNullOrEmpty(TenantId) || String.IsNullOrEmpty(TargetCloud))
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{fullMethodName} Error: Missing required environment variables. Please check the following environment variables are set:");
                sb.Append(String.IsNullOrEmpty(AppSecret) ? "AzureApp:ClientSecret\n" : "");
                sb.Append(String.IsNullOrEmpty(AppId) ? "AzureAd:ClientId\n" : "");
                sb.Append(String.IsNullOrEmpty(TenantId) ? "AzureAd:TenantId\n" : "");
                sb.Append(String.IsNullOrEmpty(TargetCloud) ? "AzureEnvironment\n" : "");
                _logger.LogError(sb.ToString());
                return null;
            }

            string tokenUri = "";
            if (TargetCloud == "AzurePublicCloud")
            {
                tokenUri = $"https://login.microsoftonline.com/{TenantId}/oauth2/token";
            }
            else
            {
                // TODO update URI
                tokenUri = $"https://login.microsoftonline.us/{TenantId}/oauth2/token";
            }

            // Get token for Log Analytics

            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUri);
            tokenRequest.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", AppId),
                new KeyValuePair<string, string>("client_secret", AppSecret),
                new KeyValuePair<string, string>("resource", uri)
            });

            try
            {
                var httpClient = new HttpClient();
                var tokenResponse = await httpClient.SendAsync(tokenRequest);
                var tokenContent = await tokenResponse.Content.ReadAsStringAsync();
                var tokenData = JsonConvert.DeserializeObject<dynamic>(tokenContent);
                return tokenData.access_token;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{fullMethodName} Error: getting access token for URI {tokenUri}: {ex.Message}");
                return null;
            }
        }
    }
}
