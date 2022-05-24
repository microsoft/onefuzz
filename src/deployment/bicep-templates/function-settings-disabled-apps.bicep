param functions_disabled_setting string

var allFunctions = [
  'agent_can_schedule'    //0
  'agent_commands'        //1
  'agent_events'          //2
  'agent_registration'    //3
  'containers'            //4
  'download'              //5
  'info'                  //6
  'instance_config'       //7
  'jobs'                  //8
  'job_templates'         //9
  'job_templates_manage'  //10
  'negotiate'             //11
  'node'                  //12
  'node_add_ssh_key'      //13
  'notifications'         //14
  'pool'                  //15
  'proxy'                 //16
  'queue_file_changes'    //17
  'queue_node_heartbeat'  //18
  'queue_proxy_update'    //19
  'queue_signalr_events'  //20
  'queue_task_heartbeat'  //21
  'queue_updates'         //22
  'queue_webhooks'        //23
  'repro_vms'             //24
  'scaleset'              //25
  'tasks'                 //26
  'timer_daily'           //27
  'timer_proxy'           //28
  'timer_repro'           //29
  'timer_retention'       //30
  'timer_tasks'           //31
  'timer_workers'         //32
  'webhooks'              //33
  'webhooks_logs'         //34
  'webhooks_ping'         //35
]

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
