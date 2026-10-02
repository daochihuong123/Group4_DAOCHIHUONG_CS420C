using System;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using ASP.Net_Remote;

namespace RemotingServer
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                int port = 8085;
                TcpServerChannel channel = new TcpServerChannel(port);
                ChannelServices.RegisterChannel(channel, false);

                RemotingConfiguration.RegisterWellKnownServiceType(
                    typeof(CalculatorService),
                    "CalculatorServiceURI",
                    WellKnownObjectMode.SingleCall
                );

                Console.Title = "REMOTING SERVER";
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("==================================================");
                Console.WriteLine($" SERVER DANG CHAY TREN CONG {port}");
                Console.WriteLine(" Dang lang nghe ket noi tu Client...");
                Console.WriteLine("==================================================");
                Console.ResetColor();

                Console.WriteLine("\nNhan Enter de dung Server.\n");
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Loi Server: " + ex.Message);
                Console.ResetColor();
                Console.ReadLine();
            }
        }
    }
}