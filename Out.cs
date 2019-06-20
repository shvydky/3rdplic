using System;
using System.Collections.Generic;
using System.Text;

namespace Breeze.ThirdPartyLicenseOverview
{
    public class Out
    {
        public static void Debug(string message)
        {
            //Print(ConsoleColor.Cyan, message);
        }

        public static void Info(string message)
        {
            Print(ConsoleColor.Gray, message);
        }

        public static void Error(string message)
        {
            Print(ConsoleColor.Red, message);
        }

        public static void Print(ConsoleColor color, string message)
        {
            var currentColor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(message);
            }
            finally
            {
                Console.ForegroundColor = currentColor;
            }
        }
    }
}
