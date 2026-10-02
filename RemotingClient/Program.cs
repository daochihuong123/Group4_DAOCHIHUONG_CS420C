using System;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using ASP.Net_Remote;

namespace RemotingClient
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Console.Title = "REMOTING CLIENT";

                TcpClientChannel channel = new TcpClientChannel();
                ChannelServices.RegisterChannel(channel, false);

                string serverUrl = "tcp://localhost:8085/CalculatorServiceURI";
                CalculatorService remoteService = (CalculatorService)Activator.GetObject(
                    typeof(CalculatorService),
                    serverUrl
                );

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("==================================================");
                Console.WriteLine(" DA KET NOI TOI SERVER TU XA THANH CONG");
                Console.WriteLine("==================================================");
                Console.ResetColor();

                // 1. Goi ham gui thong tin sinh vien
                Console.Write("\nNhap ho ten sinh vien: ");
                string hoTen = Console.ReadLine();
                Console.Write("Nhap ma sinh vien: ");
                string maSV = Console.ReadLine();

                string ketQuaChao = remoteService.GuiLoiChao(hoTen, maSV);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"-> Server phan hoi: \"{ketQuaChao}\"");
                Console.ResetColor();

                // 2. Goi ham tinh toan tu xa
                Console.WriteLine("\n--- THUC HIEN TINH TOAN TU XA ---");
                Console.Write("Nhap so a: ");
                double a = double.Parse(Console.ReadLine());
                Console.Write("Nhap so b: ");
                double b = double.Parse(Console.ReadLine());
                Console.Write("Nhap phep tinh (+, -, *, /): ");
                string op = Console.ReadLine();

                double ketQuaTinh = remoteService.TinhToan(a, b, op);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"-> Ket qua Server tinh toan tra ve: {a} {op} {b} = {ketQuaTinh}");
                Console.ResetColor();

                Console.WriteLine("\nDemo hoan tat! Bam phim bat ky de thoat.");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Loi Client: " + ex.Message);
                Console.ResetColor();
                Console.ReadKey();
            }
        }
    }
}