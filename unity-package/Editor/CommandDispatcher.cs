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
            // Phase 1
            RegisterHandler("log_to_console", new LogToConsoleHandler());
            RegisterHandler("get_hierarchy", new GetHierarchyHandler());
            RegisterHandler("create_game_object", new CreateGameObjectHandler());
            // Phase 2 — Scene
            RegisterHandler("get_scene_info", new GetSceneInfoHandler());
            // Phase 2 — Components
            RegisterHandler("add_component", new AddComponentHandler());
            RegisterHandler("set_property", new SetPropertyHandler());
            RegisterHandler("remove_component", new RemoveComponentHandler());
            // Phase 2 — Hierarchy ops
            RegisterHandler("reparent_game_object", new ReparentGameObjectHandler());
            RegisterHandler("delete_game_object", new DeleteGameObjectHandler());
            RegisterHandler("duplicate_game_object", new DuplicateGameObjectHandler());
            // Phase 2 — Prefabs
            RegisterHandler("create_prefab", new CreatePrefabHandler());
            RegisterHandler("instantiate_prefab", new InstantiatePrefabHandler());
            // Phase 2 — Assets
            RegisterHandler("find_assets_by_type", new FindAssetsByTypeHandler());
            RegisterHandler("find_assets_by_name", new FindAssetsByNameHandler());
            // Phase 4 — Inspection and spatial search
            RegisterHandler("get_game_object_info", new GetGameObjectInfoHandler());
            RegisterHandler("find_game_objects_by_tag", new FindGameObjectsByTagHandler());
            RegisterHandler("find_game_objects_by_component", new FindGameObjectsByComponentHandler());
            RegisterHandler("find_game_objects_by_layer", new FindGameObjectsByLayerHandler());
            // Phase 3 — Scene lifecycle
            RegisterHandler("open_scene", new OpenSceneHandler());
            RegisterHandler("save_scene", new SaveSceneHandler());
            RegisterHandler("new_scene", new NewSceneHandler());
            RegisterHandler("get_build_settings", new GetBuildSettingsHandler());
            RegisterHandler("set_build_scenes", new SetBuildScenesHandler());
        }

        public static int HandlerCount
        {
            get { return _handlers.Count; }
        }
    }
}
