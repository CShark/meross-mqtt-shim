using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace meross_mqtt_shim
{
    static class ConfigParser
    {
        public static Dictionary<string, string> ParseConfig(string config) {
            var lines = config.Split("\n");
            var dict = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

            foreach (var line in lines) {
                var data = line.Split("#")[0];

                if (!string.IsNullOrWhiteSpace(data)) {
                    if (data.Contains("=")) {
                        var parts = data.Split("=");
                        parts[0] = parts[0].ToLower().Trim();
                        parts[1] = parts[1].Trim();

                        dict[parts[0]] = parts[1];
                    } else {
                        Console.WriteLine($"Could not parse config option {data}");
                    }
                }
            }

            return dict;
        }
    }
}
