using DriveCLI;

var exitCode = await DriveCliProgram.RunAsync(args);
Environment.ExitCode = exitCode;
