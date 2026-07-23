% Pump protection - MATLAB front end (transpiles to CL).
% Controller points are declared global (external, controller-shared) or
% persistent (file-local). A generic SEQUENCE header is synthesized on the CL
% side; rename it and its POINT database to your controller.

global Temp
global Limit
global Warn
global Alarm
global WarnLamp
global FanCmd
global FaultLamp
persistent Attempts

function Monitor
  Attempts = 0;
  while Temp > Limit
    Alarm = true;
    Attempts = Attempts + 1;
  end

  if Temp > Warn
    WarnLamp = true;
  elseif Attempts > 3
    WarnLamp = true;
  else
    WarnLamp = false;
  end

  Alarm = false;
  Cooldown();
end

function Cooldown
  try
    FanCmd = true;
    for i = 1:5
      FanCmd = true;
    end
  catch fault
    FaultLamp = true;
  end
end
