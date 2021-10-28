using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using MQTTnet.Protocol;
using Newtonsoft.Json;

namespace meross_mqtt_shim {
    class Program {
        class Options {
            [Option('c', "config", Default = null, Required = false,
                HelpText = "Set the location of the config directory")]
            public string ConfigDir { get; set; }

            public bool UseTls { get; set; } = false;

            public bool VerifyCert { get; set; } = true;

            public string MqttBroker { get; set; }

            public string Username { get; set; }
            public string Password { get; set; }

            public string MqttRoot { get; set; } = "shim/meross/";

            public string MerossRoot { get; set; } = "/appliance/";
        }

        private static Options _options;
        private static Dictionary<string, Dictionary<string, string>> _defaults = new();
        private static Dictionary<string, Dictionary<string, Dictionary<string, object>>> _cache = new();
        private static Dictionary<string, string> _namespaces = new();
        private static IMqttClient _client;

        static void Main(string[] args) {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(InitShim);

            Console.ReadLine();
        }

        private static void InitShim(Options options) {
            if (options.ConfigDir == null) {
                options.ConfigDir = Directory.GetCurrentDirectory();

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                    options.ConfigDir = "/etc/meross-mqtt";
                }
            }

            var configFile = Path.Combine(options.ConfigDir, "meross.config");

            if (File.Exists(configFile)) {
                var config = ConfigParser.ParseConfig(File.ReadAllText(configFile));

                if (config.ContainsKey("broker"))
                    options.MqttBroker = config["broker"];
                if (config.ContainsKey("usetls"))
                    options.UseTls = config["usetls"].ToLower() == "true";
                if (config.ContainsKey("user"))
                    options.Username = config["user"];
                if (config.ContainsKey("password"))
                    options.Password = config["password"];
                if (config.ContainsKey("verifycert"))
                    options.VerifyCert = config["verifycert"].ToLower() != "false";
                if (config.ContainsKey("mqttroot"))
                    options.MqttRoot = config["mqttroot"].TrimEnd('/') + '/';
                if (config.ContainsKey("merossroot"))
                    options.MerossRoot = config["merossroot"].TrimEnd('/') + '/';

                var configDir = Path.Combine(options.ConfigDir, "conf.d");
                if (Directory.Exists(configDir)) {
                    var defaultFiles = Directory.GetFiles(configDir);
                    _defaults = new Dictionary<string, Dictionary<string, string>>();

                    foreach (var file in defaultFiles) {
                        var payloadType = Path.GetFileNameWithoutExtension(file).ToLower();
                        _defaults.Add(payloadType, ConfigParser.ParseConfig(File.ReadAllText(file)));
                    }
                } else {
                    Console.WriteLine("Config directory for groups not found");
                }

                _options = options;

                InitMqttClient();
            } else {
                Console.WriteLine($"Config file at {configFile} not found.");
                return;
            }
        }

        private static void InitMqttClient() {
            if (string.IsNullOrWhiteSpace(_options.MqttBroker)) {
                Console.WriteLine($"No MQTT-Broker was specified");
                return;
            }

            var port = 1883;
            var useTls = false;
            var websocket = false;

            _options.MqttBroker = _options.MqttBroker.ToLower();
            if (_options.MqttBroker.StartsWith("mqtts://")) {
                port = 8883;
                useTls = true;
            } else if (_options.MqttBroker.StartsWith("ws://")) {
                port = 80;
                websocket = true;
            } else if (_options.MqttBroker.StartsWith("wss://")) {
                port = 433;
                useTls = true;
                websocket = true;
            }

            if (!websocket) {
                if (_options.MqttBroker.Contains("://")) {
                    _options.MqttBroker = _options.MqttBroker.Split("://")[1];
                }

                if (_options.MqttBroker.Contains(":")) {
                    if (!Int32.TryParse(_options.MqttBroker.Split(":")[1], out port)) {
                        Console.WriteLine($"Failed to recognize port in broker address");
                        return;
                    }

                    _options.MqttBroker = _options.MqttBroker.Split(":")[0];
                }
            }

            if (_options.UseTls) useTls = true;


            var brokerOptions = new MqttClientOptionsBuilder()
                .WithClientId("meross-mqtt-shim")
                .WithCleanSession();

            if (websocket) {
                brokerOptions = brokerOptions.WithWebSocketServer(_options.MqttBroker);
            } else {
                brokerOptions = brokerOptions.WithTcpServer(_options.MqttBroker, port);
            }

            if (useTls) {
                // Bug, fixed in recent mqttnet version
                brokerOptions = brokerOptions.WithTls(parameters => {
                    parameters.SslProtocol = SslProtocols.Tls12;

                    if (!_options.VerifyCert) {
                        parameters.AllowUntrustedCertificates = true;
                        parameters.CertificateValidationHandler = context => true;
                    }
                });
            }

            RunMqttClient(brokerOptions.Build());
        }

        private static void RunMqttClient(IMqttClientOptions clientConfig) {
            var factory = new MqttFactory();
            var client = factory.CreateMqttClient();

            client.UseDisconnectedHandler(async args => {
                Console.WriteLine($"Connection to broker lost");
                await Task.Delay(5000);

                try {
                    await client.ConnectAsync(clientConfig);
                } catch (Exception ex) {
                    Console.WriteLine($"Reconnection failed");
                }
            });

            client.UseConnectedHandler(async args => {
                Console.WriteLine("Connected with broker");

                await client.SubscribeAsync(new TopicFilterBuilder().WithTopic($"{_options.MqttRoot}#").Build());
            });

            client.UseApplicationMessageReceivedHandler(HandleMessage);
            _client = client;

            Task.Run(() => client.ConnectAsync(clientConfig));

            while (true) {
                Thread.Sleep(5000);
            }
        }

        private static void HandleMessage(MqttApplicationMessageReceivedEventArgs args) {
            var path = args.ApplicationMessage.Topic;
            path = path.Substring(_options.MqttRoot.Length);

            var parts = path.Split("/");
            if (parts[1] == "ack") return;

            if (parts.Length >= 3) {
                var deviceId = parts[0];
                var payloadGroup = parts[1].ToLower();
                var data = parts[2];
                var channel = 0;

                if (parts.Length > 3) {
                    if (Int32.TryParse(parts[2], out channel)) {
                        data = parts[3];
                    }
                }

                if (!_cache.ContainsKey(deviceId)) {
                    _cache.Add(deviceId, new());
                }

                // Fill cache with defaults
                if (!_cache[deviceId].ContainsKey(payloadGroup)) {
                    _cache[deviceId].Add(payloadGroup, new());

                    if (_defaults.ContainsKey(payloadGroup)) {
                        foreach (var entry in _defaults[payloadGroup]) {
                            if (entry.Key == ".namespace") {
                                if (!_namespaces.ContainsKey(deviceId)) {
                                    _namespaces.Add(deviceId, entry.Value);
                                }
                            } else {
                                _cache[deviceId][payloadGroup].Add(entry.Key, TryParseValue(entry.Value));
                            }
                        }
                    }
                }

                // update data in cache
                var messagePayload = Encoding.ASCII.GetString(args.ApplicationMessage.Payload);
                _cache[deviceId][payloadGroup][data] = TryParseValue(messagePayload);

                var merossPath = _options.MerossRoot + deviceId + "/subscribe";

                var message = new MerossData();

                if (_defaults.ContainsKey(payloadGroup) &&
                    _defaults[payloadGroup].ContainsKey(".namespace")) {
                    message.header.@namespace = _defaults[payloadGroup][".namespace"];
                } else {
                    message.header.@namespace = "Unknown";
                }

                message.header.messageId = "message" + (DateTime.Now.Ticks / 10000);
                message.header.from = _options.MqttRoot + deviceId + "/ack";
                message.header.timestamp = ((DateTimeOffset) DateTime.Now).ToUnixTimeSeconds();
                message.header.sign =
                    MD5.HashData(Encoding.ASCII.GetBytes(message.header.messageId + message.header.timestamp))
                        .Select(x => x.ToString("x2")).Aggregate("", (s, s1) => s + s1);


                message.payload.Add(payloadGroup, new());

                foreach (var entry in _cache[deviceId][payloadGroup]) {
                    message.payload[payloadGroup][entry.Key] = entry.Value;
                }


                message.payload[payloadGroup]["channel"] = channel;

                if (payloadGroup == "light") {
                    var mode = 0;

                    switch (data) {
                        case "rgb":
                            mode |= 1  | 4;
                            break;
                        case "temperature":
                            mode |= 2 | 4;
                            break;
                        case "luminance":
                            mode |= 4;
                            break;
                    }

                    message.payload[payloadGroup]["capacity"] = mode;
                }

                var messageJson = JsonConvert.SerializeObject(message);
                var messageMqtt = new MqttApplicationMessageBuilder()
                    .WithPayload(messageJson)
                    .WithTopic(merossPath)
                    .WithResponseTopic(_options.MqttRoot + deviceId + "/ack")
                    .Build();

                Task.Run(() => _client.PublishAsync(messageMqtt));
            } else {
                Console.WriteLine($"Failed to interpret the path {path}");
            }
        }

        private static object TryParseValue(string value) {
            if (Int32.TryParse(value, out var integer)) {
                return integer;
            } else if (Boolean.TryParse(value, out var boolean)) {
                return boolean;
            } else if (Double.TryParse(value, out var floating)) {
                return floating;
            } else {
                return value;
            }
        }
    }
}