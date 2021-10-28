# Meross-MQTT-Shim
Meross devices do not play nicely with plain mqtt signals, as they take a json configuration instead of individual mqtt topics. Therefore this shim will transform individual topics (e.g. from openhab) into a json to control the device. It works with any mqtt broker you have as it only translates messages between topics. So instead of having to send a bunch of json you can now send to individual topics and the shim will build a valid json for you.

Conversion only works one way at the moment (shim > meross).

```json
{
  "header": {
    "messageId": "8e48af089ca7c38910dd5b5861a42400",
    "namespace": "Appliance.Control.Light",
    "method": "PUSH",
    "payloadVersion": 1,
    "from": "/appliance/2007297292488790822348e1e9270cd3/publish",
    "timestamp": 3,
    "timestampMs": 620,
    "sign": "987ba9cb8c3138232fd827158c712aba"
  },
  "payload": {
    "light": {
      "capacity": 6,
      "channel": 0,
      "rgb": 3013120,
      "temperature": 100,
      "luminance": 100,
      "transform": 1
    }
  }
}
```

![MQTT-Explorer](https://github.com/CShark/meross-mqtt-shim/blob/master/assets/mqtt_layout.jpg)

## Setup
The app requires a config file. In Windows, the config file is located in the same directory as the executable, in Linux it is in `/etc/meross-mqtt`. The config path can be manually set using the `-c` or `--config` flag.

A fully populated example config looks like this:

`meros.config`
```ini
# The url to the MQTT-broker. May include protocol and port.
# If no port is specified, the port will be chosen according to the protocol.
# If no protocol is specified, mqtt:// will be assumed
# If a secure protocol is defined, useTLS will be forced to true
# Supported protocols and their default ports: mqtt:// (1883), mqtts:// (8883), ws:// (80), wss:// (443)
# Required
# Examples:
# mqtts://192.168.2.4:8883
# mqtt://192.168.2.4
broker = 

# Force the use of TLS, if it is not already forced by the broker protocol
# Default False
useTls = false

# Enable or Disable validation of the broker certificate
# Default True
verifyCert = true

# Username used to connect to the broker
user = 

# Password used to connect to the broker
password = 

# The mqtt topic to which the shim will listen to. May or may not have a trailing /
mqttRoot = shim/meross/
```

## Payloads
When writing to a capability topic, the generated payload will include the last known state of all endpoints for this capability. To set the default values for a capability, create a config file in the `conf.d`-subdirectory with the name of the capability. Also use those defaults to set the required namespace for the capability type.

`conf.d/light`
```ini
.namespace = Appliance.Control.Light
luminance = 1
temperature = 1
rgb = 0
transform = 1
```

`conf.d/togglex`
```ini
.namespace = Appliance.Control.ToggleX
onoff = 0
```

### Mqtt-Topic layout
All device payloads can be written in the configured mqtt-topic (`shim/meross` by default). The two valid layouts are
- `shim/meross/{device id}/{capability}/{endpoint}`
- `shim/meross/{device id}/{capability}/{channel}/{endpoint}`

Most devices only support one channel `0`, in which case the channel can be omitted. 

The resulting json will be sent to `/appliances/{device id}/subscribe`.

### Examples
To write the `onoff`-value of the toggleX-capability an appliance, you send either a `0` or `1` to `shim/meross/2007297292488790822348e1e9270cd3/togglex/onoff`

To manipulate the brightness of an LED-Strip, you can send a value between `0` and `100` to `shim/meross/2007297292488790822348e1e9270cd3/light/luminance`

### Special Capabilities
The light capability is a bit special as it requires a flag to be set to distinguish between rgb-mode and color-temperature. The flag `capacity` will be automatically set based on which endpoint you write your data to.

Some Light-Appliances require the ToggleX-Capability to turn it on and off, some support an on/off-switch in the light-Capability itself. Analyze your device's messages and see what applies to you.

## Tools
To pair a meross device to your local mqtt-broker, you can use the [Meross utilities](https://github.com/bytespider/Meross). To analyze your meross device and figure out what capabilities it has, you can use [MQTT-Explorer](http://mqtt-explorer.com/). To fetch some infos you can use the following queries to `/appliance/{device id}/subscribe`

Fetch infos:
```json
{
  "header": {
    "messageId": "test1",
    "namespace": "Appliance.System.All",
    "method": "GET",
    "payloadVersion": 1,
    "from": "/appliance/openhab/ack",
    "timestamp": 1635323517,
    "sign": "ab050cd777ac0c9354f28e3ab901c710"
  },
  "payload": {
  
  }
}
```

Fetch all available abilities:
```json
{
  "header": {
    "messageId": "test1",
    "namespace": "Appliance.System.Ability",
    "method": "GET",
    "payloadVersion": 1,
    "from": "/appliance/openhab/ack",
    "timestamp": 1635323517,
    "sign": "ab050cd777ac0c9354f28e3ab901c710"
  },
  "payload": {
  
  }
}
```

The sign-value is the MD5 of messageId + timestamp