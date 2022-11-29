param allFunctions array

var disabledFunctions = [for f in allFunctions: 'AzureWebJobs.${f}.Disabled' ]

var disabledFunctionsAppSettings = {
  '${disabledFunctions[0]}' : 0
  '${disabledFunctions[1]}' : 0
  '${disabledFunctions[2]}' : 0
  '${disabledFunctions[3]}' : 0
  '${disabledFunctions[4]}' : 1

  '${disabledFunctions[5]}' : 1
  '${disabledFunctions[6]}' : 1
  '${disabledFunctions[7]}' : 1
  '${disabledFunctions[8]}' : 1
  '${disabledFunctions[9]}' : 1

  '${disabledFunctions[10]}' : 1
  '${disabledFunctions[11]}' : 1
  '${disabledFunctions[12]}' : 1
  '${disabledFunctions[13]}' : 1
  '${disabledFunctions[14]}' : 1

  '${disabledFunctions[15]}' : 1
  '${disabledFunctions[16]}' : 1
  '${disabledFunctions[17]}' : 1
  '${disabledFunctions[18]}' : 1
  '${disabledFunctions[19]}' : 1

  '${disabledFunctions[20]}' : 1
  '${disabledFunctions[21]}' : 1
  '${disabledFunctions[22]}' : 1
  '${disabledFunctions[23]}' : 1
  '${disabledFunctions[24]}' : 1

  '${disabledFunctions[25]}' : 1
  '${disabledFunctions[26]}' : 1
  '${disabledFunctions[27]}' : 1
  '${disabledFunctions[28]}' : 1
  '${disabledFunctions[29]}' : 1

  '${disabledFunctions[30]}' : 1
  '${disabledFunctions[31]}' : 1
  '${disabledFunctions[32]}' : 1
  '${disabledFunctions[33]}' : 1
  '${disabledFunctions[34]}' : 1

  '${disabledFunctions[35]}' : 1  
}

output functions array = disabledFunctions
output appSettings object = disabledFunctionsAppSettings

