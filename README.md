# DelegationStation


## Environment variables
Use the following environment variables to configure the application. These can be set in the `appsettings.json` file or in the Azure App Service configuration.

<b>"AzureAd:TenantId" : ""</b><br/>
Can be found in the Azure Portal under Azure Active Directory -> Properties -> Directory ID
Can also be found in the Azure Portal under the App Registration -> Overview -> Directory ID

<b>"AzureAd:ClientId" : ""</b><br/>
Can be found in the Azure Portal under the App Registration -> Overview -> Application (client) ID

<b>"AzureApp:ClientSecret" : ""</b><br/>
Can be found in the Azure Portal under the App Registration -> Certificates & secrets -> Client secrets

<b>"AzureEnvironment" : ""</b><br/>
Can be set to "AzurePublicCloud", "AzureUSDoD", or "AzureUSGovernment" depending on the environment you are using.

<b>"COSMOS_CONNECTION_STRING": ""</b><br/>
Can be found in the Azure Portal under the Cosmos DB -> Keys -> Primary Connection String

<b>"DefaultActionDisable": "false"</b><br/>
(Optional)Can be set to "true" to disable the device if not found in the database. If set to "false" the device will be allowed to connect if not found in the database.

## Service Principal Permissions
The service principal used by the application must have the following Graph API permissions to update device attributes:

<b>
Device.ReadWrite.All<br/>
Directory.Read.All<br/>
DeviceManagementManagedDevices.Read.All<br/>
</b>
<br/>

For updates to Administrative Units the service principal must have the following role assignments:

<b>
Privileged Role Administrator<br/>
</b>