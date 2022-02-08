Install-Module -Name PowerShellForGitHub


$secureString = ("<Your Access Token>" | ConvertTo-SecureString -AsPlainText -Force)
$cred = New-Object System.Management.Automation.PSCredential "username is ignored", $secureString
Set-GitHubAuthentication -Credential $cred


