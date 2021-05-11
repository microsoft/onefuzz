# Azure Ad Entities
This document describes the entities create in azure AD by our [deployment script](../src/deployment/deploy.sh)

### Onefuzz Application registration
This is the registration of the onefuzz instance

* name : <instance_name>
* app roles
    * ManagedNode
    * CliClient
* Api Permissions
    * User.Read (Microsoft Graph)
* scope
    * user_impersonation
* Authorozed application:
    * Onefuzz cli registration

### Onefuzz Application cli Service Principal
   * name: <instance_name>
   * Application Id: <Onefuzz Application registration app_id>
### Onefuzz cli registration
* name: <instance_name>-cli
### Onefuzz cli Service Principal
* name: <instance_name>-cli
* Application Id: <Onefuzz cli registration app_id>
* User Assignment required: true
* Permission
    * CliClient (from Onefuzz Application registration)

### Managed node Service Principal
This entity is available after the first deployment
* name: <instance_name>-scalesetid
* Service Principal
    * Permission
        * ManagedNode (from Onefuzz Application registration)