// Copyright Warren Harding 2026
using DriveCLI;

var exitCode = await DriveCliProgram.RunAsync(args);
Environment.ExitCode = exitCode;
