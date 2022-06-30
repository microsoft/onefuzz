param functions_disabled_setting string

param allFunctions array

var disabledFunctions = [for f in allFunctions: 'AzureWebJobs.${f}.Disabled' ]

var disabledFunctionsAppSettings = {
  '${disabledFunctions[0]}' : functions_disabled_setting
  '${disabledFunctions[1]}' : functions_disabled_setting
  '${disabledFunctions[2]}' : functions_disabled_setting
  '${disabledFunctions[3]}' : functions_disabled_setting
  '${disabledFunctions[4]}' : functions_disabled_setting

  '${disabledFunctions[5]}' : functions_disabled_setting
  '${disabledFunctions[6]}' : functions_disabled_setting
  '${disabledFunctions[7]}' : functions_disabled_setting
  '${disabledFunctions[8]}' : functions_disabled_setting
  '${disabledFunctions[9]}' : functions_disabled_setting

  '${disabledFunctions[10]}' : functions_disabled_setting
  '${disabledFunctions[11]}' : functions_disabled_setting
  '${disabledFunctions[12]}' : functions_disabled_setting
  '${disabledFunctions[13]}' : functions_disabled_setting
  '${disabledFunctions[14]}' : functions_disabled_setting

  '${disabledFunctions[15]}' : functions_disabled_setting
  '${disabledFunctions[16]}' : functions_disabled_setting
  '${disabledFunctions[17]}' : functions_disabled_setting
  '${disabledFunctions[18]}' : functions_disabled_setting
  '${disabledFunctions[19]}' : functions_disabled_setting

  '${disabledFunctions[20]}' : functions_disabled_setting
  '${disabledFunctions[21]}' : functions_disabled_setting
  '${disabledFunctions[22]}' : functions_disabled_setting
  '${disabledFunctions[23]}' : functions_disabled_setting
  '${disabledFunctions[24]}' : functions_disabled_setting

  '${disabledFunctions[25]}' : functions_disabled_setting
  '${disabledFunctions[26]}' : functions_disabled_setting
  '${disabledFunctions[27]}' : functions_disabled_setting
  '${disabledFunctions[28]}' : functions_disabled_setting
  '${disabledFunctions[29]}' : functions_disabled_setting

  '${disabledFunctions[30]}' : functions_disabled_setting
  '${disabledFunctions[31]}' : functions_disabled_setting
  '${disabledFunctions[32]}' : functions_disabled_setting
  '${disabledFunctions[33]}' : functions_disabled_setting
  '${disabledFunctions[34]}' : functions_disabled_setting

  '${disabledFunctions[35]}' : functions_disabled_setting  
}

output functions array = disabledFunctions
output appSettings object = disabledFunctionsAppSettings

