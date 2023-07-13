using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Azure.Identity;
using Microsoft.Graph.Models.ExternalConnectors;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Azure.Cosmos;
using System.Linq;
using Microsoft.Graph.Chats.Item.SendActivityNotification;
using Microsoft.Graph.Models;
using System.Reflection;
using DelegationStationShared.Models;


namespace DelegationStation.Function
{
    public static class IntuneEnrollmentFunction
    {
        private static GraphServiceClient _graphClient;
        private static ILogger _logger;
        private static HttpClient _graphHttpClient;

        [FunctionName("IntuneEnrollmentTrigger")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "IntuneEnrollmentTrigger")] HttpRequest req,
            ILogger log)
        {
            _logger = log;
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogDebug($"RequestBody: \n{requestBody}");

            string response = "Completed execution";

            try
            {
                string logUri = GetLogAnalyticsUri(requestBody);
                List<string> deviceIds = await GetDeviceIdsFromLogAnalyticsAsync(logUri);
                List<DeviceResponse> devices = await GetDeviceManagementObjectsAsync(deviceIds.ToArray());

                //Query Cosmos DB
                foreach (DeviceResponse device in devices)
                {
                    await RunDeviceUpdateActionsAsync(device);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex}", ex);
            }
            return new OkObjectResult(response);
        }


        private static async Task RunDeviceUpdateActionsAsync(DeviceResponse device)
        {
            List<DeviceUpdateAction> actions = new List<DeviceUpdateAction>();
            var databaseName = "DelegationStation";
            var containerName = "Device";
            var account = Environment.GetEnvironmentVariable("CosmosDb:account", EnvironmentVariableTarget.Process);
            var key = Environment.GetEnvironmentVariable("CosmosDb:key", EnvironmentVariableTarget.Process);

            //Microsoft.Azure.Cosmos.CosmosClient client = new Microsoft.Azure.Cosmos.CosmosClient(account, key);
            CosmosClient client = new(
                connectionString: Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING", EnvironmentVariableTarget.Process)!
            );
            Microsoft.Azure.Cosmos.Container container = client.GetContainer(databaseName, containerName);

            _logger.LogInformation("Connected to Cosmos DB");

            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" AND c.Make = @manufacturer AND c.Model = @model AND c.SerialNumber = @serialNumber")
                .WithParameter("@manufacturer", device.Manufacturer.Trim())
                .WithParameter("@model", device.Model.Trim())
                .WithParameter("@serialNumber", device.SerialNumber.Trim());
            var queryIterator = container.GetItemQueryIterator<DelegationStationShared.Models.Device>(query);

            List<DelegationStationShared.Models.Device> deviceResults = new List<DelegationStationShared.Models.Device>();
            while(queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                deviceResults.AddRange(response.ToList());
            }
            _logger.LogInformation($"Found {deviceResults.Count}", deviceResults);

            if(deviceResults.Count < 1) 
            {
                // TODO make personal / add to group / update attribute
                await UpdateAttributesOnDeviceAsync(device.AzureADDeviceId, new List<DeviceUpdateAction> { new DeviceUpdateAction() { ActionType = DeviceUpdateActionType.Attribute, Name = "AccountEnabled", Value = "false" } }); 
                _logger.LogWarning($"Did not find any devices matching '{device.Manufacturer}' '{device.Model}' '{device.SerialNumber}'");
                return;
            }

            DelegationStationShared.Models.Device d = deviceResults.FirstOrDefault();
            foreach(string tagId in d.Tags)
            {
                DeviceTag tag = new DeviceTag();
                try
                {
                    ItemResponse<DeviceTag> tagResponse = await container.ReadItemAsync<DeviceTag>(tagId, new PartitionKey("DeviceTag"));
                    tag = tagResponse.Resource;
                    _logger.LogInformation($"Found tag {tag.Name}", tag);
                } 
                catch(Exception ex)
                {
                    _logger.LogError($"Get tag {tagId} failed.\n {ex.Message}", ex);
                }
                
                foreach(DeviceUpdateAction deviceUpdateAction in tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Group))
                {
                    await AddDeviceToAzureADGroup(device.AzureADDeviceId, deviceUpdateAction.Value);
                }

                await UpdateAttributesOnDeviceAsync(device.AzureADDeviceId, tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Attribute).ToList());
            }

            return;
        }

        private static async Task UpdateAttributesOnDeviceAsync(string deviceId, List<DeviceUpdateAction> updateActions)
        {
            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);
            _logger.LogInformation($"Updating attributes on {deviceId}");
            string tokenUri = "";
            if (TargetCloud == "AzurePublicCloud")
            {
                tokenUri = "https://graph.microsoft.com";
            }
            else
            {
                // TODO update URI
                tokenUri = "https://graph.microsoft.us";
            }

            var graphAccessToken = await GetAccessTokenAsync(tokenUri);

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);


            foreach (var action in updateActions)
            {
                string attributeContent = "";
                _logger.LogDebug($"UpdateAttributesOnDeviceAsync Device: {deviceId}\nAction: {action.Name}\nValue: {action.Value}");

                switch (action.Name)
                {
                    case "ExtensionAttribute1":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute1 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute2":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute2 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute3":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute3 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute4":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object> 
                        { 
                            { 
                                "extensionAttributes", new 
                                { 
                                    extensionAttribute4 = action.Value, 
                                } 
                            }, 
                        });
                        break;
                    case "ExtensionAttribute5":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute5 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute6":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute6 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute7":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute7 = action.Value,
                                }
                            },
                        });
                        break;
                    //case for extension attributes 8-15
                    case "ExtensionAttribute8":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute8 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute9":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute9 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute10":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                            {
                                {
                                "extensionAttributes", new
                                {
                                    extensionAttribute10 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute11":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute11 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute12":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute12 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute13":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute13 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute14":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute14 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute15":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute15 = action.Value,
                                }
                            },
                        });
                        break;
                    case "AccountEnabled":
                        attributeContent = JsonConvert.SerializeObject(new
                        {
                            accountEnabled = action.Value
                        });
                        break;
                    default:
                        _logger.LogWarning($"Property update called on a property not implemented. Property - {action.Name}");
                        
                        break;
                }
                string deviceADRequestUri = String.Format("{0}/v1.0/devices(deviceId='{1}')", tokenUri, deviceId);

                var deviceADRequest = new HttpRequestMessage(HttpMethod.Patch, deviceADRequestUri)
                {
                    Content = new StringContent(attributeContent, System.Text.Encoding.UTF8, "application/json")
                };
                deviceADRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
                HttpResponseMessage deviceADResponse = await httpClient.SendAsync(deviceADRequest);
                if(deviceADResponse.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Graph Response Success: {deviceADResponse.IsSuccessStatusCode}\nStatus Code: {deviceADResponse.StatusCode}\nReason: {deviceADResponse.ReasonPhrase}\nRequest URI: {deviceADRequestUri}\nAttributeContent: {attributeContent}");
                }
                else
                {
                    _logger.LogError($"Graph Response Error: {deviceADResponse.IsSuccessStatusCode}\nStatus Code: {deviceADResponse.StatusCode}\nReason: {deviceADResponse.ReasonPhrase}\nRequest URI: {deviceADRequestUri}\nAttributeContent: {attributeContent}");
                }
            }           

            return;
        }

        private static string GetLogAnalyticsUri(string requestBody)
        {
            string logAnalyticsRegexPattern = @"\""linkToSearchResultsAPI\"":\s?\""(\S+)\""";
            Match logAnalyticsMatch = Regex.Match(requestBody, logAnalyticsRegexPattern);
            string logUri = "";
            if (logAnalyticsMatch.Success)
            {
                logUri = logAnalyticsMatch.Groups[1].Value;
                _logger.LogInformation($"Log Analytics Uri Used: {logUri}");
            }
            else
            {
                _logger.LogInformation($"Error: Unable to find Log Analytics Uri:\n{requestBody}");
            }
            return logUri;
        }

        private static async Task<List<String>> GetDeviceIdsFromLogAnalyticsAsync(string logUri)
        {
            var httpClient = new HttpClient();
            var accessToken = await GetAccessTokenAsync("https://api.loganalytics.io");
            List<String> deviceIds = new List<String>();

            var logAnalyticsRequest = new HttpRequestMessage(HttpMethod.Get, logUri);
            logAnalyticsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var logAnalyticsResponse = await httpClient.SendAsync(logAnalyticsRequest);
            var logAContent = await logAnalyticsResponse.Content.ReadAsStringAsync();

            Regex regex = new Regex(@"\b[A-Fa-f0-9]{8}(?:-[A-Fa-f0-9]{4}){3}-[A-Fa-f0-9]{12}\b");
            foreach (Match m in regex.Matches(logAContent))
            {
                deviceIds.Add(m.Value);
                _logger.LogInformation($"Device Id found in LogAnalyticsQuery: {m.Value}");
            }
            return deviceIds;
        }

        private static async Task AddDeviceToAzureADGroup(string deviceId, string groupId)
        {
            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);

            string tokenUri = "";
            if (TargetCloud == "AzurePublicCloud")
            {
                tokenUri = "https://graph.microsoft.com";
            }
            else
            {
                // TODO update URI
                tokenUri = "https://graph.microsoft.us";
            }

            var graphAccessToken = await GetAccessTokenAsync(tokenUri);
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);


            string deviceADRequestUri = $"{tokenUri}/v1.0/devices?$filter=deviceId eq '{deviceId}'";
            var deviceADRequest = new HttpRequestMessage(HttpMethod.Get, deviceADRequestUri);
            deviceADRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
            HttpResponseMessage deviceADResponse = await httpClient.SendAsync(deviceADRequest);
            deviceADResponse.EnsureSuccessStatusCode();
            string deviceADResponseContent = await deviceADResponse.Content.ReadAsStringAsync();
            Regex regex = new Regex(@"\b[A-Fa-f0-9]{8}(?:-[A-Fa-f0-9]{4}){3}-[A-Fa-f0-9]{12}\b");
            string deviceAzureAdId = "";
            Match deviceMatch = regex.Match(deviceADResponseContent);
            if (deviceMatch.Success)
            {
                deviceAzureAdId = deviceMatch.Value;
            }

            var groupRequest = new HttpRequestMessage(HttpMethod.Post, $"{tokenUri}/v1.0/groups/{groupId}/members/$ref");
            groupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);

            var values = new Dictionary<string, string>{
                { "@odata.id", $"{tokenUri}/v1.0/directoryObjects/{deviceAzureAdId}" }
            };

            var json = JsonConvert.SerializeObject(values, Formatting.Indented);

            var stringContent = new StringContent(json, System.Text.Encoding.UTF8, "text/plain");

            _logger.LogInformation($"Adding Device:{deviceId} to Group:{groupId}");

            stringContent.Headers.Remove("Content-Type");
            stringContent.Headers.Add("Content-Type", "application/json");
            groupRequest.Content = stringContent;

            var groupResponse = await httpClient.SendAsync(groupRequest);
            string response = await groupResponse.Content.ReadAsStringAsync();
            response = String.IsNullOrEmpty(response) ? "Success" : response;
            _logger.LogInformation($"Group Add Response: {response}");
        }

        private static async Task AddDeviceToAzureAdministrativeUnit (string deviceId, string auId)
        {
            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);

            string tokenUri = "";
            if (TargetCloud == "AzurePublicCloud")
            {
                tokenUri = "https://graph.microsoft.com";
            }
            else
            {
                // TODO update URI
                tokenUri = "https://graph.microsoft.us";
            }

            var graphAccessToken = await GetAccessTokenAsync(tokenUri);
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);


            string deviceADRequestUri = $"{tokenUri}/v1.0/devices?$filter=deviceId eq '{deviceId}'";
            var deviceADRequest = new HttpRequestMessage(HttpMethod.Get, deviceADRequestUri);
            deviceADRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
            HttpResponseMessage deviceADResponse = await httpClient.SendAsync(deviceADRequest);
            deviceADResponse.EnsureSuccessStatusCode();
            string deviceADResponseContent = await deviceADResponse.Content.ReadAsStringAsync();
            Regex regex = new Regex(@"\b[A-Fa-f0-9]{8}(?:-[A-Fa-f0-9]{4}){3}-[A-Fa-f0-9]{12}\b");
            string deviceAzureAdId = "";
            Match deviceMatch = regex.Match(deviceADResponseContent);
            if (deviceMatch.Success)
            {
                deviceAzureAdId = deviceMatch.Value;
            }

            var groupRequest = new HttpRequestMessage(HttpMethod.Post, $"{tokenUri}/v1.0/groups/{groupId}/members/$ref");
            groupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);

            var values = new Dictionary<string, string>{
                { "@odata.id", $"{tokenUri}/v1.0/directoryObjects/{deviceAzureAdId}" }
            };

            var json = JsonConvert.SerializeObject(values, Formatting.Indented);

            var stringContent = new StringContent(json, System.Text.Encoding.UTF8, "text/plain");

            _logger.LogInformation($"Adding Device:{deviceId} to Group:{groupId}");

            stringContent.Headers.Remove("Content-Type");
            stringContent.Headers.Add("Content-Type", "application/json");
            groupRequest.Content = stringContent;

            var groupResponse = await httpClient.SendAsync(groupRequest);
            string response = await groupResponse.Content.ReadAsStringAsync();
            response = String.IsNullOrEmpty(response) ? "Success" : response;
            _logger.LogInformation($"Group Add Response: {response}");
        }

        private static async Task<List<DeviceResponse>> GetDeviceManagementObjectsAsync(string[] deviceIds)
        {
            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);

            string tokenUri = "";
            if (TargetCloud == "AzurePublicCloud")
            {
                tokenUri = "https://graph.microsoft.com";
            }
            else
            {
                // TODO update URI
                tokenUri = "https://graph.microsoft.us";
            }

            var graphAccessToken = await GetAccessTokenAsync(tokenUri);
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
            List<DeviceResponse> devices = new List<DeviceResponse>();


            foreach (string d in deviceIds)
            {
                var graphResponse = await httpClient.GetAsync($"{tokenUri}/v1.0/deviceManagement/managedDevices/{d}?$select=id,manufacturer,model,serialNumber,azureADDeviceId");
                graphResponse.EnsureSuccessStatusCode();

                var graphContent = await graphResponse.Content.ReadAsStringAsync();
                DeviceResponse device = JsonConvert.DeserializeObject<DeviceResponse>(graphContent);
                devices.Add(device);
                _logger.LogInformation($"Intune device information\nDeviceId: {device.Id}\nMake: {device.Manufacturer}\nModel: {device.Model}\nSerialNumber: {device.SerialNumber}\nAzureADId: {device.AzureADDeviceId}");

            }
            return devices;
        }

        private static async Task<String> GetAccessTokenAsync(string uri)
        {
            var AppSecret = Environment.GetEnvironmentVariable("AzureApp:ClientSecret", EnvironmentVariableTarget.Process);
            var AppId = Environment.GetEnvironmentVariable("AzureAd:ClientId", EnvironmentVariableTarget.Process);
            var TenantId = Environment.GetEnvironmentVariable("AzureAd:TenantId", EnvironmentVariableTarget.Process);
            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);

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

            var httpClient = new HttpClient();
            var tokenResponse = await httpClient.SendAsync(tokenRequest);

            var tokenContent = await tokenResponse.Content.ReadAsStringAsync();
            var tokenData = JsonConvert.DeserializeObject<dynamic>(tokenContent);

            return tokenData.access_token;
        }

        private class DeviceResponse
        {
            [JsonProperty("manufacturer")]
            public string Manufacturer { get; set; }

            [JsonProperty("model")]
            public string Model { get; set; }

            [JsonProperty("serialNumber")]
            public string SerialNumber { get; set; }

            [JsonProperty("azureADDeviceId")]
            public string AzureADDeviceId { get; set; }

            [JsonProperty("id")]
            public string Id { get; set; }
        }
    }
}
