using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace H1Emu_Launcher.Classes
{
    // Player counts of the public servers, the same numbers as on jsemu.eu.
    internal static class ServerStatusApi
    {
        private const string Endpoint = "https://jsemu.eu/api/server-status?serverId=";

        public sealed record Server(int Id, string Name, string Mode);

        public static readonly Server[] Servers =
        {
            new(21, "Krakow", "PVP"),
            new(23, "Kraken", "PVP (Solo/Duo/Trio)"),
            new(69, "Wasteland", "PVE"),
            new(57, "Training", "Deathmatch")
        };

        public sealed record Status(Server Server, int? Online, int Max);

        private static async Task<Status> GetAsync(Server server)
        {
            try
            {
                string raw = await SplashWindow.httpClient.GetStringAsync(Endpoint + server.Id);
                using JsonDocument doc = JsonDocument.Parse(raw);
                JsonElement root = doc.RootElement;
                return new Status(server, root.GetProperty("populationNumber").GetInt32(), root.GetProperty("maxPopulationNumber").GetInt32());
            }
            catch (HttpRequestException) { return new Status(server, null, 0); }
            catch (TaskCanceledException) { return new Status(server, null, 0); }
            catch (JsonException) { return new Status(server, null, 0); }
            catch (KeyNotFoundException) { return new Status(server, null, 0); }
            catch (System.InvalidOperationException) { return new Status(server, null, 0); }
        }

        public static async Task<List<Status>> GetAllAsync() => (await Task.WhenAll(Servers.Select(GetAsync))).ToList();
    }
}
