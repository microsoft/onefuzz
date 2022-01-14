# Azure Active Directory Entities
This document describes the configuration of entities create in Azure AD by our [deployment script](../src/deployment/deploy.py)

### OneFuzz Application Registration
This is the registration of the OneFuzz instance.
* name : `<instance_name>`
* app roles
    * _ManagedNode_
        * value: ManagedNode
        * Allowed Member types: Applications
    * _CliClient_
        * value: CliClient
        * Allowed Member types: Applications
    * _UserAssignment_
        * value: UserAssignment
        * Allowed Member types: Users/Groups 
* API Permissions
    * _User.Read_ ([Microsoft Graph](https://docs.microsoft.com/en-us/graph/permissions-reference#user-permissions))
* scope
    * `user_impersonation`
* Authorized application:
    * OneFuzz CLI registration
* Properties: 
    * Assignment required?: Yes

### OneFuzz Application Service Principal
Service principal linked to the OneFuzz application registration.
* name: `<instance_name>`
* Application Id: `<OneFuzz Application registration app_id>`

### OneFuzz CLI registration
The registration for the command line interface.
* name: `<instance_name>-cli`

### OneFuzz CLI Service Principal
Service principal linked to the OneFuzz CLI application registration.
* name: `<instance_name>-cli`
* Application Id: `<OneFuzz CLI registration app_id>`
* User Assignment required: _true_
* Permission
    * _CliClient_ (from OneFuzz Application registration)

### Managed Node Service Principal
This entity is available after the first deployment. This is the service principal associated with the user-assigned managed identity `<instance_name>-scalesetid`.

* name: `<instance_name>-scalesetid`
* Service Principal
    * Permission
        * _ManagedNode_ (from OneFuzz Application registration)

### Deployment Service Principal
This entity is the 'user' service principal that invokes a OneFuzz deployment. This service principal is assigned access to the instance's primary App Registration. 

* name: `<user_name_sp>`
* Service Principal
    * Permission
        * _UserAssignment_ (from OneFuzz Application registration)
