## Architecture Overview

```mermaid
flowchart TB

  subgraph clients
    CLI
    script.py
  end

  subgraph service
    AzFunc("Azure\nFunctions\n⚡")

    EventGrid{Event Grid}
    SystemData[(System Data)]
    
    pool-linux[/linux queue/]
    pool-win[/windows queue/]
  end
  
  UserData[(User Data)]

  subgraph linux-compute
    vmss1
    vmss2
  end

  subgraph windows-compute
    vmss3
    vmss4
  end
  
  CLI--HTTP-->AzFunc
  script.py--HTTP-->AzFunc

  AzFunc--Timers-->AzFunc
  AzFunc --> pool-linux
  AzFunc --> pool-win
  AzFunc <--> UserData
  AzFunc -- ORM --> SystemData
  SystemData -- Trigger --> AzFunc

  SystemData --> EventGrid
  UserData --> EventGrid
  EventGrid -- Event --> AzFunc

  UserData <-- Container Sync --> linux-compute
  UserData <-- Container Sync --> windows-compute
  
  linux-compute -. poll .-> pool-linux
  windows-compute -. poll .-> pool-win
```
