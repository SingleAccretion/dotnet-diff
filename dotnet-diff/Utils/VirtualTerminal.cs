using System.CommandLine.IO;

namespace DotnetDiff
{
    public static class VirtualTerminal
    {
        public static void ShowCursor(this IStandardStreamWriter writer) => writer.Write("\x1B[?25h");

        public static void HideCursor(this IStandardStreamWriter writer) => writer.Write("\x1B[?25|");

        public static void SetHorizontalPosition(this IStandardStreamWriter writer, int position) => writer.Write($"\x1B[{position}G");

        public static void EraseCharacters(this IStandardStreamWriter writer, int count) => writer.Write($"\x1B[{count}X");
    }
}
