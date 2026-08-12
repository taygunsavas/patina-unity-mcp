using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Patina.Editor
{
    /// <summary>
    /// Some MCP clients serialize an empty/omitted "params" as a scalar (e.g. null,
    /// an empty string, or a number) instead of a JSON object. Every command handler
    /// expects a <see cref="JObject"/>, so tolerate non-object "params" by treating
    /// them as an empty object instead of throwing during deserialization.
    /// </summary>
    public class TolerantParametersConverter : JsonConverter<JObject>
    {
        public override JObject ReadJson(
            JsonReader reader,
            Type objectType,
            JObject existingValue,
            bool hasExistingValue,
            JsonSerializer serializer
        )
        {
            JToken token = JToken.ReadFrom(reader);
            return token is JObject obj ? obj : new JObject();
        }

        public override void WriteJson(JsonWriter writer, JObject value, JsonSerializer serializer)
        {
            (value ?? new JObject()).WriteTo(writer);
        }
    }

    public class BridgeRequest
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("command")]
        public string Command { get; set; }

        [JsonProperty("params")]
        [JsonConverter(typeof(TolerantParametersConverter))]
        public JObject Parameters { get; set; }
    }

    public class BridgeError
    {
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class BridgeResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("result")]
        public object Result { get; set; }

        [JsonProperty("error")]
        public BridgeError Error { get; set; }

        public static BridgeResponse Ok(string id, object result)
        {
            return new BridgeResponse
            {
                Id = id,
                Success = true,
                Result = result,
                Error = null,
            };
        }

        public static BridgeResponse Fail(string id, string message, string code = "COMMAND_ERROR")
        {
            return new BridgeResponse
            {
                Id = id,
                Success = false,
                Result = null,
                Error = new BridgeError { Code = code, Message = message },
            };
        }
    }
}
