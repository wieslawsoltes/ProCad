using System;

namespace ACadInspector.TraceCli;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (!RenderTraceOptions.TryParse(args, out RenderTraceOptions? options, out string? error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(RenderTraceOptions.GetUsage());
            return 1;
        }

        if (options is null)
        {
            Console.Error.WriteLine("Unable to parse options.");
            return 1;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(RenderTraceOptions.GetUsage());
            return 0;
        }

        int failures = 0;
        RenderTraceRunner runner = new(options);
        for (int i = 0; i < options.InputFiles.Count; i++)
        {
            string inputFile = options.InputFiles[i];
            try
            {
                RenderTraceResult result = runner.Run(inputFile);
                RenderTraceReporter.Write(result);
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"Failed to process '{inputFile}': {ex.Message}");
                Console.Error.WriteLine(ex);
            }
        }

        return failures == 0 ? 0 : 2;
    }
}
