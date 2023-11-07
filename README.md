# DelegationStation

## Table of Contents
- [Overview](#overview)
- [Environment variables](#environment-variables)
- [Service Principal Permissions](#service-principal-permissions)
- [Application Setup](#application-setup)	
	- [Token Configuration](#token-configuration)
	- [Authentication Blade](#authentication-blade)
		- [Web Redirect URIs](#web-redirect-uris)
		- [Implicit Grant and hybrid flows](#implicit-grant-and-hybrid-flows)
		- [Supported account types](#supported-account-types)
		- [API permissions](#api-permissions)


## Environment variables
Use the following environment variables to configure the application. These can be set in the `appsettings.json` file or in the Azure App Service configuration.

<b>"AzureAd:TenantId" : ""</b><br/>
Can be found in the Azure Portal under Azure Active Directory -> Properties -> Directory ID
Can also be found in the Azure Portal under the App Registration -> Overview -> Directory ID

<b>"AzureAd:ClientId" : ""</b><br/>
Can be found in the Azure Portal under the App Registration -> Overview -> Application (client) ID

<b>"AzureApp:ClientSecret" : ""</b><br/>
Can be found in the Azure Portal under the App Registration -> Certificates & secrets -> Client secrets

<b>"DefaultAdminGroupObjectId" : ""</b><br/>
Can be found in the Azure Portal under Azure Active Directory -> Groups -> Group -> Properties -> Object ID
This is the group that will be used to determine if a user is an administrator of the application and can perform additional delgations within the application.

<b>"AzureEnvironment" : ""</b><br/>
Can be set to "AzurePublicCloud", "AzureUSDoD", or "AzureUSGovernment" depending on the environment you are using.

<b>"COSMOS_CONNECTION_STRING": ""</b><br/>
Can be found in the Azure Portal under the Cosmos DB -> Keys -> Primary Connection String

<b>"DefaultActionDisable": "false"</b><br/>
(Optional)Can be set to "true" to disable the device if not found in the database. If set to "false" the device will be allowed to connect if not found in the database.

### Function App Environment Variables
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

<b>"TriggerTime": "0 */15 * * * *"</b><br/>
Must be set to a cron expression to change the frequency of the function. The example is every 15 minutes.

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
AdministrativeUnit.ReadWrite.All<br/>
</b>

To update Security Groups the service principal must have the following role assignments:

Be the owner of the group.

## Application Registration Setup
The application is designed to be deployed to an Azure App Service. The application can be deployed to a Windows or Linux App Service. The application can also be deployed to a container instance. The application can be deployed using the Azure CLI or Visual Studio. The application can also be deployed using GitHub Actions. The application can be deployed to a Windows or Linux App Service. The application can also be deployed to a container instance. The application can be deployed using the Azure CLI or Visual Studio. The application can also be deployed using GitHub Actions.

### Token Configuration
This application utilizes Entra ID groups for accessing the application through claims. Each group that is assigned a role must be configured to be sent as a claim from the enterprise application.
Add the following claims to the App registration:

1. In the Azure Portal, navigate to the App Registration for the application.	
1. Navigate to the Token configuration blade.
1. Click on Add groups claim.
1. Select Security groups OR Groups  assigned to the application (recommended for large enterprise companies to avoid exceeding the limit on the number of groups a token can emit).						
1. Ensure the Group ID is selected for each token type.
1. Ensure Emit groups as role claims is selected for each token type.

### Authentication Blade

#### Web Redirect URIs
Ensure the Redirect URIs are set to the application URL. For example: https://delegationstation.azurewebsites.net/signin-oidc or https://delegationstation.azurewebsites.net/.auth/login/aad/callback

#### Implicit Grant and hybrid flows
Select <b>ID tokens</b> for the Implicit grant and hybrid flows.

#### Supported account types
Select <b>Accounts in this organizational directory only</b> for the Supported account types.

#### API permissions
The service principal used by the application must have the following Graph API permissions to update device attributes:

<b>
Device.ReadWrite.All<br/>
</b>
<br/>

The service principal used by the application must have the following Graph API permissions to update read information about the devices and security groups used:

<b>
Directory.Read.All<br/>
DeviceManagementManagedDevices.Read.All<br/>
</b>
<br/>

## Enterprise Application Setup
Create an Enterprise Application in Azure AD for the application. This will allow you to assign users and groups to the application.

### Users and Groups
Navigate to the Users and groups blade and assign groups to the application. The application will use the groups assigned to the application to determine the role of the user. 

## Adding Devices to Groups
The service principal used by the application should be the owner of the groups needed to be managed or given access to the groups through an Administrative Unit. Any groups added to the application under Tags will have to have the service principals permissions added in order to work as expected.