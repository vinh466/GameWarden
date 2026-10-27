// Fake game server: exits 42 on CTRL+C (after "saving"), 7 on stdin "exit".
using System;
using System.Threading;

class Game
{
    static int Main(string[] args)
    {
        Console.CancelKeyPress += delegate(object s, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            Console.WriteLine("game: CTRL+C received -> saving world");
            Console.Out.Flush();
            Thread.Sleep(300);
            Environment.Exit(42);
        };
        Console.WriteLine("game: ready");
        Console.Out.Flush();
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            if (line == "exit")
            {
                Console.WriteLine("game: stdin 'exit' -> saving world");
                Console.Out.Flush();
                return 7;
            }
            Console.WriteLine("game: echo " + line);
            Console.Out.Flush();
        }
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }
}
