using System.Text;
using Hsis.Cli;

Console.OutputEncoding = Encoding.UTF8;
return await CommandLine.RunAsync(args, Console.Out, Console.Error);
