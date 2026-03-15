using System.Collections.Concurrent;
using System.Threading.Tasks;
using Patina.Editor.Commands;
using UnityEngine;

namespace Patina.Editor
{
    public static class CommandDispatcher
    {
        private static readonly ConcurrentDictionary<string, ICommandHandler> _handlers = new ConcurrentDictionary<string, ICommandHandler>();

        public static void RegisterHandler(string command, ICommandHandler handler)
        {
            _handlers[command] = handler;
        }

        public static async Task<BridgeResponse> Dispatch(BridgeRequest request)
        {
            if (request == null)
                return BridgeResponse.Fail(null, "Null request");

            string id = request.Id;
            string command = request.Command;

            if (string.IsNullOrEmpty(command))
                return BridgeResponse.Fail(id, "Missing command field");

            if (!_handlers.TryGetValue(command, out var handler))
                return BridgeResponse.Fail(id, "Unknown command: " + command);

            try
            {
                object result = await handler.HandleAsync(request.Parameters);
                return BridgeResponse.Ok(id, result);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Patina] Error handling command '{command}': {ex}");
                return BridgeResponse.Fail(id, ex.Message);
            }
        }

        public static void RegisterBuiltInHandlers()
        {
            RegisterHandler("log_to_console", new LogToConsoleHandler());
            RegisterHandler("get_hierarchy", new GetHierarchyHandler());
            RegisterHandler("create_game_object", new CreateGameObjectHandler());
        }

        public static int HandlerCount
        {
            get { return _handlers.Count; }
        }
    }
}
