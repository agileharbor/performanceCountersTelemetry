Palantiri
=============
A C# client to performance counters.

##Usage

*Start
App should be started with parameters to catch system counters. Multiple instances cab be started in one machine. Each instance scans specified counters and sends them to specified destinations. 

Arguments example:

``` 
Start -cs "Ram CLR .NET;iisexpress;% Time in GC; iis-ram|Processor information;_Total;% Processor usage" -ds "Console|Telegraf"
```

After start app receives all the specified counters and send them to destinations.

#Add Counters

Counters can be added on the fly. Example:
```
AddCounter -c "Processor information" -i "% Processor usage" -n "_Total" -a "cpu"
```

#Help 
``` 
-?
```


##Development
* Please have a chat about any big features before submitting PR's
* New destinations and additional control will be added if it need
