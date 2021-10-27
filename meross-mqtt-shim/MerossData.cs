using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace meross_mqtt_shim {
    class MerossData {
        public MerossHeader header { get; set; } = new();

        public Dictionary<string, Dictionary<string, object>> payload { get; set; } = new();

        public class MerossHeader {
            public string messageId { get; set; }
            public string @namespace { get; set; }
            public string method { get; set; } = "SET";
            public int payloadVersion { get; set; } = 1;
            public string from { get; set; }
            public int timestamp { get; set; }
            public string sign { get; set; }
        }
    }
}