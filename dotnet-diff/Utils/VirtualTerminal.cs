using System.CommandLine.IO;

namespace DotnetDiff
{
    public static class VirtualTerminal
    {
        public static void ShowCursor(this IStandardStreamWriter writer) => writer.Write("\x1B[?25h");

        public static void HideCursor(this IStandardStreamWriter writer) => writer.Write("\x1B[?25l");

        public static void SetHorizontalPosition(this IStandardStreamWriter writer, int position) => writer.Write($"\x1B[{position}G");

        public static void EraseCharacters(this IStandardStreamWriter writer, int count) => writer.Write($"\x1B[{count}X");

        public static void ReportProgress(this IStandardStreamWriter writer, int percentage)
        {
            var mode = 1;
            if (percentage is 100)
            {
                mode = 0;
            }

            writer.Write($"\x1B]9;4;{mode};{percentage}\x1B\\");
        }
    }
}
