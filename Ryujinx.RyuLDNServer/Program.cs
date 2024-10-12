namespace Ryujinx.RyuLDNServer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting RyuLDN server");
            RyuLDNServer server = new RyuLDNServer();
            var started = server.Start();
            if (started)
            {
                Console.WriteLine("RyuLDN server started");
                Console.WriteLine("Press any key to stop the server");
                Console.ReadKey();
            }
            else
            {
                Console.WriteLine("Error");
            }
        }
    }
}
