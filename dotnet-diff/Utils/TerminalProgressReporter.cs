using System;
using System.CommandLine;
using System.CommandLine.IO;

namespace DotnetDiff
{
    public class TerminalProgressReporter
    {
        // Last chance to not leave the terminal window with a "hung" progress bar.
        static TerminalProgressReporter() => Console.CancelKeyPress += (o, e) => Console.Write($"\x1B]9;4;0;100\x1B\\");

        public static void Report(long? done, long? total, IConsole console, ContentInterpretation contentInterpretation = ContentInterpretation.Bytes)
        {
            string Format(long value) => contentInterpretation switch
            {
                ContentInterpretation.Bytes => Math.Log10(value) switch
                {
                    <= 3 => $"{value}",
                    <= 6 => $"{value / 1024} KB",
                    <= 9 => $"{value / (1024 * 1024)} MB",
                    _ => $"{value / (1024 * 1024 * 1024)} GB",
                },
                _ => value.ToString()
            };

            if (done is null || total is null)
            {
                console.Out.Write($"[{new string('-', 100)}]");
                return;
            }

            var donePercentage = (int)Math.Round((double)done / total.Value * 100, 2);
            var remainingPepcentage = 100 - donePercentage;

            console.Out.HideCursor();
            console.Out.SetHorizontalPosition(0);
            console.Out.Write($"[{new string('=', donePercentage)}{new string('-', remainingPepcentage)}] {Format(done.Value)} / {Format(total.Value)}");
            console.Out.ReportProgress(donePercentage);
            console.Out.EraseCharacters(20);
            console.Out.ShowCursor();

            if (done == total)
            {
                console.Out.WriteLine();
            }
        }
    }
}
