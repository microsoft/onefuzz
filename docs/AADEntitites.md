# Azure Active Directory Entities
This document describes the configuration of entities create in Azure AD by our [deployment script](../src/deployment/deploy.sh)

### OneFuzz Application Registration
This is the registration of the OneFuzz instance:
* name : <instance_name>
* app roles
    * ManagedNode
        * value: ManagedNode
        * Allowed Member types: Applications
    * CliClient
        * value: ManagedNode
        * Allowed Member types: Applications
* Api Permissions
    * User.Read (Microsoft Graph)
* scope
    * user_impersonation
* Authorized application:
    * OneFuzz CLI registration

### Onefuzz Application Service Principal
Service principal linked to the OneFuzz application registration:
* name: <instance_name>
* Application Id: <Onefuzz Application registration app_id>

### OneFuzz CLI registration
The registration for the Command line interface
* name: <instance_name>-cli

### Onefuzz cli Service Principal
service principal linked to the onefuzz cli application registration
* name: <instance_name>-cli
* Application Id: <Onefuzz cli registration app_id>
* User Assignment required: true
* Permission
    * CliClient (from OneFuzz Application registration)

### Managed node Service Principal
This entity is available after the first deployment. This is the service principal associated with the user managed identity <instance_name>-scalesetid

* name: <instance_name>-scalesetid
* Service Principal
    * Permission
        * ManagedNode (from Onefuzz Application registration)
