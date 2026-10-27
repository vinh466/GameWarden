using System.Text;

namespace GameWarden.Core.Isolation;

/// <summary>Builds a Windows command line that <c>CommandLineToArgvW</c> and the C runtime split back into the same arguments.</summary>
public static class CommandLine
{
    public static string Build(IEnumerable<string> arguments)
    {
        var line = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (line.Length > 0)
            {
                line.Append(' ');
            }

            Append(line, argument);
        }

        return line.ToString();
    }

    private static void Append(StringBuilder line, string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            line.Append(argument);
            return;
        }

        line.Append('"');
        for (var i = 0; ; i++)
        {
            var backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                i++;
                backslashes++;
            }

            if (i == argument.Length)
            {
                // Double the trailing backslashes so they do not escape the closing quote.
                line.Append('\\', backslashes * 2);
                break;
            }

            if (argument[i] == '"')
            {
                line.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                line.Append('\\', backslashes).Append(argument[i]);
            }
        }

        line.Append('"');
    }
}
