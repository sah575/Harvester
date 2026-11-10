using System.Runtime.InteropServices;
using Harvester.Net;
using HarvesterRelay;

// Harvester relay sunucusu.
// Kullanım: HarvesterRelay [--port 7778] [--max-rooms 200] [--max-peers 1000]
//                          [--mode event|poll] [--update-ms 15] [--flush 1|0]
int port = RelayProtocol.DefaultPort;
int maxRooms = 200;
int maxPeers = 1000;
bool unsynced = true;
int updateMs = 15;
bool flush = true;

for (int i = 0; i + 1 < args.Length; i += 2)
{
    switch (args[i])
    {
        case "--port": port = int.Parse(args[i + 1]); break;
        case "--max-rooms": maxRooms = int.Parse(args[i + 1]); break;
        case "--max-peers": maxPeers = int.Parse(args[i + 1]); break;
        case "--mode": unsynced = args[i + 1] != "poll"; break;
        case "--update-ms": updateMs = int.Parse(args[i + 1]); break;
        case "--flush": flush = args[i + 1] != "0"; break;
        default:
            Console.Error.WriteLine($"Bilinmeyen argüman: {args[i]}");
            return 2;
    }
}

var server = new RelayServer(port, maxRooms, maxPeers, unsynced, updateMs, flush);
if (!server.Start())
{
    Console.Error.WriteLine($"UDP {port} portu açılamadı.");
    return 1;
}

using var stop = new ManualResetEventSlim();
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stop.Set(); });
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; stop.Set(); });

// Olay güdümlü modda paketler LiteNetLib'in kendi iş parçacığında işlenir; bu döngü sadece istatistik içindir.
int waitMs = unsynced ? 1000 : 1;
while (!stop.IsSet)
{
    server.Poll();
    stop.Wait(waitMs);
}

server.Stop();
return 0;
