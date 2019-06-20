using System;
using System.Threading.Tasks;

namespace Breeze.ThirdPartyLicenseOverview
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                Generator generator = new Generator(args[0]);
                try
                {
                    generator.Run().GetAwaiter().GetResult();
                    return 0;
                } catch (Exception ex)
                {
                    Out.Error(ex.Message);
                    return 2;
                }
            }
            else
            {
                Out.Error("Incorrect usage");
                Console.WriteLine("Using: 3rdplic <SolutionFile>");
                return 1;
            }
        }

    }
}
