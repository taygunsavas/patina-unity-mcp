using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Patina.Editor
{
    public class BridgeRequest
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("command")]
        public string Command { get; set; }

        [JsonProperty("params")]
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
                Error = null
            };
        }

        public static BridgeResponse Fail(string id, string message, string code = "COMMAND_ERROR")
        {
            return new BridgeResponse
            {
                Id = id,
                Success = false,
                Result = null,
                Error = new BridgeError { Code = code, Message = message }
            };
        }
    }
}
